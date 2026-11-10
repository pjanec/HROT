<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-10
current-answer: the whole file
-->

# HANDOFF — coordinator → ui: the OSM wizard LIVE — real tiles and search, and the preview's missing details

**For:** ui. **From:** coordinator. **Dispatched at `e233f69a6`**, scope frozen there. Merge the other lanes first; push `chore: started
HANDOFF_Coordinator_To_Ui_Osm_Wizard_Live at <sha>`; status file `STATUS_Ui_Osm_Wizard_Live.md`. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md).
⛔⛔ **Never stop for a question** (R-4156): every decision below is made; a premise that fails stops THAT item only (R-106).

**Why:** the wizard (CE-4375) is built, conformant and composed with the REAL pipeline (`OsmPipelines.Online`) and the importer (CE-4378) — ⛔ but
**File ▸ New ▸ Terrain from OpenStreetMap… is still GREYED**: no real `ITileSource` or `IPlaceSearch` exists (only your test fakes — measured:
`grep ': ITileSource\|: IPlaceSearch'` finds `OsmWizardFakes.cs` only). The contract named the coordinator as their implementer; it was not done —
⭐ it is yours now. And your conformance table left four ⚠ rows marked "contract" (C-10 lone trees, C-12 the hover's type / roof / door rule /
windows / shared walls, C-13 note location + severity, the data timestamp) — the preview contract does not carry them yet.

**Design basis:** [DESIGN_Terrain_From_OSM.md](../../DESIGN_Terrain_From_OSM.md) §4a (the contract), §6 / §6a / §6b (the mockup, the wizard's own map,
your as-built), §9 (licensing: tile and Nominatim usage policies), §14 (the library as designed) and §14.8 (as built). Mockup unchanged:
`docs/UX/terrain-osm-*-visual.svg`. ⭐ The library `Hrot/Engine/Hrot.Terrain.Osm` was built by the coordinator — editing it here is an allowed,
STATED cross-lane edit (name it in the commits).

📐 **Measured from the cloud VM `2026-11-10`:** `tile.openstreetmap.org` and `nominatim.openstreetmap.org` answer 200 (live checks are possible);
⛔ Overpass (`overpass-api.de`) times out — a live PREVIEW cannot run here (the wizard then shows the service error, by design §7); offline
previews go through the pipeline's file source.

| # | id | what | done when |
|---|---|---|---|
| **E-1** | CE-4381 | **the real tile source and place search**, in `Hrot.Terrain.Osm` beside the contract: `OsmTileSource : ITileSource` (XYZ from `https://tile.openstreetmap.org/{z}/{x}/{y}.png`, the URL a setting; attribution "© OpenStreetMap contributors"; the OSM tile usage policy — a User-Agent naming the app and the repo URL like `OverpassSource`'s, ≤ 2 requests in flight, a disk cache honouring the HTTP cache headers, NO prefetch beyond the visible tiles + one ring, NO bulk download) and `NominatimPlaceSearch : IPlaceSearch` (search on Enter, ⛔ never per keystroke; ≤ 1 request per second; the result's bounding box → `GeoBox`; the same User-Agent) — reuse the pipeline's `HttpRetry` (§14.8), ⛔ no second retry policy. ⭐ Fold **CE-1468** in: the wizard's tile textures go through the ONE image LRU (`PickerImageCache`'s concept) instead of a second. Compose both in `EditorSubsystem` (`osmServices`) ⇒ the menu item un-greys | the item is enabled in the editor; rails against a local fake HTTP handler (URL, User-Agent, ≤ 2 in flight, cache hit, 1/s throttle, Enter-only search); one opt-in live rail (env var) fetching tile 0/0/0 and searching "Kolín" |
| **E-2** | CE-4382 | **the preview's missing details** — extend `OsmPreview` (a contract change, in its own commit): lone trees + tree rows as points; per building its OSM type, roof (flat / pitched + source), door rule (entrance node / road-facing / back door), window count, shared-wall count; per note a location (point or box) and severity; the OSM data timestamp. Fill them in `PreviewBuilder` (the library — the rules already know every one of these) and use them in the wizard, so **C-10, C-12 and C-13 become ✅ with no ⚠** — the mockup's tooltip lines exactly ("way 112233445 · house / 2 storeys (tagged) · pitched roof (gabled) / 1 door (road-facing rule) · 9 windows / shares 1 wall with way …"), lone trees as the mockup's dots, clicking a note zooms to its location | your `TheOsmWizardMatchesItsMockupTests` assert the new rows; the library's rails cover the new fields (`Hrot.Terrain.Osm.Tests` still 108 + new); the conformance table updated in the status file |
| **E-3** | CE-1466 | *(your own row, if time is left)* the 3-D click-select uses the per-chunk picker you built in D-1 instead of the 2-D footprint boxes | its rail |

**Not in this batch:** the map background (OSM §8 — the user decides); splitting a too-large Overpass result (§7, a later item). **Order:** E-1 → E-2 →
E-3. Gates: the touched test classes + each new rail red-proved once; frame rails under `xvfb-run -a`; `tracker-counts.py --check`.

---
_coordinator lane, 2026-11-10_
