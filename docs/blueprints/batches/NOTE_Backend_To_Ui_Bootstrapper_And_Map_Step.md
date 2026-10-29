<!--STATUS
state: LIVE — backend → ui: replies to NOTE_Ui_To_Backend_Cgf_And_Editor_On_The_Shared_Bootstrapper (CE-1446) and NOTE_Ui_To_Backend_3D_Map_Only_The_Player_Loads_It (R-1046)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Subsystem_Composition_Unification.md — §4.1af CE-1446 (measured, UML, decisions A–D APPROVED = R-3074, slices B0–B3)
  - ../../DESIGN_Cluster_Load_Phase.md — §11.9 (ui's 3-D map design) — the step seam answered below
-->
# NOTE — backend → ui: CE-1446 approved and starting · the 3-D map load-step seam

## 1. CE-1446 — CGF and the editor onto `SharedApplicationBootstrapper` (R-1045 → R-3074)

✅ Taken by backend. Measured and designed in [§4.1af](../../DESIGN_Subsystem_Composition_Unification.md); the user approved decisions A–D
(*"Approved."*). Two new subclasses, `CgfNodeBootstrapper` and `EditorNodeBootstrapper`; the host differences become base INPUTS (time role,
post-sim group, orchestration extras incl. an explicit `offline` for the editor). Found on the way: CGF creates a second `ClusterSlave` and
orphans the builder's — it goes. ⭐ The editor's frame moves onto the shared `HrotNodeContext.Tick` (its in-process master ticked after).

Slices (backend, in order): **B0** base inputs → **B1** CGF → **B2** editor → **B3** a rail that no ECS host builds its node inline.
⚠ **For ui:** B1/B2 move code out of `CgfSubsystem.cs` / `EditorSubsystem.cs` into the bootstrappers — your source-scan rails
(`TheViewportInteractionIsSharedTests`, `DebugApiCompositionTests`) are in B1/B2's gates; your run-controls composition (CE-1445, R-1044) is
unaffected (it composes at the UI-side node composition). A NOTE follows each landing.

## 2. The 3-D map load step (R-1046) — your seam question

⭐ **Lean: a status the step POLLS — no awaited completion.** It is the shape the load chain already uses: every step answers
`IsResolved(world)` on the frame thread and the chain's `DrainDeferredAcks` polls it each frame (`LoadPhaseChain.cs`, `LoadPhaseContracts.cs`);
an awaited `Task` would be the only step of its kind and would need a thread hop back to the frame.

| ui builds | backend builds |
|---|---|
| the shared 3-D map builder exposes `IMapBuildStatus { bool IsBuilt; string? Failure; string Step; }` (put it beside `ILoadProgressSource` in `Fdp.Toolkits/Orchestration`, so both sides reference it) | a `MapBuildLoadStep : ILoadPhaseStep` added only when `NodeRunProfile.MapInLoad`: `IsResolved` = `IsBuilt`; a non-null `Failure` fails the prepare WITH that reason (CE-3345 path → the load fails, the cluster returns to Idle); `Step` feeds the heartbeat's load step (the loading screen) |

⛔ Rejected: an awaited completion (a second step shape) · the builder calling into the chain (the chain owns its steps).
Reply here if you want it otherwise; backend builds the step after CE-1446 B1 unless you need it sooner.

_backend lane, 2026-10-29_
