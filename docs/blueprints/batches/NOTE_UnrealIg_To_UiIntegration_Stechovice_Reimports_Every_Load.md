# Note: Stechovice is re-imported on EVERY load — ~7 min instead of ~20 s

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-25 |
| **For** | the HROT-integration session (on `unreal-ig`) |
| **HROT build** | `D:\Work\HROT-live` at `ui` `ebfff3644`, Release, `ClusterRunner --mode editor --headless`, scenario `showcases/stechovice-battle(-play)` |

Three loads in a row tonight (21:36, 21:39, 21:50) each ran the full terrain import twice and re-baked the whole navmesh (59834 tiles, "0 from the cache"):

```
[TerrainImport] importing 'Stechovice' from ...\data\shared\assets\terrain-input\Stechovice into ...\data\shared\assets\terrain (first use, the input or the importer changed)
[Navmesh] 'Stechovice.import-61aff170': 59834 tiles (6928 fine) - 59834 baked, 0 from the cache, 110439 ms
[TerrainImport] 'Stechovice' imported in 184.2 s
[TerrainImport] importing 'Stechovice' from ...\data\shared\assets\terrain-input\Stechovice into ...\data\nodes\node-1\Terrain (first use, the input or the importer changed)
```

The input did not change (`terrain-input/Stechovice` last written 2026-11-23; its `manifest.json` sha256 `fe13be9c…aafc` matches what both outputs record).

**What we measured (not fixed — HROT's code):** `SourceImport.Current` (`Hrot.Terrain.Import/SourceImport.cs:27`) needs the recorded sha AND `RecordedRevision == TerrainImporter.Revision` (2). After a load:

| terrain.json | `inputManifestSha256` | `importerRevision` |
|---|---|---|
| `data/shared/assets/terrain/Stechovice` (written 21:51:43) | `fe13be9c…` ✅ | 2 ✅ |
| `data/nodes/node-1/Terrain/Stechovice` (written 21:50:43, ~15 s after start) | `fe13be9c…` ✅ | ⛔ **absent** ⇒ read as 1 ≠ 2 |

⇒ the node's copy never counts as current, so every load re-imports it — and that import then rewrites the shared one too. The only writer we found that sets the revision is `TerrainImporter.cs:142` (`WriteTerrainJson(…, Revision)`); something else writes the node's `terrain.json` without it (we did not find what — likely a copy / distribution of an older terrain.json into the node folder). On 2026-11-23 the same scenario loaded with the navmesh "59834 from the cache, 6967 ms".

Our side: nothing to change — the Unreal client waits for HROT's load. The user noticed it ("it usually took 20 sec, not 7 minutes … terrain data like navmesh are cached").
