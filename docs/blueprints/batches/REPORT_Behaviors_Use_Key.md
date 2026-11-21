<!--STATUS
state: LIVE — the behaviors lane's report on HANDOFF_Use_Key_To_Behaviors.md (CE-1154, CE-1155, CE-1157).
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Use_Key_To_Behaviors.md — the dispatch this answers (ui, at 2dfec7a37).
  - docs/blueprints/batches/HANDOFF_Backend_To_Behaviors_Use_Key_Ready.md — backend's note (the engine half at 4439b8aba).
  - docs/DESIGN_Entity_Control.md — §8h.8, the as-built folded here.
-->

# REPORT — behaviors → ui + backend: the use key's behaviors half (`CE-1154`, `CE-1155`, `CE-1157`)

**Built on** `behaviors` after merging `backend@4f33aadf7` (contains the engine half `4439b8aba`) and `ui@168655517` (contains the
dispatch `2dfec7a37`); started marker `b5e2b97d5` (at `82842f755`). Rows: [`CE-1154`, `CE-1155`,
`CE-1157`](../Blueprint_Issues_Tracker.md) — built. No new ids allocated (the rows are ui ids, owned by behaviors for the batch).

## For the ui lane (`CE-1158`)

⭐ **Build the station against `behaviors` at the commit that carries this report** (or later). What the brain now does with your frame:

| you send | the brain does | you read back (`OperatorUseStatus`, `dtOperatorUseStatus`) |
|---|---|---|
| `UseSeq` +1, `UseVerb` = index in `OperatorUseActions.For`'s list, `UseTarget` = the crosshair's network id (0 = nothing) | the SAME rule on its replicated copy; the action at that index on the interaction channel | `UseSeq` = your press · `Running` at the start · `Done` or `Refused` + `Reason` at the end |
| a press while the last one still runs | replaces it (the executor starts over) | the new press's `UseSeq` |
| `UseVerb` past the list | nothing | `Refused` + `NothingToDo` |
| the man aboard (Drive / Man done) | the rule offers only *Get out* — any `UseTarget` | — |
| `Mount` = 32+k (a controlled weapon) | fires THAT weapon, with ITS fire mode, per-press rule and ammo (CE-1155) | — |
| frames that restart after a ride (Seq 1, counters from 0) | read as a new station: the counters reset, the first press is heard (CE-1157) | — |

⚠ While the man is `Riding`, the possession takes his frames — your hand-over (possess the vehicle, then Fps on the man again for *Get
out*) is yours; the brain's part ends at "he is aboard" and resumes at the next frame.

## For the backend lane

⭐ **The live IG check `CE-1153` left owed is done**: `EntityOrdersAcrossNodesTests.CE1154_…` asserts `OperatorUseStatus` reaches the
pure IG (and SimHost) with `Outcome = Done` after a press on the SimHost station.

## What was built — and the two gaps found

[Entity Control §8h.8](../../DESIGN_Entity_Control.md) has the as-built table. In short: `OperatorControlBehavior.OperateUse` (in the
one `ManualControl` node, after `OperateWeapon`) — no new executor, no new channel; channel start / hand-back through the carry and
embark nodes' helpers.

| found | how | fix |
|---|---|---|
| 📐 `CE-1155` — a controlled mount's index was read ON THE MAN in `OperateWeapon` (`CombatTkb.MountOf`, `WeaponMountQuery.MountEntity`) | reading the path before the rail; the rail then reds without the fix | `OperatedMount`: a controlled index ⇒ the weapon and its mount 0 (the executor's own rule) |
| 🔴 `CE-1157` — a rider's frameless ticks zeroed `ws.Seq`, so a station that restarts its frames after the ride lost its first press | writing the rail (the planned red-proof did not red; looking for what could, this did) | keep the last frame number when no frame arrives |

## Gates

| gate | command | result | `--no-build` |
|---|---|---|---|
| T-1 the feature suites (interaction nodes + possession) | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter "FullyQualifiedName~EmbarkNodesTests\|FullyQualifiedName~Possession"` | 26 / 0 (7 new) | yes, after building the TEST project |
| red-proofs | compiling mutants, each reverted: `OperateUse` not called · `Available` not checked · params not written · no stale-Seq reset · `OperatedMount` returning the man · the old `ws.Seq` zeroing | each its rail RED (6 of 6) | built per mutant |
| ⚠ not red-proved alone | `CE1157_UseOnAVehicle_DrivesIt_…` pins a composition of already-built parts (the rule's Drive / Get out, the possession taking frames); its claim is guarded by the parts' own rails | — | — |
| cross-node (row 8) | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --no-build --filter "FullyQualifiedName~EntityOrdersAcrossNodesTests.CE1154\|FullyQualifiedName~EntityOrdersAcrossNodesTests.CE3172"` | 3 / 0 — `CE1154_AUsePressOnTheStation_…` (+ the IG verdict), `CE3172_…` possession, `M7_…` drone — re-run on the final code (after CE-1155 / CE-1157) | built (the TEST project) |
| SimHost suite | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build` | 1505 / 0 / 6 skipped (the 6 are the existing quarantine) | yes |
| tracker / rulings / design digest | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` | tracker OK · 255/255 rulings · design digest OK | — |

The tree is clean after every run (no golden moved). Ids allocated: none.
