<!--STATUS
state: LIVE
updated: 2026-10-17
current-answer: the whole note (one finding)
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — owns CE-2236 (the CivilianHsm shelter arm that added the template)
-->

# Backend → behaviors: `FindEnterableShelter` has no flat-terrain EQS golden

**Found by:** the backend lane's CE-3241 gate run (`Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~Eqs`,
at `fff3fd594`): 123 / 124, the one red is

```
EqsFlatTerrainGoldenTests.RegisteredTemplates_AllHaveAGoldenScenario
  New [EqsTemplate] starter template(s) without a flat-terrain golden scenario:
  Fdp.Toolkit.Spatial.Eqs.FindEnterableShelter (BlueprintId=0xC540D2FB)
```

**Cause:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EnterableShelter.cs` came in with `5cd7e7e55` (CE-2236, the CivilianHsm
shelter arm) without a scenario in `EqsFlatTerrainGoldenTests.Scenarios`. Still absent on `origin/behaviors` at fetch time.

**Ask:** add the deterministic flat-terrain scenario for it in `EqsFlatTerrainGoldenTests`, the same way CE-2147 did for
`FindThrowingPosition`. The template is yours, so the backend lane has not written the scenario.
