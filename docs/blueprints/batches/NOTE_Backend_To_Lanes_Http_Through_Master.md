<!--STATUS
state: LIVE
updated: 2026-10-26
current-answer: the whole note (short)
related-designs:
  - docs/DESIGN_Http_Control_Through_The_Cluster_Master.md — the design this reports on (§3, §6, §7)
-->

# Backend → behaviors, ui: the HTTP surface drives the cluster master (CE-3326, CE-3325)

Dispatched at `a5b0a922f` (backend).

## behaviors

- 🔴 **`StressSystem` (`Hrot/Subsystems/Hrot.AI.Behaviors/Brains/StressSystem.cs:311`, from `01de0b843`) has no
  `[UpdateInPhase]`.** The module host refuses it at registration, so the editor test harness throws at boot
  (`System StressSystem must have [UpdateInPhase] attribute`): 44 of 46 tests in the
  `Hrot.ClusterRunner.Integration.Tests` DebugApi/Preview/Capability filter are red on `backend` before and after
  CE-3326. Backend did not touch it (your system, your phase choice).

## ui

| what changed | what it means for you |
|---|---|
| `/replay/load|step|unload`, `/replay/entities…`, `/replay/selection` are gone (R-3052…R-3055) | your CE-1265/1266 readers: during a cluster replay the ORDINARY readers (`/entities/{id}/brain`, `/journal`, `GET /selection`) read the replayed moment. `GET /entities/{id}/journal/history` (moved) and `POST /replay/search` scan the node's own replay recording. `DESIGN_Agent_Shared_View.md` §6b carries a SUPERSEDED banner (cross-lane edit, said) |
| `POST /scenario/save` is one `ExecuteStorageOpIntent` on every host | ⚠ it no longer goes through `EditorScenarioSession.SaveAs`, so it does not copy the film takes (`CopyTakes`) nor update the editor's loaded name. If takes must follow a save made by an agent, that belongs in the cluster save (the handler), not the HTTP route — your call |
| `POST /preview/enter|exit` publish `TransitionStateIntent` | ⭐ the editor's own Play/Stop still calls `IPreviewController` → `ReferencePreviewHandler.Trigger…Preview` directly: the UI half of "no shortcuts" (R-3052) — proposed to you, not done here |

## ui — measured on the editor after CE-3326 (`2026-10-26`, run C)

| what | measured |
|---|---|
| the editor's time facade (`EditorTimeTransportFacade`, what `/status` and `/replay/status` report as `sim`) does not follow the cluster replay clock | entered at 20 s → reads **48.0 s**; after the seek to 30 s → **29.4 s**, `isPaused:false` — while the master clock logs sim 20.000 / 30.000 and pauses for each seek |
| `inPreview` is inverted against the cluster state | `true` in OperatingLive and OperatingReplay, `false` in OperatingPreview (entered through the master) |

⭐ Backend fixed the two defects the same run found on its side (one wall-clock basis per recording: `OrchestratorCore.LoadedClock`, `RecorderTickSystem`).
