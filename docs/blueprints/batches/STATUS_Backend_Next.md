<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — backend, the next batch ([HANDOFF](HANDOFF_Coordinator_To_Backend_Next.md), dispatched at `4600ebcd9`)

⭐ Supersedes the unstarted rest of `HANDOFF_Coordinator_To_Backend_After_Mission_Plan` (its X-2 … X-5 = Y-3 … Y-6). The run-controls batch: [STATUS_Run_Controls_Backend](STATUS_Run_Controls_Backend.md) (B-1 … B-5 DONE; B-6 answered here as Y-1).

| item | id | state | sha | notes |
|---|---|---|---|---|
| Y-1 | CE-4326 | DONE | (log) | ① presence / place / macro + the flow; ② pen-preset params in place (host serializer + the one binder); ③ P2 kept macro children take what the args changed — [DONE note to ui](NOTE_Backend_To_Ui_Preview_Preset_Done.md) (L-10 can offer every preset) |
| Y-2 | CE-3411 rest | DONE | `41ce0418e` | `/run-mode` out of Edit: the unsaved check round + HTTP's refusing asker via ui's `Choose(target, asker, answered)` — done before this handoff landed |
| Y-3 | CE-4298 rest | DONE | `b58665b79` | `MissionControlExecutionSystem.TryBuildQueue` (the one place every plan resolves) warns once for a Wait with no then, naming the entity and the task; rail `MissionControlExecutionSystemTests.CE4298_*` (red-proved); §5.10 updated; CE-4298 closed |
| Y-4 | CE-2153 | FILED with cause | (this commit) | ⭐ the mission chain works; the HARNESS cannot path: `PathToPoint` holds until a path (CE-3176) and `SimHostInstance` has no solver and no navmesh. Fix named in the row (solver + flat test navmesh in the harness, or a Direct move in the test) |
| Y-5 | CE-4099 | STOPPED at A0 — QUESTION | (this commit) | A0 FAILS as designed (header + doors read NoReplay content singletons; Recording set drops `VariantForms`); the amended capture holds — measured in AQ108 §4c.1, [QUESTION](NOTE_Backend_To_Coordinator_CE4099_A0_Failed.md), lean (A) amend |
| Y-6 | — | QUEUED | — | the pre-existing reds, one line each |
| Y-7 | CE-4327 | DONE | `93ed4255f` | `SetAuthority` stamps `LastChangeTick` on a real change ⇒ a handover is in the delta; rail `RecorderDeltaLogicTests.CE4327_*` |
| Y-8 | CE-3399…3404 | QUEUED | — | struct-editor follow-ups, small first |
