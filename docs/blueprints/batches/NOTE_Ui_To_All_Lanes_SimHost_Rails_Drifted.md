<!--STATUS
state: LIVE — FYI / hand-back to the owning lanes (CE-1980)
updated: 2026-11-19
current-answer: the whole file
-->
# NOTE ui → every lane: seven `Hrot.SimHost.Tests` guard rails drifted with your features (CE-1980 triage, `2026-11-19`)

Measured at ui `6cd96534d` (+ this commit), each run alone: **CE-1980**'s nine reds are now **seven** — `SimHostCoreLogicPack_EmptyWorld…` and `LoadPhaseChainTests.CE3345_…`
pass, and ui fixed its own (`NoHost_ReferencesAnotherHost…`: `Hrot.ReplayBrowser → Hrot.Orchestrator` — `NodeIdRanges` moved to `Hrot.Core`, namespace kept). Each
of the seven is a guard rail doing its job on a feature its lane added; ⛔ ui did not change them — each needs the owner's decision (fix the code, or update the
rail with the reason).

| rail (`Hrot.SimHost.Tests`) | what it says now | whose |
|---|---|---|
| `StrictNetworkSeparationTests.CE4208_EveryDdsType_LivesOnTheNetworkSide…` | 4 DDS topics declared in the ENGINE assembly `Hrot.Gameplay` — `DeployOfferTopic`, `DeployRequestTopic`, `DeployStatusTopic`, `PlayerSlotTopic` (R-134) | player join (`DESIGN_Player_Join.md`) |
| `UtmEverywhereTests.TheEightCommittedTerrains_AreUtmZone33N…` | a committed terrain is in zone **32**, not 33 (or there are more than eight now) | terrain |
| `UrbanCombatCatalogRails.CreateTkb_StillCarriesTheNedCatalogueContent` | 26 where 17 were expected | the catalogue's owner |
| `BehaviourHostingSnapshotTests.CE1431_EveryBehaviourHostsAsBefore` | a new behaviour `ArcheryScore` (CE-4649 BW-11) is not in the snapshot | behaviors (bows) |
| `SopParamsRoundTripTests.CE2079_EveryBehaviourReadsTheParamsAnSopOrderSends` | `UnitRelayBreakContact` / `UnitRelayMove` IGNORE the params an SOP order sends (CE-8307) | behaviors / agg |
| `SquadManeuverTests.CE4621_TheConductTable_IsBuiltFromTheDeclarations…` | *"BackOut leaves the route: a Defend / Hold order would now refuse it"* | behaviors |
| `DrillsUnderTheReadCheckTests.TheDrills_AsTheParallelPhase_MakeNoUndeclaredCrossRead` | 2 drills red under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1`, no undeclared read named (see its output) | behaviors |

⚠ **And one FLAKY rail** (not in CE-1980's nine): `SquadManeuverTests.ST0_TheStochasticReaders_Allocate_ZeroBytes` (agg ST-0) is green alone (3 of 3) and red inside a
larger run (464 B). 📐 Measured: the bytes are one-offs in `UnitStrength.Of` (104 + 136) and `CombatEffective.IsInTheFight` (200) inside the 100 measured calls after a
5-call warm-up — timing (tiered JIT promoting the methods inside the window), not an allocation in the code. ⭐ Lean: a warm-up past the tier-up point, or mark it
`[FlakyFact]` with its row.
