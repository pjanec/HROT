using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Core;
using Fdp.Modules.Geographic.Systems;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐⭐⭐ <b>Axis-B item ② — routes the <see cref="AttributeIds.GeoHeading"/> binary attribute to
/// <see cref="SimTransform.Rotation"/>.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §6 ② · §4 *(classDiagram:
/// <c>SimTransformHeadingInstaller</c>)*.</para>
///
/// <para>⭐⭐ <b>It writes NO conversion math, and that is the point.</b> 📐 Measured <c>2026-08-25</c>:
/// <see cref="SimTransformBridgeSystem.HeadingDegToRotation"/> already implements this exact convention
/// — its own doc says *"Compass heading in degrees (0=North, 90=East, clockwise)"* — and
/// <c>RotationToHeadingDeg</c> is its documented inverse, used by <c>GeoSpatialEgressTranslator</c> to
/// put heading on the wire. ⇒ this installer is a ROUTE to that function, ⛔ not a second implementation
/// of the compass convention. 📌 *"we need a shared X"* in this codebase almost always means X exists.</para>
///
/// <para>⭐⭐⭐ <b>Registered through the TYPED <c>RegisterHandler&lt;SimTransform&gt;</c></b>, so
/// <c>UXI-30</c>'s authority gate is applied by the registration and this file contains no
/// <c>CanWrite</c> line to forget. ⚠ That is deliberate: a hand-written guard here would be the third
/// copy of a check that is now structural.</para>
///
/// <para>⚠ <b>Why a separate installer rather than a fourth handler on
/// <c>SimTransformAttributeInstaller</c></b>, given both write <see cref="SimTransform"/>: that one
/// accumulates lat/lon/alt into a scratchpad and converts ONCE at flush time, because the three
/// coordinates are one geodetic point. ⭐ Heading is an independent scalar with its own conversion and
/// needs neither the scratchpad nor the flusher — folding it in would make a partial-update
/// pre-fill/flush path carry a value that has nothing to do with position.</para>
/// </summary>
public sealed class SimTransformHeadingInstaller : IBinaryAttributeInstaller<EntityAttributeChange>
{
    private const long GeoSpatialOrdinal = (long)DescriptorOrdinal.WorldPos;

    /// <inheritdoc/>
    public void Install(BinaryInterpreterBuilder<EntityAttributeChange> builder)
    {
        // ⭐ UXI-30: the typed overload gates on CanWrite<SimTransform>() before the handler runs.
        builder.RegisterHandler<SimTransform>(AttributeIds.GeoHeading, HandleGeoHeading);
    }

    /// <summary>
    /// ⭐ Applies a compass heading directly — no scratchpad, no deferred flush.
    ///
    /// <para>⚠ <b>The descriptor IS marked dirty here, unlike a LOCAL direct write.</b> 📌 The design's
    /// routing model notes that a local <see cref="SimTransform"/> write needs no change flag because its
    /// egress translator diffs <c>lastSent</c> every tick. ⭐ This path is the OTHER one — the owner
    /// applying a remote request — and it marks the same <c>dtWorldPos</c> ordinal the position handlers
    /// mark, so heading and position behave identically on the way back out. ⛔ Not a contradiction of the
    /// no-flag rule: different direction, same component.</para>
    /// </summary>
    private void HandleGeoHeading(BinaryPatchContext ctx, EntityAttributeChange record)
    {
        // ⭐ Float64 on the wire (the Geo* family's value type); the bridge takes a float.
        float headingDeg = (float)record.Value.DoubleValue;

        ref SimTransform st = ref ctx.PatchContext.GetUnmanagedComponent<SimTransform>();
        st.Rotation = SimTransformBridgeSystem.HeadingDegToRotation(headingDeg);

        ctx.MarkDescriptorDirty(GeoSpatialOrdinal);
    }
}
