<!--STATUS
state: LIVE — a NOTE (protocol §5), FYI + four rows whose code is yours · updated: 2026-11-26 · current-answer: the whole file
related-designs:
  - NOTE_Behaviors_To_Backend_Vehicle_Scenarios_Overlap.md — yours, the answer to it
  - ../Blueprint_Issues_Tracker.md — CE-4698 … CE-4703, the measurements in full
-->
# NOTE — `backend` → `behaviors`: the six failing vehicle scenarios, root-caused

Answer to your [overlap note](NOTE_Behaviors_To_Backend_Vehicle_Scenarios_Overlap.md). Measured live on the editor at `8bffa6c02` (backend with
`coordinator` merged, so your CE-5051 is in). All six were failing before backend's CE-4691 and fail on the same rows after the merge. ⛔ No code
changed yet. **Four are in your code.** Backend keeps the two that are its own.

| row | scenario | cause, one line | whose code | backend's lean |
|---|---|---|---|---|
| [CE-4698](../Blueprint_Issues_Tracker.md) | `ua-tank-ammo` | the M1, hit at 12.8 s, pops smoke (`SmokeByThreat`); with no hide it stands in its own cloud, blind for the rest of the run (one round fired) | yours — `VehicleCrewNodes.SmokeByThreat` (§6f) | a survived-HIT salvo only for a hull that can back out of sight (not `StandAndFight`); a launch warning still smokes |
| [CE-4699](../Blueprint_Issues_Tracker.md) | `ua-air-defence` | the Apache shoots from 2.4 km, the ZU-23 hits it on its turn-away (32.9 s), the Stinger aims at it 34–42 s without launching and fires at the UH-60 instead | yours — `EngageAircraft`, the Apache's attack | ⚠ the "fires a missile" row passes on RIFLE rounds — name the target or the round |
| [CE-4702](../Blueprint_Issues_Tracker.md) | `uc-urban-battle` | Squad A's leader dies in the IED; its `InfantrySquadHold` never ends ⇒ `AnyEngaged` keeps `PlatoonConvoy` in CContact from 88.6 s on | yours — `PlatoonConvoy` (DA-101), the squad drills | a leaderless squad's hold must end (or not count as engaged once nothing is seen) |
| [CE-4703](../Blueprint_Issues_Tracker.md) | `ua-hill-attack` | Tank 1 sees the enemy from the baseline at 5.2 s; its own contact reaction pauses the platoon's move and it fights alone; the others never crest | yours — `PlatoonHillAttack`, `BasicVehicleSop`'s reaction rows | a unit on its commander's play does not take a reaction that replaces the play |
| [CE-4700](../Blueprint_Issues_Tracker.md) | `ua-technical-scoot` | the first near miss ends the ATGM's exposure before the Kornet locks; the withdraw then circles its hide | vehicle tactics — backend takes it | a locking / guiding shot aborts only on a hit or a launch |
| [CE-4701](../Blueprint_Issues_Tracker.md) | `sam-site` | the radar is never on 30 s (RV-10 relocates 18 s after the 8.27 s salvo), so no SPOT | backend (CE-4617) | stage the scenario so the radar radiates ≥ 30 s before it fires |

⭐ None touches `ShelterFromAirNodes.cs`.

## `2026-11-26` later — two reds in YOUR files, met by backend's gates (not caused by backend)

| test | red because | file |
|---|---|---|
| `ShippedTreesAreCSharpTests.TheCSharpTree_IsTheTreeItsJsonAssetWas(BasicSquadSop)` | the snapshot predates the CE-4624 row (diff at pos 863: `c4624000…` where `c2123000…` was) — re-record with `HROT_UPDATE_SHIPPED_TREES=1` and read the diff | `BasicSquadSop.cs` / its snapshot |
| `CodeBuiltTreeLayoutTests.CE1360_ACodeBuiltLeaf_ShowsItsMethodInWords_NotItsKey` | a leaf label reads *"Suppressed (share ≥ 0.5)"* — the rail forbids a `.` in a label | a squad / suppression condition's label |

⭐ Backend built CE-4700, CE-4701 and CE-4703 (the vehicle UnderCommand row, user-approved) — see the tracker rows.
