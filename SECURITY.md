# Security Policy

We take the security of **jwmv** seriously. This document explains which versions receive security fixes and how to responsibly report a vulnerability.

## Supported versions

Only the latest release on the `1.x` line receives security updates.

| Version      | Supported          |
| ------------ | ------------------ |
| 1.x (latest) | :white_check_mark: |
| < 1.0        | :x:                |

We recommend always running the most recent release. You can check your installed version with `jwmv --version` and upgrade with `jwmv selfupdate`, which verifies the SHA-256 and Sigstore signature of the release asset before installing it.

## Release integrity

Every release asset (`jwmv-win-x64.zip`, `jwmv-win-arm64.zip`) is published together with:

- `<asset>.sha256` — SHA-256 checksum,
- `<asset>.sig` and `<asset>.pem` — cosign keyless signature and Fulcio certificate issued to the GitHub Actions release workflow of this repository,
- `<asset>.sigstore.json` — the Sigstore bundle.

`jwmv selfupdate` and the npm installer refuse assets that do not verify. You can verify a download manually with `cosign verify-blob` (see the README).

## Security automation

- CodeQL analyses the C# sources, the npm installer and the GitHub Actions workflows on every pull request and on a weekly schedule.
- Dependabot proposes updates for GitHub Actions, NuGet and npm dependencies, and CI fails when a vulnerable NuGet package (including transitive ones) is detected.
- Every action used by the workflows is pinned to a commit SHA and runs with the minimum permissions it needs.

## Reporting a vulnerability

Please **do not** open public GitHub issues, discussions, or pull requests for security problems. Instead, use one of the private channels below:

1. **GitHub Private Vulnerability Reporting** — preferred.
   Go to [https://github.com/stescobedo92/jwmv/security/advisories/new](https://github.com/stescobedo92/jwmv/security/advisories/new) and submit a private advisory. This lets us discuss, patch, and coordinate disclosure in a single thread.
2. **Email** — send details to **stescobedo.31@gmail.com**. Use a clear subject line such as `[jwmv security] <short summary>`.

Please include, to the extent you can:

- A description of the issue and the impact you believe it has.
- Steps to reproduce, or a minimal proof of concept.
- The jwmv version (`jwmv --version`), Windows version, and architecture.
- Any suggested mitigations you've already identified.

## What to expect

- **Acknowledgement within 72 hours** of your initial report.
- An initial assessment (severity, affected versions, likely fix timeline) shortly after.
- Regular updates while we investigate and prepare a fix.
- Credit in the release notes and/or GitHub Security Advisory, unless you prefer to remain anonymous.

We aim to ship a fix, publish a coordinated advisory, and notify users via the release channel (GitHub Releases, winget, npm, NuGet) as quickly as responsibly possible.

## Disclosure policy

We follow a **coordinated disclosure** model:

- Please give us a reasonable window to ship a fix before any public disclosure. A 90-day window is a common baseline, but we'll work with you on the timeline based on severity and complexity.
- We will **not** accept or act on reports that are already public exploits, weaponized PoCs posted openly, or issues disclosed before we've had a chance to respond. Coordinated reports always get priority.
- Once a fix is released, we publish a GitHub Security Advisory describing the issue, affected versions, and remediation.

## Out of scope

The following are generally **not** considered security vulnerabilities in jwmv:

- Vulnerabilities in third-party JDK distributions installed by jwmv — report these upstream to the vendor.
- Social engineering, phishing, or physical attacks.
- Missing security headers on static documentation pages.
- Denial of service that requires local administrator access.

Thank you for helping keep jwmv and its users safe.
