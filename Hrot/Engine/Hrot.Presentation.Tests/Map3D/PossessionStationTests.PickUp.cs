using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

public sealed partial class PossessionStationTests
{
    /// <summary>
    /// ⭐ CE-1207 (docs/DESIGN_Entity_Control.md §8m PK-3) — a lying stone has no pick box, so the STATION chooses it: with no unit under the
    /// crosshair that offers a verb, the use target is the lying hand-item round nearest the crosshair's point (within 1 m) and within his
    /// reach; the prompt is the rule's "Pick up". A round too far from him, or from the point, is not chosen. ✅ Red-proof: UseTargetFor
    /// returning the crosshair's target only ⇒ 0.
    /// </summary>
    [Fact]
    public void CE1207_TheUseTarget_IsTheLyingStoneNearestTheCrosshairPoint_InHisReach()
    {
        using var u = new UseWorld();
        if (!u.W.IsComponentTypeRegistered<LightweightRound>()) u.W.RegisterComponent<LightweightRound>();
        var stoneType = new TkbTemplate("Stone", 9890);
        stoneType.AddDescriptor(new WarheadDto { Kind = WarheadKind.Blunt, Indirect = true });
        stoneType.AddDescriptor(new HandItemDto { LiesSeconds = 60f });
        ((Fdp.Toolkit.Tkb.TkbDatabase)u.W.GetSingletonManaged<ITkbDatabase>()!).Register(stoneType);
        Entity Stone(Vector3 at, long id)
        {
            var e = u.W.CreateEntity();
            u.W.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            u.W.AddComponent(e, new LightweightRound());
            u.W.AddComponent(e, new TkbIdentity { TkbType = 9890 });
            u.Map.Register(id, e);
            return e;
        }
        Stone(new Vector3(1.0f, 0.2f, 0f), 501);
        Stone(new Vector3(1.2f, -0.6f, 0f), 502);
        Stone(new Vector3(6f, 0f, 0f), 503);   // out of his reach
        var (_, sw, _) = Station(u, 0);
        var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);

        Assert.Equal(501L, driver.UseTargetFor(u.W, 0, new Vector3(1.1f, 0.3f, 0f)));      // the nearer of the two in reach
        Assert.Equal(502L, driver.UseTargetFor(u.W, 0, new Vector3(1.2f, -0.7f, 0f)));
        Assert.Equal(0L, driver.UseTargetFor(u.W, 0, new Vector3(6f, 0f, 0f)));            // under the crosshair, but out of his reach
        Assert.Equal(0L, driver.UseTargetFor(u.W, 0, new Vector3(3f, 0f, 0f)));            // nothing within 1 m of the point
        var door = u.Door(new Vector3(1f, 0f, 0f), Fdp.Toolkit.Terrain.TerrainDoorState.Closed, 77);
        Assert.Equal(77L, driver.UseTargetFor(u.W, 77, new Vector3(1.1f, 0.3f, 0f)));      // a unit that offers a verb wins
        Assert.Equal(UseVerbKind.PickUpRound, OperatorUseActions.For(u.W, u.Me, u.Map.TryGetEntity(501, out var s) ? s : default)[0].Verb);
        _ = door;
    }

    /// <summary>
    /// ⭐ CE-1207 (§8m PK-2, PK-6) — a lying rifle under the crosshair prompts "Take"; once he holds a weapon, a TAP with nothing else on
    /// offer sends nothing (Drop is hold-only — never a stray tap), the prompt says "hold F", and holding opens the list with Drop.
    /// ✅ Red-proof: the tap check removed ⇒ the tap counts a press.
    /// </summary>
    [Fact]
    public void CE1207_ALyingRifle_PromptsTake_AndDropIsNeverATap()
    {
        using var u = new UseWorld();
        var rifle = u.OfType("Pick-up rifle", new Vector3(1f, 0f, 0f), 88, new CarrierDefDto { Seats = { new SeatDto { Role = SeatRole.Gunner } } },
            new WeaponSystemDto { MountKinds = MountKinds.Ground | MountKinds.Carried, Portable = WeaponPortability.OneMan, FireWhileCarried = true, NeedsOperator = true });
        var (_, sw, _) = Station(u, 88);
        var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
        Frame(u, sw, new Input());
        Assert.Contains(driver.UseLines(u.W), l => l.Text.StartsWith("F  Take"));

        Assert.Equal(Fdp.Toolkit.Behavior.EmbarkResult.Applied, Embarkation.TryEmbark(u.W, rifle, u.Me));   // he took it
        var (_, sw2, _) = Station(u, 0);
        var empty = Assert.IsType<FpsCameraDriver>(sw2.Camera3D.Driver);
        Frame(u, sw2, new Input());
        Assert.Contains(empty.UseLines(u.W), l => l.Text.StartsWith("hold F  Drop"));
        byte before = Frame(u, sw2, new Input()).UseSeq;
        Frame(u, sw2, Key(Raylib_cs.KeyboardKey.F));
        Assert.Equal(before, Frame(u, sw2, new Input()).UseSeq);                              // a tap: nothing sent
    }
}
