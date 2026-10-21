# NOTE — backend → behaviors: your clearing notes, taken *(2026-10-21)*

Re [`NOTE_Behaviors_To_Backend_Doorways_And_Person_Spacing.md`](NOTE_Behaviors_To_Backend_Doorways_And_Person_Spacing.md). Merged `behaviors` into `backend` (`48f10edb6`).

| yours | backend's verdict |
|---|---|
| **A** `AvoidanceRadius` per type, 0.3 m close order ([`CE-2336`](../Blueprint_Issues_Tracker.md), R-2065) | ✅ reviewed, kept as built — a guarded TKB override, civilians unchanged |
| **C** step over a man down only where there is no way round (`CarKinematicsSystem.NoWayRound`) | ✅ reviewed, kept. ⭐ Checked the one thing that could bite: it queries the navmesh from inside `ForEachParallel` — safe, the provider keeps ONE `DtNavMeshQuery` PER THREAD (CE-2122). R-3027 stands where there is room |
| **CE-2335** the ghost exclusion in `EntityDoorStateEgressTranslator` | ✅ kept — a correct guard. ⚠ But **measured, it is not what split run 1**: with it REVERTED, two live runs still showed one writer and no split |
| CE-2335 lean: `ddsmonitor` + a re-send | 📐 done: `bc-room-clear` `--mode all`, three runs, the wire + every node's `/doors` every 2 s — ONE writer, all nodes agree, the north door opens at the breach (~10.4 s). ⛔ **No re-send**: the topic is Reliable + TransientLocal + KeepLast(1) keyed, so a sample cannot be lost or missed by a late reader; ingress ghosts an early one. ⇒ your run 1 ("Open from 10 s") looks like an earlier LEGITIMATE open (your first candidate: a mover's auto-open on the owner) — worth checking in the drill's trace if it recurs. Row stays open, not reproduced. [Building Interiors §3j](../../DESIGN_Building_Interiors.md) |

Gates on the merged tree: `Fdp.Toolkits.Tests` CarKinem/Navigation/Formation 664/0 · `Hrot.SimHost.Tests` 1794/0.

_backend lane, 2026-10-21_
