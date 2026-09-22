# NuGet.org trusted publishing

TrEInvoice packages are published through NuGet.org Trusted Publishing using GitHub Actions OIDC. No long-lived NuGet API key is required or stored in GitHub secrets.

The configured NuGet.org trusted publishing policy is:

- Owner: `ahmtkm`
- Repository: `TrEInvoice`
- Workflow file: `publish-nuget.yml` (the filename only; the policy maps to `.github/workflows/publish-nuget.yml`)
- Publisher: GitHub Actions
- Package scope: `TrEInvoice.Core`, `TrEInvoice.Calculation`, `TrEInvoice.Validation`, and `TrEInvoice.Ubl`

The workflow supports manual dispatch and published GitHub releases. A release tag must match all four project versions (for example, `v0.1.0`). Before requesting OIDC credentials, it checks NuGet.org and refuses to publish if any package already has that version. It does not use `--skip-duplicate`; partial publication therefore fails visibly and requires careful operator review before any retry.

The OIDC login step must remain close to the push step because NuGet.org issues a short-lived credential. The configured publisher username is the NuGet.org profile username `ahmtkm`, not an email address. Do not add a persistent NuGet API key to repository secrets or configuration.

For the current v0.1.0 package limitations and regulatory disclaimer, see the repository [README](../README.md), which is embedded in every package as its NuGet README.
