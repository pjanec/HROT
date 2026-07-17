# Slice: `Action_AimAndFireSpecific` (design)

> Migration slice 3 — the first slice needing the **target-resolve** capability (architect Q6-B) and
> **persistent-state ammo round-counting** (exercises the new `Compare`/`BinaryOp` + WorkingState). Uses
> shipped nodes + **two new curated context-aware helpers**. No new compiler node/IR. This note is the
> in-repo design gate; the one settled fork (target-resolve) is Q6-B. Flags two deviations for review.

## Oracle (`HillAttackTankNodes.cs:334-438`)

1. No `NetworkEntityMap` singleton → `Failure`.
2. `entityMap.TryGetEntity(p.TargetNetworkId, out target)` fails → `Failure`.
3. `!IsAlive(target)` → `Success` (target destroyed).
4. Clear `LocomotionChannel` if active (stop moving to fire). ← **DEVIATION (drop)** — see below.
5. `MaxRounds > 0 && RoundsFired >= MaxRounds` → clear weapon, `Success`.
6. No `WeaponChannel`/`WeaponState` → `Failure`.
7. Ammo-drop round counting: `LastObservedAmmo < 0` → init to `ws.Ammo`; else if `ws.Ammo < LastObservedAmmo` → `RoundsFired += (LastObservedAmmo - ws.Ammo); LastObservedAmmo = ws.Ammo;` and re-check MaxRounds → `Success`; else if ammo went up (reload) → `LastObservedAmmo = ws.Ammo`.
8. (Sync `BehaviorInstanceId`.) ← **DROP** (Q5-B — ChannelCommand owns arbitration).
9. Weapon terminal forward: `ActiveAction==AimAndFire` and `Status==Success/Failure` → return it.
10. Else issue once: `AimAndFire { Target = target, CooldownSeconds = 10 }`, `Running`.

## Two new curated helpers (context-aware `FunctionCall`, architect Q6-B pattern)

```csharp
public static class NetworkEntityMapOps    // new public blueprint API
{
    // Resolves TargetNetworkId -> local Entity via the world singleton; Entity.Null if missing/unresolved.
    public static Entity ResolveTarget(EntityRepository world, long targetNetworkId)
    {
        if (!world.HasSingletonManaged<NetworkEntityMap>()) return Entity.Null;
        var map = world.GetSingletonManaged<NetworkEntityMap>();
        return (map != null && map.TryGetEntity(targetNetworkId, out var e)) ? e : Entity.Null;
    }
}
public static class WorldOps                // new public blueprint API (general)
{
    public static bool IsAlive(EntityRepository world, Entity e) => world.IsAlive(e);
    public static bool IsNull(Entity e)      => e == Entity.Null;   // resolve-failure test
}
```
Both take `world` via the P7 **context-aware `FunctionCall`** path (baked `TrailingContext`), reflection-free.
Keying off `EntityRepository` (not `ISimulationView`) — architect Q6-B. `WorldOps` is broadly reusable.

## Blueprint graph (guard-chain + fire)

```
EventEntry
 → target = FunctionCall(NetworkEntityMapOps.ResolveTarget, netId ← GetParameter(TargetNetworkId))   [ctx: world]
 → Branch( WorldOps.IsNull(target) )   True → Return(Failure)                       // guard 2 (subsumes 1)
   False ↓
 → Branch( WorldOps.IsAlive(world, target) )   False → Return(Success)              // guard 3
   True ↓
 → [ammo round-count sub-graph, below]  → if RoundsFired reached MaxRounds → Return(Success)
   else ↓
 → ChannelCommand(WeaponChannel / AimAndFire)  Target ← target, CooldownSeconds ← PinDefault "10"
 → WaitForChannel(WeaponChannel)               // Running/Failure auto; success ↓
 → Return(Success)
```

### Ammo round-count sub-graph (persistent WorkingState + `Compare`/`BinaryOp`)
- **WorkingState:** `RoundsFired` (int, default 0), `LastObservedAmmo` (int, **default -1** via `DefaultValueJson:"-1"` → `InitDefaultWorkingState` one-time init; persists across ticks, NOT re-init per tick — this is an *action*'s cumulative state).
- Read `ammo = GetComponent(self, WeaponState).Ammo` (P2).
- `Branch( Compare(LastObservedAmmo, 0, LessThan) )` True → `SetVariable(LastObservedAmmo = ammo)` (first-tick init); False → `Branch( Compare(ammo, LastObservedAmmo, LessThan) )` True → `SetVariable(RoundsFired = BinaryOp(RoundsFired, BinaryOp(LastObservedAmmo, ammo, Subtract), Add))`, `SetVariable(LastObservedAmmo = ammo)`.
- `Branch( BooleanOp( Compare(MaxRounds,0,GreaterThan), Compare(RoundsFired,MaxRounds,GreaterThanOrEqual), And ) )` True → `Return(Success)` (round cap reached).

This exercises the **whole new operator set**: `Compare` (LessThan/GreaterThan/GreaterThanOrEqual), `BinaryOp` (Subtract/Add), `BooleanOp` (And) — a strong headless proof they compose.

## Deviations (flagged for review)

1. **Locomotion-clear (step 4) DROPPED.** The oracle zeroes the `LocomotionChannel` so the tank halts to
   fire — a *cross-channel* write from the weapon action. The CQRS model (architect Q5-B: a brain writes
   only its own channel via `ChannelCommand`) makes a second-channel poke awkward, and there is no
   "Stop/Idle" action in the Locomotion catalog. In the actual Hill-attack the tank is already stationary
   at its hull-down slot when this runs, so the clear is defensive. **Recommend: drop + document.** (To
   restore later: add a Locomotion "Stop" catalog action, or a generic ClearChannel node.)
2. **`ClearWeaponActionIfActive` on the MaxRounds-Success paths SIMPLIFIED.** The oracle clears the weapon
   channel when returning Success on the round cap; the blueprint just `Return(Success)` (the BTree
   selector/arbitration reclaims the channel). Same class as ReverseToBaseline's Clear-on-failure gap.

Everything else is faithful. Oracle untouched.

## Proof
`HillAssault2_AimAndFireSpecific.bp.json` + proof test (real generator). Source-inspection: `ResolveTarget(`,
`WorldOps.IsAlive`/`IsNull`, `GetComponentRO<...WeaponState>`, the `RoundsFired`/`LastObservedAmmo` WS
arithmetic, `AimAndFireParams { Target = ...`, weapon wait, terminal returns. Behavioral (headless): a
world with a `NetworkEntityMap` singleton + a live target + `WeaponChannel`/`WeaponState`; assert
resolve→fire (Running), ammo-drop increments `RoundsFired`, and MaxRounds→Success. Params vs WorkingState
split per Q5-D (`MaxRounds`/`TargetNetworkId` = Params; `RoundsFired`/`LastObservedAmmo` = WorkingState).
