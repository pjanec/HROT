using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Vis3D;
using Hrot.Common.Infrastructure;
using Hrot.Presentation.Tests.Effects;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-4148 (Node Features S2; DESIGN_Player_Join §7a G3/G4) — the fall screen / kill cam and the Tab squad takeover after death are the
/// features <c>hud.kill-cam</c> and <c>hud.squad-takeover-after-death</c>: OFF ⇒ a killed held unit is released to the map and no fall state
/// exists; ON ⇒ today's behaviour (the CE-1220 rail above, ungated). Red-proof: remove the <c>HudOn(KillCam)</c> test in the station's release
/// branch ⇒ the "killed with kill-cam off" rail fails.
/// </summary>
public sealed partial class PossessionStationTests
{
    private (PossessionStation Station, MapViewSwitch Sw, EntityRepository W, Entity Me, Entity Leader) KilledSetup(IFeatureGate? gate, out IDisposable owner)
    {
        var (w, me, map) = World();
        owner = w;
        w.RegisterComponent<EntityInfo>();
        w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
        w.RegisterEvent<PossessRequestEvent>();
        w.RegisterEvent<ReleasePossessionEvent>();
        w.AddComponent(me, new PossessedBy { StationNodeId = (Fdp.Core.NodeId)(0), Mode = PossessionMode.Fps, Epoch = 1 });
        var leader = w.CreateEntity();
        w.AddComponent(leader, new SimTransform { Position = new Vector3(90f, 50f, 0f), Rotation = Quaternion.Identity });
        map.Register(700, leader);
        w.AddComponent(me, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = leader });
        var (_, sw, _) = Map();
        var station = new PossessionStation(sw, () => w, featureGate: () => gate);
        station.Update(0.016f);
        sw.Camera3D.Update(0.016f);
        Assert.True(station.InFps);
        return (station, sw, w, me, leader);
    }

    private static void Kill(PossessionStation station, EntityRepository w, Entity me)
    {
        w.RemoveComponent<PossessedBy>(me);
        w.SetComponent(me, new Health { Current = 0f, Max = 100f });
        station.Update(0.016f);
    }

    [Fact]
    public void CE4148_KilledWhileHeld_KillCamOff_ReleasesToTheMap_NoFallScreen()
    {
        var (station, sw, w, me, _) = KilledSetup(EffectTests.Gate(playerDefault: false), out var owner);
        using (owner)
        {
            Kill(station, w, me);
            Assert.Null(station.Fall);
            Assert.Null(sw.Camera3D.Driver);
            Assert.False(station.InFps);
            Assert.False(sw.Is3D);   // the operator's map as he had it
        }
    }

    [Fact]
    public void CE4148_KilledWhileHeld_KillCamOn_TheFallScreenStands_TabNeedsTheSquadTakeover()
    {
        var (station, sw, w, me, leader) = KilledSetup(EffectTests.Gate(false, (HudFeatures.KillCam, true)), out var owner);
        using (owner)
        {
            Kill(station, w, me);
            Assert.NotNull(station.Fall);
            var tab = new Input();
            tab.Pressed.Add((int)Raylib_cs.KeyboardKey.Tab);
            sw.Camera3D.HandleInput(tab);                       // squad takeover OFF: Tab takes nobody
            w.Bus.SwapBuffers();
            Assert.Empty(w.Bus.Read<PossessRequestEvent>().ToArray());
            Assert.False(station.HudOn(HudFeatures.SquadTakeoverAfterDeath));
        }

        var (station2, sw2, w2, me2, leader2) = KilledSetup(EffectTests.Gate(playerDefault: true), out var owner2);
        using (owner2)
        {
            Kill(station2, w2, me2);
            var tab = new Input();
            tab.Pressed.Add((int)Raylib_cs.KeyboardKey.Tab);
            sw2.Camera3D.HandleInput(tab);
            w2.Bus.SwapBuffers();
            Assert.Equal(leader2, Assert.Single(w2.Bus.Read<PossessRequestEvent>().ToArray()).Unit);
        }
    }

    [Fact]
    public void CE4148_AStationWithNoGate_IsUngated_EveryHudFeatureOn()
    {
        var (station, _, _, _, _) = KilledSetup(null, out var owner);
        using (owner)
            foreach (var id in HudFeatures.All) Assert.True(station.HudOn(id), id);
    }
}
