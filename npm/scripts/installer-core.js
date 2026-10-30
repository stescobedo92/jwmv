"use strict";

/**
 * Dependency-free helpers used by the postinstall script to download, verify and extract the jwmv binary.
 *
 * Security model:
 *  - downloads only follow HTTPS redirects to GitHub hosts;
 *  - the archive must match the published SHA-256 and carry a valid cosign keyless signature whose
 *    Fulcio certificate chains to the Sigstore public-good root and names a GitHub Actions workflow of
 *    the expected repository;
 *  - ZIP entries are written only below the destination directory, with entry and size limits.
 */

const crypto = require("crypto");
const fs = require("fs");
const https = require("https");
const path = require("path");
const zlib = require("zlib");

// Only signatures produced by this workflow file of the expected repository are accepted.
const RELEASE_WORKFLOW_FILE = "release.yml";

const GITHUB_ACTIONS_ISSUER = "https://token.actions.githubusercontent.com";
const CODE_SIGNING_EKU = "1.3.6.1.5.5.7.3.3";
const FULCIO_ISSUER_OID = "1.3.6.1.4.1.57264.1.1";
const FULCIO_ISSUER_V2_OID = "1.3.6.1.4.1.57264.1.8";

// Sigstore public-good instance (https://github.com/sigstore/root-signing, targets/fulcio_v1.crt.pem and
// targets/fulcio_intermediate_v1.crt.pem). Same certificates embedded in the .NET SigstoreTrustRoot.
const FULCIO_ROOT_PEM = `-----BEGIN CERTIFICATE-----
MIIB9zCCAXygAwIBAgIUALZNAPFdxHPwjeDloDwyYChAO/4wCgYIKoZIzj0EAwMw
KjEVMBMGA1UEChMMc2lnc3RvcmUuZGV2MREwDwYDVQQDEwhzaWdzdG9yZTAeFw0y
MTEwMDcxMzU2NTlaFw0zMTEwMDUxMzU2NThaMCoxFTATBgNVBAoTDHNpZ3N0b3Jl
LmRldjERMA8GA1UEAxMIc2lnc3RvcmUwdjAQBgcqhkjOPQIBBgUrgQQAIgNiAAT7
XeFT4rb3PQGwS4IajtLk3/OlnpgangaBclYpsYBr5i+4ynB07ceb3LP0OIOZdxex
X69c5iVuyJRQ+Hz05yi+UF3uBWAlHpiS5sh0+H2GHE7SXrk1EC5m1Tr19L9gg92j
YzBhMA4GA1UdDwEB/wQEAwIBBjAPBgNVHRMBAf8EBTADAQH/MB0GA1UdDgQWBBRY
wB5fkUWlZql6zJChkyLQKsXF+jAfBgNVHSMEGDAWgBRYwB5fkUWlZql6zJChkyLQ
KsXF+jAKBggqhkjOPQQDAwNpADBmAjEAj1nHeXZp+13NWBNa+EDsDP8G1WWg1tCM
WP/WHPqpaVo0jhsweNFZgSs0eE7wYI4qAjEA2WB9ot98sIkoF3vZYdd3/VtWB5b9
TNMea7Ix/stJ5TfcLLeABLE4BNJOsQ4vnBHJ
-----END CERTIFICATE-----`;

const FULCIO_INTERMEDIATE_PEM = `-----BEGIN CERTIFICATE-----
MIICGjCCAaGgAwIBAgIUALnViVfnU0brJasmRkHrn/UnfaQwCgYIKoZIzj0EAwMw
KjEVMBMGA1UEChMMc2lnc3RvcmUuZGV2MREwDwYDVQQDEwhzaWdzdG9yZTAeFw0y
MjA0MTMyMDA2MTVaFw0zMTEwMDUxMzU2NThaMDcxFTATBgNVBAoTDHNpZ3N0b3Jl
LmRldjEeMBwGA1UEAxMVc2lnc3RvcmUtaW50ZXJtZWRpYXRlMHYwEAYHKoZIzj0C
AQYFK4EEACIDYgAE8RVS/ysH+NOvuDZyPIZtilgUF9NlarYpAd9HP1vBBH1U5CV7
7LSS7s0ZiH4nE7Hv7ptS6LvvR/STk798LVgMzLlJ4HeIfF3tHSaexLcYpSASr1kS
0N/RgBJz/9jWCiXno3sweTAOBgNVHQ8BAf8EBAMCAQYwEwYDVR0lBAwwCgYIKwYB
BQUHAwMwEgYDVR0TAQH/BAgwBgEB/wIBADAdBgNVHQ4EFgQU39Ppz1YkEZb5qNjp
KFWixi4YZD8wHwYDVR0jBBgwFoAUWMAeX5FFpWapesyQoZMi0CrFxfowCgYIKoZI
zj0EAwMDZwAwZAIwPCsQK4DYiZYDPIaDi5HFKnfxXx6ASSVmERfsynYBiX2X6SJR
nZU84/9DZdnFvvxmAjBOt6QpBlc4J/0DxvkTCqpclvziL6BCCPnjdlIB3Pu3BxsP
mygUY7Ii2zbdCdliiow=
-----END CERTIFICATE-----`;

const SIGSTORE_TRUST_ROOT = Object.freeze({
  roots: Object.freeze([FULCIO_ROOT_PEM]),
  intermediates: Object.freeze([FULCIO_INTERMEDIATE_PEM]),
});

class InstallerError extends Error {
  constructor(message) {
    super(message);
    this.name = "InstallerError";
  }
}

// ---------------------------------------------------------------------------
// Downloads
// ---------------------------------------------------------------------------

function isAllowedDownloadUrl(value) {
  let url;
  try {
    url = new URL(String(value));
  } catch {
    return false;
  }
  if (url.protocol !== "https:") {
    return false;
  }
  const host = url.hostname.toLowerCase();
  return host === "github.com" || host.endsWith(".github.com") || host.endsWith(".githubusercontent.com");
}

/**
 * Downloads a URL into memory. Only HTTPS GitHub hosts are contacted, redirects are capped, the
 * response size is bounded and a stalled connection is abandoned after `timeoutMs` of inactivity, so a
 * misbehaving server cannot exhaust memory or disk or block `npm install` forever.
 */
function download(url, options = {}) {
  const maxRedirects = options.maxRedirects ?? 5;
  const maxBytes = options.maxBytes ?? 512 * 1024 * 1024;
  const timeoutMs = options.timeoutMs ?? 60 * 1000;
  const userAgent = options.userAgent ?? "jwmv-npm-installer";

  return new Promise((resolve, reject) => {
    const get = (currentUrl, redirectsLeft) => {
      if (!isAllowedDownloadUrl(currentUrl)) {
        reject(new InstallerError(`Refusing to download from ${currentUrl}: only HTTPS GitHub URLs are allowed.`));
        return;
      }

      const request = https.get(currentUrl, { headers: { "User-Agent": userAgent } }, (res) => {
        const status = res.statusCode ?? 0;
        if (status >= 300 && status < 400 && res.headers.location) {
          res.resume();
          if (redirectsLeft <= 0) {
            reject(new InstallerError(`Too many redirects while downloading ${url}.`));
            return;
          }
          let next;
          try {
            next = new URL(res.headers.location, currentUrl).toString();
          } catch {
            reject(new InstallerError(`Invalid redirect while downloading ${url}.`));
            return;
          }
          get(next, redirectsLeft - 1);
          return;
        }

        if (status !== 200) {
          res.resume();
          reject(new InstallerError(`Download failed: HTTP ${status} for ${currentUrl}`));
          return;
        }

        const declared = Number(res.headers["content-length"]);
        if (Number.isFinite(declared) && declared > maxBytes) {
          res.destroy();
          reject(new InstallerError(`Download of ${currentUrl} is larger than the allowed ${maxBytes} bytes.`));
          return;
        }

        const chunks = [];
        let received = 0;
        res.on("data", (chunk) => {
          received += chunk.length;
          if (received > maxBytes) {
            res.destroy(new InstallerError(`Download of ${currentUrl} is larger than the allowed ${maxBytes} bytes.`));
            return;
          }
          chunks.push(chunk);
        });
        res.on("end", () => resolve(Buffer.concat(chunks)));
        res.on("error", reject);
      });
      request.on("error", reject);
      request.setTimeout(timeoutMs, () => {
        request.destroy(new InstallerError(`Timed out while downloading ${currentUrl}: no data received for ${timeoutMs} ms.`));
      });
    };

    get(String(url), maxRedirects);
  });
}

// ---------------------------------------------------------------------------
// Checksums
// ---------------------------------------------------------------------------

function parseChecksumFile(text, assetName) {
  const parts = String(text ?? "").trim().split(/\s+/);
  const hash = parts[0] ?? "";
  if (!/^[0-9a-fA-F]{64}$/.test(hash)) {
    throw new InstallerError(`Malformed checksum file for ${assetName}.`);
  }
  if (parts.length > 1) {
    const name = parts[1].replace(/^\*/, "");
    if (name !== assetName) {
      throw new InstallerError(`The checksum file describes '${name}', not '${assetName}'.`);
    }
  }
  return hash.toLowerCase();
}

function sha256Hex(buffer) {
  return crypto.createHash("sha256").update(buffer).digest("hex");
}

function verifyChecksum(buffer, expectedHex, assetName) {
  const actual = sha256Hex(buffer);
  if (actual !== String(expectedHex).toLowerCase()) {
    throw new InstallerError(`SHA-256 mismatch for ${assetName}: expected ${expectedHex}, got ${actual}.`);
  }
}

// ---------------------------------------------------------------------------
// ZIP extraction
// ---------------------------------------------------------------------------

function safeJoin(destDir, entryName) {
  const unsafe = () => new InstallerError(`Unsafe zip entry name: ${JSON.stringify(entryName)}`);
  if (typeof entryName !== "string" || entryName.length === 0 || entryName.includes("\0") || entryName.includes("\\")) {
    throw unsafe();
  }
  if (entryName.startsWith("/") || /^[A-Za-z]:/.test(entryName)) {
    throw unsafe();
  }
  const segments = entryName.split("/");
  for (const segment of segments) {
    if (segment === "" || segment === "." || segment === "..") {
      throw unsafe();
    }
  }

  const root = path.resolve(destDir);
  const target = path.resolve(root, ...segments);
  if (target === root || !target.startsWith(root + path.sep)) {
    throw unsafe();
  }
  return target;
}

function readUInt16(buf, offset) {
  if (offset < 0 || offset + 2 > buf.length) {
    throw new InstallerError("Invalid ZIP: truncated data.");
  }
  return buf.readUInt16LE(offset);
}

function readUInt32(buf, offset) {
  if (offset < 0 || offset + 4 > buf.length) {
    throw new InstallerError("Invalid ZIP: truncated data.");
  }
  return buf.readUInt32LE(offset);
}

function findEndOfCentralDirectory(buf) {
  if (buf.length < 22) {
    throw new InstallerError("Invalid ZIP: file too small.");
  }
  const floor = Math.max(0, buf.length - 22 - 65535);
  for (let i = buf.length - 22; i >= floor; i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) {
      return i;
    }
  }
  throw new InstallerError("Invalid ZIP: end of central directory not found.");
}

/**
 * Extracts a ZIP buffer into destDir using only Node.js built-ins.
 * Returns the list of extracted entry names.
 */
function extractZip(buf, destDir, options = {}) {
  const maxEntries = options.maxEntries ?? 64;
  const maxTotalBytes = options.maxTotalBytes ?? 256 * 1024 * 1024;
  const allowedNames = options.allowedNames ? new Set(options.allowedNames) : null;

  const eocd = findEndOfCentralDirectory(buf);
  const entryCount = readUInt16(buf, eocd + 10);
  const centralSize = readUInt32(buf, eocd + 12);
  const centralOffset = readUInt32(buf, eocd + 16);
  if (entryCount === 0xffff || centralOffset === 0xffffffff || centralSize === 0xffffffff) {
    throw new InstallerError("Invalid ZIP: ZIP64 archives are not supported.");
  }
  if (centralOffset + centralSize > eocd) {
    throw new InstallerError("Invalid ZIP: central directory out of bounds.");
  }
  if (entryCount > maxEntries) {
    throw new InstallerError(`ZIP has too many entries (${entryCount} > ${maxEntries}).`);
  }

  // First pass: parse and validate every entry before touching the file system.
  const entries = [];
  let offset = centralOffset;
  let totalBytes = 0;
  for (let i = 0; i < entryCount; i++) {
    if (readUInt32(buf, offset) !== 0x02014b50) {
      throw new InstallerError("Invalid ZIP: bad central directory entry.");
    }
    const method = readUInt16(buf, offset + 10);
    const compressedSize = readUInt32(buf, offset + 20);
    const uncompressedSize = readUInt32(buf, offset + 24);
    const nameLength = readUInt16(buf, offset + 28);
    const extraLength = readUInt16(buf, offset + 30);
    const commentLength = readUInt16(buf, offset + 32);
    const localHeaderOffset = readUInt32(buf, offset + 42);
    const nameEnd = offset + 46 + nameLength;
    if (nameEnd > buf.length) {
      throw new InstallerError("Invalid ZIP: truncated entry name.");
    }
    const name = buf.toString("utf8", offset + 46, nameEnd);
    offset = nameEnd + extraLength + commentLength;

    if (name.endsWith("/")) {
      continue; // directories are created on demand
    }
    if (method !== 0 && method !== 8) {
      throw new InstallerError(`Unsupported compression method ${method} for ${name}.`);
    }
    if (allowedNames && !allowedNames.has(name)) {
      throw new InstallerError(`Unexpected entry in archive: ${name}`);
    }

    const target = safeJoin(destDir, name);
    totalBytes += uncompressedSize;
    if (totalBytes > maxTotalBytes) {
      throw new InstallerError(`ZIP content is too large (> ${maxTotalBytes} bytes).`);
    }
    entries.push({ name, target, method, compressedSize, uncompressedSize, localHeaderOffset });
  }

  // Second pass: inflate with a hard cap on the output size so a zip bomb cannot exceed the declared sizes.
  const written = [];
  for (const entry of entries) {
    if (readUInt32(buf, entry.localHeaderOffset) !== 0x04034b50) {
      throw new InstallerError(`Invalid ZIP: bad local header for ${entry.name}.`);
    }
    const localNameLength = readUInt16(buf, entry.localHeaderOffset + 26);
    const localExtraLength = readUInt16(buf, entry.localHeaderOffset + 28);
    const dataOffset = entry.localHeaderOffset + 30 + localNameLength + localExtraLength;
    if (dataOffset + entry.compressedSize > buf.length) {
      throw new InstallerError(`Invalid ZIP: data for ${entry.name} is out of bounds.`);
    }
    const compressed = buf.subarray(dataOffset, dataOffset + entry.compressedSize);

    let data;
    if (entry.method === 0) {
      data = compressed;
    } else {
      try {
        data = zlib.inflateRawSync(compressed, { maxOutputLength: Math.max(1, entry.uncompressedSize) });
      } catch (error) {
        throw new InstallerError(`Entry ${entry.name} exceeds its declared size or is corrupt (${error.message}).`);
      }
    }
    if (data.length !== entry.uncompressedSize) {
      throw new InstallerError(`Entry ${entry.name} does not match its declared size.`);
    }

    fs.mkdirSync(path.dirname(entry.target), { recursive: true });
    fs.writeFileSync(entry.target, data);
    written.push(entry.name);
  }

  return written;
}

// ---------------------------------------------------------------------------
// Sigstore keyless signature verification
// ---------------------------------------------------------------------------

function parseCertificate(text) {
  let pem = String(text ?? "").trim();
  if (!pem.includes("-----BEGIN")) {
    // cosign writes --output-certificate as base64-encoded PEM by default.
    pem = Buffer.from(pem, "base64").toString("utf8");
  }
  if (!pem.includes("-----BEGIN CERTIFICATE-----")) {
    throw new InstallerError("The release certificate is not a PEM certificate.");
  }
  try {
    return new crypto.X509Certificate(pem);
  } catch (error) {
    throw new InstallerError(`The release certificate could not be parsed: ${error.message}`);
  }
}

function decodeSignature(text) {
  const value = String(text ?? "").trim();
  if (value.length === 0 || !/^[A-Za-z0-9+/]+={0,2}$/.test(value)) {
    throw new InstallerError("The release signature is not valid base64.");
  }
  return Buffer.from(value, "base64");
}

function readTlv(buf, offset) {
  if (offset + 2 > buf.length) {
    throw new InstallerError("The release certificate is malformed (truncated DER).");
  }
  const tag = buf[offset];
  let length = buf[offset + 1];
  let position = offset + 2;
  if (length & 0x80) {
    const count = length & 0x7f;
    if (count === 0 || count > 4 || position + count > buf.length) {
      throw new InstallerError("The release certificate is malformed (bad DER length).");
    }
    length = 0;
    for (let i = 0; i < count; i++) {
      length = length * 256 + buf[position++];
    }
  }
  if (position + length > buf.length) {
    throw new InstallerError("The release certificate is malformed (DER length out of bounds).");
  }
  return { tag, start: position, end: position + length, next: position + length };
}

function readChildren(buf, tlv) {
  const children = [];
  let position = tlv.start;
  while (position < tlv.end) {
    const child = readTlv(buf, position);
    children.push(child);
    position = child.next;
  }
  return children;
}

function decodeOid(bytes) {
  if (bytes.length === 0) {
    return "";
  }
  const parts = [Math.floor(bytes[0] / 40), bytes[0] % 40];
  let value = 0;
  for (let i = 1; i < bytes.length; i++) {
    value = value * 128 + (bytes[i] & 0x7f);
    if ((bytes[i] & 0x80) === 0) {
      parts.push(value);
      value = 0;
    }
  }
  return parts.join(".");
}

/** Returns a Map of extension OID -> extnValue (OCTET STRING content) for the certificate. */
function readExtensions(certificate) {
  const raw = certificate.raw;
  const certificateTlv = readTlv(raw, 0);
  const tbs = readTlv(raw, certificateTlv.start);
  const extensionsWrapper = readChildren(raw, tbs).find((child) => child.tag === 0xa3);
  const extensions = new Map();
  if (!extensionsWrapper) {
    return extensions;
  }
  const sequence = readTlv(raw, extensionsWrapper.start);
  for (const extension of readChildren(raw, sequence)) {
    const fields = readChildren(raw, extension);
    if (fields.length < 2 || fields[0].tag !== 0x06) {
      continue;
    }
    const oid = decodeOid(raw.subarray(fields[0].start, fields[0].end));
    const value = fields[fields.length - 1];
    if (value.tag !== 0x04) {
      continue;
    }
    extensions.set(oid, raw.subarray(value.start, value.end));
  }
  return extensions;
}

function readOidcIssuer(certificate) {
  const extensions = readExtensions(certificate);
  let issuer = null;
  const v2 = extensions.get(FULCIO_ISSUER_V2_OID);
  if (v2) {
    const utf8String = readTlv(v2, 0);
    if (utf8String.tag !== 0x0c) {
      throw new InstallerError("The release certificate has a malformed OIDC issuer extension.");
    }
    issuer = v2.toString("utf8", utf8String.start, utf8String.end);
  }
  const v1 = extensions.get(FULCIO_ISSUER_OID);
  if (v1) {
    const legacyIssuer = v1.toString("utf8");
    if (issuer !== null && issuer !== legacyIssuer) {
      throw new InstallerError("The release certificate records conflicting OIDC issuers.");
    }
    issuer = issuer ?? legacyIssuer;
  }
  return issuer;
}

function readWorkflowIdentity(certificate) {
  const san = certificate.subjectAltName ?? "";
  const uris = san
    .split(", ")
    .filter((entry) => entry.startsWith("URI:"))
    .map((entry) => entry.slice(4));
  return uris.find((uri) => uri.toLowerCase().startsWith("https://github.com/")) ?? null;
}

function verifyIdentity(identity, repository) {
  // Repository names are case-insensitive on GitHub; the workflow path and the ref prefix are not.
  const value = String(identity);
  const repositoryPrefix = `https://github.com/${String(repository).trim().replace(/^\/+|\/+$/g, "")}/`;
  const workflowPrefix = `.github/workflows/${RELEASE_WORKFLOW_FILE}@refs/`;
  const matchesRepository = value.slice(0, repositoryPrefix.length).toLowerCase() === repositoryPrefix.toLowerCase();
  const matchesWorkflow = value.slice(repositoryPrefix.length, repositoryPrefix.length + workflowPrefix.length) === workflowPrefix;
  if (!matchesRepository || !matchesWorkflow || value.length <= repositoryPrefix.length + workflowPrefix.length) {
    throw new InstallerError(`The release was signed by '${value}', which is not the release workflow of '${repository}'.`);
  }
}

function isValidAt(certificate, date) {
  const from = new Date(certificate.validFrom);
  const to = new Date(certificate.validTo);
  return from <= date && date <= to;
}

function verifyChain(leaf, trustRoot) {
  const roots = trustRoot.roots.map((pem) => new crypto.X509Certificate(pem));
  const intermediates = trustRoot.intermediates.map((pem) => new crypto.X509Certificate(pem));
  const pool = [...intermediates, ...roots];
  // Fulcio leaf certificates are valid for ten minutes, so the chain is evaluated at the leaf's notBefore.
  const signingTime = new Date(leaf.validFrom);

  let current = leaf;
  for (let depth = 0; depth < 6; depth++) {
    const issuer = pool.find((candidate) => current.checkIssued(candidate) && current.verify(candidate.publicKey));
    if (!issuer) {
      throw new InstallerError("The release certificate does not chain to the Sigstore root.");
    }
    if (!isValidAt(issuer, signingTime)) {
      throw new InstallerError("A certificate in the release signing chain was not valid at signing time.");
    }
    if (roots.includes(issuer)) {
      if (!issuer.checkIssued(issuer) || !issuer.verify(issuer.publicKey)) {
        throw new InstallerError("The trusted root certificate is not self-signed.");
      }
      return;
    }
    current = issuer;
  }
  throw new InstallerError("The release certificate chain is too long.");
}

/**
 * Verifies a cosign keyless signature over `blob`.
 * Returns { identity, issuer } on success and throws InstallerError otherwise.
 */
function verifySignature(blob, signatureText, certificateText, repository, trustRoot = SIGSTORE_TRUST_ROOT) {
  const leaf = parseCertificate(certificateText);
  const signature = decodeSignature(signatureText);

  let valid = false;
  try {
    valid = crypto.verify("sha256", blob, leaf.publicKey, signature);
  } catch {
    valid = false;
  }
  if (!valid) {
    throw new InstallerError("The release signature does not match the downloaded file.");
  }

  verifyChain(leaf, trustRoot);

  const keyUsage = leaf.keyUsage ?? [];
  if (!keyUsage.includes(CODE_SIGNING_EKU)) {
    throw new InstallerError("The release certificate is not a code-signing certificate.");
  }

  const identity = readWorkflowIdentity(leaf);
  if (!identity) {
    throw new InstallerError("The release certificate does not carry a GitHub workflow identity.");
  }
  verifyIdentity(identity, repository);

  const issuer = readOidcIssuer(leaf);
  if (!issuer) {
    throw new InstallerError("The release certificate does not record an OIDC issuer.");
  }
  if (issuer !== GITHUB_ACTIONS_ISSUER) {
    throw new InstallerError(`The release certificate was issued for OIDC issuer '${issuer}', expected '${GITHUB_ACTIONS_ISSUER}'.`);
  }

  return { identity, issuer };
}

module.exports = {
  InstallerError,
  SIGSTORE_TRUST_ROOT,
  GITHUB_ACTIONS_ISSUER,
  isAllowedDownloadUrl,
  download,
  parseChecksumFile,
  sha256Hex,
  verifyChecksum,
  safeJoin,
  extractZip,
  parseCertificate,
  verifyIdentity,
  verifySignature,
};
