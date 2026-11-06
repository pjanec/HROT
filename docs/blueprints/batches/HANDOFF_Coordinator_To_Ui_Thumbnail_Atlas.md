<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-06
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Add_Entity_Picker.md — §2j OWNS this work (READY-TO-BUILD, with its class and sequence diagrams); §2i the capture pipeline it reuses
  - docs/blueprints/Architect_Question_108_Run_Mode_Picker.md — D26 P5 owns the shared picker image cache that gains the resident path
-->

# HANDOFF — coordinator → ui: the entity thumbnail atlas, embedded; scenario stills a small JPEG (CE-4271)

**For:** the ui lane (`ui`). **From:** the coordinator lane. **Dispatched at `<stamped below>`**. ⭐ Scope FROZEN there; later documents
are FYI. A later document that invalidates an item ⇒ STOP that item, report it (R-106), keep the rest. Questions ⇒ a NOTE to coordinator.
Merge `coordinator` first. Order it after your current queue (the November batch's 🔴s, the director, mounted guns).

## Why

🔒 User, `2026-11-06`: *"how to make them part of the executable, meaning to avoid distributing them as hundreds small PNG files"* ·
*"ok jpeg, approve all three leans, write the design. No atlas for scenarios, Small JPEG is exactly right. Entity thumbnails atlas to Git,
and a documented way … to add a new entity type into that."* — ruling **R-4117**, design
[§2j](../../DESIGN_Add_Entity_Picker.md) (build it against its class and sequence diagrams; report where you deviate and fold it back).

## The items

| # | what | done when |
|---|---|---|
| **①** | **Runtime:** `AtlasIndex` (pure, Fdp.Presentation) · `IconAtlas.GetUvForRect` · `ThumbnailAtlas` (Hrot.Presentation, pages embedded by a wildcard, uploaded on first use) · `ThumbnailMap` step ④ (`atlas:entities/<stem>`, after the loose file) · `PickerImage.Uv0/Uv1` passed by the three draw sites · the cache's resident lookup (no budget, never evicted) · wired once in `EntityAuthoring.CreatePickers` and `GalleryData.Shipped` | §2j's acceptance rails green, a forwarding rail per caller, a `…FrameRail` under `xvfb-run -a` that a tile draws its own cell |
| **②** | **The packer and the documented way:** `scripts/thumbnails.py --pack · --add-image <file> --name <Type> · --atlas-list · --atlas-remove`; fixed slots, only changed pages re-encoded, sorted index; a RUNBOOK "thumbnails" section with the Windows commands | packing the 75 pictures of `2026-11-05` (they are on the NAS / regenerate with `--types`) gives 2 pages ≈ 0.54 MB, committed with the index (R-4117's exception — only these files); `--add-image` round-trip rail |
| **③** | **Scenario stills:** `--scenarios` writes `preview.jpg` 320 × 180 q85; both readers look for `.jpg` then `.png`; a generated NAS `preview.png` is removed when the repo has none | the scenario pane shows a `.jpg`; no scenario image in git |
| **④** | ⚠ **Measure first:** does a RUNNING editor's picker see a picture just written (catalog re-resolve on open; the cache's remembered failure)? Only if not: `POST /thumbnails/reload` | the measurement in §2j; the route only if needed |

⚠ Fences: hot path R-2084 (no allocation per frame after the first page upload); no C# encoder (the packer is Python only); ⛔ no other
render enters git.

## Gates (report these rows)

The touched projects' suites, `--no-build` after building the TEST project · new rails red-proved · display rails under `xvfb-run -a` (a skip
is not a pass) · the packed atlas's file sizes · every red confirmed pre-existing against the base sha · `tracker-counts.py --check` · the
ids you allocate.

---
_coordinator lane, 2026-11-06_
