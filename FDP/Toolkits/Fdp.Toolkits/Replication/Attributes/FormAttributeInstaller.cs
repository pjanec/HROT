using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Weather;
using Hrot.Common;
using Hrot.MuscleCharacter.Animation.Components;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐ B-4 / Details forms D2 (<c>docs/UX/DESIGN_Details_Forms.md</c> §4.1) — the binary arm of the form attributes: <c>Health.Current</c>, <c>Playable.Side</c> /
/// <c>SlotName</c>, <c>SpawnArea.Side</c> / <c>FacingDeg</c>, <c>Ammo.&lt;mount&gt;</c>, <c>Stance</c> and the weather scalars. The twin of the JSON setters in
/// <see cref="AttributeCompilerFactory.Build"/>; the value logic is <see cref="FormAttributes"/>'s, shared. Every handler is gated on the component (UXI-30): only its owner applies.
/// </summary>
public sealed class FormAttributeInstaller : IBinaryAttributeInstaller<EntityAttributeChange>
{
    public void Install(BinaryInterpreterBuilder<EntityAttributeChange> builder)
    {
        builder.RegisterHandler<Health>(AttributeIds.HealthCurrent, (ctx, r) =>
            FormAttributes.SetHealth(ref ctx.PatchContext.GetUnmanagedComponent<Health>(), FormAttributes.AsDouble(r.Value)));

        builder.RegisterHandler<Playable>(AttributeIds.PlayableSide, (ctx, r) =>
            ctx.PatchContext.GetUnmanagedComponent<Playable>().Side = FormAttributes.Force(r.Value));
        builder.RegisterHandler<Playable>(AttributeIds.PlayableSlotName, (ctx, r) =>
            ctx.PatchContext.GetUnmanagedComponent<Playable>().SlotName = new FixedString32(r.Value.StringValue ?? string.Empty));

        builder.RegisterHandler<SpawnArea>(AttributeIds.SpawnAreaSide, (ctx, r) =>
            ctx.PatchContext.GetUnmanagedComponent<SpawnArea>().Side = FormAttributes.Force(r.Value));
        builder.RegisterHandler<SpawnArea>(AttributeIds.SpawnAreaFacingDeg, (ctx, r) =>
            ctx.PatchContext.GetUnmanagedComponent<SpawnArea>().FacingDeg = (float)FormAttributes.AsDouble(r.Value));

        builder.RegisterHandler<WeaponState>(AttributeIds.Ammo, HandleAmmo);
        builder.RegisterHandler<StanceIntent>(AttributeIds.Stance, HandleStance);

        // ⭐ Initial state S2 — the weapon's start state, through WeaponPostureOf / HeldItems (the one writers). The component is the state, so a paused world shows it at once.
        builder.RegisterHandler<WeaponPosture>(AttributeIds.WeaponPosture, (ctx, r) =>
        {
            if (!FormAttributes.TryEnum<WeaponPostureKind>(r.Value, out var kind)) return;
            ref var p = ref ctx.PatchContext.GetUnmanagedComponent<WeaponPosture>();
            if (ctx.Repo == null) { p.Kind = kind; return; }   // creation path: the request carries it
            Fdp.Toolkit.Combat.WeaponPostureOf.Author(ctx.Repo, ctx.Entity, kind);
        });
        builder.RegisterHandler<WeaponPosture>(AttributeIds.WeaponAimPoint, (ctx, r) =>
            Fdp.Toolkit.Combat.WeaponPostureOf.AuthorAimPoint(ref ctx.PatchContext.GetUnmanagedComponent<WeaponPosture>(), r.SubIndex1, (float)FormAttributes.AsDouble(r.Value)));
        builder.RegisterHandler<WeaponPosture>(AttributeIds.WeaponAimTarget, (ctx, r) =>
        {
            var repo = ctx.Repo;
            if (repo == null) return;   // a target is a live unit: no world, nothing to aim at
            long id = (long)FormAttributes.AsDouble(r.Value);
            var target = id is > 0 and <= int.MaxValue ? Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(repo, (int)id) : Entity.Null;   // ⭐ the ONE resolver (BP-508)
            if (id != 0 && target.IsNull) return;   // ⛔ an unknown unit is refused (nothing applied)
            ctx.PatchContext.GetUnmanagedComponent<WeaponPosture>();
            Fdp.Toolkit.Combat.WeaponPostureOf.AuthorAimTarget(repo, ctx.Entity, target);
        });
        builder.RegisterHandler<WeaponPosture>(AttributeIds.WeaponHeldItem, (ctx, r) =>
        {
            if (!FormAttributes.TryEnum<HeldItemKind>(r.Value, out var item)) return;
            var repo = ctx.Repo;
            if (repo == null) { ctx.PatchContext.GetUnmanagedComponent<WeaponPosture>().HeldItem = item; return; }
            if (!Fdp.Toolkit.Combat.HeldItems.Has(repo, ctx.Entity, item)) return;   // ⛔ a thing he does not have
            ctx.PatchContext.GetUnmanagedComponent<WeaponPosture>();
            Fdp.Toolkit.Combat.HeldItems.Set(repo, ctx.Entity, item);
        });

        foreach (ushort id in WeatherIds)
        {
            ushort attribute = id;
            builder.RegisterHandler<WeatherConditions>(attribute, (ctx, r) =>
                FormAttributes.SetWeather(ref ctx.PatchContext.GetUnmanagedComponent<WeatherConditions>(), attribute, FormAttributes.AsDouble(r.Value)));
        }
    }

    /// <summary>The weather scalars' attribute ids.</summary>
    public static readonly ushort[] WeatherIds =
    {
        AttributeIds.WeatherGroundTemperatureC, AttributeIds.WeatherLapseRateKPerKm, AttributeIds.WeatherSeaLevelPressureHpa, AttributeIds.WeatherRelativeHumidity,
        AttributeIds.WeatherVisibilityM, AttributeIds.WeatherFogTopM, AttributeIds.WeatherFogVisibilityM, AttributeIds.WeatherPrecipitation,
        AttributeIds.WeatherPrecipitationMmPerHour, AttributeIds.WeatherFeatherM,
    };

    /// <summary>
    /// <c>Ammo.&lt;mount&gt;</c>. ⭐ Mount 0 is the unit's own <see cref="WeaponState"/>; mounts 1–3 are CHILD entities (<c>WeaponMountInfo.MountIndex</c>) — the handler reaches them through
    /// <see cref="BinaryPatchContext.Repo"/> (live apply). The gate (<c>RegisterHandler&lt;WeaponState&gt;</c>) is the unit's authority over its weapons; a mount child must be this node's too.
    /// The unit's own <see cref="WeaponState"/> is touched so the owner reports the edit as applied. No such mount (or no <c>Repo</c>, the creation path) ⇒ nothing applied.
    /// </summary>
    private static void HandleAmmo(BinaryPatchContext ctx, EntityAttributeChange r)
    {
        int mount = r.SubIndex1;
        double value = FormAttributes.AsDouble(r.Value);
        if (mount <= 0)
        {
            FormAttributes.SetAmmo(ref ctx.PatchContext.GetUnmanagedComponent<WeaponState>(), value);
            return;
        }
        var repo = ctx.Repo;
        if (repo == null || !FormAttributes.TryFindMount(repo, ctx.Entity, mount, out var child) || !repo.HasAuthority<WeaponState>(child)) return;
        ctx.PatchContext.GetUnmanagedComponent<WeaponState>();   // marks the edit applied (the unit's descriptor, if any, is dirtied — harmless)
        FormAttributes.SetAmmo(ref repo.GetComponentRW<WeaponState>(child), value);
    }

    /// <summary><c>Stance</c> — see <see cref="FormAttributes.ApplyStance"/>. A stance the entity's TYPE does not declare is REFUSED (nothing applied).</summary>
    private static void HandleStance(BinaryPatchContext ctx, EntityAttributeChange r)
    {
        if (!FormAttributes.TryStance(r.Value, out var stance)) return;
        var repo = ctx.Repo;
        if (repo == null)
        {
            // creation path: no world yet — the request carries the intent; the spawn seeds the status from it (NetworkSpawningSystem)
            ref var born = ref ctx.PatchContext.GetUnmanagedComponent<StanceIntent>();
            born.TargetStance = stance;
            return;
        }
        if (!FormAttributes.Declares(repo, ctx.Entity, stance)) return;
        ctx.PatchContext.GetUnmanagedComponent<StanceIntent>();   // applied + the intent's descriptor dirty
        FormAttributes.ApplyStance(repo, ctx.Entity, stance);
    }
}
