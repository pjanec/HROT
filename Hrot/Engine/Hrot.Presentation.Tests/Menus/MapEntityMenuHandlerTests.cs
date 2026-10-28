using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Common.Constants;
using Hrot.Common.Events;
using Hrot.ScenarioEditor.Map;
using Hrot.UI.Common.Menus;
using Xunit;

namespace Hrot.Presentation.Tests.Menus;

/// <summary>
/// ⭐ CE-1134 / CE-1135 (docs/designs/gizmos-1/canvas-context-menu-design.md §9) — the entity inspector's right-click menu is the map's
/// entity menu (the same JSON, run by the same actions, only what runs on this node), and the view items work on every host: Center View
/// moves the camera in 2-D AND 3-D, Select requests the selection. ⭐ CE-1150 (§12) — the actions are the HOST's (<see cref="SharedHostActions"/>),
/// and the menu needs no map. Properties… is the Details bundle's (rails in <c>Hrot.Editor.AiShared.Tests</c>).
/// ⭐ Re-homes the rails of the Editor's deleted <c>JsonEntityContextMenuHandler</c> (labels, a click, dead / no-id entities, separators).
/// </summary>
public sealed class MapEntityMenuHandlerTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly Hrot.Common.Interactions.HostActions _host = SharedHostActions.Create();
    private readonly MapInteraction _map;

    public MapEntityMenuHandlerTests()
    {
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<NetworkIdentity>();
        _w.RegisterComponent<Health>();
        _w.RegisterComponent<EntityInfo>();
        _w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitEntity>();
        _map = MapInteractionPack.Build(new MapInteractionContext { World = _w, BufferCapacity = 64, Host = _host });
    }

    public void Dispose() => _w.Dispose();

    private sealed class Menu : IContextMenuBuilder
    {
        public readonly List<string> Items = new();
        private readonly Dictionary<string, Action> _clicks = new();
        private readonly string _prefix;
        public Menu(string prefix = "") => _prefix = prefix;
        public void AddItem(string label, Action callback, bool enabled = true) { Items.Add(_prefix + label); _clicks[_prefix + label] = callback; }
        public IContextMenuBuilder BeginSubmenu(string label) { Items.Add(_prefix + label + " >"); return new Sub(this, _prefix + label + " > "); }
        public void EndSubmenu() { }
        public void AddSeparator() => Items.Add(_prefix + "---");
        public void Click(string label) => _clicks[label]();
        private sealed class Sub : IContextMenuBuilder
        {
            private readonly Menu _root; private readonly string _p;
            public Sub(Menu root, string p) { _root = root; _p = p; }
            public void AddItem(string label, Action callback, bool enabled = true) { _root.Items.Add(_p + label); _root._clicks[_p + label] = callback; }
            public IContextMenuBuilder BeginSubmenu(string label) { _root.Items.Add(_p + label + " >"); return new Sub(_root, _p + label + " > "); }
            public void EndSubmenu() { }
            public void AddSeparator() => _root.Items.Add(_p + "---");
        }
    }

    private Entity Unit(long id, Vector3 at)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new NetworkIdentity(id));
        _w.AddComponent(e, new SimTransform { Position = at });
        return e;
    }

    private GlobalActionRequestedEvent[] Requests()
    {
        _host.InteractionBus.SwapBuffers();
        return _host.InteractionBus.Read<GlobalActionRequestedEvent>().ToArray();
    }

    [Fact]
    public void CE1134_TheInspectorMenu_IsTheMapsEntityMenu_OnlyWhatRunsHere_AndAClickRunsTheMapsAction()
    {
        var unit = Unit(42, new Vector3(10, 20, 0));
        var menu = new Menu();
        new MapEntityMenuHandler(() => _host, () => _w).PopulateMenu(unit, menu);   // the host's actions and world, as in a host

        Assert.Contains("Center View", menu.Items);
        Assert.Contains("Select", menu.Items);
        Assert.DoesNotContain("Properties…", menu.Items);   // ⭐ CE-1150 — only the Details bundle runs it; none is composed here
        Assert.Contains("Pin gizmos >", menu.Items.Select(i => i.Split(" > ")[0] + " >"));
        Assert.DoesNotContain("Rotate", menu.Items);      // no host registered Rotate in this map: the inspector does not offer a dead item
        Assert.NotEqual("---", menu.Items[0]);            // never a leading separator
        for (int i = 1; i < menu.Items.Count; i++) Assert.False(menu.Items[i] == "---" && menu.Items[i - 1] == "---", "no two in a row");

        menu.Click("Select");
        var request = Assert.Single(Requests());
        Assert.Equal(GlobalActionIds.Select, request.ActionId);
        Assert.Equal(unit, request.Target);
    }

    [Fact]
    public void CE1134_ADeadEntity_OrOneWithNoNetworkId_GetsNoMenu()
    {
        var dead = Unit(7, Vector3.Zero);
        _w.DestroyEntity(dead);
        var local = _w.CreateEntity();
        var menu = new Menu();
        var handler = new MapEntityMenuHandler(() => _host, () => _w);
        handler.PopulateMenu(dead, menu);
        handler.PopulateMenu(local, menu);
        Assert.Empty(menu.Items);
    }

    [Fact]
    public void CE1134_CenterView_MovesTheCameraTheOperatorSees_In2D_AndIn3D()
    {
        var unit = Unit(8, new Vector3(300, -120, 0));
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var two = new Fdp.Toolkit.Vis2D.Components.MapCamera();
        canvas.Camera = two;
        new MapCanvasLink(canvas, () => _w).Update(0f);
        Assert.True(_map.Actions.TryGetHandler(GlobalActionIds.CenterOnEntity, out var center));

        two.Update(0f);   // ⭐ CE-4084 — the frame loop has anchored the camera at the visible centre before an operator can click
        center(_w, unit);
        two.Update(1f);
        Assert.InRange(Vector2.Distance(two.Target, new Vector2(300, -120)), 0f, 0.01f);
        Assert.InRange(Vector2.Distance(two.WorldToScreen(new Vector2(300, -120)), two.ViewCentre), 0f, 0.05f);

        var three = new MapCamera3D { ViewportOverride = new Vector2(1280, 720), GroundHeight = (_, _) => 4f };
        canvas.Camera = three;
        center(_w, unit);
        three.Update(1f);
        Assert.InRange(Vector3.Distance(three.LookAt, new Vector3(300, -120, 4f)), 0f, 0.05f);
    }

    /// <summary>
    /// ⭐⭐ CE-1150 (canvas-context-menu-design §12 HA-1 / HA-5; 🔒 user, 2026-10-16: <i>"why would it need a map??"</i>) — the inspector's
    /// menu runs on the HOST's actions alone: no map is built, and its items and a click still work. ✅ Red-proof: the menu reading a map
    /// (the deleted <c>InspectorMapLink</c> shape) ⇒ no map here ⇒ an empty menu.
    /// </summary>
    [Fact]
    public void CE1150_TheInspectorMenu_NeedsNoMap()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<NetworkIdentity>();
        var unit = world.CreateEntity();
        world.AddComponent(unit, new NetworkIdentity(77));
        world.AddComponent(unit, new SimTransform());
        var host = SharedHostActions.Create();   // ⛔ no MapInteractionPack.Build
        var menu = new Menu();

        new MapEntityMenuHandler(() => host, () => world).PopulateMenu(unit, menu);

        Assert.Contains("Center View", menu.Items);
        Assert.Contains("Select", menu.Items);
        menu.Click("Select");
        host.InteractionBus.SwapBuffers();
        var request = Assert.Single(host.InteractionBus.Read<GlobalActionRequestedEvent>().ToArray());
        Assert.Equal(GlobalActionIds.Select, request.ActionId);
        Assert.Equal(unit, request.Target);
    }

    /// <summary>⭐ CE-1150 (HA-2) — the map registers ITS actions on the host's registry, beside the shared ones: one registry, owned by
    /// the host, ⛔ not a second one inside the map.</summary>
    [Fact]
    public void CE1150_TheMapRegistersOnTheHostsActions()
    {
        Assert.Same(_host.Actions, _map.Actions);
        Assert.Same(_host.InteractionBus, _map.InteractionBus);
        Assert.Same(_host.Dispatch, _map.ActionDispatch);
        Assert.True(_host.Actions.Has(GlobalActionIds.OpenLayerControl));   // the map's own
        Assert.True(_host.Actions.Has(GlobalActionIds.Select));             // the host's shared
    }
}
