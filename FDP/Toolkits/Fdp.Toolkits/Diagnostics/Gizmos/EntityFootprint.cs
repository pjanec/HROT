using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-3147</c> — THE ONE statement of how big an entity IS on the map, in WORLD METRES.</b>
    ///
    /// <para>🔒 <b>User, <c>2026-10-09</c>, correcting a wrong premise of mine:</b> <i>"the green selection circle
    /// must change size with zoom, now it is zoom independent so when i zoom out the circle is enormous in
    /// comparison to entity symbol (which scales). the sensitive interaction area around the entity must scale as
    /// well."</i></para>
    ///
    /// <para>⛔⛔ <b>What I got wrong, twice, and why this type exists.</b> I sized the pick area and the selection
    /// ring in SCREEN PIXELS, on the assumption that the entity symbol was screen-sized too. 📐 It is not: the
    /// symbol is drawn from <see cref="VehicleParams"/>'s <c>Length</c>/<c>Width</c> — <b>metres</b> — falling back
    /// to <c>5 m × 2.5 m</c> (<c>DebugPrimitiveRenderer2D.cs:416-417</c>), so it SCALES with zoom. ⇒ a fixed-pixel
    /// marker is correct at exactly one zoom and wrong at every other: tiny when zoomed in, enormous when zoomed
    /// out. ⭐ Anything that must track the symbol has to be measured the way the symbol is measured, which is what
    /// this type makes impossible to get wrong a third time.</para>
    ///
    /// <para>⭐⭐ <b>Why it lives HERE.</b> Its three consumers are in assemblies that cannot see each other —
    /// <c>EntityPresentationGizmoShared</c> and <c>EntityDragGizmo</c> in <c>Hrot.Presentation</c>,
    /// <c>SelectionHighlightGizmo</c> in <c>Hrot.Common</c> (siblings: neither references the other). Both
    /// reference <c>Fdp.Toolkits</c>, and <see cref="VehicleParams"/> already lives in it. ⛔ Three copies of this
    /// rule is precisely how the select area, the drag area and the ring came to disagree in the first place.</para>
    ///
    /// <para>⚠ <b>What this does NOT reproduce:</b> the renderer also floors the drawn symbol at
    /// <c>MinAvatarPx = 16</c> on-screen length (<c>DebugPrimitiveRenderer2D.cs:421-428</c>) so an avatar stays
    /// visible when zoomed far out. That floor needs the live zoom, which a projector does not have. ⇒ when zoomed
    /// a long way OUT the drawn symbol stops shrinking while these world-sized areas keep shrinking. ⭐ Acceptable
    /// deliberately: the hit-test adds its own screen-space slack, and the reported problem is the opposite end of
    /// the range. ⛔ Do not "fix" it by going back to screen pixels — that is the bug this type replaced.</para>
    /// </summary>
    public static class EntityFootprint
    {
        /// <summary>⭐ The renderer's fallback length when <see cref="VehicleParams"/> is absent or zero
        /// (<c>DebugPrimitiveRenderer2D.cs:416</c>). Kept identical so pick and draw agree by construction.</summary>
        public const float DefaultLengthMetres = 5f;

        /// <summary>⭐ The renderer's fallback width — half the length (<c>DebugPrimitiveRenderer2D.cs:417</c>).</summary>
        public const float DefaultWidthFactor = 0.5f;

        /// <summary>
        /// ⭐ How much bigger than the symbol's own half-extent the interaction area is.
        /// 🔒 The user's bar: <i>"diameter shouldn't be much bigger than entity symbol"</i> ⇒ a small margin, not a
        /// multiple. ⚠ Tune HERE; it is the only place that decides it.
        /// </summary>
        public const float InteractionMargin = 1.2f;

        /// <summary>
        /// ⭐ The symbol's footprint in metres, by the renderer's own rule: <see cref="VehicleParams"/> when present
        /// and positive, else <c>5 m</c> long by half that wide.
        /// </summary>
        public static void Dimensions(ISimulationView view, Entity entity, out float lengthMetres, out float widthMetres)
        {
            lengthMetres = 0f;
            widthMetres  = 0f;

            if (view.HasComponent<VehicleParams>(entity))
            {
                ref readonly var vp = ref view.GetComponentRO<VehicleParams>(entity);
                lengthMetres = vp.Length;
                widthMetres  = vp.Width;
            }

            if (lengthMetres <= 0f) lengthMetres = DefaultLengthMetres;
            if (widthMetres  <= 0f) widthMetres  = lengthMetres * DefaultWidthFactor;
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The radius, in WORLD METRES, that both the clickable area and the selection ring use.</b>
        /// Half the symbol's LONGER side plus <see cref="InteractionMargin"/>, so a square pick box modestly
        /// circumscribes the drawn shape at every zoom and the ring sits just outside it.
        /// </summary>
        public static float InteractionRadiusMetres(ISimulationView view, Entity entity)
        {
            Dimensions(view, entity, out float len, out float wid);
            float longer = len > wid ? len : wid;
            return longer * 0.5f * InteractionMargin;
        }
    }
}
