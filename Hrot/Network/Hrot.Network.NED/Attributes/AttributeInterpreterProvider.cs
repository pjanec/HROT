using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Patching;

namespace Hrot.SimHost.Installers;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-016</c> — ONE binary attribute interpreter PER WORLD, resolved rather than built.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §15.</para>
///
/// <para>🔴 <b>WHAT THIS REPLACES, measured `2026-08-26`.</b> Two production sites each built their own:
/// <list type="bullet">
///   <item>⛔⛔ <c>EntityWriteRouter.For(repo)</c> built one <b>PER CALL</b> — and it is called at every
///   gizmo construction *(five rotator sites plus the drag definition per entity)*. So not "one per
///   host": one per gizmo, each reserving its own scratchpad.</item>
///   <item><c>UpdateEntityAttributeRequestSystem</c>'s DDS constructor built another.</item>
/// </list>
/// ⇒ ⭐ the interpreter is a <b>stateless-per-Apply compiled dispatch table</b>; N copies are pure waste,
/// and — worse — N chances for two of them to be built from DIFFERENT geographic transforms and convert the
/// same attribute differently.</para>
///
/// <para>⭐⭐⭐ <b>WHY A WORLD SINGLETON and not a constructor argument.</b> 🔒 User ruling: *"the interpreter
/// should not be bound to any network."* A parameter threaded from a NETWORK FACTORY makes the interpreter's
/// existence a property of the network stack — ⛔ which is exactly why the offline
/// <c>OfflineNetworkFactory</c> supplies none at all. ⭐ The world is the thing that actually owns entities
/// and components, so the world is where the applier belongs. ⇒ a host with no network still has one.</para>
///
/// <para>⚠ <b>What this does NOT yet fix, stated plainly:</b> this type still LIVES in
/// <c>Hrot.Network.NED</c>, because <c>AttributeCompilerFactory</c> and the installers do. ⭐ Moving that
/// stack out of the DDS assembly is a separate, agreed step *(`AX-013`)* — this one removes the
/// duplication and the network binding of the interpreter's LIFETIME, not of its home.</para>
/// </summary>
public static class AttributeInterpreterProvider
{
    /// <summary>
    /// ⭐ The world's interpreter, built on first use and cached on the world itself.
    ///
    /// <para>⭐⭐ <b>Idempotent and allocation-free after the first call</b>, so a per-gizmo or per-tick
    /// caller is no longer a problem. ⚠ Not thread-safe by design: FDP world access is single-threaded
    /// during a tick, and the shipped callers are gizmo construction and a system's <c>Execute</c>.</para>
    ///
    /// <para>⚠ <b><c>IGeographicTransform</c> is read from the world, and its ABSENCE is normal</b>
    /// *(<c>AX-010</c>)*: a host without a geodetic frame simply gets an interpreter with no
    /// <c>Geo*</c> handlers, which <c>AttributeCompilerFactory</c> already models. ⛔ It must not throw —
    /// <c>GetSingletonManaged</c> does when unset, so the presence check is required.</para>
    /// </summary>
    public static BinaryInterpreter<EntityAttributeChange> GetOrCreate(EntityRepository repo)
        => Slot(repo).Binary ??= AttributeCompilerFactory.BuildBinaryInterpreter(GeoOf(repo));

    /// <summary>
    /// ⭐⭐ <b>The world's JSON attribute compiler — same lifetime rule, for the same reason.</b>
    ///
    /// <para>⭐ <c>AX-014</c> made the two arms of <c>UpdateEntityAttributeRequestSystem</c> consistent by
    /// having its constructor default BOTH. ⭐⭐ <c>AX-016</c> keeps them consistent by moving BOTH to the
    /// world — ⛔ otherwise the binary arm would be world-scoped and its sibling constructor-scoped, which is
    /// the asymmetry <c>AX-014</c> existed to remove.</para>
    ///
    /// <para>⚠ The JSON compiler had no per-call duplication problem *(one constructor call per node)*, so
    /// this is consistency rather than a fix. ⭐ Stated so the change is not read as repairing a defect.</para>
    /// </summary>
    public static JsonAttributeCompiler GetOrCreateJson(EntityRepository repo)
        => Slot(repo).Json ??= AttributeCompilerFactory.Build(GeoOf(repo));

    // ── the cache ─────────────────────────────────────────────────────────────

    private sealed class Appliers
    {
        public BinaryInterpreter<EntityAttributeChange>? Binary;
        public JsonAttributeCompiler?                    Json;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Keyed off the world, but NOT stored as an ECS singleton — and the difference is deliberate.</b>
    ///
    /// <para>🔴 <c>SetSingletonManaged&lt;T&gt;</c> was tried first and REJECTED on measurement: it throws
    /// *"Component type … is missing a [ComponentId] attribute"*. ⛔ That mechanism is for ECS COMPONENTS, so
    /// using it here would mean burning two GLOBAL COMPONENT-ID SLOTS on things that are not entity data —
    /// and <c>BinaryInterpreter&lt;T&gt;</c> is an OPEN GENERIC, so every instantiation would have to share
    /// one id. ⇒ ⭐ these are SERVICES DERIVED FROM a world, not state stored IN one.</para>
    ///
    /// <para>⭐⭐ <see cref="ConditionalWeakTable{TKey,TValue}"/> gives exactly that: one instance per live
    /// world, collected with the world, and no ECS coupling. ⚠ It is thread-safe for add/lookup, which
    /// matters only because test assemblies run classes in parallel — ⛔ not a licence to resolve off-thread
    /// during a tick.</para>
    /// </summary>
    private static readonly ConditionalWeakTable<EntityRepository, Appliers> Cache = new();

    private static Appliers Slot(EntityRepository repo)
    {
        System.ArgumentNullException.ThrowIfNull(repo);
        return Cache.GetValue(repo, static _ => new Appliers());
    }

    /// <summary>
    /// ⚠ <b>The geodetic frame is OPTIONAL and its absence is normal</b> *(<c>AX-010</c>)* — a host without
    /// one simply gets appliers with no <c>Geo*</c> handlers. ⛔ The presence check is required because
    /// <c>GetSingletonManaged</c> THROWS when unset; that is the trap that reddened the AX-005 rail on the IG.
    /// </summary>
    private static IGeographicTransform? GeoOf(EntityRepository repo)
        => repo.HasSingletonManaged<IGeographicTransform>()
            ? repo.GetSingletonManaged<IGeographicTransform>()
            : null;
}
