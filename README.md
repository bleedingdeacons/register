# Badi

[![CI](https://github.com/bleedingdeacons/register/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/bleedingdeacons/register/actions/workflows/ci.yml) [![Coverage Status](https://coveralls.io/repos/github/bleedingdeacons/register/badge.svg?branch=main)](https://coveralls.io/github/bleedingdeacons/register?branch=main)

## Building

The Integrity client is consumed as the **`Integrity.Client`** package from
GitHub Packages (published from
[integrity-sharp](https://github.com/bleedingdeacons/integrity-sharp)). That
registry requires authentication even for packages from public repositories, so
a token is needed to restore.

Create a **classic** PAT with the `read:packages` scope — read-only; do not reuse
integrity-sharp's publishing token, which also carries `write:packages` and
`repo` — then expose it as `GITHUB_PACKAGES_TOKEN`:

```powershell
[Environment]::SetEnvironmentVariable('GITHUB_PACKAGES_TOKEN', '<token>', 'User')
```

`nuget.config` reads the variable at restore time, so no token is stored in the
repo. Package Source Mapping there routes `Integrity.*` to the private feed and
everything else to nuget.org; new first-party package prefixes must be added to
that mapping or they will not resolve.

To build against a local integrity-sharp checkout instead — for working across
both repos without cutting a release — pass `-p:UseLocalIntegritySharp=true`.
`IntegritySharpDir` points at the checkout and defaults to a sibling directory.

### Tokens in CI

| Secret | Scope | Used for | On expiry |
| --- | --- | --- | --- |
| `PACKAGES_READ_TOKEN` | `read:packages` | Restoring `Integrity.Client` | Both jobs fail within seconds at *Verify GitHub Packages credentials* with an explicit rotate-me error |

`GITHUB_TOKEN` cannot substitute: the package belongs to the integrity-sharp
repository, and a workflow token cannot read a package owned by a different
repository. It remains configured as a fallback in case the package is later
granted access to this repository under its *Manage Actions access* settings.