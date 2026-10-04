# Register domain model: who was at the meeting, and who agreed to be recorded

The living specification is the set of Reqnroll `.feature` files under
[`TheBleedingDeacons.Intergroup.Register.Specs/Features`](../TheBleedingDeacons.Intergroup.Register.Specs/Features).
This document is the overview and glossary behind them. It covers what the
words mean, which decisions are settled, and which are still assumptions.

It was written by reading the code, not the other way round. The app came
first, and the specification was reverse-engineered from it. Where this
document and the feature files disagree, the feature files are what runs,
and this document is what needs correcting.

## The core idea

A tablet on the door of an intergroup meeting records which groups and
officers attended. It also records each person's acceptance of the privacy
policy. When the meeting ends, it tells Unity, without anyone typing the
attendance in twice.

Everything else qualifies that sentence:

- **When** Unity is told: at the end, not as people arrive.
- **What** survives a tablet that crashes in the middle.
- **Who** may be registered at all.

```
sync ─▶ snapshot ─▶ register / accept (local DB + fsync'd log) ─▶ finish ─▶ diff against snapshot ─▶ Unity
                         │                                                        │
                         └── welcome / consent email queued ──▶ queue ──▶ SMTP    └── logs cleared, unless refused
```

## Ubiquitous language

- **Sync**: fetching every group, position, member and intergroup meeting
  from Unity, page by page, then *replacing* what the tablet held, in one
  transaction. Nothing is merged.
- **Snapshot**: the tablet's records at the start of the meeting, serialised
  row by row. Finishing the meeting sends Unity only what differs from it.
- **Register**: mark a group or position as attending in the tablet's own
  database. Nothing is sent to Unity yet.
- **Cascade**: registering a group also registers every position held by one
  of its members, when the tablet is set to. It only ever adds: unregistering
  the group leaves its officers where they were.
- **Stand-in**: someone attending in place of the GSR, recorded by name.
- **Contactable**: a holder with an anonymous name, and a mobile number or a
  personal email. Either one is enough.
- **Consent / acceptance**: a member's agreement to a version of the privacy
  policy. It records the version, the policy's id, its wording and the method
  (`register-app`).
- **Needing consent**: never accepted, or accepted a version *different* from
  the cached one. Different, not older: versions are free-form text, so they
  cannot be ordered.
- **Consent round**: asking each member who needs consent, in turn and by
  name. It stops at the first refusal. Each acceptance is recorded as it is
  given.
- **Registration log / compliance log**: append-only files, fsync'd before
  the volunteer sees "Registered". They hold the latest state per group or
  position, and per member. A replay rebuilds the database from them.
- **Temporary id**: a negative id given to a member added at the meeting.
  Unity has never seen it, and replaces it on creation.
- **Reconcile / finishing the meeting**: sending Unity everything that differs
  from the snapshot. The order is: new members, changed members, consent,
  then attendance.
- **Error / warning**: a refusal during reconcile. Only *errors* keep the logs
  for another attempt; see the findings below.
- **Managed value**: a setting held by the site's Freedom plugin. For
  credentials and endpoints it is the only value there is: nothing is built
  in and nothing is typed in on the tablet.

## Local first, Unity last

Registering touches nothing remote. The volunteer's "Registered" countdown
depends on only two things: a SQLite write, and a log line flushed to disk.
Unity hears about it when the meeting is finished, and receives only what
changed since the snapshot. A new member is created first, so that the
registrations that follow can use the id Unity gives them.

If Unity refuses to create a member, the registrations that depend on that
member are held back. They are not sent under the negative id, which Unity
would reject. The logs are kept, so the next attempt retries both.
`Reconciliation.feature` holds the detail.

## The two gates in front of Yes

**Contact first.** At least one holder must be contactable. If someone is
standing in for the GSR, they must give a name. Whitespace counts as present
here: the check is `IsNullOrEmpty`. The gate was moved into Register.Core
unchanged, and the behaviour is pinned in Register.Tests.

**Consent second.** Everyone who needs consent is asked before anything is
written. One refusal aborts the registration, but the acceptances given
before it stand: those people did agree. The round refuses to start if there
is no cached policy, or if the policy has no wording, because an acceptance
of nothing would corrupt the audit trail. Officers who come with a group,
possibly from another group, are found by reading the database, so anyone
who accepted moments earlier is already excluded.

## What survives a crash

The database is the record. Each log is defence in depth, written only after
the database write has succeeded, so a logged registration can never be one
that was not applied. After a crash, a replay rebuilds the `Registered` flags
and the stand-in from the log. The last line written for a group is the one
that counts. A group the last sync removed is skipped, not resurrected.

## Email is queued, never awaited

Welcome and consent emails are queued, so the volunteer never waits on SMTP.
Nobody without an email address is emailed, and that is normal. The queue
tries each email a limited number of times before marking it failed. While
offline it does not run at all.

Three background runs in a row that fail for the tablet's own reasons, such
as a refused password or a TLS mismatch, pause background sending until the
settings change or someone resumes it. A mail server the tablet cannot reach
never counts: that is the venue's network, and it comes back on its own.

## Findings: behaviour pinned rather than changed

These were found while writing the specification. Each is pinned by a
scenario or a test so that changing it is a decision. None was fixed here.

- **A consent record Unity refuses is a warning, and the logs are cleared
  anyway.** The same is true of a refused member update. Only errors keep the
  logs. An acceptance Unity never stored is gone from the tablet after Finish
  Meeting and Purge. (`Reconciliation.feature`)
- **With no intergroup meeting chosen, attendance is not sent, and the logs
  are cleared anyway.** Reconcile logs "skipping registration push" and counts
  no error. The app's meeting phases probably prevent finishing without a
  chosen meeting. That has not been checked, so it is listed under the
  assumptions below. (`Reconciliation.feature`)
- **The welcome-email dedupe cannot be reached.** Recipients are drawn from
  `group.Members`, which holds each member once, and a `Where` over distinct
  members cannot produce a duplicate. Removing the `GroupBy` fails nothing.
  It is harmless, but its rationale does not hold, the same situation as the
  read-flag branch Link's specification recorded.
- Also pinned in Register.Tests, not here:
  - `EmailValidator` accepts a space in the local part.
  - A missing template placeholder renders as nothing.
  - An empty string from Freedom is taken as the value, not as "not set".
  - The registration log writes `EntityKind` as a number.
  - `FreedomSettings.OptionsFrom` reads `/amber` as `file:///amber` on Linux
    and Android.

Fixed since:

- **A refused password never tripped the circuit breaker** (register#53).
  The batch connects once, and a refused password threw before any email
  was tried. `ProcessQueueAsync` caught it and returned `false`, which is how
  an offline run reports itself, so the background loop read it as "offline"
  and its classifier never saw it. The background now runs an inner pass
  that throws a failure of the batch itself, and the classifier counts auth
  and TLS failures while leaving an unreachable server uncounted.
  `ProcessQueueAsync` still returns `false` and never throws, for the Email
  Status page. The email itself is still left untried, on purpose.
  (`EmailQueue.feature`)

## Locked design decisions

- **Registering never waits on the network.** Not on Unity, not on SMTP. A
  queue at the door is worse than a late email.
- **The database before the log, the log before the email.** If a write
  fails, everything after it is skipped, so no layer ever claims more than
  the layer beneath it holds.
- **Reconcile sends only differences from the snapshot**, and only the fields
  that changed. A member update carries the changed field and nothing else.
- **Consent is asked by name, one person at a time, and recorded as given.**
  A batch acceptance with one shared timestamp would misstate the audit
  trail.
- **The cascade only adds.** An officer may be present in their own right,
  and an unregistration that silently removed them would erase intent the
  tablet cannot reconstruct.
- **Credentials and endpoints come from Freedom and nowhere else.** The build
  names only which Freedom site to ask. A Debug build that embedded the live
  site's settings, and a tablet whose typed-in values outlived the site's,
  were both ways of being quietly pointed somewhere else.
- **Better Stack is https or nothing.** The source token is a bearer
  credential (register#47).

## Assumptions still open to change

- Finishing a meeting with no intergroup meeting chosen is assumed to be
  impossible through the app's phases. Core does not enforce it.
- Warnings, as opposed to errors, are assumed not to need a retry. For a
  consent record that is a judgement worth revisiting.
- The cascade reads membership from the database, but welcome-email
  recipients come from the group the Verify page loaded. The two agree today
  because the page loads the group fresh.
- Only the background timer counts towards the circuit breaker. A run
  started from the Email Status page that fails is reported there and not
  counted, so pressing Process Queue repeatedly never pauses anything.
- A timeout reaching the mail server is assumed to be the network, and is
  not counted. A server that always times out would therefore never pause
  background sending.
