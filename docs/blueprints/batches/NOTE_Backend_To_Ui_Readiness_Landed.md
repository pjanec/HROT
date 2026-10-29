<!--STATUS
state: LIVE — backend → ui: CE-3344 + CE-3345 landed on `backend` (675ff07ae); everything U1 and U2 read now exists
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Cluster_Load_Phase.md — §11.4 (CE-3344 as-built), §11.5/§11.5a (error state), §11.6 (CE-3345 B as-built)
  - ../Architect_Question_110_Readiness_Protocol.md — the protocol (R-3072)
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.15 the File menu (R-3073)
  - NOTE_Backend_To_Ui_Readiness_Protocol.md · NOTE_Backend_To_Ui_File_Menu_And_Error_State.md — the earlier notes this completes
-->
# NOTE — backend → ui: readiness has landed — U1 and U2 can be built

Merge `backend` (≥ `675ff07ae`). What your controls read, all on **`ClusterView`** (one read model on every node):

| for | read |
|---|---|
| greyed controls + *"waiting for …"* | `CanOperate`, `NotOperableReason` (master gone · no state yet · error · waiting for: Brain … · not in sync) |
| the gate per host kind | `GateOpen(HostGatePolicies.For(RunHostKind))` — Operator (Editor, CGF, Orchestrator) waits for the cluster; Player (IG) for itself |
| the loading screen | `LocalPhase` (Booting · Ready · Loading · CatchingUp · PlayReady · Failed), `LoadProgress` (which participant is on which step), `ClusterPlayReady`; **Cancel** → `ClusterSlave.CancelLoad(reason)` — the load fails everywhere and the cluster returns to Idle with that reason in the op status |
| the red error indicator | `IsInError`, `ErrorReason`; only Close and Exit work |
| Close | `ScenarioDoor.Leave(LeaveReason.Close)` (`BackToStart` is gone); always allowed |
| pickers | `HasInventory` — *"waiting for the cluster's list…"* until it is true |

⭐ **Load follows the armed mode on the HOST**: read the cluster's armed mode from `ClusterView` and send an EXPLICIT load into Live / into edit. ⛔ The master never interprets the armed mode — the `FollowArmedMode` flag from part A was withdrawn and removed (user: *"all the requests to the cluster state machine are always explicit"*).

HTTP (backend, done): cluster-changing routes answer 409 with the reason while `!CanOperate`; every way back to Idle (`/replay/stop`, `/sim/stop`, `/cluster/cancel`, `/cluster/transition` → Idle) is never gated; `GET /status` carries `canOperate` / `notOperable`.

_backend lane, 2026-10-29_
