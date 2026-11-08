<!--STATUS
state: LIVE — behaviors → coordinator: NOTE_Coordinator_To_Behaviors_Resume_Order.md — all three items done
updated: 2026-11-08
current-answer: the whole note
related-designs:
  - ../../UX/DESIGN_Map_Interaction.md — B-17 AS-BUILT P1…P6
  - ../../UX/DESIGN_Review_Perspective.md — AS-BUILT V1…V7
-->
# NOTE — behaviors → coordinator: **your resume order is done**

| # | item | state |
|---|---|---|
| 1 | **B-17** map interaction | ✅ P2 `28c9b5c9d` · P4 `fce79c194` · P5 `f13a5469d` · P6 `d9991ee7f` (P1 earlier). ⏳ **P7 the look review — awaits the user** (§2.6; the frames are in chat, not git). STATUS: `STATUS_Editing_UX_Build_Behaviors.md` W-2 |
| 2 | **Review perspective** V1–V7 (CE-3419 ✅) | V1 `c2f3f7f90` · V2 `70cd37568` · V3 `1ddf7748c` · V4 `75ca76f14` · V6 `4919210fb` · V7 `fdab6f921`. ⭐ the EMPTY-world bug is fixed (a group opens MERGED). STATUS: `STATUS_Review_Perspective_Behaviors.md`. ⚠ For ui: their toolbar ▶ stays enabled in Merged — the seam to grey it is in `NOTE_Behaviors_To_Ui_Backend_Review_Source.md` |
| 3 | the pre-existing reds | ✅ every one fixed with its cause (`a2508d745`, `06ee9318b`, `ae96b61ed`; answer `NOTE_Behaviors_To_Backend_Preexisting_Reds_Answer.md`). ⭐ One was a real defect: `NodePinSchema` projected a read-only property as an input pin. Filed, not ours: CE-2426 (ui's selection cell rail); `StorageProcessManager` / `ContentMigrate` JSON options (backend) |

_behaviors lane, 2026-11-08_
