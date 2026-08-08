# Unit test plan — TheBleedingDeacons.Intergroup.Register

Companion to the `TheBleedingDeacons.Intergroup.Register.UnitTests` project. It
records what can be tested today, what is blocked and why, and the order worth
doing it in.

The app is ~16k lines across Services, ViewModels, Utilities and Support, with
no test coverage before this project existed. The existing
`TheBleedingDeacons.Unity.Intergroup.Tests` project covers the data/sync library
only.

---

## 1. What the host can and cannot do

The tests run in a plain console process (Microsoft.Testing.Platform) against
the app's **Windows head**, unpackaged. That was verified by probe, not assumed:

| Surface | Works? | Notes |
| --- | --- | --- |
| `Microsoft.Maui.Graphics` (`Colors`, `Color`) | ✅ | Pure managed. |
| `Microsoft.Maui.Controls` type refs (`IValueConverter`, `IMultiValueConverter`) | ✅ | Reference assemblies resolve; converters are pure functions. |
| Plain .NET / EF Core / SQLite / libphonenumber / Serilog | ✅ | No platform dependency. |
| `Microsoft.Maui.Storage.Preferences` | ❌ | `TypeInitializationException` on `UnpackagedPreferencesImplementation`. |
| `Microsoft.Maui.Storage.FileSystem` | ❌ | `COMException` / `REGDB_E_CLASSNOTREG`. |
| `Application.Current` | ❌ | Null — no MAUI app is ever built. |
| Anything WinUI | ❌ | No package identity, no WindowsAppRuntime. |

**This split is the single most important fact in the plan.** It is what
separates tiers 1–2 (write tests now) from tier 3 (needs a small refactor
first).

Two build details make the host work at all, both documented inline in the
`.csproj`: the project does **not** set `UseMaui` (that forces `OutputType` to
`WinExe`, which xUnit v3 rejects), and it forces
`WindowsAppSdk*Initialize=false` onto the app's project reference (otherwise the
Windows App SDK's module initializer in the *app* assembly throws
`REGDB_E_CLASSNOTREG` on first touch and every test fails before its body runs).

---

## 2. Tier 1 — pure logic, testable today

No seams, no database, no fakes. This is the bulk of the achievable coverage and
should be done first.

### 2.1 Status — **done**

Tier 1 is complete. 227 tests, covering everything listed in §2.2–§2.9 below
except the three classes noted as unreachable in §4.4.

| Target | Tests |
| --- | ---: |
| `Utilities/Converters.cs`, `BoolToColorConverter`, `DaySelectionConverters` | 152 |
| `Utilities/BasicMarkdownConverter` | 34 |
| `Services/EmailTemplateService` | 29 |
| `Services/PhoneNumberService` | 15 |
| `Services/CacheService` | 10 |
| `Support/EmailValidator` | 11 |
| `Converters/ObscurePhoneNumberConverter` | 8 |
| `Extensions/MeetingExtensions` | 9 |
| `Utilities/MeetingCriteriaConverter` | 5 |
| `Utilities/RegisterData` | 3 |
| `Support/TaskExtensions` | 4 |
| `Support/BetterStackDurable/BetterStackNdjsonBatchFormatter` | 4 |

`Utilities` went from 0% to 93.1% and `Extensions` to 100%. The suite total is
346 tests; app-assembly line coverage is **14.3%**, up from 6.0%.

Not done from §2.9: `Support/ExceptionEnricher`. It needs a Serilog
`ILogEventPropertyFactory` fake and a constructed `LogEvent`, which is more
scaffolding than the rest of tier 1 combined; deferred rather than rushed.

### 2.2 `Utilities/BasicMarkdownConverter` — **highest value in tier 1**

Converts privacy-policy markdown (fetched from the Unity/Scrutiny server) into
HTML that ends up in compliance emails. Untrusted input rendered into HTML makes
the escaping behaviour security-relevant, not cosmetic.

Cover: ATX headings h1–h6; bullet blocks (`-`, `*`, `+`) and consecutive-item
grouping; `---` horizontal rules; paragraph accumulation and the join-with-space
rule; `**bold**`, `*italic*`; `[text](url)`; bare `https://` autolinking and the
`(?<!href=")` guard against double-wrapping; HTML escaping of `&`, `<`, `>`,
`"`; CRLF/CR normalisation; empty and whitespace-only input returning
`string.Empty`.

Escaping order is worth an explicit test: `EscapeHtml` runs **before** the link
regexes, so a URL containing `&` is escaped inside the emitted `href`.

### 2.3 `Services/EmailTemplateService` — **also high value**

The `EmailTemplateService(Assembly, string)` constructor overload exists
precisely so the resource-loading path can be pointed at a chosen assembly, so
this is fully testable including embedded-resource lookup.

Cover:
- `RenderTemplate<T>` — `{{Property}}` substitution, null property → empty
  string, nested `{{Property.SubProperty}}`, `{{#each Collection}}…{{/each}}`
  loops, `{{#if Property}}…{{/if}}` conditionals, empty template → `string.Empty`.
- `RenderTemplateAsync` — loads the app's real embedded templates
  (`WelcomeEmail.html`, `ComplianceAcceptanceEmail.html`) by passing the app
  assembly; asserts a known placeholder is substituted.
- `TemplateNotFoundException` for an unknown name; `TemplateRenderingException`
  wrapping other failures (and *not* wrapping `TemplateNotFoundException` — the
  exception filter is easy to break).
- The template cache: a second `RenderTemplateAsync` for the same name must not
  re-read the resource. Note the cache is a plain `Dictionary` with no lock —
  a concurrency test here would document a real thread-safety gap.

Render against a `ComplianceEmail` model to keep the tests tied to real usage.

### 2.4 `Services/PhoneNumberService`

libphonenumber is pure managed code, so this needs nothing. Cover: valid GB
mobile and fixed-line; E.164 / national / international formatting; a number
valid in one region but not another (`IsValidNumberForRegion`); empty input →
`"Number is empty."`; unparseable input → `NumberParseException` message
surfaced in `ErrorMessage`; `GetNumberKind` mapping for mobile / fixed-line /
unknown; the `TryFormat` null-on-invalid path.

### 2.5 `Services/CacheService`

Construct with `new MemoryCache(new MemoryCacheOptions())`. Cover: factory runs
once and the value is cached; `Remove`/`RemoveAsync` evicts; `Clear`/`ClearAsync`
compacts; expiry honours the passed `TimeSpan` and otherwise the 30-minute
default.

Worth pinning deliberately: `GetOrSetAsync` tests `cachedValue is not null`, so
a **cached `null` re-runs the factory every call**. That may be intended or may
be a caching hole; a test makes the choice explicit.

### 2.6 `Utilities/Converters.cs` + `Utilities/BoolToColorConverter.cs` + `Utilities/DaySelectionConverters.cs`

~24 small `IValueConverter` / `IMultiValueConverter` classes, all pure, all
one-or-two-assert tests. Low individual value, high aggregate value, very cheap
— good `[Theory]` fodder. Cover each `Convert`, each documented `ConvertBack`
(most throw `NotImplementedException`), and the wrong-type/null fallbacks.

One exclusion: the converter around `Utilities/Converters.cs:513` reads
`Application.Current!.Resources[...]` and will `NullReferenceException` in this
host. It belongs in tier 3.

### 2.7 `Extensions/MeetingExtensions.IsOnline`

Small but on the meeting-classification path. Cover: null → `ArgumentNullException`;
`IsOnline` true short-circuits; `Types` containing `"online"` in mixed case;
`Types` null/empty; a `Types` value that merely contains the substring (e.g.
`"onlinex"`) — currently matches, which is worth pinning either way.

### 2.8 `Utilities/MeetingCriteriaConverter` — **carries a probable bug**

`ConvertFrom` splits the string on `','` but `ConvertTo` joins with `'|'`, so a
round-trip through this `TypeConverter` does not survive. `ConvertFrom` also
indexes `parts[1]` unguarded, throwing `IndexOutOfRangeException` on any string
without a comma.

Write the tests to document current behaviour and flag both as defects rather
than silently "fixing" them here — the XAML that uses this converter needs
checking first to know which separator is correct.

### 2.9 Smaller pure targets

`Support/ExceptionEnricher` (inner-exception walk, `AggregateException`
flattening, the 40-line stack cap, the depth-10 cycle guard — driveable with a
Serilog in-memory sink); `Support/BetterStackDurable/BetterStackNdjsonBatchFormatter`
(NDJSON shape, one line per event, trailing newline); `Utilities/RegisterData`
(`Total*` counts, the null-coalescing constructor); `Support/TaskExtensions`
(exception is swallowed and `onException` invoked — synchronise on a
`TaskCompletionSource`, since `SafeFireAndForget` is `async void`;
`RunSafeFireAndForget` null guard).

Skip `Support/ServiceConstants` — asserting a constant equals itself tests
nothing.

---

## 3. Tier 2 — needs a real SQLite database

Follow the pattern already established in
`TheBleedingDeacons.Unity.Intergroup.Tests/RepositoryTests.cs`
(`Microsoft.EntityFrameworkCore.Sqlite`, already referenced by this project).

### 3.1 `Data/SqlitePragmaInterceptor`

Cheap and genuinely valuable — it protects against Android DB corruption, and
nothing else verifies it. Open a context through the interceptor and assert
`PRAGMA journal_mode` is `wal`, `foreign_keys` is `1`, `synchronous` is `1`
(NORMAL), and `busy_timeout` is set.

Use a **temp file** database, not `:memory:` — in-memory SQLite cannot enter WAL
mode and the assertion would be meaningless.

### 3.2 `Data/MailDbContext` + `Data/Configurations/QueuedEmailConfiguration`

Assert the model that `ApplyConfigurationsFromAssembly` produces: table name
`QueuedEmails`, max lengths, required columns, any indexes, and a full
round-trip of a `QueuedEmail` including the nullable `ReplyTo` / `Cc` / `Bcc`
and the `EmailStatus` enum mapping.

### 3.3 `Services/EmailService` — queue operations only

Constructible with an `IDbContextFactory<MailDbContext>` over SQLite. The
DB-only half of the surface needs no SMTP: `QueueEmailAsync`,
`GetQueuedEmailsAsync`, `GetQueuedEmailsByStatusAsync`, `GetQueueCountAsync`,
`ClearQueueAsync`, `ClearSentEmailsAsync`, `RetryFailedEmailsAsync` (resets
`AttemptCount`, clears `LastAttemptAt`, flips `Failed` → `Pending`),
`ResetRetryCountAsync` both overloads, and the `ResetCircuitBreaker` /
`IsCircuitOpen` / `CircuitStateChanged` state machine.

`SendEmailAsync` and `ProcessQueueAsync` reach real SMTP and are **out of scope**
for unit tests.

`IsRetryableException` is the highest-value logic in the file and is
`private static`. Make it `internal` plus `InternalsVisibleTo`, or exercise it
through `HandleSendFailureAsync`.

### 3.4 `Services/SnapshotService`

`CaptureAsync` wipes and rewrites `EntitySnapshots` and is the input to the
whole reconcile diff. Cover: capture counts per entity type match seeded data;
a second capture replaces rather than appends; `ReferenceHandler.IgnoreCycles`
survives entities with navigation cycles.

### 3.5 `Services/ReconciliationService` — partial, and only after tier 3

The change-detection diff is the most consequential logic in the app (a false
negative silently drops a registration). But the constructor needs seven
collaborators, including `RegistrationEventLog` and `ComplianceEventLog`, which
are blocked on `FileSystem` — so this is gated on §4. Once unblocked, the
detect-phase diff (including the `__gdpr_compliance__` sentinel and the
negative-temp-ID creation ordering) deserves the most thorough tests in the
suite. The API-push phase needs `UnityRestSharp` faked behind
`Func<Task<UnityRestSharp>>`.

---

## 4. Tier 3 — MAUI statics — **done**

This was the largest untested surface in the app. It is now unblocked and
covered by 100 tests.

### 4.1 What changed in the app

MAUI ships interfaces for its platform services and the statics simply delegate
to them, so injecting them changes no runtime behaviour:

| Target | Change |
| --- | --- |
| `Services/ConfigurationService` (1017 lines) | Constructor now takes `IPreferences`, `IFileSystem`, `ISecureStorage`, `IDeviceInfo`. |
| `Services/PrivacyPolicyCache` | Constructor now takes `IPreferences`. |
| `Support/TemporaryIdGenerator` | Static class → sealed instance class behind `ITemporaryIdGenerator`, taking `IPreferences`. Registered as a singleton, so the one-counter-per-device guarantee is unchanged. `IsTemporary` stays static — it is a pure predicate. |
| `ViewModels/EditGroupViewModel`, `ViewModels/PositionEditViewModel` | Take `ITemporaryIdGenerator` instead of calling the static. |
| `MauiProgram` | Registers the four MAUI platform services and `ITemporaryIdGenerator`. |
| App `.csproj` | `InternalsVisibleTo` the test project. |

The two event logs needed **no** production change: both already carried an
`internal` constructor taking an explicit log path, added for non-MAUI hosts.
`InternalsVisibleTo` was all that was missing — the earlier estimate that they
needed `IFileSystem` injection was wrong.

The test project also forces `UseDevCredentials=false` onto the app reference.
Without it, `USE_DEV_CREDENTIALS` makes `ConfigurationService` short-circuit to
an embedded `devsettings.json` and skip the Preferences/SecureStorage paths
entirely — the tests would have covered the wrong branches and depended on a
git-ignored file.

### 4.2 Coverage added

| Target | Tests | Focus |
| --- | --- | --- |
| `ConfigurationService` | 57 | Every toggle's default-on/default-off policy, unparseable values, and prefs-unavailable fail-safes; device-label synthesis across platforms; active-meeting handling; SMTP/Unity/Better Stack round-trips, including that secrets reach SecureStorage and never the settings files. |
| `RegistrationEventLog` | 12 | Append/read/purge, upsert-by-entity, torn-final-line tolerance, survival across instances, concurrent appends. |
| `PrivacyPolicyCache` | 11 | Round-trip, corrupt-blob and read-failure recovery, single-write atomicity. |
| `TemporaryIdGenerator` | 13 | Countdown from −1, persistence per call, resume-after-restart, positive-seed guard, reset, prefs-failure degradation. |
| `ComplianceEventLog` | 7 | Acceptance/revocation audit fields, supersede semantics, torn lines, purge. |

Fakes live in `Fakes/`: `FakePreferences`, `FakeSecureStorage` (both with a
`FailWith` switch so the "platform store is broken" branches are reachable),
`TempFileSystem`, and `FakeDeviceInfo`.

### 4.3 Two defects found, pinned but **not** fixed

Both are covered by tests that assert current behaviour, so a fix will fail the
test and force a deliberate decision. Fixing them changes app behaviour, which
is outside "make this testable".

1. **`ConfigurationService.LoadSmtpConfigurationAsync` cannot see a save from
   the same session.** The `IConfiguration` it binds from is built once in the
   constructor, so a `mailsettings.json` written later is never read. Worse,
   `Load` overwrites the cache that `Save` had just populated correctly — so
   save-then-reload (what the Settings page does) leaves the service serving the
   *pre-save* host with the *post-save* password. Correct values return after an
   app restart. Test:
   `SmtpConfiguration_ReloadingOnTheSameInstanceLosesTheJustSavedValues`.

2. **`IsComplianceAcceptanceEmailEnabled` is inconsistent with every other
   toggle.** Unset means enabled, but an unparseable value or a broken prefs
   store means *disabled*; the other default-on toggles stay on in both cases.
   The property's own XML doc contradicts itself — it says the default is true,
   then explains that fresh installs "do not send ... until an operator opts
   in". Someone needs to decide which was intended. Test:
   `ComplianceAcceptanceEmail_DisagreesWithItsSiblingsOnTheFailurePaths`.

### 4.4 Still blocked

| Target | Blocker |
| --- | --- |
| `Services/BetterStackLoggerController` | MAUI statics |
| `Services/PopupNotificationService` | UI |
| `Support/AppLogger` | `FileSystem` |
| `HasGsrToColorConverter` | Reads `Application.Current.Resources`, which is null with no running app |
| `StringToBoolConverter`, `CountToBoolConverter` | Derive from CommunityToolkit's `BaseConverterOneWay`, whose **constructor** calls `DispatcherProvider.GetForCurrentThread()` and throws `REGDB_E_CLASSNOTREG` in a console host. They cannot be instantiated here at all — no amount of test-side work reaches them. |
| `RegistrationEventLog.ReplayIntoDatabaseAsync` / `ComplianceEventLog` replay | Not blocked — needs a seeded `UnityDbContext`, so it belongs with tier 2 §3 |

ViewModels are a separate question. `BaseViewModel` itself is clean
(`ObservableObject` + a `CancellationTokenSource`), but the concrete ViewModels
navigate through `Shell` and marshal via `MainThread`. They are worth testing
only after the services beneath them are covered.

---

## 5. Out of scope

Views and XAML, `Platforms/*`, `App`/`AppShell`, and `MauiProgram` DI wiring.
These need a running MAUI app; if that coverage is ever wanted, the tool is a
MAUI device-test / UI-test project, not this one.

---

## 6. CI — **done**

`.github/workflows/ci.yml` gained a `test-register` job on `windows-latest`
(this suite cannot run on the Ubuntu `test` job — it needs the Windows head and
the `maui-windows` workload) plus a `coverage` job that closes the parallel
Coveralls build.

Two details are easy to get wrong and are commented in the workflow:

- **`-p:RegisterWindowsOnly=true` must be passed on the command line**, not left
  to the test project's `ProjectReference`. `AdditionalProperties` applies to
  the build but **not to restore** — restore would still walk all four of the
  app's target frameworks and demand the Android and Apple workloads on the
  Windows runner. A global property narrows both, and implicit restore inherits
  it, so one `dotnet build` does the job. (`UseDevCredentials=false` only
  affects compilation, so the csproj setting is sufficient for that one.)
- **Both coverage uploads are flagged and marked `parallel: true`**
  (`unity-intergroup` and `register`), with a `coverage` job posting
  `parallel-finished`. Without this the second upload replaces the first
  instead of adding to it. That job runs under `if: always()` so a failing
  suite still closes the build rather than leaving Coveralls pending.

The Register report is written to `coverage/coverage.register.cobertura.xml` —
the leading `coverage` matters, because `.gitignore` ignores `coverage*.xml` by
filename, not the directory.

`--exclude-by-file "**/obj/**"` drops the XAML plumbing the MAUI build
generates. `XamlTypeInfo.g.cs` alone is ~1,600 lines that no test can execute
and nobody can act on; left in, it was 15% of the reported uncovered total and
made the percentage describe the code generator rather than the code we write.

Current app-assembly line coverage is **14.3%** (branch 16.7%, method 20.7%),
measured across all remaining hand-written code including the Views, Controls
and ViewModels that no tier has reached yet.

---

## 7. Remaining order

Tier 1 (§2), tier 3 (§4) and the CI job (§6) are done. What is left, in order:

1. **Decide on the four pinned defects** — §4.3 (SMTP reload, compliance-email
   toggle) and §2.8 (`MeetingCriteriaConverter` round trip and unguarded
   index). All four are behaviour choices needing product context, and all four
   have a test ready to flip. None should sit unresolved indefinitely.
2. Tier 2 §3.1–§3.3 — pragmas, mail model, queue operations.
3. Tier 2 §3.4–§3.5 plus the event-log replay from §4.4 — snapshot, replay,
   then the reconcile diff.
4. **ViewModels** — 3,645 lines at 0%, now the single largest block at 44% of
   all uncovered code. They look hopeless but the coupling is shallow: about 90
   call sites, dominated by `Shell.Current.GoToAsync` (29),
   `Shell.Current.DisplayAlert` / `DisplayAlert` (26) and
   `MainThread.BeginInvokeOnMainThread` / `InvokeOnMainThreadAsync` (22). Three
   seams — an `INavigationService`, an extension of the **existing**
   `IPopupNotification`, and MAUI's `IDispatcher` — cover 77 of them.

   `ApiSettingsViewModel` (446 lines) and `MailSettingsViewModel` (234) take
   nothing but interfaces and need no refactor at all, but both call
   `LoadConfigurationAsync().SafeFireAndForget(...)` **in the constructor**, so
   tests would race it. They need an awaitable initialise first.

### Realistic ceiling

Views, Controls, Platforms and `MauiProgram` are ~1,610 lines (16.6%) that need
a running MAUI app; no console-hosted test will ever reach them. That puts the
absolute ceiling near 83%, and **55–60% is the realistic target** for this
harness. Anything beyond needs a MAUI device-test project.
