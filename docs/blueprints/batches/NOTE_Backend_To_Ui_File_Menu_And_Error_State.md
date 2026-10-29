<!--STATUS
state: LIVE — backend → ui (coordinator FYI): R-3073 — one File menu on every host, and the cluster error state's ui half
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.15 the menu (corrects §2.8 row 9: Load follows the armed mode)
  - ../../DESIGN_Cluster_Load_Phase.md — §11.5 the error state; §11 readiness (CE-3344 built: ClusterView.CanOperate / WaitingFor / NotOperableReason)
-->
# NOTE — backend → ui: the File menu (R-3073) and the red error indicator

🔒 **User, `2026-10-29`:** *"Nothing like "New exercise" makes clear sense to the user."* · *"with Live armed, Load goes straight to live - this is what it should do"* · *"approved, record it."*

**ui builds (AQ108 §2.15 has the menu):**
1. The File menu exactly as drawn there, on every host, through the shared scenario-menu registration: Load Scenario · New Scenario · Load Checkpoint · Open Exercise Recording · Save Checkpoint (live only) · the editing group (only while editing, hidden otherwise) · Close · Exit. ⛔ Remove `File/Live/…`, `File/Edit/…`, `File/Checkpoint/…`, **New Exercise**, and **`File/Terrain…` with `TerrainPickerLauncher`** (the recipe owns the terrain).
2. ⭐ **Load follows the cluster's armed mode** — this corrects your CE-4097 as-built row 9 (*"Open always opens INTO editing"*), which drifted from the user's fifth pass.
3. **Close** = `Door.Leave(LeaveReason.Close)` (a new reason; `BackToStart` folds into it). Exempt from the readiness gate.
4. Start / every operator host greys its cluster actions on `!ClusterView.CanOperate`, showing `ClusterView.NotOperableReason` (CE-3344, built on `backend`).
5. **The red status-bar indicator** for the cluster error state (§11.5): its reason, and only Close and Exit enabled; Close asks only *"discard and close?"* (Save is refused during an error).

**backend builds (CE-3345):** the error state on the master (Degraded + reason, clock paused, no teardown, sticky, cleared at Idle), `LeaveReason.Close`'s exemption + every HTTP way back to Idle exempt, and Load following the armed mode on the master's side if it does not already. A NOTE follows when it lands.

_backend lane, 2026-10-29_
