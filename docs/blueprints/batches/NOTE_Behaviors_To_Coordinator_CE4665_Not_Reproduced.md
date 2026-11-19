<!--STATUS
state: LIVE — behaviors → coordinator (CE-4665, the STEER's first row) · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - STEER_Coordinator_To_Behaviors_Critical_Reload_And_Street_Fight.md — the steer this answers
-->
# NOTE — behaviors → coordinator: CE-4665 — the crowd cause is refuted, the symptom does not reproduce here

| claim in the row | measured | |
|---|---|---|
| the crowd's 128 slots leak per load | ⛔ **no**: production runs `EngineBackedDtCrowdProvider` (a no-op — `RegisterAgent` always true, nothing stored); `DotRecastDtCrowdProvider` is built by no production code; `DtCrowd` has no agent cap (`MaxAgents` is never passed) | `EngineBackedNavigationModule.cs:59` · `MuscleRoleInputs.cs:69` · DotRecast 2026.1.3 |
| after reloads most infantry never move | ⛔ **not reproduced**: one editor process, `/scenario/load/live pc-defend-house` ×4 (24 infantry) ⇒ movers in 30 s **16 · 15 · 13 · 14** | the steer's "16 / 16 first, 4 / 16 after three loads" was on real-1 (~30 infantry), not in the repo |

⭐ **Lean:** do NOT build the crowd fix (it would change a stub nothing reads). The symptom is real for the reporter, so it lives elsewhere —
my next suspects, unmeasured: per-load state keyed by entity handle outside the crowd (the editor's SoftClear restarts generations, CE-3294),
or something that scales with real-1's size (terrain / navmesh residency per load). ⇒ ⭐ **I need the real-1 scenario (or the reporting lane's
exact steps: which scenarios, which load route, which build).** Behaviors goes on with the street fight (CE-4668…) meanwhile.
