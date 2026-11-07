using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Map.Common.Components;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐⭐ B-11 A1 (docs/UX/DESIGN_Action_Catalogue.md §4 A1) — THE GOLDEN: the context menu JSON <see cref="ContextMenuProjectorGizmo.MenuJsonFor"/>
/// builds, per entity kind × mode, captured BEFORE the catalogue replaces the hard-coded menus. A1's acceptance is that this file stays
/// byte-identical; a later stage that moves it (A2, A3) lists every moved item in its report.
/// <para>Capture: <c>MENU_GOLDEN_CAPTURE=1 dotnet test … --filter TheMenusAreGoldenTests</c> rewrites <c>Goldens/menus.golden.txt</c> beside this file.</para>
/// </summary>
[Xunit.Collection(Hrot.Presentation.Tests.ImGuiTestCollection.Name)]
public sealed class TheMenusAreGoldenTests
{
    private const ulong Rifleman = (3UL << 56) | (1UL << 48) | (225UL << 32) | (1UL << 24);    // a lifeform the order vocabulary serves
    private const ulong Tank = (1UL << 56) | (1UL << 48) | (225UL << 32) | (1UL << 24) | (1UL << 16);

    private static string GoldenPath([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Goldens", "menus.golden.txt");
    private static string StationGoldenPath([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Goldens", "menus.station.golden.txt");

    private sealed class World : IDisposable
    {
        public readonly EntityRepository W = new();
        private int _next = 100;
        public World(bool orders = false, bool review = false)
        {
            W.RegisterComponent<SimTransform>();
            W.RegisterComponent<NetworkIdentity>();
            W.RegisterComponent<Health>();
            W.RegisterComponent<TkbIdentity>();
            W.RegisterComponent<Fdp.Core.CommandHierarchy.UnitEntity>();
            W.RegisterManagedComponent<EditablePolyline>();
            W.RegisterManagedComponent<RoutePlan>();
            if (orders) W.SetSingletonManaged(new Hrot.Common.Orders.OrdersOffered());
            if (review) Fdp.Toolkit.Replay.ReplayState.Enter(W);
        }
        public Entity Add(ulong dis = 0)
        {
            var e = W.CreateEntity();
            W.AddComponent(e, new SimTransform { Position = new Vector3(5, 5, 0) });
            W.AddComponent(e, new NetworkIdentity(_next++));
            if (dis != 0) W.SetDisType(e, new DISEntityType { Value = dis });
            return e;
        }
        public void Dispose() => W.Dispose();
    }

    /// <summary>Every case, named; its menu JSON. Order is the golden's order.</summary>
    private static IEnumerable<(string Case, string Json)> Cases(Func<EntityRepository, Entity, string>? menuOf = null)
    {
        menuOf ??= ContextMenuProjectorGizmo.MenuJsonFor;
        foreach (var (mode, orders, review) in new[] { ("edit", false, false), ("live", true, false), ("review", true, true) })
        {
            using (var w = new World(orders, review))
            {
                var area = w.Add();
                w.W.SetManagedComponent(area, new EditablePolyline());
                yield return ($"{mode} · area", menuOf(w.W, area));

                var zone = w.Add();
                w.W.SetManagedComponent(zone, new EditablePolyline());
                w.W.AddComponent(zone, new TkbIdentity { TkbType = (TkbTypeId)Hrot.Map.Common.TkbEntityTypes.TerrainZone });
                yield return ($"{mode} · terrain zone", menuOf(w.W, zone));

                var route = w.Add();
                w.W.SetManagedComponent(route, new RoutePlan());
                yield return ($"{mode} · route", menuOf(w.W, route));

                var man = w.Add(Rifleman);
                yield return ($"{mode} · rifleman", menuOf(w.W, man));

                var hurt = w.Add(Rifleman);
                w.W.AddComponent(hurt, new Health { Current = 20f, Max = 100f });
                yield return ($"{mode} · rifleman, heavily damaged", menuOf(w.W, hurt));

                var tank = w.Add(Tank);
                yield return ($"{mode} · tank", menuOf(w.W, tank));

                var unit = w.Add(Rifleman);
                w.W.AddComponent(unit, new Fdp.Core.CommandHierarchy.UnitEntity());
                yield return ($"{mode} · a unit (body-less)", menuOf(w.W, unit));

                var plain = w.Add();
                yield return ($"{mode} · an entity with no DIS type", menuOf(w.W, plain));
            }
        }
    }

    private static string Render(Func<EntityRepository, Entity, string>? menuOf = null)
    {
        var sb = new StringBuilder();
        foreach (var (name, json) in Cases(menuOf))
            sb.Append("## ").Append(name).Append('\n').Append(json).Append("\n\n");
        return sb.ToString();
    }

    /// <summary>
    /// ⭐⭐ The golden. ✅ Red-proof: any menu item's label, id, shortcut, order or enabled state changed in <c>ContextMenuProjectorGizmo</c> ⇒ the
    /// diff names the case.
    /// </summary>
    [Fact]
    public void A1_TheMenuOfEveryEntityKindInEveryMode_IsTheGolden() => AssertGolden(Render(), GoldenPath());

    /// <summary>
    /// ⭐⭐ B-11 A2 (design §7) — the same menus AS A STATION OFFERS THEM: on a host built by the shared map pack (what every map host has),
    /// a row it binds no handler for is greyed "Not available on this station". The station golden differs from the A1 golden ONLY in those rows
    /// — <see cref="A2_TheStationGolden_DiffersFromTheA1Golden_OnlyInRowsTheHostCannotRun"/> pins that.
    /// </summary>
    [Fact]
    public void A2_TheMenusAsTheSharedMapHostOffersThem_AreTheStationGolden() => AssertGolden(Render(MenuOnSharedHost), StationGoldenPath());

    /// <summary>
    /// ⭐ B-11 A2 — every row the station golden greys names an action the host has no handler for, and nothing else moved. ✅ Red-proof: greying
    /// every row (runsHere => false) ⇒ rows the host does run are greyed too ⇒ the "runs here" assertion fails.
    /// </summary>
    [Fact]
    public void A2_TheStationGolden_DiffersFromTheA1Golden_OnlyInRowsTheHostCannotRun()
    {
        var host = SharedHost();
        foreach (var ((name, plain), (_, station)) in System.Linq.Enumerable.Zip(Cases(), Cases(MenuOnSharedHost)))
        {
            var a = System.Text.Json.JsonSerializer.Deserialize<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto[]>(plain, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
            var b = System.Text.Json.JsonSerializer.Deserialize<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto[]>(station, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
            Compare(name, a, b, host.Actions);
        }

        static void Compare(string name, Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto[] a, Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto[] b, Hrot.Common.Interactions.GlobalActionRegistry actions)
        {
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.Equal((a[i].Id, a[i].Label, a[i].Shortcut), (b[i].Id, b[i].Label, b[i].Shortcut));
                if (a[i].Children is { } ca) Compare(name, ca, b[i].Children!, actions);
                bool greyedHere = a[i].Enabled != false && b[i].Enabled == false;
                if (greyedHere)
                {
                    Assert.False(actions.Has(a[i].Id), $"{name}: '{a[i].Label}' greyed though the host runs it");
                    Assert.Equal(Hrot.Common.Actions.MenuComposer.NotOnThisStation, b[i].Tooltip);
                }
                else if (a[i].Id != 0 && a[i].Enabled != false)
                    Assert.True(actions.Has(a[i].Id), $"{name}: '{a[i].Label}' left live though the host does not run it");
            }
        }
    }

    /// <summary>
    /// ⭐⭐ B-11 A2 — end to end: the map's own menu resolver (the render layer every host attaches) finds its host through the bus and greys what
    /// that host cannot run — no host passes anything. ✅ Red-proof: the resolver passing no runsHere ⇒ "Save selection as macro…" (the editor's
    /// action, not the pack's) stays live.
    /// </summary>
    [Fact]
    public void A2_TheMapsMenuResolver_GreysWhatItsHostCannotRun_ByConstruction()
    {
        using var w = new World(orders: true);
        var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
        w.W.SetSingletonManaged(map);
        var mi = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext { World = w.W });
        var man = w.Add(Rifleman);
        int net = w.W.GetComponentRO<NetworkIdentity>(man).Value;
        map.Register(net, man);
        var layer = Hrot.ScenarioEditor.Map.MapInteractionPack.BuildRenderLayer(new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer(64), mi.InteractionBus, null, () => w.W);
        Assert.False(mi.Host.Actions.Has(Hrot.Common.Constants.GlobalActionIds.SaveSelectionAsMacro));
        var menu = layer.MenuResolver!(net)!;
        Assert.Contains("\"label\":\"Save selection as macro\\u2026\",\"enabled\":false,\"tooltip\":\"Not available on this station\"", menu);
    }

    private static Hrot.Common.Interactions.HostActions? _sharedHost;

    /// <summary>The actions every map host has: the shared map pack's, on its own host (one per test process).</summary>
    private static Hrot.Common.Interactions.HostActions SharedHost()
    {
        if (_sharedHost != null) return _sharedHost;
        var world = new EntityRepository();
        return _sharedHost = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext { World = world }).Host;
    }

    private static string MenuOnSharedHost(EntityRepository w, Entity e) => Hrot.Common.Actions.MapMenuActions.JsonFor(w, e, SharedHost().Actions.Has);

    private static void AssertGolden(string actual, string path)
    {
        if (Environment.GetEnvironmentVariable("MENU_GOLDEN_CAPTURE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }
        Assert.True(File.Exists(path), $"no golden at {path} — capture it with MENU_GOLDEN_CAPTURE=1");
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        if (expected == actual) return;
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (int i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            string x = i < e.Length ? e[i] : "<none>", y = i < a.Length ? a[i] : "<none>";
            if (x != y) Assert.Fail($"menu golden differs at line {i + 1}:\n  expected: {x}\n  actual:   {y}");
        }
    }
}
