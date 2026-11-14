using System;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐ CE-4024 (AQ93 §15 K5, CV-12; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md) — the binary arm of a civilian's fright, the twin of the
/// <c>Fear</c> / <c>FrightFrom.*</c> JSON paths in <see cref="AttributeCompilerFactory.Build"/>. ⭐ <see cref="CivilianState"/> is
/// brain-local and never replicated, so a SUMO pedestrian converted on the traffic node reaches the brain node as a creation request whose
/// attributes say he is terrified and where it came from; his <c>CivilianHsm</c> then goes Alarmed ⇒ Panic and runs from it.
/// </summary>
/// <remarks>⛔ Applied only to an entity that already has <see cref="CivilianState"/>: a runtime edit never adds a mind to a soldier.
/// At creation the request's component list gains one (<c>ListPatchContext</c> creates a missing component).</remarks>
public sealed class CivilianFrightAttributeInstaller : IBinaryAttributeInstaller<EntityAttributeChange>
{
    public void Install(BinaryInterpreterBuilder<EntityAttributeChange> builder)
    {
        builder.RegisterHandler<CivilianState>(AttributeIds.CivilianFear, (ctx, r) => Apply(ctx, -1, Value(r)));
        builder.RegisterHandler<CivilianState>(AttributeIds.CivilianFrightFromX, (ctx, r) => Apply(ctx, 0, Value(r)));
        builder.RegisterHandler<CivilianState>(AttributeIds.CivilianFrightFromY, (ctx, r) => Apply(ctx, 1, Value(r)));
        builder.RegisterHandler<CivilianState>(AttributeIds.CivilianFrightFromZ, (ctx, r) => Apply(ctx, 2, Value(r)));
    }

    private static double Value(EntityAttributeChange r) => r.Value.Kind switch
    {
        AttributeValueKind.CsInt32 => r.Value.IntValue,
        AttributeValueKind.CsInt64 => r.Value.LongValue,
        _ => r.Value.DoubleValue,
    };

    private static void Apply(BinaryPatchContext ctx, int axis, double value)
    {
        if (ctx.Repo is { } repo && (!repo.IsAlive(ctx.Entity) || !repo.HasComponent<CivilianState>(ctx.Entity))) return;
        ref var mind = ref ctx.PatchContext.GetUnmanagedComponent<CivilianState>();
        if (axis < 0) CivilianFright.SetFear(ref mind, value);
        else CivilianFright.SetFrom(ref mind, axis, value);
    }
}

/// <summary>⭐ CE-4024 — the one writer of a fright attribute into <see cref="CivilianState"/>, shared by the JSON and binary arms.</summary>
public static class CivilianFright
{
    /// <summary>Sets his fear, clamped to 0 … 1.</summary>
    public static void SetFear(ref CivilianState mind, double fear) => mind.Fear = (float)Math.Clamp(fear, 0.0, 1.0);

    /// <summary>Sets one axis (0 = X, 1 = Y, 2 = Z) of where the fright came from, and marks it known.</summary>
    public static void SetFrom(ref CivilianState mind, int axis, double value)
    {
        var from = mind.FrightFrom;
        if (axis == 0) from.X = (float)value;
        else if (axis == 1) from.Y = (float)value;
        else from.Z = (float)value;
        mind.FrightFrom = from;
        mind.HasFrightFrom = 1;
    }
}
