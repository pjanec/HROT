using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐ <c>CE-3120</c> (R-227) — the ONE rule that decides whether a gizmo of a FAMILY draws for a unit: always when the family's
    /// scope is <see cref="GizmoScope.All"/>; otherwise when the unit is selected (<see cref="SelectionState.IsSelected"/>) or has the
    /// family pinned (<see cref="DebugState.Ai"/>). The scope is read live from the <see cref="GizmoSettingsRegistry"/>, so switching it
    /// in the layer panel takes effect on the next frame. Attached by <c>MapInteractionPack</c> to every projector that names a
    /// <see cref="GizmoProjectorAttribute.Family"/>. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5b.
    /// </summary>
    public sealed class GizmoFamilyVisibilityPolicy : IGizmoVisibilityPolicy
    {
        private readonly GizmoSettingsRegistry? _settings;

        public AiOverlayFlags Family { get; }

        public GizmoFamilyVisibilityPolicy(GizmoSettingsRegistry? settings, AiOverlayFlags family)
        {
            _settings = settings;
            Family = family;
            if (settings != null) GizmoFamilies.Register(settings);
        }

        public bool IsGloballyEnabled(ISimulationView view) => true;

        public bool IsEntityVisible(ISimulationView view, Entity entity)
        {
            if (GizmoFamilies.ScopeOf(_settings, Family) == GizmoScope.All) return true;
            if (IsRegistered<SelectionState>(view) && view.HasComponent<SelectionState>(entity)
                && view.GetComponentRO<SelectionState>(entity).IsSelected) return true;
            return IsRegistered<DebugState>(view) && view.HasComponent<DebugState>(entity)
                   && (view.GetComponentRO<DebugState>(entity).Ai & Family) != 0;
        }

        // A host that never registered the component cannot have it on a unit (and HasComponent on an unknown type is not safe on
        // every view); a non-repository view is trusted to answer HasComponent.
        private static bool IsRegistered<T>(ISimulationView view) where T : unmanaged =>
            view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>();
    }
}
