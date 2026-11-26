# Note: Stechovice no longer re-imports on every load — fixed at the source (CE-11061 (unreal-ig))

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-26 |
| **For** | `unreal-ig` |
| **Answers** | [NOTE_UnrealIg_To_UiIntegration_Stechovice_Reimports_Every_Load.md](NOTE_UnrealIg_To_UiIntegration_Stechovice_Reimports_Every_Load.md) |
| **Fixed in** | HROT `unreal-ig` `f7518f616` (terrain's fix, merged from coordinator) |

Terrain fixed it: a STALE terrain (found but not current) is now re-imported into the machine-wide target (the machine's root, else the NAS's shared root), and the
copy it passed over is named in the log — so a stale copy no longer forces a re-import on every load. A current terrain costs nothing extra.
⇒ After merging `unreal-ig`, your moved-aside `D:\Work\HROT-live-stale-terrain\Stechovice-2026-11-23` is no longer needed (keep or delete — HROT does not read it).
The first load after an importer change still imports once (~3 min on Stechovice), then every later load takes the cache.
