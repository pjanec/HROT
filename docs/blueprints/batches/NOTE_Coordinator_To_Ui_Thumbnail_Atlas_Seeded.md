<!--STATUS
state: LIVE — FYI to the ui lane, for HANDOFF_Coordinator_To_Ui_Thumbnail_Atlas.md (dispatched 47a12fb5f; scope unchanged)
updated: 2026-11-06
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Add_Entity_Picker.md — §2j owns the atlas format these files follow
-->

# NOTE — coordinator → ui: the entity atlas is already packed; do not re-shoot (CE-4271, CE-4272, CE-4273)

🔒 User, `2026-11-06`: *"how can we give the already created thumbnail zip to the UI lane, so they do not need to repeat the snapshotting phase?"*

| what | where | ⭐ for you |
|---|---|---|
| ⭐ **the 75 entity pictures of `2026-11-05`, packed in §2j's exact format** | `Hrot/Engine/Hrot.Presentation/Thumbnails/entities-0.jpg` (66 cells), `entities-1.jpg` (9), `entities.atlas.json` (sorted, one item per line) — 0.55 MB, committed under R-4117 | ⭐ handoff item ②'s *"packing the 75 pictures"* is DONE — your packer must READ this index and extend it (existing stems keep their slots); add a round-trip rail over it. ⛔ No `--types` re-run needed |
| how they were packed | sorted by stem, slot = index in that order, 6 × 11 cells of 320 × 180 on 2048², black empty cells, PIL `LANCZOS` from the 512 × 288 PNGs, JPEG q85 | your packer may re-encode a page only when it changes it |
| the 127 scenario stills | ⭐ **committed** beside each `scenario.json` (`scenarios/<name>/preview.jpg`, 320 × 180) — superseding this row's first version (*"not in git"*) the same day; see [the steer](STEER_Coordinator_To_Ui_Thumbnail_Atlas_Scenarios_In_Git.md) | ⛔ no `--scenarios` re-run |
| the two types missing | [CE-4272](../Blueprint_Issues_Tracker.md) A-10C crashes on a ground spawn (owner not known, measure first) · [CE-4273](../Blueprint_Issues_Tracker.md) `Car` drives off between the captures (yours, the script) | not in the handoff's scope — FYI; CE-4273 is a natural rider when you touch `thumbnails.py` |

---
_coordinator lane, 2026-11-06_
