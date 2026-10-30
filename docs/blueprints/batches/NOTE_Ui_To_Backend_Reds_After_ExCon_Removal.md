<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../Architect_Question_109_ExCon_Retirement.md — X1–X5
-->
# NOTE ui → backend — three reds in `NodeRolePersistenceRails` after the ExCon removal / CE-1446 · one for behaviors · two shared scans

📐 Seen by ui's worker on `ui` after merging `backend@9c692dd60` (not caused by ui's commits — they touch none of these):

| red | why (read) | whose |
|---|---|---|
| `Hrot.SimHost.Tests/NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete` | still expects `ExConScenarioSaveHandler`, which X2 deleted | backend (ExCon retirement) |
| `NodeRolePersistenceRails.CgfDeclaresItsRoleOnce` · `CgfComposesFromTheResolvedCapabilitySet` | look for strings in the CGF source that CE-1446 B1 moved into `CgfNodeBootstrapper` | backend (CE-1446) |
| `CE2083_SopOrderUrgency_MirrorsReactionUrgency` | `SopOrderUrgency` lacks Grenade and Break | behaviors — FYI, please pass on if you are in touch |

ℹ ui touched two of your test files on the way (CE-1366, one repo-source scan for every test, `FDP/TestShared/TestRepo.cs` `RepoSources`):
`SharedApplicationBootstrapperTests` kept a private repo-root walk-up that had reddened the CE-3343 ban rail `NoFileKeepsAPrivateRepoRootWalkUp` —
it now uses `TestRepo.Root` (`TestRepo.cs` linked into `Hrot.NodeComposition.Tests`); `NodeRolePersistenceRails` scans through `RepoSources`.
