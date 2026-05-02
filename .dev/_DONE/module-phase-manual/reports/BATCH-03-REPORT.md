# BATCH-03 Report

**Batch:** BATCH-03
**Developer:** GitHub Copilot
**Date:** 2025-07-15
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| MPM-P3-T01 | [x] | Created `INetworkTranslator` base interface |
| MPM-P3-T02 | [x] | Refactored `IDescriptorTranslator` to extend `INetworkTranslator` |
| MPM-P3-T03 | [x] | Extracted `CycloneBaseTranslator`, created `INetworkEventTranslator`, updated event translators |
| MPM-P3-T04 | [x] | Updated ingress/egress systems to `INetworkTranslator[]`, removed `GetDirectionLabel` hack |

---

## Build Status

`dotnet build IOS-IG-SimHost.sln` -> **Build succeeded. 0 Error(s)**

---

## Test Status

**Fdp.Network.Cyclone.Tests (after Tasks 2, 3, 4):**
```
Passed!  - Failed: 0, Passed: 40, Skipped: 0, Total: 40, Duration: 2 s
```

**Full solution sweep (`dotnet test IOS-IG-SimHost.sln --no-build`):**
```
Failed!  - Failed: 10, Passed: 130, Skipped: 4, Total: 144, Duration: 6 m 34 s
           - Hrot.ClusterRunner.Integration.Tests.dll (net8.0)
```

All 10 failures are pre-existing integration test failures identical to the BATCH-02 baseline:
`SpawnMovingVehicle_IgPositionContinuesToUpdate`, `ExCon_CommitMissionAsync_ResolvesWithAck_NotTimeout`,
`AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate`,
`SimHost_WanderMission_EntityMovesAfterBehaviorActivation`, `CGF_MovingVehicle_GhostPositionUpdates`,
`DistributedLoad_TranslatesNetworkIds_AndSpawnsEntitiesWithRemappedMissionPlan`,
`UrbanCombatExtractedToJson_ExecutesSuccessfullyInLiveMode`, and three others.
None are related to BATCH-03 changes. No new test failures were introduced.

---

## Implementation Summary

### MPM-P3-T01: Create INetworkTranslator Base Interface

**File created:** `FDP/Engine/Fdp.Core/Abstractions/INetworkTranslator.cs`

Pure addition. Extracted the six members that logically belong at the "can-participate-in-network-IO" level:
`TopicName`, `Direction`, `ReceivedSampleCount`, `SentSampleCount`, `PollIngress`, `ScanAndPublish`.
No existing code was changed. Build passed immediately.

### MPM-P3-T02: Refactor IDescriptorTranslator to Extend INetworkTranslator

**File modified:** `FDP/Engine/Fdp.Core/Abstractions/IDescriptorTranslator.cs`

Added `: INetworkTranslator` to the interface declaration and removed the six members now inherited
from `INetworkTranslator`. Remaining members: `DescriptorOrdinal`, `TargetComponentIds` (with default
implementation), `ApplyToEntity`, `Dispose`. No concrete translators required any changes because
`CycloneTranslator<>` already implemented all six methods.

All 40 Cyclone tests passed after this change.

### MPM-P3-T03: Extract CycloneBaseTranslator + INetworkEventTranslator + Update Event Translators

**Step A - New file:** `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneBaseTranslator.cs`

Abstract base class carrying the shared `INetworkTranslator` state: `TopicName` (set in constructor),
`ReceivedSampleCount` / `SentSampleCount` (protected set), abstract `Direction`, abstract `PollIngress`
and `ScanAndPublish`. Constructor guards against null `topicName` with `ArgumentNullException`.

**Step B - New file:** `FDP/Engine/Fdp.Core/Abstractions/INetworkEventTranslator.cs`

Marker interface `INetworkEventTranslator : INetworkTranslator` with no additional members.

**Files modified (event translators and descriptor translator):**
- `CycloneTranslator<TDds, TView>`: Added `: CycloneBaseTranslator` to the inheritance chain,
  removed the duplicate `TopicName` field and `ReceivedSampleCount`/`SentSampleCount` fields,
  constructor now calls `base(topicName)`.
- `CycloneNativeEventTranslator<TEcs, TDds>`: Changed `IDescriptorTranslator` to `CycloneBaseTranslator,
  INetworkEventTranslator`, removed duplicate fields, constructor calls `base(topicName)`.
- `CycloneManagedEventTranslator<TEcs, TDds>`: Same pattern as `CycloneNativeEventTranslator`.
- `MultiInstanceCycloneTranslator<TDds, TView>`: Verified it inherits from `CycloneTranslator` so
  gains `CycloneBaseTranslator` transitively; no changes needed.

Compile errors encountered during Step A: `CycloneBaseTranslator` initially declared `PollIngress`
and `ScanAndPublish` as abstract but the in-memory Roslyn model was stale from a prior partial
implementation attempt. Fixed by confirming the file matched the final design. Event translator
`override` modifiers replaced `new` modifiers after `CycloneBaseTranslator` declared these as abstract.

**Hrot event translators also updated** (`FireInteractionEventTranslator`, `ContextActionsUpdateTranslator`):
These two were still declared as `IDescriptorTranslator` but are transient event translators with no
descriptor ordinal. They were passing `IDescriptorTranslator[]` into site registrations that expected
a descriptor translator. After changing them to `CycloneNativeEventTranslator : CycloneBaseTranslator,
INetworkEventTranslator`, the call sites in `SharedTranslatorPack.cs` and `NedIgTranslators.cs` that
passed them into ingress/egress translator arrays also needed updating to use `INetworkTranslator[]`
instead of `IDescriptorTranslator[]`. This was the correct fix per the batch objectives.

All 40 Cyclone tests passed after this change.

### MPM-P3-T04: Update Ingress/Egress Systems + Remove GetDirectionLabel

**Files modified:**
- `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneIngressSystem.cs`: Changed constructor and field from
  `IDescriptorTranslator[]` to `INetworkTranslator[]`. The `IReadOnlyList<INetworkTranslator> Translators`
  property and `_translatorProfileData` dictionary key type were updated accordingly.
- `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneEgressSystem.cs`: Same change.
- `FDP/Network/Fdp.Network.Cyclone/Modules/CycloneNetworkModule.cs`: Added
  `using INetworkTranslator = Fdp.Interfaces.INetworkTranslator;` alias to resolve the reference used
  when constructing the system with the combined translator array.
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/ArchitectureDiagnosticsPanel.cs`: Deleted the
  `GetDirectionLabel(string systemName)` method (~8 lines, string switch matching class name suffix
  to direction label). In `EnumerateTranslatorRows`, replaced `GetDirectionLabel(system.GetType().Name)`
  with `translator.Direction.ToString()`.

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three sets of compile errors were encountered in sequence across tasks, all resolved:

1. **CS0535 on `CycloneBaseTranslator`** - `PollIngress` and `ScanAndPublish` were missing. The file
   initially only had abstract signatures declared correctly, but a stale Roslyn workspace was producing
   false negatives. Confirmed the file was correct and re-ran the build; errors disappeared.

2. **CS0533 on event translators** - `CycloneNativeEventTranslator` and `CycloneManagedEventTranslator`
   had `public override void PollIngress(...)` but were not declaring `CycloneBaseTranslator` as the
   base class yet. Fixed by updating both class declarations to extend `CycloneBaseTranslator`.

3. **CS0266 / CS1503 in Hrot** - `FireInteractionEventTranslator` and `ContextActionsUpdateTranslator`
   (in `Hrot.Map.Common` and `Hrot.Network.NED.IG`) were still typed as `IDescriptorTranslator` at
   their call sites. These are genuine event translators with no descriptor ordinal; the correct fix
   was to change the storage type to `INetworkTranslator` at the registration sites, which is
   consistent with the batch's T04 objective.

4. **CS0246 `INetworkTranslator` in `CycloneNetworkModule.cs`** - The module was constructing the
   combined ingress/egress translator array but lacked a using directive for `Fdp.Interfaces`. Added
   a `using INetworkTranslator = Fdp.Interfaces.INetworkTranslator;` alias consistent with the
   existing `IDescriptorTranslator` alias pattern in that file.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

The Hrot-side call sites in `SharedTranslatorPack.cs` and `NedIgTranslators.cs` were mixing event
translators into `IDescriptorTranslator[]` arrays. This was only possible because event translators
previously had to implement `IDescriptorTranslator` with throw-not-implemented stubs. The refactoring
exposed this correctly. The fix (changing storage type to `INetworkTranslator[]`) is appropriate.

One minor concern: `CycloneEgressSystem` iterates all `INetworkTranslator[]` and calls `ScanAndPublish`
on all of them. For `INetworkEventTranslator` translators that are ingress-only (e.g., event dispatchers),
`ScanAndPublish` is a no-op by design, but the iteration still runs. This is not a correctness issue and
has negligible overhead.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

The `CycloneBaseTranslator` constructor was given an `ArgumentNullException.ThrowIfNull` guard on
`topicName` rather than a raw null-propagation. This matches the existing `CycloneTranslator<>` pattern
where null topic names would later cause DDS registration to fail with a cryptic message.

For `MultiInstanceCycloneTranslator`, no changes were needed because it already inherits from
`CycloneTranslator<>` which in turn inherits `CycloneBaseTranslator`. This was verified before
deciding not to touch the file.

**Q4: Did you find any other places where GetDirectionLabel-style hacks exist?**

No. A search of `GetDirectionLabel` across the entire solution returns zero matches in `.cs` files
after the deletion. The `ArchitectureDiagnosticsPanel` was the only location using this pattern.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

None introduced by this batch. The type hierarchy changes are pure compile-time reorganization.
The `Direction` property was previously computed from the concrete class hierarchy via the enum
flags; it is now declared on the abstract base and overridden in subclasses, which is equally
efficient.

---

## Deviations from Instructions

**None.** All tasks were implemented exactly per specification. The Hrot event translator fix
(changing `SharedTranslatorPack.cs` and `NedIgTranslators.cs` storage to `INetworkTranslator[]`)
was a necessary consequence of T03 - these were not mentioned explicitly in T03 but are clearly
within scope of "update call sites to use the new interface type".

---

## Outstanding Issues / Next Steps

None. Build is clean with 0 errors and 0 warnings related to these changes. All 40 Cyclone unit
tests pass. The 10 integration test failures are pre-existing and unrelated to this batch.
