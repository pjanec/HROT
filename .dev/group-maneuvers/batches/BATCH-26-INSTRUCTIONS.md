# BATCH-26 Instructions — Phase 4 Part 2: Event-Driven Rotation Engine (P4-03)

**Covers:** TASK-SQD-P4-03  
**Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` §9

---

## Context

Phase 4 P4-01/P4-02/P4-04 are committed. `PhaseSequencer.Advance` (Phase 1) already handles dwell-timeout (TimerFallback) transitions. The missing piece is a `SquadEventIngressSystem` that translates per-member ECS state changes into `PhaseEvent`s fed to the sequencer.

Four completion-event sources:
1. **ShotFired** — a squad member's `WeaponState.Ammo` decreased since last tick
2. **FarSideReached** — a member's `NavigationStatus.Result == Arrived` AND `IntentId` matches a configured "far-side intent" ID
3. **BoundComplete** — same as FarSideReached but for a "bound intent" ID
4. **DefiladeReached** — same pattern for a "defilade intent" ID
5. **TimerFallback** — already handled by `PhaseSequencer.Advance` (no separate event needed)

Note: SC-P4-03-4 (hill-attack parity) is deferred to Phase 5 (TASK-SQD-P5-04). Only SC-P4-03-1, SC-P4-03-2, SC-P4-03-3 are required here.

---

## Task 1: `SquadEventIngressSystem`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadEventIngressSystem.cs`

```
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;   // NavigationStatus, NavigationResult
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Systems
```

```csharp
/// <summary>
/// Translates per-member ECS state changes into squad-level <see cref="PhaseEvent"/>s
/// that can be fed to <see cref="PhaseSequencer.Advance"/>.
///
/// Four detection sources:
/// <list type="bullet">
///   <item><see cref="PhaseEventKind.ShotFired"/> — member WeaponState.Ammo decreased.</item>
///   <item><see cref="PhaseEventKind.FarSideReached"/> — member NavigationStatus.Result == Arrived
///     and IntentId matches <see cref="FarSideIntentId"/>.</item>
///   <item><see cref="PhaseEventKind.BoundComplete"/> — same pattern, <see cref="BoundIntentId"/>.</item>
///   <item><see cref="PhaseEventKind.DefiladeReached"/> — same pattern, <see cref="DefiladeIntentId"/>.</item>
/// </list>
/// TimerFallback is NOT emitted here; <see cref="PhaseSequencer.Advance"/> handles it internally.
/// </summary>
public sealed class SquadEventIngressSystem
{
    /// <summary>NavigationIntent.IntentId that signals far-side arrival. 0 = disabled.</summary>
    public uint FarSideIntentId;
    /// <summary>NavigationIntent.IntentId that signals bound completion. 0 = disabled.</summary>
    public uint BoundIntentId;
    /// <summary>NavigationIntent.IntentId that signals defilade reached. 0 = disabled.</summary>
    public uint DefiladeIntentId;

    // Per-member previous ammo snapshot (roster-slot indexed).
    private PrevAmmoArray _prevAmmo;

    [InlineArray(16)]
    private struct PrevAmmoArray
    {
#pragma warning disable CS0169
        private int _element;
#pragma warning restore CS0169
    }

    /// <summary>
    /// Scans all roster members and appends detected <see cref="PhaseEvent"/>s to
    /// <paramref name="events"/>. Caller feeds the span to
    /// <see cref="PhaseSequencer.Advance"/> afterward.
    /// </summary>
    /// <param name="repo">Active ECS repository.</param>
    /// <param name="commander">Commander entity (must carry UnitRoster).</param>
    /// <param name="events">Output list; append-only.</param>
    public void Run(EntityRepository repo, Entity commander, IList<PhaseEvent> events)
    {
        if (!repo.HasComponent<UnitRoster>(commander)) return;
        ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);

        var prevAmmoSpan = MemoryMarshal.CreateSpan(
            ref Unsafe.As<PrevAmmoArray, int>(ref _prevAmmo), 16);

        for (int m = 0; m < roster.Count; m++)
        {
            var member = new Entity((ulong)roster.SubordinateEntities[m]);

            // ── ShotFired ────────────────────────────────────────────────
            if (repo.HasComponent<WeaponState>(member))
            {
                int currentAmmo = repo.GetComponentRO<WeaponState>(member).Ammo;
                if (currentAmmo < prevAmmoSpan[m])
                    events.Add(new PhaseEvent(PhaseEventKind.ShotFired));
                prevAmmoSpan[m] = currentAmmo;
            }

            // ── NavigationStatus events ─────────────────────────────────
            if (repo.HasComponent<NavigationStatus>(member))
            {
                ref readonly var navStatus = ref repo.GetComponentRO<NavigationStatus>(member);
                if (navStatus.Result == NavigationResult.Arrived)
                {
                    if (FarSideIntentId  != 0 && navStatus.IntentId == FarSideIntentId)
                        events.Add(new PhaseEvent(PhaseEventKind.FarSideReached));
                    if (BoundIntentId    != 0 && navStatus.IntentId == BoundIntentId)
                        events.Add(new PhaseEvent(PhaseEventKind.BoundComplete));
                    if (DefiladeIntentId != 0 && navStatus.IntentId == DefiladeIntentId)
                        events.Add(new PhaseEvent(PhaseEventKind.DefiladeReached));
                }
            }
        }
    }
}
```

Note: The class uses `System.Runtime.CompilerServices` and `System.Runtime.InteropServices` for the InlineArray pattern. Use the same imports as other squad systems.

---

## Task 2: Register `NavigationStatus` in tests

The tests must register `NavigationStatus`:
```csharp
repo.RegisterComponent<NavigationStatus>();
```

Check the component ID: search for `NavigationStatus` in `NavigationContractsComponentIds` or `GlobalComponentIds`.

---

## Task 3: Tests

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/SquadEventIngressSystemTests.cs`

### Setup helper

Each test creates:
- Commander with `UnitRoster`, `Blackboard1024`
- 2 members with `WeaponState`, `NavigationStatus`, `UnitSubordinate`

```csharp
private EntityRepository _repo;
private SquadEventIngressSystem _system;
private Entity _commander, _member0, _member1;
private List<PhaseEvent> _events;

public SquadEventIngressSystemTests()
{
    _repo = new EntityRepository();
    _repo.RegisterComponent<UnitRoster>();
    _repo.RegisterComponent<Blackboard1024>();
    _repo.RegisterComponent<WeaponState>();
    _repo.RegisterComponent<NavigationStatus>();
    _repo.RegisterComponent<UnitSubordinate>();

    _system = new SquadEventIngressSystem();
    _events = new List<PhaseEvent>();

    _commander = _repo.CreateEntity();
    _repo.AddComponent(_commander, new UnitRoster());
    _repo.AddComponent(_commander, new Blackboard1024());

    _member0 = _repo.CreateEntity();
    _repo.AddComponent(_member0, new WeaponState { Ammo = 10, MaxAmmo = 10 });
    _repo.AddComponent(_member0, new NavigationStatus());
    ref var roster = ref _repo.GetComponentRW<UnitRoster>(_commander);
    UnitRoster.Add(ref roster, (long)_member0.PackedValue);

    _member1 = _repo.CreateEntity();
    _repo.AddComponent(_member1, new WeaponState { Ammo = 10, MaxAmmo = 10 });
    _repo.AddComponent(_member1, new NavigationStatus());
    ref var roster2 = ref _repo.GetComponentRW<UnitRoster>(_commander);
    UnitRoster.Add(ref roster2, (long)_member1.PackedValue);
}
```

---

**SC-P4-03-1 (ShotFired):**

1. Call `_system.Run(repo, commander, events)` → 0 events (initial ammo snapshotted).
2. Decrease `member0.WeaponState.Ammo` to 9.
3. Call Run again → 1 event: `ShotFired`.
4. Call Run again without ammo change → 0 events (no second fire this tick).

```csharp
[Fact]
public void Run_DetectsShotFired_WhenMemberAmmoDecreases()
{
    // First call: snapshot prevAmmo = 10. No events.
    _system.Run(_repo, _commander, _events);
    Assert.Empty(_events);

    // Decrement ammo.
    _repo.GetComponentRW<WeaponState>(_member0).Ammo = 9;

    _events.Clear();
    _system.Run(_repo, _commander, _events);
    Assert.Single(_events);
    Assert.Equal(PhaseEventKind.ShotFired, _events[0].Kind);
}

[Fact]
public void Run_NoShotFired_WhenAmmoUnchanged()
{
    _system.Run(_repo, _commander, _events);  // snapshot
    _events.Clear();
    _system.Run(_repo, _commander, _events);  // same ammo
    Assert.Empty(_events);
}
```

---

**SC-P4-03-2 (FarSideReached):**

Configure `_system.FarSideIntentId = 42u`. Set member's `NavigationStatus.Result = Arrived, IntentId = 42u`. Call Run → event `FarSideReached` emitted. Then set `IntentId = 99u` → no event.

```csharp
[Fact]
public void Run_DetectsFarSideReached_WhenIntentIdMatches()
{
    _system.FarSideIntentId = 42u;
    _system.Run(_repo, _commander, _events);  // snapshot ammo
    _events.Clear();

    ref var ns = ref _repo.GetComponentRW<NavigationStatus>(_member0);
    ns.Result   = NavigationResult.Arrived;
    ns.IntentId = 42u;

    _system.Run(_repo, _commander, _events);
    Assert.Contains(_events, e => e.Kind == PhaseEventKind.FarSideReached);
}

[Fact]
public void Run_NoFarSideReached_WhenIntentIdMismatch()
{
    _system.FarSideIntentId = 42u;
    _system.Run(_repo, _commander, _events);  // snapshot
    _events.Clear();

    ref var ns = ref _repo.GetComponentRW<NavigationStatus>(_member0);
    ns.Result   = NavigationResult.Arrived;
    ns.IntentId = 99u;  // different

    _system.Run(_repo, _commander, _events);
    Assert.DoesNotContain(_events, e => e.Kind == PhaseEventKind.FarSideReached);
}
```

---

**SC-P4-03-3 (TimerFallback via PhaseSequencer integration):**

PhaseSequencer.Advance fires the dwell-timeout transition when no events arrive. Verify integration: set `state.PhaseEnteredTick = 0`, `currentTick = 100`, `dwellTimeoutTicks = 50`. With empty events from Run, `PhaseSequencer.Advance` should transition to `recoveryPhaseId`.

```csharp
[Fact]
public void PhaseSequencer_Advance_TriggersTimerFallback_WhenDwellElapsed()
{
    // Fabricate a SquadCognitiveState with phase 1 entered at tick 0.
    var state = default(SquadCognitiveState);
    state.PhaseId          = 1;
    state.PhaseEnteredTick = 0;

    // No events.
    var events = ReadOnlySpan<PhaseEvent>.Empty;
    var table  = ReadOnlySpan<PhaseTransitionEntry>.Empty;

    // Dwell of 50 ticks; current tick = 100. Should transition to recovery (0).
    bool transitioned = PhaseSequencer.Advance(ref state, events, table,
                                               currentTick: 100, dwellTimeoutTicks: 50,
                                               recoveryPhaseId: 0);

    Assert.True(transitioned);
    Assert.Equal(0, state.PhaseId);
}

[Fact]
public void PhaseSequencer_Advance_NoTimerFallback_BeforeDwell()
{
    var state = default(SquadCognitiveState);
    state.PhaseId          = 1;
    state.PhaseEnteredTick = 80;

    var events = ReadOnlySpan<PhaseEvent>.Empty;
    var table  = ReadOnlySpan<PhaseTransitionEntry>.Empty;

    // Current tick = 100, entered at 80 = 20 ticks, dwell = 50. Not yet.
    bool transitioned = PhaseSequencer.Advance(ref state, events, table,
                                               currentTick: 100, dwellTimeoutTicks: 50,
                                               recoveryPhaseId: 0);

    Assert.False(transitioned);
    Assert.Equal(1, state.PhaseId);  // unchanged
}
```

---

**Exactly-one-event guard (SC-P4-03-1 parity):**

```csharp
[Fact]
public void Run_EmitsExactlyOneShotFired_PerFiringEvent()
{
    // Two members, only one fires.
    _system.Run(_repo, _commander, _events);  // snapshot both at 10
    _events.Clear();

    _repo.GetComponentRW<WeaponState>(_member0).Ammo = 9;  // member0 fires
    // member1 ammo stays at 10

    _system.Run(_repo, _commander, _events);
    Assert.Single(_events);
    Assert.Equal(PhaseEventKind.ShotFired, _events[0].Kind);
}
```

---

## Checklist

Before submitting report:

- [ ] `SquadEventIngressSystem` correctly tracks per-member ammo via `PrevAmmoArray` InlineArray.
- [ ] First call snapshots ammo without emitting events.
- [ ] `NavigationStatus.Result == Arrived && IntentId == configured` triggers correct events.
- [ ] `PhaseSequencer.Advance` dwell-timeout tests pass.
- [ ] All 6 tests pass (SC-P4-03-1 = 2 tests, SC-P4-03-2 = 2 tests, SC-P4-03-3 = 2 tests).
- [ ] `SquadCognitiveStateLayoutTests` still pass.
- [ ] Build: 0 errors, 0 new warnings.

## File summary

| Action | File |
|---|---|
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadEventIngressSystem.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/SquadEventIngressSystemTests.cs` |
