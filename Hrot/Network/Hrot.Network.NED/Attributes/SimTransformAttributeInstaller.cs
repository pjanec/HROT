using System.Numerics;
using System.Runtime.CompilerServices;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Core;
using Fdp.Modules.Geographic;

namespace Hrot.SimHost.Installers;

/// <summary>
/// <see cref="IBinaryAttributeInstaller"/> that routes <c>GeoLat</c>, <c>GeoLon</c>,
/// and <c>GeoAlt</c> binary attribute records to <see cref="SimTransform"/> ECS
/// component writes via a scratchpad-based accumulation + deferred flush pattern.
///
/// <para>
/// Rather than converting geodetic coordinates on every record receipt, the three
/// handler delegates accumulate lat/lon/alt into a per-context
/// <see cref="GeoCoordScratchpad"/> block and mark a single subsystem-flush bit.
/// A single <c>ToCartesian</c> conversion is performed at flush time, regardless of
/// how many of the three coordinates are present in the packet.
/// </para>
///
/// <para>
/// <b>Partial update semantics:</b> if only one or two of the three coordinates
/// are present in the packet, the missing coordinates are pre-filled from the
/// entity's current <see cref="SimTransform.Position"/> via a reverse-geodetic
/// <c>ToGeodetic</c> call.  This mirrors the behaviour documented in
/// <c>ATTR2-DESIGN.md §3.3</c> and the <c>TODO</c> note inside
/// <see cref="AttributeCompilerFactory"/>.
/// </para>
/// </summary>
public sealed class SimTransformAttributeInstaller : IBinaryAttributeInstaller<EntityAttributeChange>
{
    // ── Subsystem flusher bit ─────────────────────────────────────────────────
    private const int GeoFlushBit = 0;
    private const long GeoSpatialOrdinal = (long)EDescriptorType.dtWorldPos;

    // ── Scratchpad layout ─────────────────────────────────────────────────────

    /// <summary>
    /// Per-context scratchpad for accumulating individual geodetic coordinate updates.
    /// Zeroed by <see cref="BinaryInterpreter.Apply"/> at the start of each call;
    /// pre-populated from the entity's current position by a registered pre-apply handler.
    /// </summary>
    private struct GeoCoordScratchpad
    {
        /// <summary>Accumulated WGS-84 latitude (degrees).</summary>
        public double Lat;
        /// <summary>Accumulated WGS-84 longitude (degrees).</summary>
        public double Lon;
        /// <summary>Accumulated altitude above reference ellipsoid (metres).</summary>
        public double Alt;
    }

    // ── Instance state ────────────────────────────────────────────────────────

    private readonly IGeographicTransform _geoTransform;
    private int _scratchpadOffset;

    /// <summary>
    /// Initialises the installer with a geographic transform used for coordinate
    /// conversion.
    /// </summary>
    /// <param name="geoTransform">Transform for <c>ToCartesian</c> (forward) and
    /// <c>ToGeodetic</c> (inverse) geodetic conversions.</param>
    public SimTransformAttributeInstaller(IGeographicTransform geoTransform)
    {
        _geoTransform = geoTransform;
    }

    /// <inheritdoc/>
    public void Install(BinaryInterpreterBuilder<EntityAttributeChange> builder)
    {
        _scratchpadOffset = builder.ReserveScratchpad(Unsafe.SizeOf<GeoCoordScratchpad>());

        // Pre-apply: runs once per Apply invocation after the scratchpad is zeroed,
        // before any handler fires.  Pre-fills the scratchpad with the entity's current
        // geodetic position so partial-update packets (e.g. only GeoLat) preserve the
        // unchanged coordinates without an Initialized flag inside the hot dispatch loop.
        builder.RegisterPreApplyHandler(PreFillFromCurrentPosition);

        // ⭐⭐⭐ UXI-30 — registered through the TYPED overload, so the authority gate is applied by the
        //    registration and the handler bodies need no guard of their own. 📌 The hand-written
        //    `if (!CanWrite<SimTransform>()) return;` that used to open each of the three is GONE: it was
        //    correct, and it was also per-installer and therefore forgettable — which is what UXI-30 is
        //    actually about. 📄 BinaryInterpreterBuilder.RegisterHandler<TComponent>.
        builder.RegisterHandler<SimTransform>(AttributeIds.GeoLat, HandleGeoLat);
        builder.RegisterHandler<SimTransform>(AttributeIds.GeoLon, HandleGeoLon);
        builder.RegisterHandler<SimTransform>(AttributeIds.GeoAlt, HandleGeoAlt);
        builder.RegisterSubsystemFlusher(GeoFlushBit, FlushGeo);
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private void HandleGeoLat(BinaryPatchContext ctx, EntityAttributeChange record)
    {
        ref GeoCoordScratchpad scratch = ref ctx.GetScratchpad<GeoCoordScratchpad>(_scratchpadOffset);
        scratch.Lat = record.Value.DoubleValue;
        ctx.MarkSubsystemDirty(GeoFlushBit);
    }

    private void HandleGeoLon(BinaryPatchContext ctx, EntityAttributeChange record)
    {
        ref GeoCoordScratchpad scratch = ref ctx.GetScratchpad<GeoCoordScratchpad>(_scratchpadOffset);
        scratch.Lon = record.Value.DoubleValue;
        ctx.MarkSubsystemDirty(GeoFlushBit);
    }

    private void HandleGeoAlt(BinaryPatchContext ctx, EntityAttributeChange record)
    {
        ref GeoCoordScratchpad scratch = ref ctx.GetScratchpad<GeoCoordScratchpad>(_scratchpadOffset);
        scratch.Alt = record.Value.DoubleValue;
        ctx.MarkSubsystemDirty(GeoFlushBit);
    }

    // ── Flusher ───────────────────────────────────────────────────────────────

    private void FlushGeo(BinaryPatchContext ctx)
    {
        ref GeoCoordScratchpad scratch = ref ctx.GetScratchpad<GeoCoordScratchpad>(_scratchpadOffset);

        var cart = _geoTransform.ToCartesian(scratch.Lat, scratch.Lon, scratch.Alt);
        ref SimTransform st = ref ctx.PatchContext.GetUnmanagedComponent<SimTransform>();
        st.Position = new Vector3((float)cart.X, (float)cart.Y, (float)cart.Z);

        ctx.MarkDescriptorDirty(GeoSpatialOrdinal);
    }

    // ── Pre-apply handler ─────────────────────────────────────────────────────

    /// <summary>
    /// Pre-populates the <see cref="GeoCoordScratchpad"/> from the entity's current
    /// <see cref="SimTransform"/> position via a reverse-geodetic conversion.
    /// Runs once per <see cref="BinaryInterpreter.Apply"/> call, after the scratchpad
    /// has been zeroed, so partial packets (e.g. only GeoLat) preserve the unchanged
    /// coordinates without branching inside the dispatch loop.
    /// </summary>
    private void PreFillFromCurrentPosition(BinaryPatchContext ctx)
    {
        // Skip pre-fill if this node has no authority to write SimTransform —
        // the handlers will also early-out, so the pre-fill data would never be used.
        if (!ctx.PatchContext.CanWrite<SimTransform>()) return;

        ref SimTransform st = ref ctx.PatchContext.GetUnmanagedComponent<SimTransform>();
        var (lat, lon, alt) = _geoTransform.ToGeodetic(st.Position);
        ref GeoCoordScratchpad scratch = ref ctx.GetScratchpad<GeoCoordScratchpad>(_scratchpadOffset);
        scratch.Lat = lat;
        scratch.Lon = lon;
        scratch.Alt = alt;
    }
}
