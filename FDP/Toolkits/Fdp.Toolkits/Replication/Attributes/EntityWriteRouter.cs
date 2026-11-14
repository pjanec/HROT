using Fdp.Core;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-005b</c> — the ONE composition of the write router, so a host does not assemble one.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §11.3.</para>
///
/// <para>⭐⭐ <b>Why a factory and not three call sites doing it by hand.</b> 📐 Measured <c>2026-08-25</c>:
/// <c>EntityRotatorGizmo</c> is constructed in <b>five</b> places *(<c>CgfSubsystem</c>,
/// <c>SimHostVisualization</c>, <c>SimHostApp</c>, and <c>EditorSubsystem</c> twice)*. ⛔ Five hand-built
/// writers is five chances to forget the <c>publishRequest</c> arm — 📌 exactly the SILENT-DEFAULT pattern
/// *(<c>.claude/CLAUDE.md</c>)*: *"a production caller that HAS a dependency must PASS it."* ⭐ Here the
/// dependency is derived, not passed, so it cannot be forgotten.</para>
///
/// <para>⭐⭐ <b>Nothing is read from the world but the world itself.</b> ⭐ <c>CE-4073</c>: the position attributes are local ENU metres, so
/// the interpreter needs no geographic transform, and a world with no geodetic frame has the same <c>Position.X/Y/Z</c> handlers as any other
/// (it used to have none).</para>
///
/// <para>⛔ <c>R-134</c>: nothing here mentions DDS. The router speaks
/// <see cref="Fdp.Toolkit.Replication.Patching.EntityAttributeChange"/> and the request it publishes is the
/// FDP-internal <c>UpdateEntityAttributeCommand</c>.</para>
/// </summary>
public static class EntityWriteRouter
{
    /// <summary>
    /// ⭐ The write router for <paramref name="repo"/>: local apply when this node owns the component,
    /// a change-request to the owner when it does not.
    /// </summary>
    public static IEntityComponentWriter For(EntityRepository repo)
    {
        System.ArgumentNullException.ThrowIfNull(repo);

        // ⭐⭐ N-6 (CE-4468, U-3) — a THREADED node's presentation world: the write runs on the sim thread, through the router of the SIMULATION
        //   world (the same code, retargeted), and answers Requested — the outcome travels like a request to an owner. A refused write leaves S
        //   untouched, so the next staging pull shows the true value: the gizmo's snap-back holds by construction.
        if (Fdp.Core.Threading.SimJobs.LinkOf(repo) is { } link) return new SimThreadWriter(link);

        // ⭐⭐⭐ AX-016 — RESOLVE the world's interpreter; do NOT build one.
        //    🔴 This used to call `AttributeCompilerFactory.BuildBinaryInterpreter(geo)` inline — and
        //    `For(repo)` is called at EVERY gizmo construction, so that was one interpreter per gizmo, each
        //    with its own scratchpad. 🔒 User ruling: *"the interpreter should not be bound to any network."*
        //    ⇒ it belongs to the WORLD, which is what actually owns the components it writes.
        //    ⚠ The geographic-transform presence check moved into the provider (AX-010's throwing getter).
        return new AttributeEntityComponentWriter(
            repo,
            AttributeInterpreterProvider.GetOrCreate(repo),
            EntityAttributeChangeRequests.PublishOnto(repo));
    }

    /// <summary>⭐ N-6 — the writer a threaded presentation world hands out: each write is a sim job over S's own router.</summary>
    private sealed class SimThreadWriter : IEntityComponentWriter
    {
        private readonly Fdp.Core.Threading.SimLink _link;
        public SimThreadWriter(Fdp.Core.Threading.SimLink link) => _link = link;

        public EntityWriteRoute Write(Entity entity, ushort attributeId, double value)
        {
            _link.Jobs.Post(sim => { For(sim).Write(entity, attributeId, value); }, "write: attribute");
            return EntityWriteRoute.Requested;
        }

        public EntityWriteRoute Write(Entity entity, System.Collections.Generic.IReadOnlyList<EntityAttributeChange> changes)
        {
            var copy = new EntityAttributeChange[changes.Count];   // the caller may reuse its list: the job keeps its own
            for (int i = 0; i < copy.Length; i++) copy[i] = changes[i];
            _link.Jobs.Post(sim => { For(sim).Write(entity, copy); }, "write: attributes");
            return EntityWriteRoute.Requested;
        }
    }
}
