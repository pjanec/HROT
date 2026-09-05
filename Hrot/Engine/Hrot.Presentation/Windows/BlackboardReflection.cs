using System;
using Fdp.Presentation.Panels;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Presentation.Renderers;

namespace Hrot.Presentation.Windows;

/// <summary>
/// ⭐⭐ <b>The blackboard-reflection setup for an entity inspector — ONE implementation, two callers.</b>
///
/// <para>📐 <b>Measured `2026-08-27`:</b> this block was duplicated <b>verbatim</b> between
/// <c>CgfSubsystem</c> and <c>EditorSubsystem</c> — the two <c>AddBufferViewProvider</c> calls plus the
/// entire <c>EditContextFactory</c> lambda, ~30 lines each, identical down to the comments. ⇒ ⭐ at the
/// time it was the single largest verbatim duplicate in the two composition roots.</para>
///
/// <para>⛔⛔ <b>Why this is NOT part of <see cref="DiagnosticsWindowsBundle"/>.</b> 📐 IG and SimHost do
/// <b>none</b> of it. ⇒ folding it into the bundle every host composes would hand two hosts a capability
/// they do not have today — ⚠ <b>and it would look like a successful unification</b>, which is the exact
/// trap <c>IUiBundle</c>'s own doc warns about. ⭐ A shared method with two explicit callers keeps
/// "who gets this" a host decision. 📄 Design §5c.7 <c>F5</c> / <c>G3</c>.</para>
///
/// <para>⚠ A third caller would be a BEHAVIOUR CHANGE, not an adoption — argue it in a design first.</para>
/// </summary>
public static class BlackboardReflection
{
    /// <summary>
    /// Registers the typed-DTO buffer view providers and the <c>EditContextFactory</c> that lets
    /// StructEdit project a brain's blackboard.
    /// </summary>
    /// <param name="inspector">the host's entity inspector panel.</param>
    /// <param name="registry">
    /// the behavior registry used to resolve a brain's <c>ParamsDtoType</c>/<c>HeavyDtoType</c>.
    /// ⚠ Captured, so a host may pass a field that is populated later — but ⛔ a production caller that
    /// HAS one must pass it (the silent-default rule): a null registry makes every edit context null,
    /// which renders the panel's typed projection silently inert.
    /// </param>
    public static void Apply(EntityInspectorPanel inspector, BehaviorRegistry? registry)
    {
        if (inspector is null) throw new ArgumentNullException(nameof(inspector));

        // Project the raw BrainBlackboard.BehaviorParameters as its typed DTO.
        inspector.Reflector.AddBufferViewProvider(new BrainBlackboardViewProvider());
        // And the heavy Blackboard1024.
        inspector.Reflector.AddBufferViewProvider(new Blackboard1024ViewProvider());

        // Inject EditContextFactory so TryOpenEditWindow passes ParamsDtoType/HeavyDtoType to StructEdit.
        inspector.Reflector.EditContextFactory = (session, e, type) =>
        {
            if (type != typeof(BrainBlackboard) && type != typeof(Blackboard1024)) return null;
            if (!session.HasComponent(e, typeof(BehaviorState))) return null;
            var ds = session.GetComponent(e, typeof(BehaviorState)) as BehaviorState?;
            if (ds == null) return null;
            if (registry?.TryGetDefinition(ds.Value.ActiveBehaviorHash, out var def) != true) return null;
            if (def == null) return null;

            if (type == typeof(BrainBlackboard))
            {
                if (def.ParamsDtoType == null) return null;
                return new StructEdit.Core.EditContext().With("ParamsDtoType", def.ParamsDtoType);
            }

            // Blackboard1024
            if (def.HeavyDtoType == null) return null;
            return new StructEdit.Core.EditContext().With("HeavyDtoType", def.HeavyDtoType);
        };
    }
}
