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
| `…Intergroup.Register.Specs` | Reqnroll over Register.Core — the behaviour, in the words a meeting would use for it. |

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

### Tests and the specification

They are not duplicates and none replaces another. Register.Tests and
Unity.Intergroup.Tests are where the edges live: a torn log line, an SMTP
server that refuses the password, a Scrutiny that answers HTML. They are
what the coverage badge measures, merged into one report by Coverlet.
Register.Specs is where the behaviour lives, written as sentences: a group
brings its officers with it, the last thing said about a group is what
counts, a refused registration keeps the logs for next time.

```bash
dotnet test TheBleedingDeacons.Intergroup.Register.Tests
```

```bash
dotnet test TheBleedingDeacons.Intergroup.Register.Specs
```

All three run in CI. The specs are a gate, not a contribution to the
badge: folding them into the coverage run would quietly change what the
number means. That number fell when Register.Core joined it, from about
92% of Unity.Intergroup alone to about 46% of both. That is Core being
measured for the first time, not anything getting worse.

The narrative behind the feature files, and the glossary they use, is
[`specs/domain-model.md`](specs/domain-model.md). It was written by
reading the code: the app came first and the specification was
reverse-engineered from it, so where the two disagree the feature files
are what runs. It also lists the behaviour found while writing them that
is pinned rather than fixed. The sharpest: a wrong SMTP password never
trips the email circuit breaker.

**Scenarios tagged `@manual @ignore` are on-device acceptance criteria**
and Reqnroll skips them: the Yes button greying out, Freedom's sign-in
through its callback, Reset Device deleting both logs. No test host can
reach any of them.

## Settings from Freedom

**Every credential and endpoint comes from the site, and from nowhere
else**: SMTP, Unity, Better Stack and the compliance contact. Nothing is built
into the app and nothing can be typed in on the tablet — see
[freedom-sharp](https://github.com/bleedingdeacons/freedom-sharp) and the
Freedom WordPress plugin. The build names only *which* Freedom site to ask, in
the git-ignored `appsettings.json`, alongside the logging level. That choice is
the test-or-live choice: `/amber` is the test bed.

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
from **API Settings → Freedom**. **Android only**: the Windows head builds, but
has no Freedom sign-in and so runs with no settings.

- **A key the site has not set is not set at all.** Mail Settings and API
  Settings show it as "Not set", keep their Test buttons, and point at the
  sign-in. Only the shape of a connection has a default (port 587, TLS on).
  The keys are in `Register.Core/Services/FreedomSettings.cs`: `smtp.host`,
  `smtp.port`, `smtp.username`, `smtp.password`, `smtp.enable_ssl`,
  `smtp.from_display_name`, `smtp.timeout_seconds`, `unity.base_url`,
  `unity.api_key`, `betterstack.endpoint`, `betterstack.source_token`,
  `compliance.email`. Tick the password, API key and source token as
  **Secret** in the admin, so they arrive sealed to the tablet.
- **Nothing waits on the network.** Each start reads what Freedom stored last
  time, then syncs in the background; a change is applied as it arrives — the
  email service and Better Stack sink are reconfigured, no restart needed.
- **There is no `devsettings.json` any more**, and no `UseDevCredentials`
  switch. A Debug build used to embed one — whose Unity address was the live
  site, which is how a test tablet ended up synced from live. Any copies left
  in a working tree are git-ignored and unused; delete them.
- **The feature switches stay on the tablet**: both event logs, auto-register
  positions, the single-GSR shortcut, add position holder, welcome and
  acceptance emails, and the device label. So does the active meeting.
- **An upgraded tablet forgets what was typed in.** The first start of a build
  without the fallback removes the SMTP password, Unity key and Better Stack
  token from SecureStorage, the three settings files and the compliance
  address, once (`Services/LegacySettings.cs`).

### Tokens in CI

| Secret | Scope | Used for | On expiry |
| --- | --- | --- | --- |
| `PACKAGES_READ_TOKEN` | `read:packages` | Restoring `Integrity.Client` | Both jobs fail within seconds at *Verify GitHub Packages credentials* with an explicit rotate-me error |

`GITHUB_TOKEN` cannot substitute: the package belongs to the integrity-sharp
repository, and a workflow token cannot read a package owned by a different
repository. It remains configured as a fallback in case the package is later
granted access to this repository under its *Manage Actions access* settings.