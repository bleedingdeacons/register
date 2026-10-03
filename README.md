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

The Freedom packages (`Freedom.Client`, and `Freedom.Client.Maui` on
Android) come from the same feed with the same token; `Freedom.*` is in the
mapping.

## Project layout

| Project | What |
| --- | --- |
| `…Intergroup.Register` | The MAUI app: views, view models, and everything that touches the platform. |
| `…Intergroup.Register.Core` | Plain net10.0. Attendance and compliance, the two event logs, the snapshot and reconcile, the email queue and its templates, the Freedom overlay. |
| `…Unity.Intergroup` | The local SQLite replica of Unity's data, its repositories, and the sync that fills it. |
| `…Unity.Intergroup.Tests` | xUnit v3 over Unity.Intergroup. |
| `…Intergroup.Register.Tests` | xUnit v3 over Register.Core — the edges: torn logs, refused passwords, answers that are not JSON. |

**Core exists because a test project cannot reference a MAUI app.** The
app's target frameworks are platform heads; a `net10.0` test host has no
compatible framework to resolve against, and there is no such thing as a
`net10.0` head of a MAUI app. So the code worth testing has to live
somewhere a test project can see it. Link and Hand reached the same
arrangement for the same reason.

The line is not stylistic: the moment a file references `Preferences`,
`SecureStorage`, `FileSystem`, `Connectivity` or any `Microsoft.Maui` type,
it stops compiling in Core. That constraint is why the event logs take the
path they write to, the email service takes a network probe and an SMTP
client factory, and the temporary id counter takes a store. Each seam is
the one a platform detail would otherwise have been welded across, and the
one a test drives. What stayed in the app, and why, is listed in Core's
project file.

Both test projects run in CI under Coverlet, merged into the one report
the Coveralls badge reads:

```bash
dotnet test TheBleedingDeacons.Intergroup.Register.Tests
```

The figure fell when Register.Core joined it, from about 92% of
Unity.Intergroup alone to about 46% of both. That is Core being measured
for the first time, not anything getting worse.

## Settings from Freedom

A tablet can take its SMTP, Unity, Better Stack and compliance settings from
the site instead of from this build or from what was typed in on it — see
[freedom-sharp](https://github.com/bleedingdeacons/freedom-sharp) and the
Freedom WordPress plugin. **Android only for now**, and **off unless the build
names a site**: add a `Freedom` section to the git-ignored `appsettings.json`
before building.

```json
"Freedom": {
  "BaseUrl": "https://aa-bristol.org/amber",
  "Application": "register"
}
```

`CallbackUri` defaults to
`com.thebleedingdeacons.intergroup.register.freedom://auth`, which is what
`Platforms/Android/FreedomCallbackActivity.cs` catches; the Freedom admin's
`register` application must have the same Callback URI. Then sign the tablet in
from **API Settings → Freedom**.

- **Freedom wins where it holds a value, and only there.** A key the site has
  not set falls through to the tablet's own setting, so a tablet set up by hand
  keeps working. The keys are in `Services/FreedomSettings.cs`: `smtp.host`,
  `smtp.port`, `smtp.username`, `smtp.password`, `smtp.enable_ssl`,
  `smtp.from_display_name`, `smtp.timeout_seconds`, `unity.base_url`,
  `unity.api_key`, `betterstack.endpoint`, `betterstack.source_token`,
  `compliance.email`. Tick the password, API key and source token as
  **Secret** in the admin, so they arrive sealed to the tablet.
- **Nothing waits on the network.** Each start reads what Freedom stored last
  time, then syncs in the background; a change is applied as it arrives — the
  email service and Better Stack sink are reconfigured, no restart needed.
- **`devsettings.json` is untouched.** Dev builds still embed it; once the
  `register` application's values are set on the site, it can go.

### Tokens in CI

| Secret | Scope | Used for | On expiry |
| --- | --- | --- | --- |
| `PACKAGES_READ_TOKEN` | `read:packages` | Restoring `Integrity.Client` | Both jobs fail within seconds at *Verify GitHub Packages credentials* with an explicit rotate-me error |

`GITHUB_TOKEN` cannot substitute: the package belongs to the integrity-sharp
repository, and a workflow token cannot read a package owned by a different
repository. It remains configured as a fallback in case the package is later
granted access to this repository under its *Manage Actions access* settings.