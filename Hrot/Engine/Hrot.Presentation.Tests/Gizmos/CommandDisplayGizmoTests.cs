using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Squad.Command;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ <c>CE-1192</c> (UI-5, 📄 docs/DESIGN_Command_Display.md §6 D1 / D2) — the command display's gizmos: every unit's sector outlined in
/// its colour with its name, each NoFire area hatched red ONCE however many units name it, the selected commander's picture as
/// age-faded symbols, and a man's sector arc. A fixture of two sectors, one NoFire room and three elements (the hand-off's acceptance).
/// </summary>
public sealed class CommandDisplayGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly DebugPrimitiveBuffer _draw = new();

    public CommandDisplayGizmoTests()
    {
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<NetworkIdentity>();
        _w.RegisterComponent<EntityInfo>();
        _w.RegisterComponent<EngagementArea>();
        _w.RegisterComponent<CommandPicture>();
        _w.RegisterManagedComponent<EditablePolyline>();
        _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 100.0 });
    }

    public void Dispose() => _w.Dispose();

    private Entity Area(long net, Vector2 origin, params Vector2[] points)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new SimTransform { Position = new Vector3(origin, 0) });
        _w.AddComponent(e, new NetworkIdentity { Value = net });
        _w.SetManagedComponent(e, new EditablePolyline { Points = points.ToList() });
        return e;
    }

    private Entity Unit(long net, string name, long allow = 0, params long[] noFire)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new SimTransform { Position = new Vector3(net, 0, 0) });
        _w.AddComponent(e, new NetworkIdentity { Value = net });
        _w.AddComponent(e, new EntityInfo { Name = new FixedString64(name) });
        var a = new EngagementArea { AllowArea = allow };
        foreach (var id in noFire) a.NoFire.Items[a.NoFire.Count++] = id;
        _w.AddComponent(e, a);
        return e;
    }

    private DebugPrimitive[] Lines(Rgba32 colour) => _draw.GetFrame().ToArray().Where(p => p.Shape == DebugPrimitiveShape.Line && p.Color.Equals(colour)).ToArray();

    [Fact]
    public void CE1192_TwoSectorsInTheirUnitsColours_AndTheNoFireRoomHatchedRedOnce_HoweverManyMenNameIt()
    {
        var square = new[] { new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20) };
        Area(7001, new Vector2(100, 100), square);
        Area(7002, new Vector2(200, 100), square);
        Area(7003, new Vector2(150, 150), new Vector2(0, 0), new Vector2(6, 0), new Vector2(6, 6), new Vector2(0, 6));   // the NoFire room
        Unit(9210, "1st Squad", 7001, 7003);
        Unit(9220, "2nd Squad", 7002, 7003);
        for (int i = 0; i < 9; i++) Unit(9211 + i, $"man {i}", 0, 7003);                                               // nine men name it too

        new SectorAreasGizmo().Draw(_w, _draw);

        var first = Lines(SectorAreasGizmo.ColourOf(9210));
        var second = Lines(SectorAreasGizmo.ColourOf(9220));
        Assert.NotEqual(SectorAreasGizmo.ColourOf(9210), SectorAreasGizmo.ColourOf(9220));
        Assert.Equal(4, first.Length);    // its square's four edges
        Assert.Equal(4, second.Length);
        Assert.All(first, p => Assert.InRange(p.LineStart.X, 99.9f, 120.1f));
        var texts = _draw.GetFrame().ToArray().Where(p => p.Shape == DebugPrimitiveShape.Text).Select(p => p.TextContent.ToString()).ToArray();
        Assert.Contains("1st Squad", texts);
        Assert.Contains("2nd Squad", texts);

        var hatch = new List<(Vector2, Vector2)>();
        SectorAreasGizmo.Hatch(new[] { new Vector2(0, 0), new Vector2(6, 0), new Vector2(6, 6), new Vector2(0, 6) }, SectorAreasGizmo.HatchSpacing, hatch);
        var red = Lines(SectorAreasGizmo.NoFireColour);
        Assert.Equal(4 + hatch.Count, red.Length);   // ONE outline + ONE hatch, though eleven units name the room
        Assert.True(hatch.Count >= 2, $"{hatch.Count} hatch lines over a 6 m room");
        Assert.All(_draw.GetFrame().ToArray(), p => Assert.Equal(DebugTraceLayers.Sectors, p.DebugLayer));   // ⭐ CE-1225 — its own layer
    }

    [Fact]
    public void CE1192_TheHatch_LiesInsideThePolygon_AlsoAConcaveOne()
    {
        // an L: a 10 x 10 square with its north-east quarter cut away
        var l = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 5), new Vector2(5, 5), new Vector2(5, 10), new Vector2(0, 10) };
        var hatch = new List<(Vector2 A, Vector2 B)>();
        SectorAreasGizmo.Hatch(l, 1.5f, hatch);
        Assert.True(hatch.Count >= 6, $"{hatch.Count} segments");
        foreach (var (a, b) in hatch)
        {
            Assert.Equal(1f, (b - a).X / MathF.Max(1e-6f, (b - a).Y), 3);   // 45°
            foreach (var t in new[] { 0.25f, 0.5f, 0.75f })
            {
                var p = Vector2.Lerp(a, b, t);
                Assert.True(Fdp.Toolkit.Terrain.PolygonMath.Contains(l, p), $"hatch point {p} is outside the L");
            }
        }
    }

    [Fact]
    public void CE1192_TheSelectedCommandersPicture_ThreeElementsAsSymbols_SizedByClass_FadingWithAge()
    {
        var cmd = _w.CreateEntity();
        _w.AddComponent(cmd, new SimTransform { Position = new Vector3(0, 0, 0) });
        var pic = default(CommandPicture);
        void Add(Vector3 at, ElementSize size, ElementEquipment eq, float observedAt, byte count)
        {
            ref var e = ref pic.Elements.Items[pic.Elements.Count++];
            e.At = at; e.Size = size; e.Equipment = eq; e.ObservedAt = observedAt; e.Count = count;
        }
        Add(new Vector3(50, 0, 0), ElementSize.Team, ElementEquipment.MachineGun, 100f, 3);                // fresh
        Add(new Vector3(0, 80, 0), ElementSize.Individual, ElementEquipment.Sniper, 40f, 1);                // a minute old
        Add(new Vector3(-60, 0, 0), ElementSize.PlatoonSize, ElementEquipment.AntiTank | ElementEquipment.MachineGun, -500f, 20);   // stale
        _w.AddComponent(cmd, pic);

        new CommandPictureGizmo().Draw(_w, cmd, _draw);
        var frame = _draw.GetFrame().ToArray();
        var lines = frame.Where(p => p.Shape == DebugPrimitiveShape.Line).ToArray();
        Assert.Equal(12, lines.Length);   // three diamonds
        var texts = frame.Where(p => p.Shape == DebugPrimitiveShape.Text).Select(p => p.TextContent.ToString()).ToArray();
        Assert.Contains(texts, t => t.StartsWith("3M 0s"));
        Assert.Contains(texts, t => t.StartsWith("1S 60s"));
        Assert.Contains(texts, t => t.StartsWith("20MA "));

        float HalfAt(Vector3 c) => lines.Where(p => Vector3.Distance((p.LineStart + p.LineEnd) / 2, c) < 10f).Max(p => MathF.Max(MathF.Abs(p.LineStart.X - c.X), MathF.Abs(p.LineStart.Y - c.Y)));
        Assert.True(HalfAt(new Vector3(-60, 0, 0)) > HalfAt(new Vector3(50, 0, 0)) && HalfAt(new Vector3(50, 0, 0)) > HalfAt(new Vector3(0, 80, 0)), "platoon > team > individual");

        byte AlphaAt(Vector3 c) => lines.First(p => Vector3.Distance((p.LineStart + p.LineEnd) / 2, c) < 10f).Color.A;
        Assert.True(AlphaAt(new Vector3(50, 0, 0)) > AlphaAt(new Vector3(0, 80, 0)) && AlphaAt(new Vector3(0, 80, 0)) > AlphaAt(new Vector3(-60, 0, 0)), "fresh > a minute > stale");
        Assert.Equal((byte)(CommandPictureGizmo.MinAlpha * 255f), AlphaAt(new Vector3(-60, 0, 0)));
    }

    [Fact]
    public void CE1192_AMansArc_TwoEdgesAlongHisBearings_AndItsFarEdge()
    {
        var man = _w.CreateEntity();
        _w.AddComponent(man, new SimTransform { Position = Vector3.Zero });
        _w.AddComponent(man, new EngagementArea { ArcSet = 1, ArcOrigin = Vector3.Zero, LeftBearing = 0f, RightBearing = 45f, MaxRange = 40f });
        new SectorArcGizmo().Draw(_w, man, _draw);
        var lines = _draw.GetFrame().ToArray().Where(p => p.Shape == DebugPrimitiveShape.Line).ToArray();
        Assert.Contains(lines, p => Vector3.Distance(p.LineEnd, new Vector3(0, 40, 0)) < 0.01f);    // north edge
        float d = 40f * MathF.Sqrt(0.5f);
        Assert.Contains(lines, p => Vector3.Distance(p.LineEnd, new Vector3(d, d, 0)) < 0.01f);      // the north-east edge (bearing 45, clockwise from north)
        // an asymmetric arc on purpose: a 0..90 arc reads the same with sin and cos swapped
        Assert.All(lines.Skip(2), p => Assert.Equal(40f, p.LineEnd.Length(), 2));                    // the far edge at 40 m
    }

    /// <summary>⭐ CE-1192 D4 (behaviors' answer, R-2058) — a selected COMMANDER whose picture has not reached this node says so; a man who
    /// commands nobody, or a commander whose picture is here, gets no such line. 🔒 The user asked for absence to be visible.</summary>
    [Fact]
    public void D4_ACommanderWithNoPictureOnThisNode_SaysSo()
    {
        _w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
        var leader = Unit(10, "Leader");
        var man = Unit(11, "Rifleman");
        _w.AddComponent(man, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = leader });
        string[] Texts(Entity e)
        {
            _draw.Clear();
            new CommandPictureAbsentGizmo().Draw(_w, e, _draw);
            return _draw.GetFrame().ToArray().Where(p => p.Shape == DebugPrimitiveShape.Text).Select(p => p.TextContent.ToString()).ToArray();
        }
        Assert.Equal(new[] { CommandPictureAbsentGizmo.Text }, Texts(leader));
        Assert.Empty(Texts(man));                                          // he commands nobody
        _w.AddComponent(leader, new CommandPicture());
        Assert.Empty(Texts(leader));                                       // the picture is here: CommandPictureGizmo draws it
    }

    /// <summary>⭐ <c>CE-1225</c> (§8 S-2, S-3) — with the Sectors layer on, every COMMANDER's arc (a platoon's slice on a squad) is drawn
    /// for everyone by the global gizmo, on the ground (z = 0, draped in 3-D) and on the Sectors layer; a man's own arc is not (it stays
    /// behind selection, <see cref="SectorArcGizmo"/>). 🔒 User, 2026-10-20: "rendering the sectors behind a switch/checkbox".</summary>
    [Fact]
    public void CE1225_EveryCommandersArc_IsDrawnForEveryone_OnTheGround_OnTheSectorsLayer_AndAMansOwnArcIsNot()
    {
        _w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
        var squad = Unit(20, "1st Squad");
        _w.SetComponent(squad, new EngagementArea { ArcSet = 1, ArcOrigin = new Vector3(0, 0, 7f), LeftBearing = 0f, RightBearing = 45f, MaxRange = 40f });
        var man = Unit(21, "Rifleman");
        _w.SetComponent(man, new EngagementArea { ArcSet = 1, ArcOrigin = new Vector3(100, 0, 0), LeftBearing = 90f, RightBearing = 120f, MaxRange = 30f });
        _w.AddComponent(man, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = squad });

        new SectorAreasGizmo().Draw(_w, _draw);

        var squads = Lines(SectorAreasGizmo.ColourOf(20));
        Assert.NotEmpty(squads);
        Assert.Contains(squads, p => Vector3.Distance(p.LineEnd, new Vector3(0, 40, 0)) < 0.01f);          // the north edge, on the ground
        Assert.All(squads, p => { Assert.Equal(0f, p.LineStart.Z); Assert.Equal(0f, p.LineEnd.Z); });       // z = 0 ⇒ draped in 3-D
        Assert.Empty(Lines(SectorAreasGizmo.ColourOf(21)));                                                  // the man's arc: selection only
        Assert.All(_draw.GetFrame().ToArray(), p => Assert.Equal(DebugTraceLayers.Sectors, p.DebugLayer));
    }

    /// <summary>⭐ <c>CE-1225</c> (§8 S-1) — the Sectors checkbox off ⇒ no sector area, NoFire hatch or arc is drawn at all, by either gizmo,
    /// while the units' own layer stays on.</summary>
    [Fact]
    public void CE1225_TheSectorsLayerOff_NoAreaNoFireOrArc_IsDrawn()
    {
        var square = new[] { new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20) };
        Area(7001, new Vector2(100, 100), square);
        Area(7003, new Vector2(150, 150), square);
        var unit = Unit(9210, "1st Squad", 7001, 7003);
        var sector = _w.GetComponentRO<EngagementArea>(unit);
        sector.ArcSet = 1; sector.LeftBearing = 0f; sector.RightBearing = 90f;
        _w.SetComponent(unit, sector);

        var mask = new Hrot.Common.Diagnostics.Gizmos.LayerControlDto { Sectors = false }.ToMask();
        Assert.True(mask.IsSet(0));
        _draw.SetActiveLayers(mask);
        new SectorAreasGizmo().Draw(_w, _draw);
        new SectorArcGizmo().Draw(_w, unit, _draw);
        Assert.Empty(_draw.GetFrame().ToArray());

        _draw.SetActiveLayers(new Hrot.Common.Diagnostics.Gizmos.LayerControlDto { Sectors = true }.ToMask());
        new SectorAreasGizmo().Draw(_w, _draw);
        new SectorArcGizmo().Draw(_w, unit, _draw);
        Assert.NotEmpty(Lines(SectorAreasGizmo.ColourOf(9210)));
        Assert.NotEmpty(Lines(SectorAreasGizmo.NoFireColour));
    }
}
