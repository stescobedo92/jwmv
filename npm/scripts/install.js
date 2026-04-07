#!/usr/bin/env node
"use strict";

const https = require("https");
const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const PACKAGE = require("../package.json");
const VERSION = PACKAGE.version;
const REPO = "stescobedo92/jwmv";

/**
 * Resolves the GitHub Release asset name for the current platform/arch.
 * jwmv currently only supports Windows (win-x64, win-arm64).
 */
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

/**
 * Follow redirects (GitHub sends 302) and download to a file.
 */
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
 * Extract ZIP using PowerShell (available on all Windows versions).
 */
function extractZip(zipPath, destDir) {
  fs.mkdirSync(destDir, { recursive: true });
  execSync(
    `powershell -NoProfile -Command "Expand-Archive -Path '${zipPath}' -DestinationPath '${destDir}' -Force"`,
    { stdio: "inherit" }
  );
}

async function main() {
  const binDir = path.join(__dirname, "..", "bin");
  const exePath = path.join(binDir, "jwmv.exe");

  // Skip if already installed (e.g. CI caching)
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
