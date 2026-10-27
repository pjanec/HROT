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
}
