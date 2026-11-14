using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Constants;
using Fdp.Toolkit.Combat.Components;
using Hrot.Map.Common.Components;
using Fdp.Core.Serialization;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// Emits a <see cref="Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.ContextMenuBinding"/>
    /// meta-primitive for every networked entity so that the IG dumb terminal can present a
    /// right-click context menu via the gizmo stream.
    ///
    /// Menu JSON strings are pre-serialised as static fields - one per distinct entity state
    /// permutation - and interned in the <see cref="Fdp.Toolkit.Diagnostics.Gizmos.StringInternMap"/>
    /// on the first frame each string is encountered. After that, only the 4-byte FNV-1a hash
    /// travels with every DebugPrimitive; the full string is cached on both sides.
    ///
    /// Currently two permutations are defined:
    /// <list type="bullet">
    ///   <item>Healthy   - unit is combat-effective (health &gt;= 50 %)</item>
    ///   <item>Degraded  - unit has taken significant damage (health &lt; 50 %)</item>
    /// </list>
    /// </summary>
    [GizmoProjector(typeof(NetworkIdentity))]
    public sealed class ContextMenuProjectorGizmo : IStatelessGizmo
    {
        // ---- Pre-serialised menu permutations (built once at class-load time) ----

        private static readonly JsonSerializerOptions SerializerOptions =
            // CE-3187 (R-2005): the shared options; every ContextMenuItemDto member carries its own ignore condition.
            FdpJsonOptionsRegistry.DefaultRelaxed;

        /// <summary>⭐ B-11 A1 — forwards to <see cref="Hrot.Common.Actions.MapMenuActions.OrderItem"/> (the order rows are the catalogue's now).</summary>
        public static ContextMenuItemDto? OrderItem(Hrot.Common.Orders.OrderInfo o, bool degraded, bool form)
            => Hrot.Common.Actions.MapMenuActions.OrderItem(o, degraded, form);

        // ---- IStatelessGizmo --------------------------------------------------

        /// <summary>
        /// ⭐ CE-2387 (T6; docs/DESIGN_Evaluation_Cost_And_Cadence.md §5 C3, R-2083) — draws NOTHING any more. 📐 It built and hashed every entity's menu
        /// JSON every frame (≈ 6 % of a Crossfire frame) for a menu almost never opened. 🔒 User, <c>2026-08-10</c>: <i>"Serialise on right-click, not per
        /// <c>Draw</c>"</i> (UX_Feature_Cross_Surface_Actions.md:158, :225). ⭐ The map now asks <see cref="MenuJsonFor"/> for the ONE entity right-clicked
        /// (<c>MapInteractionPack.BuildRenderLayer</c> → the terminal's <c>MenuResolver</c>), the same builder the inspector and the ORBAT already call.
        /// </summary>
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw) { }

        /// <summary>
        /// The menu permutation for <paramref name="entity"/> — what <see cref="Draw"/> binds. ⭐ CE-3024: factored out so a
        /// rail can assert WHICH menu an entity gets (the zone menu is the only one carrying "Load zone").
        /// </summary>
        public static string MenuJsonFor(ISimulationView view, Entity entity)
            // ⭐⭐ B-11 A1 (docs/UX/DESIGN_Action_Catalogue.md §6) — the catalogue composes it; ⛔ SUPERSEDED: the hand-built if chain, the three
            //    static graphic menus and the per-flag unit builder (TheMenusAreGoldenTests holds the output byte-identical).
            => Hrot.Common.Actions.MapMenuActions.JsonFor(view, entity);
    }
}
