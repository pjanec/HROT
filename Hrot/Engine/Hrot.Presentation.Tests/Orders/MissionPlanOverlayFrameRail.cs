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
        new MissionPlanOverlayGizmo(() => new[] { unit }).UpdateAndDraw(w, 0f, buffer);
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
    }
}
