using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Core.Tkb;
// Disambiguate from GizmoMap.Contracts' Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐ CE-1232 (docs/DESIGN_Visual_Effects.md §11 CH-2, R-1016) — a PLACED explosive charge (an IED) on the 2-D map: a small diamond in its
    /// side's colour reading "IED", the same size at every zoom. ⛔ Was: its class chain reached <c>LandPlatform</c>, so it drew a
    /// ground-VEHICLE outline at its ~30 cm size — sub-pixel, the label alone showing. Keyed on the CLASS
    /// (<c>ExplosiveChargeWeaponSystem</c>), like the 3-D kit (<c>WeaponKits.KindOf</c>). A WORN charge (a suicide vest) is carried — it is
    /// never marked (Civilians AW-12: the bomber is a person); a car bomb is a passenger of its car, which has the symbol.
    /// </summary>
    public static class ChargeMarker
    {
        /// <summary>The diamond's half-size on screen (px).</summary>
        public const float HalfPixels = 7f;

        /// <summary>The class that makes a type an explosive charge (<see cref="EntityClassTable"/>).</summary>
        public const string ChargeClass = "ExplosiveChargeWeaponSystem";

        private static readonly Rgba32 Fill = new(30, 26, 22, 230);
        private static readonly FixedString32 Text = new("IED");

        /// <summary>True when <paramref name="disType"/> is an explosive charge's type.</summary>
        public static bool IsChargeType(ulong disType)
        {
            if (disType == 0) return false;
            foreach (var c in EntityClassTable.Default.Chain(new DISEntityType { Value = disType }))
                if (c.Name == ChargeClass) return true;
            return false;
        }

        /// <summary>True when <paramref name="entity"/> (of DIS type <paramref name="disType"/>) is a charge lying where it was placed — not
        /// carried by a man nor stowed in a vehicle.</summary>
        public static bool IsPlaced(ISimulationView view, Entity entity, ulong disType)
            => IsChargeType(disType) && !IsCarried(view, entity);

        private static bool IsCarried(ISimulationView view, Entity entity)
            => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<IsEmbarkedTag>()) && view.HasComponent<IsEmbarkedTag>(entity);

        /// <summary>The marker at <paramref name="at"/>: the diamond (2-D only — the 3-D map draws the charge itself, FL-7) and "IED" beside it.</summary>
        public static void Draw(IDebugDrawBuilder draw, Vector3 at, Rgba32 sideColour)
        {
            draw.DrawBox2D(new Vector2(at.X, at.Y), new Vector2(HalfPixels, HalfPixels), sideColour, angleDeg: 45f, thickness: 2f,
                           sizeMode: SizeMode.ScreenPixels, target: PipelineTarget.Map2D | PipelineTarget.FlatOnly, fillColor: Fill);
            draw.DrawText(at.X, at.Y, Text, sideColour, fontSizePx: 11f, lineOffsetPx: 14f);
        }
    }
}
