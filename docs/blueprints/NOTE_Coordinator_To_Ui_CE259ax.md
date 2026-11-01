<!--STATUS
state: LIVE
updated: 2026-10-31
current-answer: the whole note
related-designs:
  - docs/DESIGN_Entity_Authoring_Surface.md — §5 / §5b own CE-259ax (the UI half of the authoring surface)
  - docs/DESIGN_Entity_Creation_Unification.md — §3.4 the one authoring affordance (R-145)
-->

# NOTE — coordinator → ui: **the coordinator is building CE-259ax** *(2026-10-31)*

🔒 **User, `2026-10-31`:** *"You run it here on the background please."* — so [CE-259ax](Blueprint_Issues_Tracker.md) (the UI half of the
authoring surface: the editor / Player / SimHost / Stride creation sites call `EntityCreation.RequestEntityCreation` instead of publishing
`SpawnEntityCommand` themselves) is being built on `coordinator` by a coordinator worker. It is a **cross-lane edit** of `ui`-owned files:
`Hrot.Presentation/ScenarioEditor/{Gizmos,Tools}`, `Hrot.Presentation/Adapters/ScenarioSpawnAdapter.cs`, `Hrot.IG` (Player) entity creation,
`Hrot.SimHost/UI/SimHostScenarioManager.cs`, `Stride` creation.

⭐ Please avoid editing those files until the coordinator reports the batch done (a later note), or say so and we coordinate.
At dispatch `origin/ui` was not ahead of `coordinator` (0 commits).

## ✅ DONE `2026-10-31` — commit `676464716` on `coordinator`

`ScenarioSpawnAdapter` is a thin caller of `EntityCreation.RequestEntityCreation` (its own request mapping deleted; `EntityAuthoringInputs.Requests` → `Creation`), the placement gizmo's name resolver is honoured (G2), `SimHostScenarioManager`'s six spawners author through the pack, `IgEntityCreationRequests` (zero callers) deleted. Not built: `BeginPlacement` (its only consumer, IG's `MapCommandController` session, was retired by R-3067). As-built: [`DESIGN_Entity_Authoring_Surface.md`](../DESIGN_Entity_Authoring_Surface.md) §9. ⭐ The files are free again.
