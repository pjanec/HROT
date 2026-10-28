<!--STATUS
state: LIVE — coordinator (HROT) → the CycloneDDS.NET lane: ddsmonitor 1.2.6 (e2677da) verified under ~1000 SUMO cars; nothing open from HROT's side
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Coordinator_To_CycloneDds_1_2_4_Headless.md — the two leftovers this note confirms fixed
  - ../Blueprint_Issues_Tracker.md CE-4018 — the HROT row (stays open only until the tool is published)
-->
# NOTE — ddsmonitor 1.2.6 verified: both leftovers fixed, nothing open from HROT's side

**Dispatched at** the commit that adds this file (HROT `coordinator`). Local build of `claude/dds-security` `e2677da`
(`CycloneDDS.NET.DdsMonitor 1.2.6-ge2677da33e`, Linux, .NET 8.0.33). The setup is the same as in the 1.2.4 note: demo-city with
`TargetCars 1000`, one HROT process, and ddsmonitor instances recording at the same time.

| check | 1.2.4 | 1.2.6 |
|---|---|---|
| pipe mode, `RecordMaxSamples=50` met by durable history within 0.5 ms (3 runs) | exit 134 | ✅ exit 0, empty stderr, 50 lines each |
| JSON Lines order (14 260 lines, 3 rotated files) | in order | ✅ strictly in sequence |
| JSON-array order (16 564 samples, closed on SIGTERM) | ~88 swapped pairs | ✅ contiguous, in order |
| `SelfTest.*` topics in the summary | subscribed, 0 samples | ✅ absent |
| relative `--AppSettings:TopicSources:0=bin/Debug/net8.0` | not tried | ✅ every HROT and SUMO topic decoded |
| every `sumo/*` 64 KB blob (vehicles, tl, geometry, ped-crowd, ped-activity) | ✅ | ✅ each decodes to 65 536 bytes |
| `--help` / `--version` | — | ✅ |
| `DdsMonitor.Engine.Tests` (Linux, .NET 8) | 17 targeted, green | ✅ full suite 656/0 |

⏭ HROT's tracker row CE-4018 closes once the tool is published. The user has said "no publishing yet", so there is no ask here.

_coordinator lane (HROT), 2026-10-28_
