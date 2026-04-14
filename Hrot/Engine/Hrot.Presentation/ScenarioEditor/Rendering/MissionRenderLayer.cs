using System.Numerics;
using System.Text.Json;
using Fdp.Toolkit.Behavior.Components;
using Hrot.IG.Components;
using Fdp.Kernel;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Transforms;
using Fdp.ModuleHost.Abstractions;
using Raylib_cs;

namespace Hrot.ScenarioEditor.Rendering;

public class MissionRenderLayer : IMapLayer
{
    public const string LayerName = "MissionRoutes";
    public const int MissionRouteseLayerBitIndex = 4; // Or another available bit index
    
    private readonly ISimulationView _view;
    private readonly EntityQuery _query;
    private readonly IGeographicTransform _geoTransform;

    public string Name => LayerName;
    public int LayerBitIndex => MissionRouteseLayerBitIndex;

    public MissionRenderLayer(ISimulationView repo, IGeographicTransform geoTransform)
    {
        _view = repo;
        _query = repo.Query()
            .WithManaged<ActiveMissionPlan>()
            .With<SimTransform>()
            .With<SelectionState>()
            .Build();
        _geoTransform = geoTransform;
    }

    public void Update(float dt) { }

    public void Draw(RenderContext ctx)
    {
        foreach (var entity in _query)
        {
            if (!_view.GetComponentRO<SelectionState>(entity).IsSelected) continue;

            var activePlan = _view.GetManagedComponentRO<ActiveMissionPlan>(entity);
            if (activePlan?.Plan?.Tasks == null) continue;

            ref readonly var simTr = ref _view.GetComponentRO<SimTransform>(entity);
            var currentPos = new Vector2(simTr.Position.X, simTr.Position.Y);
            
            Vector2 lastPos = currentPos;

            foreach (var task in activePlan.Plan.Tasks)
            {
                if (string.IsNullOrEmpty(task.BehaviorParams)) continue;

                // Try parse basic coordinates
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
                    // Project lat/lon to world space
                    var cartesian = _geoTransform.ToCartesian(targetLat, targetLon, 0.0);
                    var targetPos = new Vector2((float)cartesian.X, (float)cartesian.Y);

                    // Draw line from last pos to target
                    Raylib.DrawLineEx(lastPos, targetPos, 2.0f / (ctx.Zoom > 0 ? ctx.Zoom : 1f), Color.Orange);

                    // Draw a marker at target
                    float pointSize = 6.0f / (ctx.Zoom > 0 ? ctx.Zoom : 1f);
                    Raylib.DrawCircleV(targetPos, pointSize, Color.DarkBlue);
                    Raylib.DrawCircleLinesV(targetPos, pointSize, Color.SkyBlue);

                    lastPos = targetPos;
                }
            }
        }
    }

    public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;
    public bool HandleHover(Vector2 worldPos) => false;
    public bool HandleClick(Vector2 worldPos, MouseButton button) => false;
    public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;
    public bool HandleKeyPressed(KeyboardKey key) => false;

    public Entity? PickEntity(Vector2 worldPos) => null;
}