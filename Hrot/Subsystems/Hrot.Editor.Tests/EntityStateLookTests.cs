using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Look;
using Fdp.Toolkit.Replication.Services;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐ <c>CE-3252</c> (LD-D / LD-I; docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.2b) — <c>GET /entities/{id}/state</c>
/// shows where a PERSON looks: body yaw, head turn, the eyes (body + head — the cone vision uses), who decides it and the wanted point;
/// a body with no look shows none. Built on the cluster shape (a node's world through its provider), because the editor-shape harness
/// rails are excluded from the build (DEBT-MCP-001) — no compiled suite read this route before.
/// </summary>
public sealed class EntityStateLookTests
{
    /// <summary>✅ Red-proof: drop the <c>look</c> block in <c>GetEntityState</c> ⇒ the field is absent.</summary>
    [Fact]
    public void CE3252_GetEntityState_ShowsWhereAPersonLooks()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<LookState>();
        var map = new NetworkEntityMap();
        var svc = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[] { new SubsystemDebugProvider("CGF", "Scenario", world: () => world, entityMap: () => map) },
            currentPerspective: () => "Scenario",
            acksPending: null));

        var man = world.CreateEntity();
        map.Register(90_300L, man);
        world.AddComponent(man, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });
        world.AddComponent(man, new LookState
        {
            Target = new Vector3(-10f, 10f, 0f), HasTarget = 1, Source = LookSource.LookOrdered, HeadYaw = MathF.PI / 4f,
        });
        var car = world.CreateEntity();
        map.Register(90_301L, car);
        world.AddComponent(car, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

        var (state, error, _) = svc.GetEntityState(90_300L);
        Assert.Null(error);
        var look = state!["look"]!;
        Assert.Equal(90f, look["bodyYawDeg"]!.GetValue<float>(), 1);
        Assert.Equal(45f, look["headYawDeg"]!.GetValue<float>(), 1);
        Assert.Equal(135f, look["eyeYawDeg"]!.GetValue<float>(), 1);
        Assert.Equal("order", look["source"]!.GetValue<string>());
        Assert.Equal(-10f, look["target"]!["x"]!.GetValue<float>(), 3);

        var (carState, _, _) = svc.GetEntityState(90_301L);
        Assert.Null(carState!["look"]);
    }
}
