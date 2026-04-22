# BATCH-02 Report

## Completion Status
- [x] MPM-P2-T01: Extend EDescriptorType enum
- [x] MPM-P2-T02: Fix NED translator magic ordinals
- [x] MPM-P2-T03: Create TimeDescriptorType + update time translators
- [x] MPM-P2-T04: Create BdcDescriptorType + update BDC translators

## Build Status

`dotnet build IOS-IG-SimHost.sln` → **Build succeeded. 0 Error(s)**

## Test Status

**Fdp.Toolkits.Tests (after Task 3):** Pre-existing failures in Physics, Combat, and Navigation test suites (unrelated to Time changes). No time-related test failures introduced.

**Final full sweep (`dotnet test IOS-IG-SimHost.sln --no-build`):**
- `Hrot.ClusterRunner.Integration.Tests.dll`: Failed: 10, Passed: 130, Skipped: 4, Total: 144 — all 10 failures are pre-existing integration test failures (cluster spawn/mission/ghost position tests requiring live subsystems). None are related to descriptor ordinal changes.

**Verification sweeps:**
- `Select-String -Path "Hrot/Network/Hrot.Network.NED/Replication/**/*.cs" -Pattern "OrdinalValue = [0-9]|DescriptorOrdinal => [0-9]"` → **0 matches**
- `Select-String -Path "Hrot/Network/Hrot.Network.BDC/**/*.cs" -Pattern "=> 1000|=> 1002"` → **0 matches**

---

## Ordinal Collision Pre-Check (MPM-P2-T02 — EntityMasterIngressTranslator)

**Context:** `EntityMasterIngressTranslator` used `OrdinalValue = -2` to avoid collision with the FDP SST EntityMaster ordinal (-1). The design requires changing it to `(long)EDescriptorType.dtEntityMaster` (= 0), which is the same ordinal as `EntityMasterEgressTranslator`.

**Finding a — Translator storage (no KeyAlreadyExists risk):**
`CycloneIngressSystem` stores translators as `IDescriptorTranslator[]` (plain array, not a Dictionary). Multiple translators with the same ordinal can coexist in the array without exception. **SAFE.**

**Finding b — DescriptorOwnershipMap write semantics (silent overwrite):**
`DescriptorOwnershipMap.RegisterFromTranslator` uses dictionary indexer assignment:
```csharp
_descriptorToComponentIds[descriptorOrdinal] = ids;
```
Not `.Add()`. A second registration at ordinal 0 silently overwrites. **SAFE.**

**Finding c — TargetComponentIds comparison (no data divergence):**
`EntityMasterIngressTranslator` does **not** override `TargetComponentIds`; it uses the interface default (`Array.Empty<int>()`). `RegisterFromTranslator` returns early when the list is empty (`if (targetComponentIds == null || targetComponentIds.Count == 0) return;`), so the ingress translator never writes to the map regardless of its ordinal. `EntityMasterEgressTranslator` owns the ordinal-0 registration: `{ NetworkIdentity, TkbIdentity }`. After the change, ordinal 0 still maps exclusively to the egress translator's component IDs. **No data divergence. SAFE.**

**Conclusion:** Changing `OrdinalValue` from -2 to `(long)EDescriptorType.dtEntityMaster` (0) is safe on all three counts. Change applied.

---

## Developer Insights

**Q1: Issues encountered?**

No issues encountered. All changes were straightforward constant replacements. The `EntityMasterIngressTranslator` comment `// distinct from the FDP SST_EntityMaster ordinal (-1)` was removed since it described the -2 rationale which no longer applies.

**Q2: Additional sweep findings beyond the three specified?**

Yes — three additional raw ordinals were found and fixed:
- `NavigationIntentIngressTranslator.cs`: `DescriptorOrdinal => 52` → `(long)EDescriptorType.dtNavigationIntent`
- `NavigationStatusIngressTranslator.cs`: `DescriptorOrdinal => 53` → `(long)EDescriptorType.dtNavigationStatus`
- `EntityDamageEgressTranslator.cs`: `OrdinalValue   = 30` → `(long)EDescriptorType.dtEntityDamage`
- `EntityMissionEgressTranslator.cs`: `DescriptorOrdinal => 51` → `(long)EDescriptorType.dtEntityMission`
- `EntityMasterEgressTranslator.cs`: `DescriptorOrdinal => 0` → `(long)EDescriptorType.dtEntityMaster`

`DestroyEntityCommandEgressTranslator` has `OrdinalValue = -1003L` — this is an intentional out-of-band negative ordinal with no corresponding enum member; left unchanged.

**Q3: Surprises in BDC or time translators?**

`SwitchTimeModeDescriptorTranslator.cs` is in the `Time/` folder (not `Time/Translators/`). No other surprises — all translators used `private const long OrdinalValue` pattern (except `EntityMissionIngressTranslator` which used an inline property, and the Navigation translators which also used inline properties).

`MasterTimeSyncTranslator` used `205L` and `SlaveTimeSyncTranslator` used `206L` (with `L` suffix). Changed to `(long)TimeDescriptorType.TimeSyncRequest` and `(long)TimeDescriptorType.TimeSyncResponse` respectively.

**Q4: Ordinal or namespace concerns for future work?**

- `TimeDescriptorType` (201-206) and `BdcDescriptorType` (1000, 1002) are isolated from `EDescriptorType` — ACL boundaries respected.
- Ordinal gap at 204 in `TimeDescriptorType` is intentional (no enum member defined for it).
- `EDescriptorType` now covers 0-4, 30, 40, 51-66, 80-84, 90-91. The range 5-29, 31-39, 41-50, 56-59, 67-79, 85-89, 92+ are unallocated — future descriptor types should follow the existing grouping pattern.

---

## Suggested Commit Message

```
feat: descriptor ordinal cleanup - Phase 2 (MPM-P2-T01..T04)

Replace all magic integer literals in translator ordinal properties with
named enum constants.

MPM-P2-T01: Extend EDescriptorType with 15 new values
- dtMapEntitySymbol=40, dtSensorConfig=60..dtGroundClampingOverride=66,
  dtWeaponFireRequest=80..dtAudioTargetDetected=84,
  dtMissionControlRequest=90, dtMissionControlAck=91

MPM-P2-T02: Fix NED translator magic ordinals (8 translators)
- EntityMissionIngressTranslator: 50 -> dtEntityMission (fix: was wrong, should be 51)
- EntityMasterIngressTranslator: -2 -> dtEntityMaster (pre-check: safe, ingress has no TargetComponentIds)
- MapEntitySymbolIngressTranslator: 40 -> dtMapEntitySymbol
- Sweep finds: NavigationIntent/Status (52/53), EntityMission/Master egress (51/0), EntityDamage egress (30)

MPM-P2-T03: Create TimeDescriptorType enum + update 5 time translators
- New: FDP/Toolkits/Fdp.Toolkits/Time/TimeDescriptorType.cs (Fdp.Toolkit.Time)
- SwitchTimeModeEvent=201, MasterFrameOrder=202, SlaveFrameOrder=203,
  TimeSyncRequest=205, TimeSyncResponse=206

MPM-P2-T04: Create BdcDescriptorType enum + update 2 BDC translators
- New: Hrot/Network/Hrot.Network.BDC/BdcDescriptorType.cs (Hrot.BDC)
- EntityMaster=1000, WorldPos=1002

Build: IOS-IG-SimHost.sln succeeds with 0 errors
```
