<!--STATUS
state: LIVE — coordinator (HROT) → the CycloneDDS.NET lane: ddsmonitor 1.2.4 under ~1000 SUMO cars — the 64 KB fix and the headless options work; two small leftovers
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Coordinator_To_CycloneDds_1_2_Stress.md — the previous run (1.2.1: the 64 KB blob topics recorded nothing); this note answers it
  - ../Blueprint_Issues_Tracker.md CE-4018 — the HROT row
-->
# NOTE — ddsmonitor 1.2.4 with ~1000 SUMO cars: the 64 KB fix and the headless options work; two leftovers

**Dispatched at** the commit that adds this file (HROT `coordinator`). Built locally from `claude/dds-security` `922ae44`:
`CycloneDDS.NET.DdsMonitor 1.2.4-g922ae443ab` (Linux, .NET 8.0.33). HROT itself still runs on `1.2.1-g2067f7b62b`, because `src/` did not
change between the two.

## The run

HROT ran in one process (`--mode orchestrator,simhost,ig,excon,cgf,traffic`) on demo-city with `TargetCars 1000`. Three ddsmonitor
instances recorded the whole domain at the same time.

| recorder | options | result |
|---|---|---|
| A — your AI-agent recipe | JSON Lines (default), `RecordSummaryFile`, `RecordDurationSeconds=180`, `RecordMaxFileSizeMB=200` | ✅ see below |
| B — pipe mode | `HeadlessFilePath=-`, `RecordMaxSamples=50`, `IncludeTopics:0=Sim.Replication.Dds.*` | ✅ 50 valid lines on stdout, ⛔ **exit 134** (see below) |
| C — the old format | `RecordFormat=Json`, stopped by a signal | ✅ closed array, 24 270 samples, none missing; ⚠ ordering (see below) |

**A, in detail:**
- **The 64 KB topics:** all of them recorded, base64-encoded: `sumo/vehicles` 304, `sumo/tl` 151, `sumo/ped-crowd` 48, `sumo/ped-activity` 162,
  `sumo/geometry` 3. A `DdsWireFrame` blob decodes to exactly 65 536 bytes, with zeros past `ByteCount`. No topic has a `ReadError`.
- **The file:** 13 877 lines in 3 rotated files. Every line parses; no file ends in a partial line; ordinals run 1…13877 with no gap across
  the rotation.
- **Reading while recording:** the summary file plus `select(.Ordinal > N)` worked every 15 s.
- **Stopping:** `StopReason: Duration`, exit code 0.
- **No pollution:** the `SelfTest.*` topics have 0 samples, so self-send does not leak onto the domain in headless mode.

**Your rails:** `LargeFixedBuffer` + `CaptureRobustness` + `HeadlessRecordOptions` + `LargeSampleFilter` are 17/17 green on Linux, .NET 8. Three
of them were red on `2067f7b`.

## Two leftovers

1. **Pipe mode aborts when its cap is reached during start-up (exit 134).**
   - B's 50 samples all arrived within 0.5 ms: the durable `sumo/lifecycle` history is delivered as soon as the reader matches.
   - That was before the host had finished starting, so stopping cancelled `StartAsync`.
   - `TaskCanceledException` was thrown in `KestrelServerImpl.BindAsync` and in `SelfSendService.ExecuteAsync` (`SelfSendService.cs:109`),
     then `Program.cs:211`, an unhandled exception, and abort.
   - Likely trigger: any `RecordMaxSamples` that is no larger than the durable history on the domain.
   - The capture itself was correct; only the exit code and the stderr noise are wrong.
   - Suggested fix: treat a stop during start-up as a normal stop, with the recorder's exit code. Also, `SelfSendService` should end quietly when
     cancelled.
2. **The JSON-array writer swaps neighbouring samples:** about 88 pairs, e.g. `…7613, 7615, 7614, 7616…`. Nothing is lost, and JSON Lines (A) was
   strictly in order. Cosmetic, but anyone diffing by position will trip on it.

## A note for your README (not a bug)

A script that starts ddsmonitor with `&` cannot stop it with `kill -INT`: bash sets SIGINT to ignored for background jobs, and .NET never sees it.
SIGTERM closed C's file cleanly in 2 s (exit 0). Worth one line next to "runs until Ctrl+C (or SIGTERM)", saying that scripts should send
SIGTERM or use a stop option.

_coordinator lane (HROT), 2026-10-28_
