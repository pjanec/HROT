<!--STATUS
state: LIVE
updated: 2026-10-29
current-answer: whole file
related-designs:
  - ../../DESIGN_Subsystem_Composition_Unification.md — §4.1j / §4.1x / §4.1ae W3: the deferral this supersedes
  - ../../DESIGN_Cluster_Load_Phase.md — §11.8: the run controls for every ECS node (CE-1445), which this unblocks properly
-->
# NOTE ui → backend — CGF and the editor onto `SharedApplicationBootstrapper` (CE-1446, R-1045)

🔒 **User, `2026-10-29`:** *"Why is CGF not using shared bootstrapper? It must use the same bootstrapper as the other ECS node. That's the
point of unification and sharing."* Also R-1044: ECS hosts are unified and configurable — every control by construction from the shared
bootstrap; a role only parameterises (the player shows the lobby screen because its bootstrap honours its role).

| measured | where |
|---|---|
| derive from the base | `SimHostNodeBootstrapper`, `IgNodeBootstrapper`, `StrideNodeBootstrapper`, `TrafficNodeBootstrapper` |
| build inline through `HrotNodeBuilder` | CGF `CgfSubsystem.cs:685` · editor `EditorSubsystem.cs:1304` |
| why they were kept off | Composition Unification §4.1j *"optional, LAST"* (orchestration / participant / time authority), restated §4.1x, §4.1ae W3 — now SUPERSEDED (banner at §4.1x) |
| what the split cost | CE-151 · CE-4013 D1 · CE-1445 (ui: the player had no waiting screen because run controls are per host) |

**Ask:** own [CE-1446](../Blueprint_Issues_Tracker.md) (bootstrap is backend's area). First measure what CGF's and the editor's orchestration
phase does that `BuildOrchestration` does not — the editor hosts the in-process master (R-1043's initial armed mode is its bootstrap parameter).
ui's CE-1445 does not wait for this: it composes the run controls at the UI-side node composition, which every ECS host passes either way.
