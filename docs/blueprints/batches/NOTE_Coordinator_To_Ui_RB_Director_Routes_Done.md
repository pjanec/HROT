<!--STATUS
state: LIVE — coordinator → ui (cc backend): your lane question answered — the RB's /director/* routes are attached, by the coordinator
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §11f, the "P3 step" paragraph under the phases table (as built)
  - NOTE_Ui_To_Coordinator_Films_P1_P2_Built.md — the note whose question this answers
-->
# NOTE — coordinator → ui: the RB's `/director/*` routes are done (do not build them)

**Dispatched at** the commit that adds this file (`coordinator`).

🔒 **User, `2026-10-27`, on your lane question:** *"if its one call feel free to do it yourself no ned to bother other lanes"*. ⇒ Done here.

| what | where |
|---|---|
| `DirectorPack.LayerControl` (the host's layer function, kept) | `Hrot.Presentation/Map3D/Director/DirectorPack.cs` |
| `IProvidesDirector { DirectorPack? Director }` — `ReplayBrowserSubsystem` implements it (its `Director` is public now) | same file · `ReplayBrowserSubsystem.Director.cs` |
| `DebugApiHost.AttachDirector(DirectorPack)` — ⭐ the editor uses it too (one call, not three arguments) | `DebugApiHost.Director.cs` · `EditorSubsystem.cs` |
| the runner attaches the director of whichever subsystem provides one | `ClusterDebugApiComposition.Director` · `Program.cs` |

📐 Live: `--mode replaybrowser --headless` ⇒ `GET /director/status` 200 (Idle), `GET /director/takes` 200. Rail
`ReplayBrowserSubsystemTests.CE1303_P3_TheReplayBrowsersDirector_AnswersTheSharedDirectorRoutes` (503 before the attach, 200 after).
⏭ Still yours (P3): `recording=` on the routes (TE-15 — today the OPEN recording is the source; `POST /replay` opens it), `/director/draft`,
`/director/stills`, MP4. ⚠ Cross-lane edits named: `DirectorPack.cs` (ui), `ReplayBrowserSubsystem.Director.cs` (ui), `Program.cs` (backend).

_coordinator lane, 2026-10-27_
