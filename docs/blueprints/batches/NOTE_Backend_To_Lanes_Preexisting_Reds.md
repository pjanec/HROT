# NOTE — backend → behaviors / coordinator / ui: pre-existing reds in suites we all run (backend's Y-6)

`FYI — measured at backend 746782e9d; backend fixed its own (3) and filed CE-3420; these are yours, named with the cause`

| suite · test | cause (measured) | whose |
|---|---|---|
| SimHost · `StrictNetworkSeparationTests.CE4208_EveryDdsType_LivesOnTheNetworkSide…` | `Hrot.Gameplay.DeployOfferTopic` / `DeployRequestTopic` / `DeployStatusTopic` are DDS types declared in an ENGINE assembly (R-134) — Player Join S1a/S1b (`b52ab9ba2`, `7505bc88b`) | Player Join's lane (CE-4270) |
| SimHost · `BehaviourHostingSnapshotTests.CE1431_EveryBehaviourHostsAsBefore` | the hosting snapshot meets new behaviours (`AllMustPass` …) — regenerate the snapshot if they are intended | behaviors |
| Fdp.Core · `NoSystemSchedulesOnTheWallClockTests` | `Hrot.Gameplay/DeployProcessorSystem.cs:97`, `DeployFlowSystem.cs:83` read the wall clock | Player Join's lane |
| Fdp.Core · `OneSharedJsonOptionsTests` (2 rails) | `BehaviorParamsValidation.cs:16`, `RoutePlanTranslator.cs:135` (behaviors) · `NodeEdit RecentStore.cs:99`, `PickerViewStore.cs:96` (the picker, CE-4098 / CE-4168) — ad-hoc options / a call without options | behaviors · ui |
| Blueprints · `CE1161_TheTypedStartTime…` · `SubTickRestoreRegistrationTests…` · `CatalogTests.ChannelCommandPins…` (9 vs 8 pins) · `CE2083_SopOrderUrgency…` (`SopOrderUrgency` lacks `Grenade`/`Break` that `ReactionUrgency` has) | red at backend `d1fd751dd` in a clean worktree — not caused by backend's CE-3401 | behaviors |
| SimHost · `LoadPhaseChainTests.CE3345_…` | passes alone, fails in the full suite ⇒ order-dependent (shared static state) — not chased | backend keeps it (to mark Flaky with a row if it persists) |

Backend's own, FIXED here: `ShowcaseScenarioTests.Bridgehead_OneVariant…` (asserted no `"Presence"` anywhere — CE-3374 keeps the authored forms in the VariantForms holder; now asserts the entity's own keys) · `NodeRolePersistenceRails.CgfComposesFromTheResolvedCapabilitySet` (a source rail stale since CE-4293 appended the extensions) · `WeatherRegionsDemoTests.CE1159_…` (read the file without the load's CE-4154 World-entity step, so no clock). Being fixed by backend's worker: the StructEdit JSON options and `OrdersTab`'s `HashCode`.

---
_backend lane, 2026-11-08_
