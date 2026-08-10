# register
Intergroup Attendance Register

## Logging durability

Log events are shipped to Better Stack via Serilog. To survive loss of internet
and hard app kills, the pipeline is configured as follows:

1. **Local file sink** — writes every event to a rolling text file in
   `AppDataDirectory/logs/`. Independent of network state; always available
   for on-device diagnosis.

2. **Durable HTTP sink** (`Serilog.Sinks.Http`) — writes every event to a
   separate rolling buffer in `AppDataDirectory/logs/betterstack-buffer/`
   *synchronously* as part of the `Log.*` call. A background shipper
   then POSTs batches of NDJSON to Better Stack's ingest API with a
   `Bearer` token.

   * If the device is offline, the POST fails and the batch stays on disk.
   * If the process is killed (OOM, force-stop, reboot), the buffer
     survives and is replayed on next launch.
   * Retention is capped at ~128 MB (16 × 8 MB files); older events are
     dropped if the device is offline long enough to fill the buffer.

The glue between the sink and Better Stack lives in
`Support/BetterStackDurable/` — a custom `IHttpClient` that attaches the
bearer token and NDJSON content-type, a text formatter that writes each
event in Better Stack's ingest schema, and a batch formatter that frames
those events one per line.

### Event schema

Better Stack reserves three field names and treats everything else as
structured metadata, so events are serialised (by `BetterStackTextFormatter`,
matching Better Stack's own `BetterStack.Logs.Serilog` client) as:

```json
{"dt":"2026-08-10T19:04:31.1234567Z","level":"INFO","message":"...","messageTemplate":"...","exception":"...","properties":{"DeviceLabel":"...","AppVersion":"..."}}
```

`dt` matters here more than in a typical app: events can sit in the buffer
for hours before they ship, and an event without `dt` is stamped by Better
Stack with the time the *request* arrived — which would collapse a whole
meeting's worth of logs onto whenever the device next found signal. Enriched
properties are nested rather than hoisted to the top level, so query them as
`properties.DeviceLabel`.

Serilog's `SelfLog` is enabled in `ReconfigureSerilogWithBetterStack`
so any sink errors (bad endpoint, revoked token, permission issues on
the buffer directory) surface in the Debug output without additional
instrumentation.
