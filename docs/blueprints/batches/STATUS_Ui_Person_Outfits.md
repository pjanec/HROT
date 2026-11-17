<!--STATUS
state: LIVE — building
updated: 2026-11-17
current-answer: the step table + the lane decisions
related-designs:
  - ../../designs/person-looks/DESIGN_Person_Outfits.md — the design (OF-4 / OF-5 garments); the mockups sit beside it
  - ../../DESIGN_Map_3D_Mode.md — §6q the block figure's outfits (updated to the as-built at the end)
-->
# STATUS — ui: PERSON OUTFITS, S2 the garments (CE-4648)

Handoff: [`HANDOFF_Coordinator_To_Ui_Person_Outfits.md`](HANDOFF_Coordinator_To_Ui_Person_Outfits.md). Started at `91171cabc` (merged coordinator@34d17c34c, backend@4073e0a90).

| # | step | state |
|---|---|---|
| 1 | mockups (13 garments, front + side, `docs/designs/person-looks/person-outfits-*.svg`, generator `scripts/gen-person-outfits-mockup.py`) | ✅ approved by the coordinator (checkpoint 1, two rounds) |
| 2 | the real drawing in `Outfits.Details` + the Native American enum members | ⏳ |
| 3 | thumbnails of the new types backend registered | ⏳ |
| 4 | fold the as-built into the design + Map 3D §6q; CAPABILITIES | ⏳ |

## Lane decisions (for the user's review)

| id | decision | what would change it |
|---|---|---|
| R-1062 | the Native American garment kinds are added to the outfit enums by ui (`OutfitTop.Buckskin`, `OutfitLegs.Breechcloth`, `OutfitHead.Headband`, hair `Braids`) — backend's catalogue rows name them later | backend declaring different member names |
| R-1063 | FACE PAINT lives in the BEARD slot (`BeardKind.Paint`) — adding a slot would add a draw and reshuffle every existing person; the stripe colours come from a hash of the seed, no extra draw | a wish for authored paint colours per type ⇒ a new slot and a re-baselined fixture |
