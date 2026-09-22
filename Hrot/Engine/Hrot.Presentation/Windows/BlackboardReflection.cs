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
    /// StructEdit project a brain's ROOT PARAMS out of its occurrence store.
    /// </summary>
    /// <param name="inspector">the host's entity inspector panel.</param>
    /// <param name="registry">
    /// the behavior registry used to resolve a brain's <c>BlackboardLayoutType</c>/<c>HeavyDtoType</c>.
    /// ⚠ Captured, so a host may pass a field that is populated later — but ⛔ a production caller that
    /// HAS one must pass it (the silent-default rule): a null registry makes every edit context null,
    /// which renders the panel's typed projection silently inert.
    /// </param>
    public static void Apply(EntityInspectorPanel inspector, BehaviorRegistry? registry)
    {
        if (inspector is null) throw new ArgumentNullException(nameof(inspector));

        // ⭐ P4-③ (2026-09-22): project the behaviour's ROOT PARAMS SLOT as its typed DTO.
        //    ⛔ It was BrainBlackboardViewProvider, bound to BrainBlackboard.$.BehaviorParameters —
        //    a buffer P3 stopped filling while leaving the component attached, so the editor bound
        //    its fields to permanently zero bytes. 📄 CE-312, §30.22.
        inspector.Reflector.AddBufferViewProvider(new RootParamsViewProvider());
        // ⛔ P4-① (2026-09-22): the heavy Blackboard1024 provider is GONE with its component. The
        //    "heavy" tier it projected was the params-overflow path, and HeavyDtoType was null at
        //    every production site — so this arm could only ever return null. 📄 §30.13.

        // Inject EditContextFactory so TryOpenEditWindow passes the DTO type AND the slot offset.
        inspector.Reflector.EditContextFactory = (session, e, type) =>
        {
            // ⭐ The gate is now "is this the entity's occurrence-store component?", asked of the tier
            //   TABLE — the params live in whichever tier the allocator put the entity on, and it may
            //   promote between frames. ⛔ Naming one component here would silently stop working on
            //   promotion, which is the failure this slice is fixing in the first place.
            if (registry == null) return null;
            if (!RootParamsProjection.TryLocateRootParams(
                    session, e, registry, out var tierType, out int payloadOffset, out var def))
                return null;
            if (type != tierType) return null;
            if (def!.BlackboardLayoutType == null) return null;

            return new StructEdit.Core.EditContext()
                .With(RootParamsViewProvider.LayoutTypeKey, def.BlackboardLayoutType)
                .With(RootParamsViewProvider.OffsetKey, payloadOffset);
        };
    }
}
