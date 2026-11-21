# BATCH-04: Runtime Mutation Events + Consuming System (BSA-301)

**Batch Number:** BATCH-04  
**Tasks:** BSA-301 (Runtime mutation events + Input-phase consuming system)  
**Phase:** Phase 3 — Dynamic / mid-runtime assignment  
**Estimated Effort:** 3-4 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (BSA-102 — core attach/detach seam)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Create three unmanaged struct events and an Input-phase system that drains them, calling the BSA-102 core seam. The system must apply all Remove events before any Attach events (so swaps reuse freed capacity).

### Required Reading (IN ORDER)
1. **Design Document:** `.dev/_DONE/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §7 (dynamic assignment), §10 (module placement)
2. **Task Details:** `.dev/_DONE/blueprint-scenario/TASK-DETAIL.md` — BSA-301 section
3. **Task Tracker:** `.dev/_DONE/blueprint-scenario/TASK-TRACKER.md`

### Source Code Location
- **Event structs (NEW):** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Events/BlueprintLifecycleEvents.cs`
- **Consuming system (NEW):** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintEventIngressSystem.cs`
- **Pattern — events:** `FDP/Toolkits/Fdp.Toolkits/Lifecycle/Events/LifecycleEvents.cs` (for `[EventId]` pattern)
- **Pattern — ingress system:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs`
- **Core attach seam:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs`
- **Event bus:** `repo.Bus.ReadManaged<T>()` / `repo.Bus.Publish<T>()`

### Report Submission
**When done, submit your report to:**  
`.dev/_DONE/blueprint-scenario/reports/BATCH-04-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1:** Create event structs → Write tests → **ALL tests pass** ✅
2. **Task 2:** Create consuming system → Write tests → **ALL tests pass** ✅

---

## Context

Mid-runtime blueprint changes are commands published via `FdpEventBus` (unmanaged struct events, zero-alloc), consumed by a dedicated Input-phase system. The consuming system calls the BSA-102 seam — same attach/detach logic used by genesis and editor.

**Ordering requirement (Design §7):** Within a frame, apply ALL Remove events BEFORE any Attach events. This way an in-place swap (remove X + add same-size Y) frees and dense-compacts the slot first, and the add reuses that capacity — no spurious tier upgrade.

---

## 🎯 Batch Objectives

1. Create 3 unmanaged event structs: `Attach`/`Remove`/`ReplaceInstanceBlueprintEvent`
2. Create `BlueprintEventIngressSystem` (Input phase) that drains events and calls BSA-102 seam, respecting remove-before-add ordering

---

## ✅ Tasks

### Task 1: Create event structs

**File:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Events/BlueprintLifecycleEvents.cs` (NEW)

**Description:** Three unmanaged struct events with `[EventId(...)]`:

```csharp
namespace Fdp.Toolkit.Blueprints.Events;

/// <summary>
/// Requests attaching an Instance blueprint to an entity at runtime.
/// Consumed by <see cref="Systems.BlueprintEventIngressSystem"/> in the Input phase.
/// </summary>
[EventId(BlueprintConstants.EventId_AttachInstanceBlueprint)]
public struct AttachInstanceBlueprintEvent
{
    public Entity Entity;
    public int BlueprintId;
}

/// <summary>
/// Requests detaching an Instance blueprint from an entity at runtime.
/// </summary>
[EventId(BlueprintConstants.EventId_RemoveInstanceBlueprint)]
public struct RemoveInstanceBlueprintEvent
{
    public Entity Entity;
    public int BlueprintId;
}

/// <summary>
/// Requests replacing one Instance blueprint with another on an entity.
/// Applied as: detach old → attach new (remove-before-add ordering).
/// </summary>
[EventId(BlueprintConstants.EventId_ReplaceInstanceBlueprint)]
public struct ReplaceInstanceBlueprintEvent
{
    public Entity Entity;
    public int OldBlueprintId;
    public int NewBlueprintId;
}
```

**EventId assignment:** Define constants in a new `BlueprintConstants` class (or add to an existing constants file):
```csharp
namespace Fdp.Toolkit.Blueprints;

public static class BlueprintConstants
{
    public const int EventId_AttachInstanceBlueprint   = 9100;
    public const int EventId_RemoveInstanceBlueprint   = 9101;
    public const int EventId_ReplaceInstanceBlueprint  = 9102;
}
```

Place `BlueprintConstants` in `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintConstants.cs` (NEW).

⚠️ **Verify no EventId collision:** Search the codebase for `9100`, `9101`, `9102` to ensure these IDs aren't in use.

**Tests (in `FDP/Toolkits/Fdp.Toolkits.Tests/` or `Hrot/.../Tests/`):**
- **Test 1 — Event struct layout:** Verify each event struct is a value type (`IsValueType`), has the correct fields, and has `[EventId]` attribute with the expected value.
- **Test 2 — Publish/Read round-trip:** Publish an event to `repo.Bus`, read it back via `repo.Bus.ReadManaged<T>()`, assert fields match.

---

### Task 2: Create consuming system

**File:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintEventIngressSystem.cs` (NEW)

**Description:** Input-phase system that mirrors `BehaviorIngressSystem`:

```csharp
[UpdateInPhase(SystemPhase.Input)]
public sealed class BlueprintEventIngressSystem : IEcsModuleSystem
{
    private readonly BlueprintRegistry _registry;

    public BlueprintEventIngressSystem(BlueprintRegistry registry) { ... }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) throw ...;

        // ── Phase 1: Apply ALL Removes FIRST (Design §7) ──
        // Drain Remove events (both RemoveInstanceBlueprintEvent and
        // ReplaceInstanceBlueprintEvent's old-blueprint half)
        foreach (var evt in repo.Bus.ReadManaged<RemoveInstanceBlueprintEvent>())
        {
            if (evt == null) continue;
            BlueprintInstanceService.DetachFromEntity(repo, evt.BlueprintId, evt.Entity);
        }

        // Drain Replace events — detach the OLD blueprint
        foreach (var evt in repo.Bus.ReadManaged<ReplaceInstanceBlueprintEvent>())
        {
            if (evt == null) continue;
            BlueprintInstanceService.DetachFromEntity(repo, evt.OldBlueprintId, evt.Entity);
        }

        // ── Phase 2: Apply ALL Attaches AFTER all removes ──
        // Drain Attach events
        foreach (var evt in repo.Bus.ReadManaged<AttachInstanceBlueprintEvent>())
        {
            if (evt == null) continue;
            BlueprintInstanceService.AttachToEntity(repo, _registry, evt.BlueprintId, evt.Entity);
        }

        // Drain Replace events — attach the NEW blueprint
        foreach (var evt in repo.Bus.ReadManaged<ReplaceInstanceBlueprintEvent>())
        {
            if (evt == null) continue;
            BlueprintInstanceService.AttachToEntity(repo, _registry, evt.NewBlueprintId, evt.Entity);
        }
    }
}
```

**⚠️ IMPORTANT ordering detail:** The Replace event must be processed in two phases:
1. Phase 1: detach the OLD blueprint (alongside Remove events)
2. Phase 2: attach the NEW blueprint (alongside Attach events)

This means `ReadManaged<ReplaceInstanceBlueprintEvent>()` is called TWICE — once for old, once for new. Verify that `ReadManaged` returns the same events on subsequent calls within the same frame (consumption is at end-of-phase).

If `ReadManaged` is a consuming read (drains the queue), you'll need to collect all Replace events in Phase 1 and hold them for Phase 2:
```csharp
var replaces = repo.Bus.ReadManaged<ReplaceInstanceBlueprintEvent>().ToList();
// Phase 1: detach OLD
foreach (var evt in replaces) { /* detach old */ }
// Phase 2: attach NEW  
foreach (var evt in replaces) { /* attach new */ }
```

**Check the behavior of `ReadManaged<T>()`** — look at the implementation in `FdpEventBus`. If it's a consuming read (clears after return), use the collect-and-hold approach.

**Tests (in a new test file):**
- **Test 3 — Attach event:** Publish `AttachInstanceBlueprintEvent`, tick system. Assert the blueprint is attached to the entity (slot exists, `TryGetSlotOffset` returns true).

- **Test 4 — Remove event:** Attach a blueprint via core seam, then publish `RemoveInstanceBlueprintEvent`, tick system. Assert the slot is gone (slot count decreased, `TryGetSlotOffset` returns false).

- **Test 5 — Replace event:** Attach blueprint A via core seam, publish `ReplaceInstanceBlueprintEvent(A→B)`, tick system. Assert A is detached (no slot) and B is attached (has slot + `InitDefault` ran).

- **Test 6 — Idempotent/no-op:** Publish `RemoveInstanceBlueprintEvent` for a blueprint not on the entity. Assert no throw. Publish `ReplaceInstanceBlueprintEvent` where old blueprint is absent. Assert no throw (detach returns false, attach still proceeds).

- **Test 7 — Drain ordering (remove-before-add):** Start with a tier at capacity (e.g., 4 blueprints in B1024). Publish `RemoveInstanceBlueprintEvent(X)` + `AttachInstanceBlueprintEvent(Y)` in the same frame (same size). After one Input tick: assert BOTH succeed, the tier component is unchanged (still B1024 — no upgrade triggered), and Y reused X's freed slot. Assert `HasComponent<BlueprintBlackboard1024>` and NOT `HasComponent<BlueprintBlackboard4096>`.

---

## 🧪 Testing Requirements

**Test file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintEventIngressSystemTests.cs` (NEW)

Use `BlueprintTestFixture` for tests that need the full sim loop (Test 7 needs the system registered in a sim group to verify tier doesn't upgrade).

For basic publish/consume tests (Tests 3-6), a bare `EntityRepository` + manual system execution is sufficient:
```csharp
var repo = new EntityRepository();
// register components...
var sys = new BlueprintEventIngressSystem(registry);
repo.Bus.Publish(new AttachInstanceBlueprintEvent { Entity = entity, BlueprintId = bpId });
sys.Execute(repo, 0f);
// assert attachment...
```

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] 3 event structs created with unique `[EventId]` values
- [ ] `BlueprintEventIngressSystem` created with correct remove-before-add ordering
- [ ] System registered in CGF module Input phase (alongside `BehaviorIngressSystem`)
- [ ] All 7 specified tests pass
- [ ] All pre-existing blueprint tests pass (0 net-new failures)
- [ ] Build: 0 errors

---

## ⚠️ Common Pitfalls to Avoid

1. **EventId collision** — search for `9100`, `9101`, `9102` before using them.
2. **ReadManaged consumption** — verify whether `FdpEventBus.ReadManaged<T>()` is consuming (draining) or non-consuming. If consuming, collect Replace events before Phase 1 dispose loop.
3. **Remove-before-add for Replace** — the Replace event's old blueprint MUST be detached in Phase 1 (alongside Remove events), or a same-size swap can trigger a spurious tier upgrade.
4. **Entity validity** — don't forget to check entity is alive before mutating. `BehaviorIngressSystem` doesn't explicitly check `IsAlive` because it checks `HasComponent<BehaviorState>`. For our system, consider checking `repo.IsAlive(evt.Entity)`.
5. **System registration** — the system needs `BlueprintRegistry` injected. Register it in CGF where `BehaviorIngressSystem` is registered.

---

## 📊 Report Requirements

- **Q1:** Is `ReadManaged<T>()` consuming or non-consuming? How did you handle the Replace event's two-phase processing?
- **Q2:** Where did you register the system in CGF? Which file/line?
- **Q3:** What EventId values did you use? Were there collisions?
- **Q4:** Suggested commit message.
