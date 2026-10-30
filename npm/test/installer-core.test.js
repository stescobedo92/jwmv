"use strict";

const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("fs");
const os = require("os");
const path = require("path");
const zlib = require("zlib");
const crypto = require("crypto");

const core = require("../scripts/installer-core");

const FIXTURES = path.join(__dirname, "fixtures", "sigstore");
const REPOSITORY = "example-org/example-repo";
const IDENTITY = "https://github.com/example-org/example-repo/.github/workflows/release.yml@refs/tags/v9.9.9";

function fixture(name) {
  return fs.readFileSync(path.join(FIXTURES, name), "utf8");
}

function testTrustRoot() {
  return {
    roots: [fixture("test-root.pem")],
    intermediates: [fixture("test-intermediate.pem")],
  };
}

function tempDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), "jwmv-npm-test-"));
}

/**
 * Minimal ZIP writer (local headers + central directory + EOCD) for tests.
 * entries: [{ name, data, method: 0|8, declaredUncompressedSize? }]
 */
function buildZip(entries) {
  const localParts = [];
  const centralParts = [];
  let offset = 0;
  for (const entry of entries) {
    const nameBuf = Buffer.from(entry.name, "utf8");
    const method = entry.method ?? 8;
    const raw = Buffer.isBuffer(entry.data) ? entry.data : Buffer.from(entry.data, "utf8");
    const compressed = method === 8 ? zlib.deflateRawSync(raw) : raw;
    const uncompressedSize = entry.declaredUncompressedSize ?? raw.length;

    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0, 6);
    local.writeUInt16LE(method, 8);
    local.writeUInt32LE(0, 10);
    local.writeUInt32LE(0, 14); // crc32 (not validated by the extractor)
    local.writeUInt32LE(compressed.length, 18);
    local.writeUInt32LE(uncompressedSize, 22);
    local.writeUInt16LE(nameBuf.length, 26);
    local.writeUInt16LE(0, 28);

    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0, 8);
    central.writeUInt16LE(method, 10);
    central.writeUInt32LE(0, 12);
    central.writeUInt32LE(0, 16);
    central.writeUInt32LE(compressed.length, 20);
    central.writeUInt32LE(uncompressedSize, 24);
    central.writeUInt16LE(nameBuf.length, 28);
    central.writeUInt16LE(0, 30);
    central.writeUInt16LE(0, 32);
    central.writeUInt16LE(0, 34);
    central.writeUInt16LE(0, 36);
    central.writeUInt32LE(0, 38);
    central.writeUInt32LE(offset, 42);

    localParts.push(local, nameBuf, compressed);
    centralParts.push(central, nameBuf);
    offset += local.length + nameBuf.length + compressed.length;
  }

  const centralDirectory = Buffer.concat(centralParts);
  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(0x06054b50, 0);
  eocd.writeUInt16LE(0, 4);
  eocd.writeUInt16LE(0, 6);
  eocd.writeUInt16LE(entries.length, 8);
  eocd.writeUInt16LE(entries.length, 10);
  eocd.writeUInt32LE(centralDirectory.length, 12);
  eocd.writeUInt32LE(offset, 16);
  eocd.writeUInt16LE(0, 20);
  return Buffer.concat([...localParts, centralDirectory, eocd]);
}

test("safeJoin accepts plain relative entry names", () => {
  const dest = path.join(os.tmpdir(), "jwmv-dest");
  assert.equal(core.safeJoin(dest, "jwmv.exe"), path.join(dest, "jwmv.exe"));
  assert.equal(core.safeJoin(dest, "sub/dir/file.txt"), path.join(dest, "sub", "dir", "file.txt"));
});

test("safeJoin rejects entries that escape the destination", () => {
  const dest = path.join(os.tmpdir(), "jwmv-dest");
  for (const name of ["../evil.exe", "a/../../evil.exe", "/etc/passwd", "C:\\Windows\\evil.exe", "C:/Windows/evil.exe", "..", "sub\\..\\..\\evil", "", "a//b", "./a"]) {
    assert.throws(() => core.safeJoin(dest, name), /unsafe/i, `expected ${JSON.stringify(name)} to be rejected`);
  }
});

test("extractZip writes entries below the destination directory", () => {
  const dest = tempDir();
  const zip = buildZip([
    { name: "jwmv.exe", data: "binary" },
    { name: "docs/readme.txt", data: "hello", method: 0 },
  ]);

  const written = core.extractZip(zip, dest);

  assert.deepEqual(written.sort(), ["docs/readme.txt", "jwmv.exe"]);
  assert.equal(fs.readFileSync(path.join(dest, "jwmv.exe"), "utf8"), "binary");
  assert.equal(fs.readFileSync(path.join(dest, "docs", "readme.txt"), "utf8"), "hello");
});

test("extractZip rejects path traversal entries without writing anything", () => {
  const dest = tempDir();
  const zip = buildZip([{ name: "../outside.txt", data: "evil" }]);

  assert.throws(() => core.extractZip(zip, dest), /unsafe/i);
  assert.equal(fs.existsSync(path.join(dest, "..", "outside.txt")), false);
});

test("extractZip enforces the entry count limit", () => {
  const dest = tempDir();
  const zip = buildZip([
    { name: "a.txt", data: "a" },
    { name: "b.txt", data: "b" },
    { name: "c.txt", data: "c" },
  ]);

  assert.throws(() => core.extractZip(zip, dest, { maxEntries: 2 }), /too many entries/i);
});

test("extractZip enforces the total uncompressed size limit", () => {
  const dest = tempDir();
  const zip = buildZip([{ name: "big.bin", data: Buffer.alloc(2048, 1) }]);

  assert.throws(() => core.extractZip(zip, dest, { maxTotalBytes: 1024 }), /too large/i);
});

test("extractZip rejects entries whose inflated size exceeds the declared size", () => {
  const dest = tempDir();
  const zip = buildZip([{ name: "bomb.bin", data: Buffer.alloc(1024 * 1024, 0), declaredUncompressedSize: 16 }]);

  assert.throws(() => core.extractZip(zip, dest), /declared size/i);
});

test("extractZip rejects truncated archives", () => {
  const dest = tempDir();
  const zip = buildZip([{ name: "jwmv.exe", data: "binary" }]);
  const truncated = zip.subarray(0, zip.length - 10);

  assert.throws(() => core.extractZip(truncated, dest), /invalid zip/i);
});

test("extractZip only accepts expected entry names when an allow-list is given", () => {
  const dest = tempDir();
  const zip = buildZip([{ name: "jwmv.exe", data: "binary" }, { name: "extra.dll", data: "x" }]);

  assert.throws(() => core.extractZip(zip, dest, { allowedNames: ["jwmv.exe"] }), /unexpected entry/i);
});

test("parseChecksumFile reads the hash from the cosign/sha256sum format", () => {
  const hash = "f".repeat(64);
  assert.equal(core.parseChecksumFile(`${hash} *jwmv-win-x64.zip\n`, "jwmv-win-x64.zip"), hash);
  assert.equal(core.parseChecksumFile(`${hash.toUpperCase()}  jwmv-win-x64.zip`, "jwmv-win-x64.zip"), hash);
  assert.equal(core.parseChecksumFile(hash, "jwmv-win-x64.zip"), hash);
});

test("parseChecksumFile rejects malformed content and foreign file names", () => {
  assert.throws(() => core.parseChecksumFile("not a hash", "jwmv-win-x64.zip"), /malformed/i);
  assert.throws(() => core.parseChecksumFile("", "jwmv-win-x64.zip"), /malformed/i);
  assert.throws(() => core.parseChecksumFile(`${"a".repeat(64)} *other.zip`, "jwmv-win-x64.zip"), /other.zip/);
});

test("verifyChecksum compares the SHA-256 of the buffer", () => {
  const data = Buffer.from("payload");
  const hash = crypto.createHash("sha256").update(data).digest("hex");
  assert.doesNotThrow(() => core.verifyChecksum(data, hash, "asset.zip"));
  assert.throws(() => core.verifyChecksum(Buffer.from("tampered"), hash, "asset.zip"), /mismatch/i);
});

test("isAllowedDownloadUrl only accepts HTTPS GitHub hosts", () => {
  assert.equal(core.isAllowedDownloadUrl("https://github.com/stescobedo92/jwmv/releases/download/v1.0.0/jwmv-win-x64.zip"), true);
  assert.equal(core.isAllowedDownloadUrl("https://objects.githubusercontent.com/github-production-release-asset/abc"), true);
  assert.equal(core.isAllowedDownloadUrl("https://release-assets.githubusercontent.com/abc"), true);
  assert.equal(core.isAllowedDownloadUrl("http://github.com/stescobedo92/jwmv/releases/download/v1.0.0/jwmv-win-x64.zip"), false);
  assert.equal(core.isAllowedDownloadUrl("https://evil.example/jwmv-win-x64.zip"), false);
  assert.equal(core.isAllowedDownloadUrl("https://github.com.evil.example/x"), false);
  assert.equal(core.isAllowedDownloadUrl("ftp://github.com/x"), false);
  assert.equal(core.isAllowedDownloadUrl("not a url"), false);
});

test("verifySignature accepts a signature from the expected repository workflow", () => {
  const blob = fs.readFileSync(path.join(FIXTURES, "blob.bin"));

  const result = core.verifySignature(blob, fixture("leaf-ok.sig"), fixture("leaf-ok.pem"), REPOSITORY, testTrustRoot());

  assert.equal(result.identity, IDENTITY);
  assert.equal(result.issuer, "https://token.actions.githubusercontent.com");
});

test("verifySignature accepts the base64-wrapped PEM that cosign writes", () => {
  const blob = fs.readFileSync(path.join(FIXTURES, "blob.bin"));

  const result = core.verifySignature(blob, fixture("leaf-ok.sig"), fixture("leaf-ok.pem.b64"), REPOSITORY, testTrustRoot());

  assert.equal(result.identity, IDENTITY);
});

test("verifySignature rejects a certificate issued for another repository", () => {
  const blob = fs.readFileSync(path.join(FIXTURES, "blob.bin"));
  assert.throws(
    () => core.verifySignature(blob, fixture("leaf-wrong-repo.sig"), fixture("leaf-wrong-repo.pem"), REPOSITORY, testTrustRoot()),
    /not a workflow of/i
  );
});

test("verifySignature rejects a certificate from another OIDC issuer", () => {
  const blob = fs.readFileSync(path.join(FIXTURES, "blob.bin"));
  assert.throws(
    () => core.verifySignature(blob, fixture("leaf-wrong-issuer.sig"), fixture("leaf-wrong-issuer.pem"), REPOSITORY, testTrustRoot()),
    /issuer/i
  );
});

test("verifySignature rejects a self-signed certificate even with the expected identity", () => {
  const blob = fs.readFileSync(path.join(FIXTURES, "blob.bin"));
  assert.throws(
    () => core.verifySignature(blob, fixture("leaf-selfsigned.sig"), fixture("leaf-selfsigned.pem"), REPOSITORY, testTrustRoot()),
    /chain/i
  );
});

test("verifySignature rejects a tampered blob and a malformed signature", () => {
  const blob = Buffer.from("jwmv test blob fixture-v1 (tampered)\n");
  assert.throws(() => core.verifySignature(blob, fixture("leaf-ok.sig"), fixture("leaf-ok.pem"), REPOSITORY, testTrustRoot()), /does not match/i);

  const original = fs.readFileSync(path.join(FIXTURES, "blob.bin"));
  assert.throws(() => core.verifySignature(original, "not base64!", fixture("leaf-ok.pem"), REPOSITORY, testTrustRoot()), /signature/i);
});

test("verifySignature rejects a certificate that is not chained to the trusted root", () => {
  const blob = fs.readFileSync(path.join(FIXTURES, "blob.bin"));
  const otherRoot = { roots: [fixture("leaf-selfsigned.pem")], intermediates: [] };
  assert.throws(() => core.verifySignature(blob, fixture("leaf-ok.sig"), fixture("leaf-ok.pem"), REPOSITORY, otherRoot), /chain/i);
});

test("the embedded Sigstore trust root contains the public-good Fulcio root and intermediate", () => {
  const { X509Certificate } = crypto;
  const roots = core.SIGSTORE_TRUST_ROOT.roots.map((pem) => new X509Certificate(pem));
  const intermediates = core.SIGSTORE_TRUST_ROOT.intermediates.map((pem) => new X509Certificate(pem));

  assert.ok(roots.some((cert) => cert.subject.includes("CN=sigstore") && cert.checkIssued(cert)));
  assert.ok(intermediates.some((cert) => cert.subject.includes("sigstore-intermediate")));
});
