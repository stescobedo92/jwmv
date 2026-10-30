#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");
const core = require("./installer-core");

const PACKAGE = require("../package.json");
const VERSION = PACKAGE.jwmv?.binaryVersion || PACKAGE.version;
const REPO = "stescobedo92/jwmv";
const MAX_ARCHIVE_BYTES = 256 * 1024 * 1024;
const MAX_COMPANION_BYTES = 64 * 1024;

function getAssetName() {
  if (process.platform !== "win32") {
    console.error(`jwmv currently only supports Windows. Your platform: ${process.platform}`);
    process.exit(1);
  }
  const arch = process.arch === "arm64" ? "win-arm64" : "win-x64";
  return `jwmv-${arch}.zip`;
}

async function main() {
  const binDir = path.join(__dirname, "..", "bin");
  const exePath = path.join(binDir, "jwmv.exe");

  if (fs.existsSync(exePath)) {
    console.log("jwmv binary already present, skipping download.");
    return;
  }

  const asset = getAssetName();
  const base = `https://github.com/${REPO}/releases/download/v${VERSION}/`;

  console.log(`Downloading jwmv v${VERSION} (${asset})...`);
  try {
    const [archive, checksumText, signatureText, certificateText] = await Promise.all([
      core.download(base + asset, { maxBytes: MAX_ARCHIVE_BYTES }),
      core.download(`${base}${asset}.sha256`, { maxBytes: MAX_COMPANION_BYTES }).then((buffer) => buffer.toString("utf8")),
      core.download(`${base}${asset}.sig`, { maxBytes: MAX_COMPANION_BYTES }).then((buffer) => buffer.toString("utf8")),
      core.download(`${base}${asset}.pem`, { maxBytes: MAX_COMPANION_BYTES }).then((buffer) => buffer.toString("utf8")),
    ]);

    console.log("Verifying checksum and signature...");
    core.verifyChecksum(archive, core.parseChecksumFile(checksumText, asset), asset);
    const signer = core.verifySignature(archive, signatureText, certificateText, REPO);
    console.log(`Signature verified: ${signer.identity}`);

    console.log("Extracting...");
    fs.mkdirSync(binDir, { recursive: true });
    core.extractZip(archive, binDir, { allowedNames: ["jwmv.exe"], maxEntries: 8 });
    if (!fs.existsSync(exePath)) {
      throw new core.InstallerError("The release archive did not contain jwmv.exe.");
    }

    console.log(`jwmv v${VERSION} installed successfully.`);
  } catch (err) {
    console.error(`Failed to install jwmv: ${err.message}`);
    console.error("You can install manually from: https://github.com/stescobedo92/jwmv/releases");
    process.exit(1);
  }
}

main();
