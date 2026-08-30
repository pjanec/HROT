using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Gizmos;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-23</c> <c>S4</c> — off-screen culling, as a POLICY.</b>
    /// 📄 The design is <c>UX_Feature_Entity_Symbology.md</c> §3.4, written verbatim as
    /// <c>registry.Register(new EntityPresentationGizmo(), new CullingStateVisibilityPolicy())</c>;
    /// <c>UX_Feature_Map_Parity.md</c> §3.2f records why it could not be built until now.
    ///
    /// <para>🔒 <b>This is the ONE implementation of <i>"should this entity draw?"</i>.</b>
    /// <c>S2a</c> had the same logic inside <c>EntityPresentationGizmo.Draw</c> — a setting check plus a
    /// component presence test. That was a second mechanism for one concept, which ruling 9 forbids, so it
    /// moved here wholesale and the projector went back to doing one job: emitting primitives.</para>
    ///
    /// <para>⭐⭐ <b>What moving it BUYS, beyond tidiness.</b> Inside the projector, culling could only ever
    /// apply to that one projector. As a policy it attaches to ANY of them through the registrar's
    /// resolver — so routes, tactical areas and map overlays become cullable without another line of
    /// projector code.</para>
    ///
    /// <para>⚠⚠ <b>Default OFF, and the reason is measured, not cautious.</b> <c>CE-131</c>: IG's
    /// <c>MapCullingSystem</c> derives <c>IsVisible</c> from <c>MapCameraViewport</c>, filled from the
    /// PROJECTED SCREEN CORNERS of the live map view — degenerate without a real viewport, so every entity
    /// tests out of view. Enabling this by default blanked the IG perspective in a live run. ⛔ <c>S4</c>
    /// puts culling in the right PLACE; it does not make its INPUT correct.</para>
    ///
    /// <para>⭐ Two conditions gate it, and both are load-bearing: the host must have asked
    /// (<c>map.entity.cullOffscreen</c>), and the entity must actually carry <c>CullingState</c> — a host
    /// that produces none draws everything, which is what four of five did before the merge.</para>
    /// </summary>
    public sealed class CullingStateVisibilityPolicy : IGizmoVisibilityPolicy
    {
        private readonly GizmoSettingsRegistry? _settings;
        private readonly uint _cullKey;

        public CullingStateVisibilityPolicy(GizmoSettingsRegistry? settings)
        {
            _settings = settings;
            EntityPresentationGizmoSettings.Register(settings!);
            _cullKey = GizmoSettingsRegistry.ComputeHash(EntityPresentationGizmoSettings.CullOffscreen);
        }

        /// <summary>
        /// ⭐ Always true. Culling is a per-ENTITY question; suppressing the whole projector for the frame
        /// would be a different feature, and one nothing has asked for.
        /// </summary>
        public bool IsGloballyEnabled(ISimulationView view) => true;

        /// <inheritdoc/>
        public bool IsEntityVisible(ISimulationView view, Entity entity)
        {
            if (_settings is null || !_settings.Read(_cullKey).BoolValue) return true;
            if (!view.HasComponent<CullingState>(entity)) return true;

            return view.GetComponentRO<CullingState>(entity).IsVisible;
        }
    }
}
