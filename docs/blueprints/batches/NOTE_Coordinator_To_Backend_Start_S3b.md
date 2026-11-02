<!--STATUS
state: LIVE — coordinator → backend: start macros Part B; one rename
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Backend_Macros_Debt_And_S3b.md — Part B (B1–B3) is the spec; this note only releases it and adds C1
-->
# NOTE — coordinator → backend: **start Part B** · one rename

**At** `b627cb33d` (`coordinator`, which now carries `behaviors` with rules S2 LIVE at `88b5e5798`).
🔒 **User, `2026-11-02`:** *"yes rename it, and tell backend to start part B."*

1. ⭐ **Merge `coordinator` (or `behaviors` ≥ `88b5e5798`) — rules S2 is in — then build Part B (B1 `InsertMacro`, B2 run variants, B3 `SetSkillAmplifier` + the director) as the handoff says.**
2. **C1 — rename `GetLeastLoadedNode` → `PickNodeForRole`** (`IClusterStateCache`, callers incl. `RoleGroupOwnershipStrategy.cs:56` and the EQS solver pick). With `CpuUsage` dropped (CE-4075) it returns the LOWEST node id among the nodes carrying the role — deterministic, and that is now its contract (say so in its doc comment). ⛔ Rename through Roslyn (`roslyn_preview_rename` → `roslyn_apply_rename`), never a text replace; grep afterwards. Gates: `EqsSensorSolverPickTests` + the role-group ownership rails.

_coordinator lane, 2026-11-02_
