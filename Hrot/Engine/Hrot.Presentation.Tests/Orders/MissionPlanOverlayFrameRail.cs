using System;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Orders;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Orders;
using Hrot.Presentation.Tests.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Orders;

/// <summary>
/// ⭐ W-1 / P-8 8d (mockup <c>mission-plan-editor.svg</c>, the map) — the selected unit's plan drawn on a REAL 2-D map frame: the route in the unit's
/// side colour and a numbered marker per task (done green, running amber, planned grey). Renders <c>mission-plan-map.png</c> under
/// <c>HROT_RAIL_SHOTS</c>; asserts the amber (running) and green (done) markers are on the picture.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class MissionPlanOverlayFrameRail
{
    [SkippableFact]
    public void P8d_ThePlanOnARealMapFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var w = new EntityRepository();
        w.RegisterComponent<SimTransform>();
        w.RegisterManagedComponent<ActiveMissionPlan>();
        w.RegisterComponent<MissionPlanQueue>();
        Assert.True(OrderMenu.TryGetByName("Move", out var move));
        string MoveTo(float x, float y)
        {
            var dto = Activator.CreateInstance(move.Dto)!;
            var f = move.Dto.GetField("Location");
            if (f != null) f.SetValue(dto, new Fdp.Toolkit.Replication.PointLink(new Vector3(x, y, 0)));
            else move.Dto.GetProperty("Location")!.SetValue(dto, new Fdp.Toolkit.Replication.PointLink(new Vector3(x, y, 0)));
            return Hrot.Presentation.Behavior.BehaviorParamsForm.ToJson(dto, move.Dto);
        }
        var unit = w.CreateEntity();
        w.AddComponent(unit, new SimTransform { Position = new Vector3(-120, -80, 0) });
        var plan = new DomainMissionPlan();
        plan.Tasks.Add(new DomainMissionTask { TaskId = Guid.NewGuid(), BehaviorName = "Order.Move", BehaviorParams = MoveTo(-60, -10) });
        plan.Tasks.Add(new DomainMissionTask { TaskId = Guid.NewGuid(), BehaviorName = "Order.Move", BehaviorParams = MoveTo(-20, 50) });
        plan.Tasks.Add(new DomainMissionTask { TaskId = Guid.NewGuid(), BehaviorName = "Order.Move", BehaviorParams = MoveTo(80, 90) });
        w.SetManagedComponent(unit, new ActiveMissionPlan { Plan = plan });
        w.AddComponent(unit, new MissionPlanQueue { CurrentTask = 1, TaskCount = 3 });

        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-plan-map");
        Directory.CreateDirectory(shots);
        using var frame = UiFrameHarness.Begin(800, 520);
        Fdp.Toolkit.Vis3D.LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var camera = new MapCamera();
        const float zoom = 2.5f;
        var target = new Vector2(-800f / 2f / zoom, 520f / 2f / zoom);   // centre (0, 0), north up
        camera.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        canvas.Camera = camera;
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
        canvas.AddLayer(gizmos);
        new Hrot.ScenarioEditor.Gizmos.MissionPresentationGizmo(new Fdp.Modules.Geographic.Transforms.WGS84Transform(0, 0, 0)).Draw(w, unit, buffer);
        gizmos.Update(0.016f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        string png = Path.Combine(shots, "mission-plan-map.png");
        frame.Screenshot(png);
        var image = Raylib.LoadImage(png);
        int amber = 0, green = 0;
        try
        {
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    var c = Raylib.GetImageColor(image, x, y);
                    if (Math.Abs(c.R - 242) + Math.Abs(c.G - 192) + Math.Abs(c.B - 51) < 50) amber++;
                    if (Math.Abs(c.R - 91) + Math.Abs(c.G - 208) + Math.Abs(c.B - 106) < 50) green++;
                }
        }
        finally { Raylib.UnloadImage(image); }
        Assert.True(amber > 20, $"the running task's marker is drawn amber ({amber} px)");
        Assert.True(green > 20, $"the done task's marker is drawn green ({green} px)");

        // ⭐ 8d-2 — the same plan with the Plan tool armed, task 3 mid-drag: a white handle on each point task, the dragged marker amber where the
        //   mouse is, its legs previewed. Renders mission-plan-drag.png.
        var missions = new NoMissions();
        var edit = new Hrot.ScenarioEditor.Gizmos.MissionPlanEditGizmo(w, unit, 1, () => { }, missions);
        edit.SetFocus(true);
        edit.OnInteractionStarted(new Fdp.Toolkit.Diagnostics.Gizmos.GizmoPickToken { SubElementId = 3 }, new Vector3(80, 90, 0));
        edit.OnDragUpdate(new Vector3(110, 20, 0));
        buffer.Clear();
        new Hrot.ScenarioEditor.Gizmos.MissionPresentationGizmo(new Fdp.Modules.Geographic.Transforms.WGS84Transform(0, 0, 0)).Draw(w, unit, buffer);
        edit.UpdateAndDraw(w, 0.016f, buffer);
        gizmos.Update(0.016f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        string drag = Path.Combine(shots, "mission-plan-drag.png");
        frame.Screenshot(drag);
        var dragged = Raylib.LoadImage(drag);
        int amberNear = 0;
        try
        {
            // the dropped place (110, 20) on screen: x = (110 - target.X) * zoom, y = (target.Y - 20) * zoom (north up)
            int cx = (int)((110 - target.X) * zoom), cy = (int)((target.Y - 20) * zoom);
            for (int y = Math.Max(0, cy - 14); y < Math.Min(dragged.Height, cy + 14); y++)
                for (int x = Math.Max(0, cx - 14); x < Math.Min(dragged.Width, cx + 14); x++)
                {
                    var c = Raylib.GetImageColor(dragged, x, y);
                    if (Math.Abs(c.R - 242) + Math.Abs(c.G - 192) + Math.Abs(c.B - 51) < 50) amberNear++;
                }
        }
        finally { Raylib.UnloadImage(dragged); }
        Assert.True(amberNear > 20, $"the dragged marker is drawn where the mouse is ({amberNear} amber px near it)");
    }

    private sealed class NoMissions : Hrot.UI.Common.Facades.IMissionEditorService
    {
        public System.Collections.Generic.IReadOnlyList<string> GetAvailableBehaviors(int entityId) => Array.Empty<string>();
        public (Hrot.Core.Mission.MissionPlan? Plan, long Version) GetMissionSnapshot(int entityId) => (null, 0);
        public System.Threading.Tasks.Task<Hrot.UI.Common.Models.MissionCommitResult> CommitMissionAsync(int entityId, Hrot.Core.Mission.MissionPlan plan, long baseVersion)
            => System.Threading.Tasks.Task.FromResult(new Hrot.UI.Common.Models.MissionCommitResult(false, 0));
        public System.Threading.Tasks.Task<Hrot.UI.Common.Models.MissionCommitResult> SendControlCommandAsync(int entityId, Hrot.Core.Mission.eMissionCommandType type, Guid taskId)
            => System.Threading.Tasks.Task.FromResult(new Hrot.UI.Common.Models.MissionCommitResult(false, 0));
    }
}
