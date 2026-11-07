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
    private static IEnumerable<(string Case, string Json)> Cases()
    {
        foreach (var (mode, orders, review) in new[] { ("edit", false, false), ("live", true, false), ("review", true, true) })
        {
            using (var w = new World(orders, review))
            {
                var area = w.Add();
                w.W.SetManagedComponent(area, new EditablePolyline());
                yield return ($"{mode} · area", ContextMenuProjectorGizmo.MenuJsonFor(w.W, area));

                var zone = w.Add();
                w.W.SetManagedComponent(zone, new EditablePolyline());
                w.W.AddComponent(zone, new TkbIdentity { TkbType = (TkbTypeId)Hrot.Map.Common.TkbEntityTypes.TerrainZone });
                yield return ($"{mode} · terrain zone", ContextMenuProjectorGizmo.MenuJsonFor(w.W, zone));

                var route = w.Add();
                w.W.SetManagedComponent(route, new RoutePlan());
                yield return ($"{mode} · route", ContextMenuProjectorGizmo.MenuJsonFor(w.W, route));

                var man = w.Add(Rifleman);
                yield return ($"{mode} · rifleman", ContextMenuProjectorGizmo.MenuJsonFor(w.W, man));

                var hurt = w.Add(Rifleman);
                w.W.AddComponent(hurt, new Health { Current = 20f, Max = 100f });
                yield return ($"{mode} · rifleman, heavily damaged", ContextMenuProjectorGizmo.MenuJsonFor(w.W, hurt));

                var tank = w.Add(Tank);
                yield return ($"{mode} · tank", ContextMenuProjectorGizmo.MenuJsonFor(w.W, tank));

                var unit = w.Add(Rifleman);
                w.W.AddComponent(unit, new Fdp.Core.CommandHierarchy.UnitEntity());
                yield return ($"{mode} · a unit (body-less)", ContextMenuProjectorGizmo.MenuJsonFor(w.W, unit));

                var plain = w.Add();
                yield return ($"{mode} · an entity with no DIS type", ContextMenuProjectorGizmo.MenuJsonFor(w.W, plain));
            }
        }
    }

    private static string Render()
    {
        var sb = new StringBuilder();
        foreach (var (name, json) in Cases())
            sb.Append("## ").Append(name).Append('\n').Append(json).Append("\n\n");
        return sb.ToString();
    }

    /// <summary>
    /// ⭐⭐ The golden. ✅ Red-proof: any menu item's label, id, shortcut, order or enabled state changed in <c>ContextMenuProjectorGizmo</c> ⇒ the
    /// diff names the case.
    /// </summary>
    [Fact]
    public void A1_TheMenuOfEveryEntityKindInEveryMode_IsTheGolden()
    {
        var actual = Render();
        var path = GoldenPath();
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
