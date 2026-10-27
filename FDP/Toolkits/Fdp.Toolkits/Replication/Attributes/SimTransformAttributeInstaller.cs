using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-4073</c> (AQ107 B's tail, R-4092) — routes the <see cref="AttributeIds.PositionX"/>/<see cref="AttributeIds.PositionY"/>/
/// <see cref="AttributeIds.PositionZ"/> binary attribute records to <see cref="SimTransform.Position"/>: LOCAL ENU metres, a plain copy per axis.</b>
///
/// <para>⭐⭐ <b>What this replaced, and why it is smaller.</b> 🔴 It used to route <c>GeoLat</c>/<c>GeoLon</c>/<c>GeoAlt</c> (ids 10–12, retired):
/// a scratchpad that pre-filled the entity's CURRENT position through <c>ToGeodetic</c>, accumulated the geodetic point, and converted back
/// once at flush time through <c>ToCartesian</c> — because three geodetic coordinates are ONE point. 📐 Local metres are three independent
/// axes, so the pre-fill, the scratchpad and the flusher are all gone: a partial patch (only <c>Position.X</c>) leaves the other two
/// untouched BY CONSTRUCTION, with nothing to pre-fill.</para>
///
/// <para>⭐⭐⭐ <b>It needs no <c>IGeographicTransform</c>, so it is added on EVERY host</b> — the constructor takes nothing, and
/// <see cref="AttributeCompilerFactory.BuildBinaryInterpreter"/> adds it unconditionally. ⛔ It used to be added only
/// <c>if (geoTransform != null)</c>, which silently dropped a position on a host with no terrain.</para>
///
/// <para>⭐ Registered through the TYPED <c>RegisterHandler&lt;SimTransform&gt;</c> (<c>UXI-30</c>), so the authority gate is applied by the
/// registration: a node that does not own <see cref="SimTransform"/> applies nothing and the writer asks the owner instead.</para>
///
/// <para>⭐ The one writer of an axis is <see cref="SimTransformPosition.SetAxis"/>, shared with the JSON arm
/// (<c>AttributeCompilerFactory.Build</c>) — ⛔ never a second copy of the copy.</para>
/// </summary>
public sealed class SimTransformAttributeInstaller : IBinaryAttributeInstaller<EntityAttributeChange>
{
    /// <inheritdoc/>
    public void Install(BinaryInterpreterBuilder<EntityAttributeChange> builder)
    {
        builder.RegisterHandler<SimTransform>(AttributeIds.PositionX, (ctx, r) => Apply(ctx, 0, r));
        builder.RegisterHandler<SimTransform>(AttributeIds.PositionY, (ctx, r) => Apply(ctx, 1, r));
        builder.RegisterHandler<SimTransform>(AttributeIds.PositionZ, (ctx, r) => Apply(ctx, 2, r));
    }

    private static void Apply(BinaryPatchContext ctx, int axis, EntityAttributeChange record)
    {
        ref SimTransform st = ref ctx.PatchContext.GetUnmanagedComponent<SimTransform>();
        SimTransformPosition.SetAxis(ref st, axis, record.Value.DoubleValue);
    }
}

/// <summary>⭐ <c>CE-4073</c> — the one writer of a local-ENU position axis into <see cref="SimTransform"/>, shared by the JSON and binary arms.</summary>
public static class SimTransformPosition
{
    /// <summary>Sets one axis (0 = X east, 1 = Y north, 2 = Z up) of <see cref="SimTransform.Position"/> to <paramref name="metres"/>; the other two are kept.</summary>
    public static void SetAxis(ref SimTransform st, int axis, double metres)
    {
        var p = st.Position;
        if (axis == 0) p.X = (float)metres;
        else if (axis == 1) p.Y = (float)metres;
        else p.Z = (float)metres;
        st.Position = p;
    }
}
