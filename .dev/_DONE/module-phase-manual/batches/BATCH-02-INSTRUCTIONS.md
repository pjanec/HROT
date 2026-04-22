# BATCH-02: Phase 2 - Descriptor Ordinal Cleanup

**Batch Number:** BATCH-02  
**Tasks:** MPM-P2-T01, MPM-P2-T02, MPM-P2-T03, MPM-P2-T04  
**Phase:** Phase 2 - Descriptor Ordinal Cleanup  
**Estimated Effort:** 4-6 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 completed

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch eliminates magic integer literals from all network translator ordinal properties. You will extend one existing enum, create two new enums (in separate domains), and update translator files to reference named constants. No logic changes - only replace raw numbers with enum member references.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` - How to work with batches
2. **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` - See MPM-P2-T01, MPM-P2-T02, MPM-P2-T03, MPM-P2-T04
3. **Design Document:** `.dev/module-phase-manual/DESIGN.md` - Sections 2.1, 2.2, 2.3, 2.4
4. **Previous Review:** `.dev/module-phase-manual/reviews/BATCH-01-REVIEW.md` - Context

### Source Code Locations
- `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs` - EDescriptorType enum to extend
- `Hrot/Network/Hrot.Network.NED/Replication/` - NED translator files to update
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/` - Time translator files to update
- `FDP/Toolkits/Fdp.Toolkits/Time/` - Location for new `TimeDescriptorType.cs`
- `Hrot/Network/Hrot.Network.BDC/Replication/` - BDC translator files to update
- `Hrot/Network/Hrot.Network.BDC/` - Location for new `BdcDescriptorType.cs`

### Test Projects
- `Hrot/Network/Hrot.Network.NED.Tests/` (if it exists)
- `FDP/Toolkits/Fdp.Toolkits.Tests/` - Run after Task 3 changes
- `Hrot/Network/Hrot.Network.BDC.Tests/` (if it exists)
- Full solution build: `dotnet build IOS-IG-SimHost.sln` from repo root

### Report Submission
**When done, submit your report to:**  
`.dev/module-phase-manual/reports/BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev/module-phase-manual/questions/BATCH-02-QUESTIONS.md`

---

## Context

This is the second batch of the MPM project. BATCH-01 eliminated dead code. This batch replaces the remaining magic integer literals in all network translator `DescriptorOrdinal` and `OrdinalValue` properties with named enum constants.

The ACL principle requires that each domain (NED, FDP time toolkit, BDC) owns its own descriptor type enum. `TimeDescriptorType` must NOT reference `Hrot.NED.Descriptors`. `BdcDescriptorType` must NOT reference `Hrot.NED.Descriptors`.

**Related Tasks:**
- [MPM-P2-T01](./../TASK-DETAIL.md#mpm-p2-t01---extend-edescriptortype-enum) - Extend EDescriptorType enum
- [MPM-P2-T02](./../TASK-DETAIL.md#mpm-p2-t02---fix-ned-translator-magic-ordinals) - Fix NED translator magic ordinals
- [MPM-P2-T03](./../TASK-DETAIL.md#mpm-p2-t03---create-timedescriptortype-enum-and-update-time-translators) - Create TimeDescriptorType enum and update time translators
- [MPM-P2-T04](./../TASK-DETAIL.md#mpm-p2-t04---create-bdcdescriptortype-enum-and-update-bdc-translators) - Create BdcDescriptorType enum and update BDC translators

---

## 🎯 Batch Objectives

Replace all raw integer literals in translator ordinal properties with type-safe named enum constants so the codebase is self-documenting and ordinal collisions are detectable at compile time.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing build:**

1. **Task 1:** Extend enum → Build → **Build passes** ✅
2. **Task 2:** Fix NED ordinals → Build → Run NED tests → **All pass** ✅
3. **Task 3:** Create TimeDescriptorType + update translators → Build → Run time toolkit tests → **All pass** ✅
4. **Task 4:** Create BdcDescriptorType + update translators → Build → Final full build → **All pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ **Build passes** (`dotnet build IOS-IG-SimHost.sln` from `d:\Work\IOS-IG-SimHost-FDP-2`)
- ✅ **Relevant tests pass**

**No stopping to ask for permission. Fix any compilation errors before moving on. Work autonomously until all success criteria are met.**

---

## ✅ Tasks

### Task 1: Extend EDescriptorType Enum (MPM-P2-T01)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p2-t01---extend-edescriptortype-enum)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 2.1

**File to modify:** `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs`

Add the following enum values to `EDescriptorType` (preserve ALL existing values unchanged):
```
dtMapEntitySymbol       = 40
dtSensorConfig          = 60
dtRaycastRequestBatch   = 61
dtSensorTrackState      = 62
dtRaycastResponseBatch  = 63
dtPathRequestBatch      = 64
dtPathResponseBatch     = 65
dtGroundClampingOverride= 66
dtWeaponFireRequest     = 80
dtWeaponFire            = 81
dtMunitionDetonation    = 82
dtEntityHitDamage       = 83
dtAudioTargetDetected   = 84
dtMissionControlRequest = 90
dtMissionControlAck     = 91
```

The DESIGN.md § 2.1 shows the complete final enum with all existing + new values and their associated translator names as comments.

**Verify:**
- `dotnet build IOS-IG-SimHost.sln` passes.
- No existing enum values changed.

---

### Task 2: Fix NED Translator Magic Ordinals (MPM-P2-T02)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p2-t02---fix-ned-translator-magic-ordinals)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 2.2

**Specific files to modify:**

1. `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityMissionIngressTranslator.cs`
   - Change `DescriptorOrdinal => 50` to `DescriptorOrdinal => (long)EDescriptorType.dtEntityMission`
   - Note: `dtEntityMission = 51` (not 50 - the old value was wrong!)

2. `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityMasterIngressTranslator.cs`
   - Change `OrdinalValue = -2` to `OrdinalValue = (long)EDescriptorType.dtEntityMaster`
   - Ordinal collision pre-check (REQUIRED before this change - document findings in report):
     a. Confirm `CycloneIngressSystem`, `CycloneEgressSystem`, and `CycloneNetworkCleanupSystem` store translators as plain arrays (not Dictionary). If so, no `KeyAlreadyExistsException` can occur.
     b. Confirm `DescriptorOwnershipMap._descriptorToComponentIds` uses indexer assignment `[key] = value` (not `.Add()`). A second write with ordinal `0` must silently overwrite.
     c. Confirm `EntityMasterIngressTranslator.TargetComponentIds` and `EntityMasterEgressTranslator.TargetComponentIds` return the same component IDs (safe overwrite).

3. `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/MapEntitySymbolIngressTranslator.cs`
   - Change `OrdinalValue = 40` to `OrdinalValue = (long)EDescriptorType.dtMapEntitySymbol`

**Additional sweep:** Scan all files under `Hrot/Network/Hrot.Network.NED/Replication/` for any remaining `OrdinalValue = [digit]` or `DescriptorOrdinal => [digit]` patterns. Update each to reference the corresponding `EDescriptorType` member added in Task 1.

**Verify:**
- `dotnet build IOS-IG-SimHost.sln` passes.
- `Select-String -Path "Hrot/Network/Hrot.Network.NED/Replication/**/*.cs" -Pattern "OrdinalValue = [0-9]|DescriptorOrdinal => [0-9]"` - should yield zero results.

---

### Task 3: Create TimeDescriptorType Enum and Update Time Translators (MPM-P2-T03)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p2-t03---create-timedescriptortype-enum-and-update-time-translators)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 2.3

**New file to create:** `FDP/Toolkits/Fdp.Toolkits/Time/TimeDescriptorType.cs`

Exact content (from TASK-DETAIL.md):
```csharp
namespace Fdp.Toolkit.Time
{
    public enum TimeDescriptorType
    {
        SwitchTimeModeEvent = 201,
        MasterFrameOrder    = 202,
        SlaveFrameOrder     = 203,
        TimeSyncRequest     = 205,
        TimeSyncResponse    = 206
    }
}
```

**Files to modify** (update `OrdinalValue` from raw integer to enum reference):
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/SwitchTimeModeDescriptorTranslator.cs`: `OrdinalValue = 201` → `OrdinalValue = (long)TimeDescriptorType.SwitchTimeModeEvent`
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/MasterLockstepTranslator.cs`: `OrdinalValue = 202` → `OrdinalValue = (long)TimeDescriptorType.MasterFrameOrder`
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/SlaveLockstepTranslator.cs`: `OrdinalValue = 203` → `OrdinalValue = (long)TimeDescriptorType.SlaveFrameOrder`
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/MasterTimeSyncTranslator.cs`: `OrdinalValue = 205` → `OrdinalValue = (long)TimeDescriptorType.TimeSyncRequest`
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/SlaveTimeSyncTranslator.cs`: `OrdinalValue = 206` → `OrdinalValue = (long)TimeDescriptorType.TimeSyncResponse`

**IMPORTANT:** `TimeDescriptorType` must NOT reference `Hrot.NED.Descriptors` or any Hrot namespace. It lives entirely in the FDP toolkit layer.

**Verify:**
- `TimeDescriptorType.cs` exists at `FDP/Toolkits/Fdp.Toolkits/Time/TimeDescriptorType.cs`.
- `dotnet build IOS-IG-SimHost.sln` passes.
- Run time toolkit tests: `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build` (if the project exists).
- The numeric values at runtime are unchanged (201, 202, 203, 205, 206).

---

### Task 4: Create BdcDescriptorType Enum and Update BDC Translators (MPM-P2-T04)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p2-t04---create-bdcdescriptortype-enum-and-update-bdc-translators)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 2.4

**New file to create:** `Hrot/Network/Hrot.Network.BDC/BdcDescriptorType.cs`

Exact content (from TASK-DETAIL.md):
```csharp
namespace Hrot.BDC
{
    public enum BdcDescriptorType
    {
        EntityMaster = 1000,
        WorldPos     = 1002
    }
}
```

**Files to modify:**
- `Hrot/Network/Hrot.Network.BDC/Replication/BdcEntityMasterTranslator.cs`: `DescriptorOrdinal => 1000` → `DescriptorOrdinal => (long)BdcDescriptorType.EntityMaster`
- `Hrot/Network/Hrot.Network.BDC/Replication/BdcWorldPosTranslator.cs`: `DescriptorOrdinal => 1002` → `DescriptorOrdinal => (long)BdcDescriptorType.WorldPos`

**IMPORTANT:** `BdcDescriptorType` must NOT reference `Hrot.NED.Descriptors` or any NED namespace.

**Verify:**
- `BdcDescriptorType.cs` exists at `Hrot/Network/Hrot.Network.BDC/BdcDescriptorType.cs`.
- `dotnet build IOS-IG-SimHost.sln` passes.
- `Select-String -Path "Hrot/Network/Hrot.Network.BDC/**/*.cs" -Pattern "=> 1000|=> 1002"` yields zero matches in translator files.

---

## 🧪 Testing Requirements

1. **Build after each task:** `dotnet build IOS-IG-SimHost.sln` from `d:\Work\IOS-IG-SimHost-FDP-2`
2. **After Task 3:** `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
3. **Final full test sweep:**
   ```
   dotnet test IOS-IG-SimHost.sln --no-build
   ```

No new tests required by this batch - ordinal cleanup is mechanical. However, verify existing time toolkit tests still pass (they validate ordinal values at runtime).

---

## 📊 Report Requirements

Submit your report to `.dev/module-phase-manual/reports/BATCH-02-REPORT.md`.

```markdown
# BATCH-02 Report

## Completion Status
- [ ] MPM-P2-T01: Extend EDescriptorType enum
- [ ] MPM-P2-T02: Fix NED translator magic ordinals
- [ ] MPM-P2-T03: Create TimeDescriptorType + update time translators
- [ ] MPM-P2-T04: Create BdcDescriptorType + update BDC translators

## Build Status
[Result of: dotnet build IOS-IG-SimHost.sln]

## Test Status
[Result of relevant test runs]

## Ordinal Collision Pre-Check (Required for MPM-P2-T02)
[Document findings about EntityMasterIngressTranslator ordinal change from -2 to 0]

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did the additional sweep (Task 2) find any magic ordinals beyond the three specified?

**Q3:** Were there any surprises in the BDC or time translator files (e.g., already-named ordinals, different property names)?

**Q4:** Any ordinal or namespace concerns you'd flag for future work?

## Suggested Commit Message
[Your suggested git commit message]
```

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `EDescriptorType` contains all 15 new values listed in Task 1 (no existing values changed)
- [ ] All NED translators in `Hrot.Network.NED/Replication/` use `(long)EDescriptorType.dtXxx` - zero raw integer literals remain
- [ ] `TimeDescriptorType.cs` exists; all 5 time translators use `(long)TimeDescriptorType.Xxx`
- [ ] `BdcDescriptorType.cs` exists; both BDC translators use `(long)BdcDescriptorType.Xxx`
- [ ] `dotnet build IOS-IG-SimHost.sln` passes with zero errors
- [ ] Existing toolkit tests pass
- [ ] Report submitted

---

## ⚠️ Common Pitfalls to Avoid

- **`EntityMissionIngressTranslator` had ordinal 50 but `dtEntityMission = 51`.** The old value was WRONG. Change it to `(long)EDescriptorType.dtEntityMission` which is 51.
- **`EntityMasterIngressTranslator` used -2 as collision avoidance.** The design says it's safe to change to 0 (same as egress) because translators are stored as arrays, not dictionaries. Complete the pre-check and document it.
- **ACL boundaries:** `TimeDescriptorType` is in `Fdp.Toolkit.Time` namespace. `BdcDescriptorType` is in `Hrot.BDC` namespace. Neither may reference NED types.
- **Do not touch any translator that already uses named enum values** (e.g., `EntityInfoIngressTranslator` and `EntityInfoEgressTranslator` already use `EDescriptorType`). Leave those alone.
- **Don't stop to ask.** Fix compilation errors yourself. Run tests yourself. Report done only when everything is green.

---

## 📚 Reference Materials
- **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` - See MPM-P2-T01 through MPM-P2-T04
- **Design:** `.dev/module-phase-manual/DESIGN.md` - Sections 2.1, 2.2, 2.3, 2.4
- **Previous Review:** `.dev/module-phase-manual/reviews/BATCH-01-REVIEW.md`
