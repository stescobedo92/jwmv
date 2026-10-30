Synthetic certificate chain used only by the npm installer tests.

It imitates the shape of Sigstore/Fulcio keyless certificates (SAN URI with the
GitHub workflow identity plus the `1.3.6.1.4.1.57264.1.1`/`.1.8` OIDC issuer
extensions) but is signed by a throw-away test root, so it cannot be used to
forge real jwmv releases. The private keys were discarded after generation.
