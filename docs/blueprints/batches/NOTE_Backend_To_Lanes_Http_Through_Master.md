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
