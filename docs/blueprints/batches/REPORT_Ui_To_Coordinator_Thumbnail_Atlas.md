<!--STATUS
state: LIVE
updated: 2026-11-06
current-answer: the whole file (a batch report)
stale-below: none
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Add_Entity_Picker.md §2j — CE-4271 as built
-->
# REPORT — ui → coordinator: the entity thumbnail atlas, scenario stills in git *(answers the handoff at `47a12fb5f` and the steer; merged `coordinator@07e6d3592`)*

| item | state | built |
|---|---|---|
| ① runtime | ✅ | `AtlasIndex` (pure) · `IconAtlas.GetUvForRect` · `ThumbnailAtlas` (embedded by a wildcard `EmbeddedResource`, a page uploaded once, each cell's `PickerImage` cached ⇒ 0 bytes per frame after) · `ThumbnailMap` step ④ after ③ · `PickerImage.Uv0/Uv1` at the 3 draw sites · `PickerImageCache` resident lookup (no budget, never evicted) · `ThumbnailAtlas.Shared` passed by `GalleryData.ShippedOver` and `CreatePickers` |
| ② packer + documented way | ✅ — your 75 packed pictures kept as shipped (0.55 MB: 66 + 9 cells) | `thumbnails.py --pack · --add-image F --name T · --atlas-list · --atlas-remove T · --atlas-dir` (tests on a temp copy); reads and EXTENDS the shipped index (a stem keeps its slot; a page re-encoded only when a cell changes); RUNBOOK_Filming §8 |
| ③ (steered) scenario stills in git | ✅ | `--scenarios` writes `scenarios/<name>/preview.jpg` in the repo, skips one with a `.jpg`/`.png` unless `--force`; no NAS writes, no deletes; both readers share `ScenarioPaneFacts.PreviewOf` (`.jpg` then `.png`); prefetch rail `StorageGatewayTests.CE4271_AScenarioFolderWithAPreviewJpg_…` |
| ④ measure first | ✅ no route needed | 📐 the picker re-resolves the catalogue on every open, and a file that did not exist is never a cached failure ⇒ a running editor shows a new picture. ⚠ A file REPLACED at the same path keeps its cached texture until restart |

**Not done / not verified:** `--scenarios --force <name>` not run against a live editor · the scenario pane after a dev start not looked at
(dev-start seeding is the §2j measurement) · CE-4273 (Car drives off between captures) left open.
**Deviation:** one reader edit covers both readers (they already share `PreviewOf`); `--atlas-dir` added for tests.

## Gates

| gate | result |
|---|---|
| `Hrot.Presentation.Tests` (EntityTypeCatalog, AddEntityAction, `ThumbnailAtlasTileFrameRail` under `xvfb-run -a`, PickerDoubleClick, DeployScreen) | 66 / 0 |
| `Hrot.Orchestrator.Tests` StorageGateway | 43 / 0 |
| `Hrot.Editor.Tests` AssetPickerLauncher | 23 / 0 |
| `Fdp.Presentation.Tests` IconAtlas (re-run after the merge) | 23 / 0 |
| `NodeEditor.UI.Tests` Picker (re-run after the merge) | 72 / 0 |
| `scripts/test_thumbnails_atlas.py` | 4 / 0 |
| red-proofs | whole-page UVs ⇒ atlas + tile frame rails red · ④ before ③ ⇒ order rail red · `resident:` dropped from `CreatePickers` ⇒ forwarding rail red · `.png` before `.jpg` ⇒ reader rail red — all restored |
| `tracker-counts.py --check` | OK |
| ⚠ `Hrot.Network.NED.Tests` | the `DdsTopicName` restore trap again after this merge (CycloneDDS 1.2) — not a code red; `dotnet restore` clears it |

Ids allocated: none.
