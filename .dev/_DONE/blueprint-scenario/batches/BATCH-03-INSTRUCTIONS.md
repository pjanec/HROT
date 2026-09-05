# BATCH-03: BlueprintMaterializationSystem (BSA-203)

**Batch Number:** BATCH-03  
**Tasks:** BSA-203 (BlueprintMaterializationSystem — tier pre-provision + ceiling guard + ECB removal)  
**Phase:** Phase 2 — Static scenario assignment (CGF genesis)  
**Estimated Effort:** 3-5 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (core seam), BATCH-02 (NoSave + translator + Intent component)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Create the CGF genesis system that consumes `InitialBlueprintsIntent`, pre-provisions the correct blackboard tier, attaches blueprints via the core seam, and removes the intent via ECB. Mirrors the existing `GenesisMaterializationSystem` pattern.

### Required Reading (IN ORDER)
1. **Design Document:** `.dev/_DONE/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §4 (materialization steps), §5 (tier pre-provisioning + ceiling guard)
2. **Task Details:** `.dev/_DONE/blueprint-scenario/TASK-DETAIL.md` — BSA-203 section
3. **Task Tracker:** `.dev/_DONE/blueprint-scenario/TASK-TRACKER.md`

### Source Code Location
- **New system:** `Hrot/Subsystems/Hrot.SimHost/Systems/BlueprintMaterializationSystem.cs` (NEW — alongside `GenesisMaterializationSystem.cs`)
- **Registration point:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` or `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` (EDIT — find where `GenesisMaterializationSystem` is registered)
- **Pattern to mirror:** `Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs`
- **Core attach seam:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs`
- **Tier components:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard1024.cs` (and 4096, 16384)
- **Intent component:** `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs` → `InitialBlueprintsIntent`
- **Existing tests pattern:** `Hrot/Subsystems/Hrot.SimHost.Tests/Systems/GenesisMaterializationSystemTests.cs`

### Report Submission
**When done, submit your report to:**  
`.dev/_DONE/blueprint-scenario/reports/BATCH-03-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Register in CGF → Write tests → **ALL tests pass** ✅

---

## Context

After scenario load, entities carry `InitialBlueprintsIntent` (set by `BlueprintStateTranslator.Inject`). The `BlueprintMaterializationSystem` runs in the `Input` phase to resolve these intents into actual attached blueprints, then removes the intent. It must pre-provision the correct tier upfront (avoiding mid-tick upgrades) and enforce the absolute ceiling.

---

## 🎯 Batch Objectives

Create `BlueprintMaterializationSystem` that:
1. Queries entities with `InitialBlueprintsIntent`
2. Resolves AssetIds → BlueprintIds → definitions via registry
3. **Pre-provisions the tier** from aggregate: sum StateSize + count, pick smallest fitting tier meeting both slot AND byte bounds
4. **Enforces ceiling**: if aggregate exceeds 16 slots / 16096 bytes → log error + truncate (never throw)
5. Attaches each blueprint via `BlueprintInstanceService.AttachToEntity`
6. Removes `InitialBlueprintsIntent` via `IEntityCommandBuffer`

---

## ✅ Tasks

### Task 1: Create `BlueprintMaterializationSystem`

**File:** `Hrot/Subsystems/Hrot.SimHost/Systems/BlueprintMaterializationSystem.cs` (NEW)

**Description:** Create a system class that mirrors `GenesisMaterializationSystem`:
- `[UpdateInPhase(SystemPhase.Input)]` attribute
- Implements `IEcsModuleSystem`
- Constructor takes `BlueprintRegistry`

```csharp
[UpdateInPhase(SystemPhase.Input)]
public sealed class BlueprintMaterializationSystem : IEcsModuleSystem
{
    private readonly BlueprintRegistry _registry;
    private readonly FdpLogger _logger;

    public BlueprintMaterializationSystem(BlueprintRegistry registry, FdpLogger logger) { ... }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) throw ...;
        var cmd = new EntityCommandBuffer();
        try
        {
            MaterializeBlueprints(view, cmd, repo);
            cmd.Playback(repo);
        }
        finally { cmd.Dispose(); }
    }

    private void MaterializeBlueprints(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
    {
        foreach (var entity in view.Query().WithManaged<InitialBlueprintsIntent>().Build())
        {
            var intent = view.GetManagedComponentRO<InitialBlueprintsIntent>(entity);
            if (intent.Blueprints.Count == 0)
            {
                cmd.RemoveManagedComponent<InitialBlueprintsIntent>(entity);
                continue;
            }

            // Step 1: Resolve AssetIds → definitions
            var resolved = new List<(int BlueprintId, BlueprintDefinition Def)>();
            foreach (var dto in intent.Blueprints)
            {
                int bpId = BlueprintIdHash.Compute(dto.AssetId);
                if (_registry.TryGetById(bpId, out var def) && def != null)
                    resolved.Add((bpId, def));
                else
                    _logger.Warn($"[BlueprintMat] AssetId {dto.AssetId} not registered; skipping.");
            }

            if (resolved.Count == 0)
            {
                cmd.RemoveManagedComponent<InitialBlueprintsIntent>(entity);
                continue;
            }

            // Step 2: Compute aggregate → pick tier (Design §5)
            int totalSlots = resolved.Count;
            int totalBytes = 0;
            foreach (var (_, def) in resolved)
                totalBytes += def.StateSize;

            // Ceiling guard (16 slots / 16096 bytes)
            if (totalSlots > BlueprintBlackboard16384.MaxSlots || totalBytes > BlueprintBlackboard16384.PayloadSize)
            {
                _logger.Error($"[BlueprintMat] Entity {entity} exceeds absolute ceiling " +
                    $"({totalSlots} slots / {totalBytes} bytes). Truncating to tier capacity.");
                // Truncate to ceiling capacity
                int truncated = 0;
                int truncatedBytes = 0;
                var truncatedList = new List<(int, BlueprintDefinition)>();
                foreach (var r in resolved)
                {
                    if (truncated >= BlueprintBlackboard16384.MaxSlots) break;
                    if (truncatedBytes + r.Def.StateSize > BlueprintBlackboard16384.PayloadSize) break;
                    truncatedList.Add(r);
                    truncated++;
                    truncatedBytes += r.Def.StateSize;
                }
                resolved = truncatedList;
                totalSlots = truncated;
                totalBytes = truncatedBytes;
            }

            BlackboardTier tier = ChooseTierFromAggregate(totalSlots, totalBytes);

            // Step 3: Pre-provision the tier component
            AddTierComponentIfMissing(repo, entity, tier);

            // Step 4: Attach each blueprint via core seam
            foreach (var (bpId, _) in resolved)
            {
                var result = BlueprintInstanceService.AttachToEntity(repo, _registry, bpId, entity);
                if (result.Status == BlueprintAttachStatus.NoSlotAvailable)
                {
                    _logger.Error($"[BlueprintMat] NoSlotAvailable for bpId 0x{bpId:X8} " +
                        $"on entity {entity} (tier {tier}). This should not happen after pre-provision.");
                }
            }

            // Step 5: Remove intent via ECB (NOT direct repo removal)
            cmd.RemoveManagedComponent<InitialBlueprintsIntent>(entity);
        }
    }
}
```

**Tier selection helper:**
```csharp
// Choose smallest tier satisfying BOTH slot count and payload bytes
private static BlackboardTier ChooseTierFromAggregate(int totalSlots, int totalBytes)
{
    if (totalSlots <= BlueprintBlackboard1024.MaxSlots && totalBytes <= BlueprintBlackboard1024.PayloadSize)
        return BlackboardTier.B1024;
    if (totalSlots <= BlueprintBlackboard4096.MaxSlots && totalBytes <= BlueprintBlackboard4096.PayloadSize)
        return BlackboardTier.B4096;
    return BlackboardTier.B16384;
}
```

**Tier component helper:**
```csharp
private static void AddTierComponentIfMissing(EntityRepository repo, Entity entity, BlackboardTier tier)
{
    switch (tier)
    {
        case BlackboardTier.B1024:
            if (!repo.HasComponent<BlueprintBlackboard1024>(entity))
                repo.AddComponent(entity, default(BlueprintBlackboard1024));
            break;
        case BlackboardTier.B4096:
            if (!repo.HasComponent<BlueprintBlackboard4096>(entity))
                repo.AddComponent(entity, default(BlueprintBlackboard4096));
            break;
        case BlackboardTier.B16384:
            if (!repo.HasComponent<BlueprintBlackboard16384>(entity))
                repo.AddComponent(entity, default(BlueprintBlackboard16384));
            break;
    }
}
```

**Tests required (all in `Hrot/Subsystems/Hrot.SimHost.Tests/Systems/BlueprintMaterializationSystemTests.cs`):**

Study the existing test pattern in `GenesisMaterializationSystemTests.cs` for how to set up the ECS world, register managed components, and execute the system.

- **Test 1 — One-frame, single-tier:** Register 3 blueprints summing ≤ 928 bytes. Create entity with `InitialBlueprintsIntent` containing those 3 AssetIds. Execute `Execute(view, 0)`. Assert: entity has exactly one `BlueprintBlackboard1024` with 3 occupied slots, AND no `InitialBlueprintsIntent` remains. Assert `BlueprintMaintenanceSystem` would NOT trigger an upgrade (the tier is still B1024 — use `HasComponent<BlueprintBlackboard1024>` and NOT `HasComponent<BlueprintBlackboard4096>`).

- **Test 2 — Correct tier from aggregate:** Register blueprints summing > 928 but ≤ 3936 bytes (and ≤ 8 slots). Assert after materialization the entity has `BlueprintBlackboard4096` (not 1024, not 16384).

- **Test 3 — Ceiling guard:** Create an entity with `InitialBlueprintsIntent` containing 17 AssetIds (or > 16096 bytes aggregate). Execute. Assert: no exception is thrown, entity has `BlueprintBlackboard16384` (the max tier), slot count ≤ 16 (truncated), intent is removed.

- **Test 4 — Resilience (unregistered AssetId):** Create an intent with 1 valid AssetId + 1 unregistered AssetId. Execute. Assert: the valid blueprint attaches (slot count == 1), the unregistered one is skipped (warning logged, no crash), intent is removed.

- **Test 5 — Intent removed after materialization:** After `Execute`, assert `view.HasManagedComponent<InitialBlueprintsIntent>(entity)` is false (confirming the ECB-queued removal took effect).

- **Test 6 — ECB removal (no iterator invalidation):** Create 2 entities each with `InitialBlueprintsIntent`, execute, assert both intents are removed. This verifies ECB-queued removal doesn't invalidate the chunk iterator.

- **Test 7 — Attached blueprints tick:** After materialization, tick the `BlueprintTickSystem` for N frames and assert the blueprints are actually executing (e.g., CounterDemoBlueprint's count advances).

---

### Task 2: Register in CGF module

**File:** Find where `GenesisMaterializationSystem` is registered in the CGF bootstrapping code and add `BlueprintMaterializationSystem` registration.

Search for `GenesisMaterializationSystem` in the CGF subsystem code. Likely in:
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` or
- `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs`

The registration should add the system to the `Input` phase, alongside `GenesisMaterializationSystem`. Look for the pattern `AddSystem<GenesisMaterializationSystem>()` or similar.

If the CGF subsystem uses DI, register `BlueprintMaterializationSystem` as a singleton/transient and add it to the simulation module's Input phase systems list.

**Note:** The system needs `BlueprintRegistry` injected. Check how `BlueprintRegistry` is available in the CGF context. It may need to be registered as a DI service, or passed from the subsystem.

---

## 🧪 Testing Requirements

**Test file:** `Hrot/Subsystems/Hrot.SimHost.Tests/Systems/BlueprintMaterializationSystemTests.cs` (NEW)

**Test setup pattern** (mirror `GenesisMaterializationSystemTests`):
```csharp
public sealed class BlueprintMaterializationSystemTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly BlueprintRegistry _registry;

    public BlueprintMaterializationSystemTests()
    {
        _repo = new EntityRepository();
        // Register tier components + managed intent
        _repo.RegisterComponent<BlueprintBlackboard1024>();
        _repo.RegisterComponent<BlueprintBlackboard4096>();
        _repo.RegisterComponent<BlueprintBlackboard16384>();
        _repo.RegisterManagedComponent<InitialBlueprintsIntent>();
        _registry = new BlueprintRegistry();
    }

    public void Dispose() => _repo.Dispose();
    // ... tests
}
```

**How to execute the system in tests:**
```csharp
var system = new BlueprintMaterializationSystem(_registry, ...); // constructor params
system.Execute(view, deltaTime: 0f);
```

**Test quality:** All tests must drive the real production path — no mocks for the materializer. Register real blueprints in a real `BlueprintRegistry`, attach via the real `BlueprintInstanceService`, execute the real system.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `BlueprintMaterializationSystem` created implementing `IEcsModuleSystem` with `[UpdateInPhase(SystemPhase.Input)]`
- [ ] System registered in CGF alongside `GenesisMaterializationSystem`
- [ ] `ChooseTierFromAggregate` correctly implements "smallest tier satisfying BOTH slot count AND byte bounds"
- [ ] Ceiling guard: clamps at 16 slots / 16096 bytes, logs error, no throw
- [ ] Intent removal uses ECB (`cmd.RemoveManagedComponent<InitialBlueprintsIntent>(entity)`)
- [ ] All 7 specified tests pass
- [ ] All pre-existing tests in touched projects pass (0 net-new failures)
- [ ] Build: 0 errors

---

## ⚠️ Common Pitfalls to Avoid

1. **Do NOT remove intent directly from the repo** — use `EntityCommandBuffer` like `GenesisMaterializationSystem` does. Direct removal invalidates the chunk iterator and crashes on multi-entity queries.
2. **Do NOT choose tier by slot count alone** — must check BOTH slots AND bytes. 4 × 300-byte blueprints = 1200 bytes > 928 → needs 4096, not 1024.
3. **Do NOT throw on ceiling exceed** — truncate + log, don't crash. Scenario load must complete.
4. **Do NOT forget `cmd.Playback(repo)`** — ECB operations are deferred until playback.
5. **The system should NOT depend on `NetworkEntityMap`** — blueprint materialization has no cross-entity references.
6. **`FdpLogger` injection** — check how other systems get their logger. May use `FdpLog<T>` static class instead of DI.

---

## 📊 Report Requirements

- **Q1:** Where is `GenesisMaterializationSystem` registered in the CGF code? Which file/line did you add `BlueprintMaterializationSystem` registration?
- **Q2:** How did you handle `FdpLogger`? Static generic or DI?
- **Q3:** What edge cases did you discover during testing?
- **Q4:** Does `BlueprintInstanceService.AttachToEntity` handle the `NoSlotAvailable` case correctly when the tier was pre-provisioned? Did you need any adjustments?
- **Q5:** Suggested commit message.

---

## 📚 Reference Materials
- **Design:** `.dev/_DONE/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §4, §5
- **Task Details:** `.dev/_DONE/blueprint-scenario/TASK-DETAIL.md` — BSA-203
- **Pattern system:** `Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs`
- **Pattern tests:** `Hrot/Subsystems/Hrot.SimHost.Tests/Systems/GenesisMaterializationSystemTests.cs`
- **Core attach seam:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs`
- **Tier constants:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard1024.cs` (`.PayloadSize`, `.MaxSlots`)
- **Intent component:** `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs`
