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
bearer token and NDJSON content-type, and a batch formatter that emits
one JSON event per line.

Serilog's `SelfLog` is enabled in `ReconfigureSerilogWithBetterStack`
so any sink errors (bad endpoint, revoked token, permission issues on
the buffer directory) surface in the Debug output without additional
instrumentation.
