#!/usr/bin/env node
"use strict";

const https = require("https");
const fs = require("fs");
const path = require("path");
const zlib = require("zlib");

const PACKAGE = require("../package.json");
const VERSION = PACKAGE.jwmv?.binaryVersion || PACKAGE.version;
const REPO = "stescobedo92/jwmv";

function getAssetName() {
  if (process.platform !== "win32") {
    console.error(
      `jwmv currently only supports Windows. Your platform: ${process.platform}`
    );
    process.exit(1);
  }
  const arch = process.arch === "arm64" ? "win-arm64" : "win-x64";
  return `jwmv-${arch}.zip`;
}

function download(url, dest) {
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(dest);
    const get = (u) => {
      https
        .get(u, { headers: { "User-Agent": "jwmv-npm-installer" } }, (res) => {
          if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
            return get(res.headers.location);
          }
          if (res.statusCode !== 200) {
            return reject(new Error(`Download failed: HTTP ${res.statusCode} for ${u}`));
          }
          res.pipe(file);
          file.on("finish", () => file.close(resolve));
        })
        .on("error", reject);
    };
    get(url);
  });
}

/**
 * Extract a ZIP file using only Node.js built-ins (no PowerShell dependency).
 * ZIP format: https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
 */
function extractZip(zipPath, destDir) {
  const buf = fs.readFileSync(zipPath);
  fs.mkdirSync(destDir, { recursive: true });

  // Find End of Central Directory record (search last 65KB for signature 0x06054b50)
  let eocdOffset = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 65557); i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) {
      eocdOffset = i;
      break;
    }
  }
  if (eocdOffset === -1) throw new Error("Invalid ZIP: EOCD not found");

  const cdOffset = buf.readUInt32LE(eocdOffset + 16);
  const cdEntries = buf.readUInt16LE(eocdOffset + 10);

  let offset = cdOffset;
  for (let i = 0; i < cdEntries; i++) {
    if (buf.readUInt32LE(offset) !== 0x02014b50) throw new Error("Invalid central directory entry");

    const method = buf.readUInt16LE(offset + 10);
    const compSize = buf.readUInt32LE(offset + 20);
    const uncompSize = buf.readUInt32LE(offset + 24);
    const nameLen = buf.readUInt16LE(offset + 28);
    const extraLen = buf.readUInt16LE(offset + 30);
    const commentLen = buf.readUInt16LE(offset + 32);
    const localHeaderOffset = buf.readUInt32LE(offset + 42);
    const fileName = buf.toString("utf8", offset + 46, offset + 46 + nameLen);

    // Skip directories
    if (!fileName.endsWith("/")) {
      // Read local file header to get actual data offset
      const localNameLen = buf.readUInt16LE(localHeaderOffset + 26);
      const localExtraLen = buf.readUInt16LE(localHeaderOffset + 28);
      const dataOffset = localHeaderOffset + 30 + localNameLen + localExtraLen;

      const compressedData = buf.subarray(dataOffset, dataOffset + compSize);
      let fileData;

      if (method === 0) {
        // Stored (no compression)
        fileData = compressedData;
      } else if (method === 8) {
        // Deflate
        fileData = zlib.inflateRawSync(compressedData);
      } else {
        console.warn(`Skipping ${fileName}: unsupported compression method ${method}`);
        offset += 46 + nameLen + extraLen + commentLen;
        continue;
      }

      const outPath = path.join(destDir, ...fileName.split("/"));
      fs.mkdirSync(path.dirname(outPath), { recursive: true });
      fs.writeFileSync(outPath, fileData);
    }

    offset += 46 + nameLen + extraLen + commentLen;
  }
}

async function main() {
  const binDir = path.join(__dirname, "..", "bin");
  const exePath = path.join(binDir, "jwmv.exe");

  if (fs.existsSync(exePath)) {
    console.log("jwmv binary already present, skipping download.");
    return;
  }

  const asset = getAssetName();
  const url = `https://github.com/${REPO}/releases/download/v${VERSION}/${asset}`;
  const zipPath = path.join(binDir, asset);

  console.log(`Downloading jwmv v${VERSION} (${asset})...`);
  fs.mkdirSync(binDir, { recursive: true });

  try {
    await download(url, zipPath);
    console.log("Extracting...");
    extractZip(zipPath, binDir);
    fs.unlinkSync(zipPath);
    console.log(`jwmv v${VERSION} installed successfully.`);
  } catch (err) {
    console.error(`Failed to install jwmv: ${err.message}`);
    console.error(
      "You can install manually from: https://github.com/stescobedo92/jwmv/releases"
    );
    process.exit(1);
  }
}

main();
