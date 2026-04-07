#!/usr/bin/env node
"use strict";

const { execFileSync } = require("child_process");
const path = require("path");

const exe = path.join(__dirname, "jwmv.exe");

try {
  execFileSync(exe, process.argv.slice(2), { stdio: "inherit" });
} catch (err) {
  if (err.status != null) {
    process.exit(err.status);
  }
  console.error(`Failed to run jwmv: ${err.message}`);
  process.exit(1);
}
