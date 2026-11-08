<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the whole file
-->

# NOTE — backend → behaviors: a possessed shot is remembered through a reload (CE-4344)

**From:** backend (X-2 of [HANDOFF_Coordinator_To_Backend_Queue_After_V](HANDOFF_Coordinator_To_Backend_Queue_After_V.md): "measure which side
latches it; fix yours, else file the cause on behaviors' row"). **The latch is yours** — nothing to fix on the weapon side.

| measured | where |
|---|---|
| the station sends a press as `ShotSeq` + 1 (no ammo or reload check — it cannot know) | `Hrot/Engine/Hrot.Presentation/Map3D/PossessionStation.cs` ~1918 |
| ⭐ the LATCH: a new `ShotSeq` sets `ws.Pending` (1 = Lock, 2 = Sight) and `ws.Cap = fired + 1`; it is cleared only when a round leaves, the locked unit falls, or X cancels | `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/OperatorControlBehavior.cs:265-283` (`OperateWeapon`) |
| the weapon HOLDS the action while the magazine is not `Magazine.Ready` (reload, overheat, a shot in flight) and fires when it is — correct for the AI, and what turns a held latch into a late shot | `FDP/Toolkits/Fdp.Toolkits/Combat/Executors/AimAndFireExecutor.cs:235-243` |

**Proposed patch (yours to take or reshape)** — at the press, a weapon that cannot fire NOW drops it:

```csharp
if (input.ShotSeq != ws.ShotSeq)
{
    ws.ShotSeq = input.ShotSeq;
    var pressMount = WeaponMountQuery.MountEntity(world, platform, platformMount);
    bool canFireNow = !world.HasComponent<WeaponState>(pressMount) || Magazine.Ready(world.GetComponentRO<WeaponState>(pressMount));
    if (canFireNow) { /* the existing Lock / Sight latch */ }
    else ws.Pending = 0;   // CE-4344 — a shot that cannot leave now is dropped, never fired when the reload ends
}
```

⚠ `Magazine.Ready` is also false for one in-flight shot (the `ShotLedger` cooldown) — if a rapid second press inside the cooldown must still
count, test `Magazine.Reloading` (+ out of ammo) instead. Rail shape: possess a rifleman, empty the magazine, press during the reload, run past
it → `RoundsFired` unchanged.
