# Prerequisite — Cache `MaxAmmo` in `WeaponState` *(superseded)*

> **Status:** **SUPERSEDED 2026-05-28.** See [`PREREQ_Phase0_Bundle.md`](./PREREQ_Phase0_Bundle.md) — this single-field
> prerequisite expanded into a six-item Phase-0 bundle after the v236 codebase review (multi-mount infra,
> `MaxTrackedTargets`, `UnitRoster` helpers, `Blackboard1024.Project<T>`, test-world helper, plus this `MaxAmmo` cache).
> The content below is retained for history.
>
> **Owner:** whoever owns `WeaponState` and TKB weapon-mount spawning.
> **Blocks:** Utility AI weapon-selection and combat-posture decisions (the `AmmoFraction` /
> `WeaponHasAmmo` input readers). Also benefits the runtime tuning overlay and any ammo HUD.
> **Size:** one field + one assignment at spawn. No behavior change to existing systems.

---

## 1. The problem

The Utility AI layer needs a normalized **ammo fraction** (0–1) as a consideration input — it is
the hard gate that drives a weapon's utility to ~0 when empty (the entire reason product-mode
scoring was chosen) and a graded input for posture decisions ("low ammo → prefer cover/regroup
over advance").

Today `WeaponState` stores only:

- `int Ammo` — current rounds.
- `float CooldownSecondsRemaining` — time until the weapon can fire again.

The **maximum** capacity is not retained on the component. It exists upstream as
`WeaponMountDto.InitialAmmunition`, consumed during TKB spawning and then discarded. So at tick
time there is no live value to divide by — `AmmoFraction = Ammo / ???`.

## 2. Why not pass max as a reader parameter

The Utility input reader *could* take max ammo as a packed `InputParams` value:

```csharp
.Consider(In.AmmoFraction(Ctx.Self, maxAmmo: 30), w: 0.9f, Curve.Threshold)  // ✗ do not do this
```

This is rejected because the magic number drifts from the actual mount. If a weapon's
`InitialAmmunition` changes in the TKB, or an entity carries a variant mount, every authored
consideration silently computes the wrong fraction with no error. The whole point of reading live
state is to avoid hard-coded assumptions about the entity; baking max into the asset reintroduces
exactly that coupling, in the worst place (authored data, far from the mount definition).

## 3. The fix — cache `MaxAmmo` at spawn

Add one field to `WeaponState` and populate it once, at the same point TKB spawning reads
`WeaponMountDto.InitialAmmunition` to seed `Ammo`.

```csharp
public struct WeaponState   // unmanaged component
{
    public int   Ammo;                    // existing
    public float CooldownSecondsRemaining;// existing
    public int   MaxAmmo;                 // NEW — cached from WeaponMountDto.InitialAmmunition at spawn
}
```

At the weapon-mount spawn site (wherever `Ammo` is currently initialized from
`InitialAmmunition`):

```csharp
ref var ws = ref repo.AddComponent<WeaponState>(weaponEntity);
ws.Ammo    = mount.InitialAmmunition;
ws.MaxAmmo = mount.InitialAmmunition;     // NEW — single extra assignment
ws.CooldownSecondsRemaining = 0f;
```

That is the entire change. `MaxAmmo` is written once and never mutated by firing (only `Ammo`
decrements), so there is no ongoing maintenance and no system needs to be aware of it.

## 4. What it enables

The Utility input readers become clean live reads with no parameters:

```csharp
[UtilityInput(Name = "AmmoFraction")]
public static float AmmoFraction(in UtilityInputCtx ctx)
{
    ref readonly var ws = ref ctx.ReadWeaponState(ctx.Self);
    return ws.MaxAmmo > 0 ? Math.Clamp((float)ws.Ammo / ws.MaxAmmo, 0f, 1f) : 0f;
}

[UtilityInput(Name = "WeaponHasAmmo")]
public static float WeaponHasAmmo(in UtilityInputCtx ctx)
    => ctx.ReadWeaponState(ctx.Candidate).Ammo > 0 ? 1f : 0f;   // Step-curve gate
```

The `MaxAmmo > 0` guard makes the reader safe for entities whose weapon was spawned before this
change (legacy `MaxAmmo == 0` reads as "no ammo / fully gated" rather than dividing by zero) — so
the change is forward-safe even if some spawn path is missed initially; those weapons simply gate
out until their spawn site is updated, which is a visible, debuggable failure rather than a silent
wrong number.

## 5. Secondary beneficiaries

- **Runtime tuning overlay** (AI overlays doc §7) — the perception/channel overlay can show
  `Ammo / MaxAmmo` as a bar without re-deriving max.
- **Any ammo HUD or telemetry** — same reason.
- **`WeaponReadiness` reader** — unaffected (uses `CooldownSecondsRemaining`), noted only so the
  implementer knows readiness and ammo are separate considerations.

## 6. Test

- Spawn a weapon from a TKB mount with `InitialAmmunition = N`; assert `WeaponState.MaxAmmo == N`.
- Fire until `Ammo == 0`; assert `MaxAmmo` is unchanged and `AmmoFraction` reads 0.
- Reload/refill path (if any) sets `Ammo` back up; assert `AmmoFraction` tracks correctly against
  the unchanged `MaxAmmo`.
- Legacy guard: a `WeaponState` with `MaxAmmo == 0` makes `AmmoFraction` return 0 and
  `WeaponHasAmmo` gate on `Ammo > 0` without throwing.
