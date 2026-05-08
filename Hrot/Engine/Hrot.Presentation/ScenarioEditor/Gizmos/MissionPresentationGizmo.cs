using System;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // GZ058: mirrors MissionRenderLayer rendering logic via StatelessGizmoSystem.
    // Draws orange gradient lines from each selected entity to its mission task targets.
    // No [GizmoProjector] because the constructor requires IGeographicTransform;
    // registered manually in composition roots (IgApplication and CgfSubsystem).
    public sealed class MissionPresentationGizmo : IStatelessGizmo
    {
        private static readonly Rgba32 StartColor = new Rgba32(255, 165, 0, 200);  // orange
        private static readonly Rgba32 EndColor   = new Rgba32(0,   0, 139, 200);  // dark blue

        private readonly IGeographicTransform _geoTransform;

        public MissionPresentationGizmo(IGeographicTransform geoTransform)
        {
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<SelectionState>(entity)) return;
            ref readonly var sel = ref view.GetComponentRO<SelectionState>(entity);
            if (!sel.IsSelected) return;

            if (!view.HasManagedComponent<ActiveMissionPlan>(entity)) return;
            var activePlan = view.GetManagedComponentRO<ActiveMissionPlan>(entity);
            if (activePlan?.Plan?.Tasks == null) return;

            ref readonly var simTr = ref view.GetComponentRO<SimTransform>(entity);
            var lastPos = new Vector3(simTr.Position.X, simTr.Position.Y, 0f);

            foreach (var task in activePlan.Plan.Tasks)
            {
                if (string.IsNullOrEmpty(task.BehaviorParams)) continue;

                float targetLat = float.NaN;
                float targetLon = float.NaN;

                try
                {
                    using var doc = JsonDocument.Parse(task.BehaviorParams);
                    if (doc.RootElement.TryGetProperty("targetLat", out var latEl))
                        targetLat = latEl.GetSingle();
                    if (doc.RootElement.TryGetProperty("targetLon", out var lonEl))
                        targetLon = lonEl.GetSingle();
                }
                catch { }

                if (!float.IsNaN(targetLat) && !float.IsNaN(targetLon))
                {
                    var cartesian = _geoTransform.ToCartesian(targetLat, targetLon, 0.0);
                    var targetPos = new Vector3((float)cartesian.X, (float)cartesian.Y, 0f);

                    draw.DrawLineGradient(lastPos, targetPos, StartColor, EndColor, thickness: 2f, SizeMode.WorldMeters);

                    lastPos = targetPos;
                }
            }
        }
    }
}
