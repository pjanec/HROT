<!--STATUS
state: LIVE — for terrain (MS-T4)
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — §3 MF-9 / MF-10, §8 MR-11 / MR-18, §11 the behaviours as built
  - NOTE_Terrain_To_All_Minefield_Contract.md — the contract this asks to extend
-->

# NOTE — behaviors → terrain: two pieces of the minefield contract `ClearLane` needs (MS-T4)

From the `behaviors` lane, CE-4624 MS-B3. ⭐ Nothing here is decided for you — it is what the brain calls today and the gap it leaves.

| # | the gap | what the brain does now | ⭐ a lean (yours to take or change) |
|---|---|---|---|
| 1 | **the clearer's EQUIPMENT SWITCH** (handoff MS-T4: *"with its equipment switch (the bulldozer's outlet pattern)"*) has no name in the NOTE | `ClearLaneBehavior` calls ONE seam, `Hrot.AI.Behaviors.Brains.ClearerEquipment.SetActive(world, clearer, on)` — tool ON at the lane's mouth, OFF at its end and when the order is cut off (the deactivator). ⚠ A stand-in: it returns false and switches nothing | an own-entity, unmanaged, recorded component on TKB 116 (e.g. `MineClearerTool { byte Active; byte Tool }`, an id from your block) that the clearer's OWN brain writes (a per-entity write, no command needed) and your serial swath clear reads. Tell me its name and I re-point the seam's one line |
| 2 | **`LaneCleared` carries no geometry** (`Field, Lane, SidePlus1`) — a lane cut by a clearer is NEW: there is no authored lane index to name | the brain raises nothing; it drives `Start → End` with the order's `Width` (4.5 m default) | your swath clear appends the lane to the field (`SetLane`, `ClearedBy` = the clearer's side + 1) from the swath it saw, and raises `LaneCleared` with the index it allotted — the brain never writes the field (MR-11) |

⭐ The line charge (MF-9's second tool, ~100 × 8 m at once) is NOT driven by the brain yet: it needs the launcher's mount on TKB 116 and the
munition 6053 — the same seam can grow `FireLineCharge(world, clearer, toward)` once the kit exists.
