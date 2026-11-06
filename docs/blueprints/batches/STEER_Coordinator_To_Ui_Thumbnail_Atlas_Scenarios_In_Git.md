<!--STATUS
state: LIVE — STEER for a RUNNING batch (rule 1c): HANDOFF_Coordinator_To_Ui_Thumbnail_Atlas.md, dispatched 47a12fb5f, started by ui (554fc5ae)
updated: 2026-11-06
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Add_Entity_Picker.md — §2j "Scenarios — a small JPEG beside scenario.json, IN GIT" is the new basis for item ③
-->

# STEER — coordinator → ui: item ③ changes — scenario stills go IN GIT beside `scenario.json` (CE-4271)

⭐ **Relayed by the user.** Your batch is running; this replaces ONE item. **Items ①, ② and ④ are UNCHANGED** (R-106: do not stop the batch).
This is not a correction of your work — the user changed the rule after dispatch.

🔒 User, `2026-11-06`: *"The thumbnails for the scenarios can of course be saved to the Git next to where the scenario JSONs are located. They do
not change much. And the same applies to the entity atlas textures. Just we need to make sure that the orchestrator deploys those scenario JPEGs
together with the scenarios."* — R-4117 amended.

## ③ — replaced

| | was (handoff) | ⭐ now |
|---|---|---|
| where a scenario still lives | NAS only, ⛔ not in git | ⭐ **`scenarios/<name>/preview.jpg` in the REPOSITORY**, 320 × 180, q85 — ✅ **already committed for all 127 scenarios** (merge `coordinator`) |
| `thumbnails.py --scenarios` | writes the NAS, deletes a generated NAS `preview.png` | writes the **repository** folder; skips a scenario that has a `preview.jpg` / `preview.png` unless `--force`; ⛔ no NAS writing, no deleting |
| readers | `preview.jpg`, then `preview.png` | unchanged |
| deploy to the nodes | — | ✅ measured, no change needed (§2j table: the dev-start seed, the load's prefetch, the exercise snapshot and a save all carry it) — ⭐ add ONE rail in the orchestrator's prefetch suite: a scenario folder holding a `preview.jpg` prefetches cleanly and the JPEG lands in the node's staging |
| done when | the scenario pane shows a `.jpg`; no scenario image in git | the scenario pane shows the committed `preview.jpg` after a dev start; the prefetch rail green; `--scenarios --force <name>` re-shoots one into the repo |

## Also merge `coordinator` again — two FYIs you started without

You merged `coordinator@bb8c1eee`. Since then: ⭐ the 75 entity pictures are **already packed** in §2j's format
(`Hrot/Engine/Hrot.Presentation/Thumbnails/`, see [the seeded note](NOTE_Coordinator_To_Ui_Thumbnail_Atlas_Seeded.md)) — item ②'s packing is
done, your packer extends them; and the 127 scenario stills above. ⛔ No re-shooting of either.

---
_coordinator lane, 2026-11-06_
