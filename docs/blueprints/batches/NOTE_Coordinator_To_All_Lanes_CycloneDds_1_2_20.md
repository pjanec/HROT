<!--STATUS
state: LIVE — coordinator → every lane: what changes when you merge coordinator at or after df04652e6
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../reference/NED_DDS_Protocol.md — the wire as it is now (derived topic names, 125 types)
  - ../Architect_Question_107_NED_Wire_Frame_And_Names.md — the design (A: derived names; D: the WorldPos quaternion)
-->
# NOTE — coordinator → all lanes: CycloneDDS.NET 1.2.20, derived topic names, BDC gone

**Dispatched at** the commit that adds this file (`coordinator`). FYI, with nothing to build. Merging `coordinator` brings the following.

| what | what you do |
|---|---|
| ⭐⭐ **CycloneDDS.NET `0.3.2` → `1.2.20`** (nuget.org; `CycloneDdsNetVersion` in `Directory.Build.props`) | ⛔ **`dotnet restore` your test projects once after the merge.** A `--no-restore` build on the old assets keeps 0.3.2 in `bin` and fails in confusing ways (CLAUDE.md, third stale-binary trap) |
| ⭐⭐ **Every DDS topic name is derived from its C# type** (`Hrot.NED.Descriptors.WorldPos` → topic `Hrot_NED_Descriptors_WorldPos`; AQ107 A) | ⛔ Don't write a name in `[DdsTopic(...)]` and don't pass a topic name to a reader or writer. `scripts/ned-wire-inventory.py --check` fails on one. ⚠ **Renaming a namespace or type is now a wire break** |
| ⚠ **Mixed builds don't talk.** A node built before this commit uses the old topic names | Rebuild every node (and ddsmonitor's `TopicSources`) from the same tree |
| **The BDC network stack is removed** (R-4095), and with it the runner's `--network` option | Don't pass `--network`; it's now rejected. NED is the only network implementation |
| **`WorldPos.OrientationXyz` is an `OrientationQuat`** (same bytes) | ddsmonitor shows `HPR [hdg, pitch, roll] deg, Quat [x, y, z, w]`; JSON stays `{X, Y, Z}` |
| **ddsmonitor 1.2.20** (nuget.org) reads the SUMO 64 KB topics and has headless options for scripts and agents | `dotnet tool update -g CycloneDDS.NET.DdsMonitor`; runbook §5a (`docs/RUNBOOK_Cluster_Debugging_Over_Http.md`) has the recipe |

_coordinator lane, 2026-10-28_
