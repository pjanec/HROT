# BATCH-11: Decouple IG, CGF, and Orchestrator from NED

**Batch Number:** BATCH-11
**Tasks:** TASK-P4-003
**Phase:** Phase 4 — Subsystem Decoupling
**Estimated Effort:** 3-4 hours
**Priority:** HIGH
**Dependencies:** BATCH-10 complete

---

## Onboarding & Workflow

### Developer Instructions

This batch completes TASK-P4-003: remove all direct `Hrot.Network.NED` (and old `Hrot.NED`)
references from `Hrot.CGF.csproj`, `Hrot.IG.csproj`, and `Hrot.Orchestrator.csproj`.

CGF is straightforward — its NED usage resolves through `Hrot.Network.Orchestration` already.
IG requires moving 5 translator files and adding a factory method. Orchestrator requires moving
`NedStatusCode` to `Hrot.Network.Orchestration`.

### Required Reading (in order)

1. **Task Definition:** `.dev/modular-2/TASK-DETAIL.md#task-p4-003` — success conditions
2. **Previous report:** `.dev/modular-2/reports/BATCH-10-REPORT.md`
3. **Pattern reference:** `Hrot.Network.NED/SimHost/NedSimHostAuxiliaryTranslators.cs` — follow this pattern for IG translator pack
4. **Factory interface:** `Hrot.Core/Network/INetworkFactory.cs` — add method here
5. **Orchestration types location:** `Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs` — types already here

### Source Code Areas

- **CGF project:** `Hrot.CGF/`, `Hrot.CGF/Hrot.CGF.csproj`
- **IG project:** `Hrot.IG/`, `Hrot.IG/Translators/`, `Hrot.IG/Hrot.IG.csproj`
- **Orchestrator project:** `Hrot.Orchestrator/`, `Hrot.Orchestrator/Hrot.Orchestrator.csproj`
- **NED project:** `Hrot.Network.NED/`, `Hrot.Network.NED/Hrot.Network.NED.csproj`
- **NED factory:** `Hrot.Network.NED/Factory/NedNetworkFactory.cs`
- **BDC factory:** `Hrot.Network.BDC/Factory/BdcNetworkFactory.cs`
- **Core network:** `Hrot.Core/Network/`

### Report Submission

When done, submit your report to: `.dev/modular-2/reports/BATCH-11-REPORT.md`

If you have questions, create: `.dev/modular-2/questions/BATCH-11-QUESTIONS.md`

---

## Context

After BATCH-10, three projects still reference `Hrot.Network.NED`:

| Project | Remaining NED Usage |
|---------|---|
| `Hrot.CGF` | `Hrot.NED.Descriptors.Orchestration` types (NodeOpCommand etc.) — but these are already DEFINED in `Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs` |
| `Hrot.IG` | 5 translator files in `Hrot.IG/Translators/` that use NED DDS descriptors |
| `Hrot.Orchestrator` | `Hrot.NED.Descriptors.Orchestration.*` (already in Orchestration) + `NedStatusCode` from `Hrot.NED.Messages` |

The key insight: `Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs` already defines
all the orchestration protocol types under namespace `Hrot.NED.Descriptors.Orchestration`. So CGF and
Orchestrator just need their project references pointed to `Hrot.Network.Orchestration` instead of
`Hrot.Network.NED`. The only blocker is `NedStatusCode` — move it to `Hrot.Network.Orchestration`.

---

## Objectives

1. `Hrot.CGF.csproj` references `Hrot.Network.Orchestration` instead of `Hrot.Network.NED`
2. `Hrot.Orchestrator.csproj` references `Hrot.Network.Orchestration` instead of `Hrot.Network.NED`
3. `Hrot.IG.csproj` has no reference to `Hrot.Network.NED`
4. All existing tests for these projects continue to pass

---

## Tasks

---

### Phase 1: Decouple Hrot.CGF from Hrot.Network.NED

**Files affected:** `Hrot.CGF/Hrot.CGF.csproj`

**Investigation first:** Confirm that the ONLY files in `Hrot.CGF/**/*.cs` that use NED are:
- `Hrot.CGF/CgfApplication.cs` — `using Hrot.NED.Descriptors.Orchestration;` (NodeOpCommand, NodeOpStatus, NodeHeartbeat)
- `Hrot.CGF/Modules/Orchestration/Handlers/FailLoudRecordReplayStub.cs` — `using Hrot.NED.Descriptors.Orchestration;` (NodeOpCommand)

Both use only the `Hrot.NED.Descriptors.Orchestration` namespace. All types in that namespace are
defined in `Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs`. No code changes needed.

**Step 1.1 — Update Hrot.CGF.csproj:**
- Remove: `<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />`
- Add: `<ProjectReference Include="..\Hrot.Network.Orchestration\Hrot.Network.Orchestration.csproj" />`

**Verify:** `dotnet build Hrot.CGF/Hrot.CGF.csproj` must produce 0 errors.

---

### Phase 2: Move NedStatusCode to Hrot.Network.Orchestration

This makes `NedStatusCode` available to `Hrot.Orchestrator` without a NED reference.

**Step 2.1 — Locate NedStatusCode:**
It lives in `Hrot.Network.NED/GenericMessages.cs` under namespace `Hrot.NED.Messages`.

**Step 2.2 — Move the enum:**
Cut the `NedStatusCode` enum (with its XML doc comment and the `/// <remarks>` table above it)
from `Hrot.Network.NED/GenericMessages.cs` and append it to
`Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs`.

Keep the namespace as `Hrot.NED.Messages` (place it inside `namespace Hrot.NED.Messages { ... }`
at the bottom of OrchestrationMessages.cs so no callers anywhere need `using` changes).

**Step 2.3 — Add Hrot.Network.Orchestration reference to Hrot.Network.NED:**
`Hrot.Network.NED/Hrot.Network.NED.csproj` must add:
```xml
<ProjectReference Include="..\Hrot.Network.Orchestration\Hrot.Network.Orchestration.csproj" />
```

This gives all existing Hrot.Network.NED code the `NedStatusCode` type via the new location.
No `using` statement changes needed anywhere since the namespace is unchanged.

**Verify no circular deps:** `Hrot.Network.Orchestration.csproj` does NOT reference
`Hrot.Network.NED` (confirmed), so adding the reverse direction is safe.

---

### Phase 3: Decouple Hrot.Orchestrator from Hrot.Network.NED

**Investigation first:** Confirm that all NED usages in `Hrot.Orchestrator/**/*.cs` are limited to:
- `Hrot.NED.Descriptors.Orchestration.*` (all defined in Hrot.Network.Orchestration) 
- `Hrot.NED.Messages.NedStatusCode` (moved to Hrot.Network.Orchestration in Phase 2)
- `Hrot.NED.Messages.AssetInventoryTopic` (already defined in Hrot.Network.Orchestration)

**Step 3.1 — Update Hrot.Orchestrator.csproj:**
- Remove: `<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />`
- Add: `<ProjectReference Include="..\Hrot.Network.Orchestration\Hrot.Network.Orchestration.csproj" />`

No code changes needed inside Hrot.Orchestrator/*.cs files — the namespaces are identical.

**Verify:** `dotnet build Hrot.Orchestrator/Hrot.Orchestrator.csproj` must produce 0 errors.

---

### Phase 4: Move IG Translator Files to Hrot.Network.NED/IG/

**Files to move (5 files):**

| Source | Dest | Old namespace | New namespace |
|--------|------|---|---|
| `Hrot.IG/Translators/IgMissionIngressTranslator.cs` | `Hrot.Network.NED/IG/IgMissionIngressTranslator.cs` | `Hrot.IG.Translators` | `Hrot.Network.NED.IG` |
| `Hrot.IG/Translators/WeaponFireIngressTranslator.cs` | `Hrot.Network.NED/IG/WeaponFireIngressTranslator.cs` | `Hrot.IG.Translators` | `Hrot.Network.NED.IG` |
| `Hrot.IG/Translators/GroundClampingOverrideTranslator.cs` | `Hrot.Network.NED/IG/GroundClampingOverrideTranslator.cs` | `Hrot.IG.Translators` | `Hrot.Network.NED.IG` |
| `Hrot.IG/Translators/AudioTargetDetectedIngressTranslator.cs` | `Hrot.Network.NED/IG/AudioTargetDetectedIngressTranslator.cs` | `Hrot.IG.Translators` | `Hrot.Network.NED.IG` |
| `Hrot.IG/Translators/ContextActionsUpdateTranslator.cs` | `Hrot.Network.NED/IG/ContextActionsUpdateTranslator.cs` | `Hrot.IG.Translators` | `Hrot.Network.NED.IG` |

The `.gitkeep` file stays in `Hrot.IG/Translators/` (do not delete it).

**Namespace change:** In each moved file, change `namespace Hrot.IG.Translators` to
`namespace Hrot.Network.NED.IG`. No other changes needed in the translator files themselves.

---

### Phase 5: Add IIgTranslators Interface and Factory Method

**Step 5.1 — Create interface `Hrot.Core/Network/IIgTranslators.cs`:**

```csharp
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Fdp.Kernel;

namespace Hrot.Core.Network;

/// <summary>
/// Provides IG-specific DDS ingress translators.
/// </summary>
public interface IIgTranslators
{
    /// <summary>
    /// Creates all IG DDS ingress translators for the given session.
    /// Returns an empty list in headless mode or when NED is not available.
    /// </summary>
    IReadOnlyList<IDescriptorTranslator> GetTranslators(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        FdpEventBus bus,
        GhostCreationSystem? ghostCreationSystem,
        long localNodeId,
        bool headless);
}
```

**Step 5.2 — Add method to `Hrot.Core/Network/INetworkFactory.cs`:**

```csharp
/// <summary>Creates the IG-specific DDS ingress translator provider.</summary>
IIgTranslators CreateIgTranslators();
```

**Step 5.3 — Create `Hrot.Network.NED/IG/NedIgTranslators.cs`:**

```csharp
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.Core.Network;

namespace Hrot.Network.NED.IG;

/// <summary>
/// NED implementation of <see cref="IIgTranslators"/>.
/// Creates all NED IG ingress translators for the given session context.
/// </summary>
internal sealed class NedIgTranslators : IIgTranslators
{
    public IReadOnlyList<IDescriptorTranslator> GetTranslators(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        FdpEventBus bus,
        GhostCreationSystem? ghostCreationSystem,
        long localNodeId,
        bool headless)
    {
        var translators = new List<IDescriptorTranslator>();

        // ContextActionsUpdateTranslator is always added (not headless-gated).
        translators.Add(new ContextActionsUpdateTranslator(
            participant, entityMap, bus, ghostCreationSystem!, localNodeId));

        if (!headless && ghostCreationSystem != null)
        {
            translators.Add(new IgMissionIngressTranslator(
                participant, entityMap, ghostCreationSystem, localNodeId));
            translators.Add(new WeaponFireIngressTranslator(
                participant, entityMap));
            translators.Add(new GroundClampingOverrideTranslator(
                participant, entityMap));
            translators.Add(new AudioTargetDetectedIngressTranslator(
                participant, entityMap));
        }

        return translators;
    }
}
```

**IMPORTANT:** Verify the actual constructor signatures of the 5 translator files before writing
this code. The constructor arg list above is an approximation — match the actual signatures.
Also verify whether `ContextActionsUpdateTranslator` is really headless-safe or also gated.

**Step 5.4 — Implement in NedNetworkFactory:**

Add to `NedNetworkFactory`:
```csharp
/// <inheritdoc/>
public IIgTranslators CreateIgTranslators()
    => new NedIgTranslators();
```

(NedIgTranslators takes no constructor args; all args flow through GetTranslators at call time.)

**Step 5.5 — Implement null stub in NedNetworkFactory (at bottom):**

```csharp
/// <summary>No-op stub for IIgTranslators (headless/offline mode).</summary>
internal sealed class NullIgTranslators : IIgTranslators
{
    public IReadOnlyList<IDescriptorTranslator> GetTranslators(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        FdpEventBus bus,
        GhostCreationSystem? ghostCreationSystem,
        long localNodeId,
        bool headless)
        => Array.Empty<IDescriptorTranslator>();
}
```

Note: if BdcNetworkFactory also has a stub section, add `NullIgTranslators` there or in a shared
location — it can be internal to whichever file makes sense given the existing pattern.

**Step 5.6 — Implement `CreateIgTranslators()` in BdcNetworkFactory:**

```csharp
public IIgTranslators CreateIgTranslators() => new NullIgTranslators();
```

---

### Phase 6: Update IgApplication.cs to Use the Factory

**Context:** `IgApplication.cs` currently creates the 5 translator instances directly using the
class names from `Hrot.IG.Translators`. After Phase 4, those classes live in `Hrot.Network.NED.IG`
so they are no longer accessible (IG will not reference NED).

The fix: pass an `INetworkFactory?` into `IgApplication` and use `factory.CreateIgTranslators()`.

**Step 6.1 — Check IgApplication constructor / InitializeEmbedded signature:**
Look at how `IgSubsystem.cs` creates and initializes IgApplication. Find where to inject
the factory.

**Step 6.2 — Add optional `INetworkFactory?` field to IgApplication:**
```csharp
private readonly INetworkFactory? _networkFactory;
```
Add it as an optional constructor parameter (or inject via a method similar to how SimHostApp
received it). Follow whatever pattern is already established.

**Step 6.3 — In IgApplication.InitializeNetwork(), replace direct translator instantiation:**

Replace the block that creates `customTranslators` entries for the 5 NED-specific translators:
```csharp
// OLD (to be removed):
customTranslators.Add(new Hrot.IG.Translators.IgMissionIngressTranslator(...));
customTranslators.Add(new Hrot.IG.Translators.GroundClampingOverrideTranslator(...));
// ... etc. (all 5 Hrot.IG.Translators.* entries including ContextActionsUpdateTranslator)
```
With:
```csharp
// NEW:
if (_networkFactory != null)
{
    var igTranslators = _networkFactory.CreateIgTranslators();
    foreach (var t in igTranslators.GetTranslators(
        participant, _entityMap, _world.Bus, _ghostCreationSystem, _effectiveInstanceId, _headless))
    {
        customTranslators.Add(t);
    }
}
```

**Step 6.4 — Update IgSubsystem.cs to pass the factory:**
In `IgSubsystem.cs`, pass the `INetworkFactory` to IgApplication when it is available.
Follow the same pattern used in `SimHostSubsystem.cs` (which passes factory to SimHostApp).

**Step 6.5 — Remove stale using directives:**
Remove `using Hrot.IG.Translators;` and any other NED-sourced usings from `IgApplication.cs`
once the translator calls are replaced.

---

### Phase 7: Remove Hrot.Network.NED from Hrot.IG.csproj

**Prerequisite:** Phase 4-6 complete; no remaining `Hrot.NED.*` or `Hrot.Network.NED.*` usages
in any `Hrot.IG/**/*.cs` file.

**Step 7.1 — Verify zero NED usages in Hrot.IG:**
Run:
```
grep -r "Hrot\.NED\|Hrot\.Network\.NED" Hrot.IG/ --include="*.cs"
```
Must return zero matches.

**Step 7.2 — Remove from Hrot.IG.csproj:**
```xml
<!-- REMOVE this line: -->
<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />
```

**Verify:** `dotnet build Hrot.IG/Hrot.IG.csproj` must produce 0 errors.

---

## Build and Test Verification

After all phases are complete:

```powershell
cd D:\Work\IOS-IG-SimHost-FDP-2

# 1 - Verify project references
dotnet list Hrot.IG/Hrot.IG.csproj reference
dotnet list Hrot.CGF/Hrot.CGF.csproj reference
dotnet list Hrot.Orchestrator/Hrot.Orchestrator.csproj reference

# 2 - Full solution build
dotnet build IOS-IG-SimHost.sln -v quiet

# 3 - Unit tests (no integration: integration tests take longer, check after build)
dotnet test IOS-IG-SimHost.sln --filter "FullyQualifiedName!~Integration" -v quiet
```

**Success conditions:**
- `dotnet list Hrot.IG reference` output: NO line containing `Hrot.Network.NED`
- `dotnet list Hrot.CGF reference` output: NO line containing `Hrot.Network.NED`
- `dotnet list Hrot.Orchestrator reference` output: NO line containing `Hrot.Network.NED`
- Build: **0 errors**
- Unit tests: **all pass** (same counts as BATCH-10: 433 unit, 39/41 integration)

---

## Report Requirements

Create `.dev/modular-2/reports/BATCH-11-REPORT.md` containing:

1. **Summary table** — for each phase: Status (Done/Partial/Skipped), blockers encountered
2. **Reference verification** — output of the three `dotnet list ... reference` commands
3. **Test results** — exact passing counts from unit and integration test runs
4. **Build output** — confirm 0 errors / 0 warnings (or list any new warnings)
5. **Deferred items** — if any phase was skipped, explain why and propose a debt item
