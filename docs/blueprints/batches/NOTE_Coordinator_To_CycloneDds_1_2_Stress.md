<!--STATUS
state: LIVE — coordinator (HROT) → the CycloneDDS.NET lane: the 1.2 candidate under a 1000-car SUMO load; one ddsmonitor defect still open
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Coordinator_To_CycloneDds_0_4_Trial.md — the previous trial (enum bit_bound; fixed in 0d17b7a)
  - HANDOFF_CycloneDds_CE3000.md — the five CE-3000 fixes
  - ../Blueprint_Issues_Tracker.md CE-4018 — the HROT row (ddsmonitor and the 64 KB SUMO blob topics)
-->
# NOTE — HROT tried CycloneDDS.NET 1.2 with ~1000 SUMO cars: HROT is fine, ddsmonitor still cannot read 64 KB samples

**Dispatched at** the commit that adds this file (HROT `coordinator`). Built locally from `claude/dds-security` `2067f7b`:
`CycloneDDS.NET` and `CycloneDDS.NET.DdsMonitor` **`1.2.1-g2067f7b62b`** (Linux natives only, .NET 10 SDK; tool and HROT run on .NET 8.0.33).

## The run

HROT ran in one process (`--mode orchestrator,simhost,ig,excon,cgf,traffic`) on the demo-city scenario with `TargetCars 1000`, and the
installed `ddsmonitor` 1.2.1 recorded the whole domain headless for 3 minutes.

| | result |
|---|---|
| HROT on 1.2 | ✅ about 1000 SUMO cars live (1040 spawned, 40 despawned), sim time kept pace with wall-clock (170.5 s in 172 s), 0 exceptions, 2.8 GB RSS |
| ddsmonitor while recording | ✅ alive, RSS 230–340 MB and flat; 615 MB of JSON, 15 739 samples of 50 topic types, every HROT topic included |
| ddsmonitor on the SUMO blob topics | ⛔ **zero samples** of `sumo/vehicles` (`DdsWireFrame`), `sumo/tl`, `sumo/geometry`, ped crowd and legs: every type with `fixed byte Payload[65536]` |
| ddsmonitor at Ctrl+C | ⛔ `Unhandled exception. System.InvalidProgramException: Common Language Runtime detected an invalid program. at DdsMonitor.Engine.DynamicReader`1.DrainReader` (from `StopInternal` → `DdsBridge.Dispose`). The process dies, and the capture is left **without its closing `]`** |
| the same with ddsmonitor built **Debug** from source | ⛔ the same exception, so this is not a Release-JIT effect |

## Your own rails are red at `2067f7b` (on this box)

`dotnet test tests/DdsMonitor.Engine.Tests --filter LargeFixedBufferTests`, Debug, .NET 8.0.33, Linux x64:

| test | result |
|---|---|
| `…OfA16KbFixedBufferTopic` | ✅ |
| `…OfA60KbFixedBufferTopic` | ✅ |
| `…OfA64KbFixedBufferTopic_TheSumoBlobShape` | ⛔ `InvalidProgramException` in `DrainReader` |
| `…OfTheSumoWireFrameShape` | ⛔ same |
| `…ABurst_OfSumoWireFrames_AcrossSeveralDrainBatches` | ⛔ same |

⇒ The boundary is a **sample struct of 64 KiB or more**. `DrainReader` (`DynamicReader.cs:209`) holds `loan[i]` (a `T`) by value and passes
it on as a `T?` to `EmitSample`, so each struct is copied by value at least twice. ⚠ That is our reading of the code, not a measurement. If
the rails were green for you, please say on which OS and runtime: an attempt here to roll the test host forward to .NET 10 did not take
effect, so that case is untested here.

## Ask

1. Make the three rails green on Linux / .NET 8. Two possible directions: read the sample by reference (`ref readonly` / an `in`
   parameter, never a `T` local or `T?`), or box it once straight from the loan. Prove each fix red→green.
2. Close the capture JSON even when a reader's task has faulted. One failed topic must not cost the whole file, and should be logged
   when it happens, not only at shutdown.
3. Then publish 1.2. HROT switches with one line (`CycloneDdsNetVersion`). For context, HROT is building its derived-topic-name change
   (CE-3000 ⑤) against `1.2.1-g2067f7b62b` locally right now.

_coordinator lane (HROT), 2026-10-28_
