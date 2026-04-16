using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;
using Serilog.Sinks.Http;
using System.Collections.Generic;
using System.IO;

namespace TheBleedingDeacons.Intergroup.Register.Support.BetterStackDurable
{
    /// <summary>
    /// Writes a batch of <see cref="LogEvent"/>s as newline-delimited JSON (NDJSON).
    ///
    /// Better Stack's HTTP ingest API accepts a body of <c>application/x-ndjson</c> — one
    /// JSON object per line, no outer array. Each object can include a <c>dt</c> field
    /// carrying the original event timestamp, which is important for this app: events may
    /// sit in the on-disk buffer for hours (or days, on a phone that's been off a data
    /// signal) before they ship. Without <c>dt</c>, Better Stack would stamp them all with
    /// the eventual delivery time and we'd lose the real chronology of what happened at
    /// the meeting.
    /// </summary>
    public sealed class BetterStackNdjsonBatchFormatter : IBatchFormatter
    {
        private readonly ITextFormatter _eventFormatter;

        public BetterStackNdjsonBatchFormatter()
        {
            // JsonFormatter writes a Serilog event as a single-line JSON object with all
            // enriched properties flattened to top-level fields — exactly what Better Stack's
            // "string and JSON format" parser expects. renderMessage:true means the fully
            // substituted message text appears alongside the raw template, so Live Tail is
            // readable without having to re-render on ingest.
            _eventFormatter = new JsonFormatter(renderMessage: true);
        }

        public void Format(IEnumerable<string> logEvents, TextWriter output)
        {
            // When events are replayed from the durable buffer they arrive pre-rendered as
            // JSON strings — one per line. We just pass them through.
            if (logEvents == null) return;

            foreach (var logEvent in logEvents)
            {
                if (string.IsNullOrWhiteSpace(logEvent)) continue;

                output.WriteLine(logEvent);
            }
        }

        public void Format(IEnumerable<LogEvent> logEvents, ITextFormatter formatter, TextWriter output)
        {
            // Not used by Serilog.Sinks.Http ≥ 9.x — the sink always calls the string overload
            // above because events are persisted as JSON strings in the rolling buffer first.
            // Implemented for completeness against the interface contract.
            if (logEvents == null) return;

            foreach (var logEvent in logEvents)
            {
                _eventFormatter.Format(logEvent, output);
                // JsonFormatter.Format does not emit a trailing newline; NDJSON needs one.
                output.WriteLine();
            }
        }
    }
}
