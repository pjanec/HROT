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
/// entity menu (the same JSON, run by the same map actions, only what runs on this node), and the map's view items work on every map host:
/// Center View moves the camera in 2-D AND 3-D, Select requests the selection, Properties… selects and shows the inspector window.
/// ⭐ Re-homes the rails of the Editor's deleted <c>JsonEntityContextMenuHandler</c> (labels, a click, dead / no-id entities, separators).
/// </summary>
public sealed class MapEntityMenuHandlerTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly InspectorState _state = new();
    private readonly MapInteraction _map;

    public MapEntityMenuHandlerTests()
    {
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<NetworkIdentity>();
        _w.RegisterComponent<Health>();
        _w.RegisterComponent<EntityInfo>();
        _w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitEntity>();
        _map = MapInteractionPack.Build(new MapInteractionContext { World = _w, BufferCapacity = 64, Inspector = () => _state });
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
        _map.InteractionBus.SwapBuffers();
        return _map.InteractionBus.Read<GlobalActionRequestedEvent>().ToArray();
    }

    [Fact]
    public void CE1134_TheInspectorMenu_IsTheMapsEntityMenu_OnlyWhatRunsHere_AndAClickRunsTheMapsAction()
    {
        var unit = Unit(42, new Vector3(10, 20, 0));
        var menu = new Menu();
        new MapEntityMenuHandler(() => (object?)_state).PopulateMenu(unit, menu);   // found through the inspector state, as in a host

        Assert.Contains("Center View", menu.Items);
        Assert.Contains("Select", menu.Items);
        Assert.Contains("Properties…", menu.Items);
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
        var handler = new MapEntityMenuHandler(() => _map);
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

        center(_w, unit);
        two.Update(1f);
        Assert.InRange(Vector2.Distance(two.Target, new Vector2(300, -120)), 0f, 0.01f);

        var three = new MapCamera3D { ViewportOverride = new Vector2(1280, 720), GroundHeight = (_, _) => 4f };
        canvas.Camera = three;
        center(_w, unit);
        three.Update(1f);
        Assert.InRange(Vector3.Distance(three.LookAt, new Vector3(300, -120, 4f)), 0f, 0.05f);
    }

    /// <summary>
    /// ⭐ BP-515 DS-7 (🔒 user, 2026-10-22: <i>"Properties activation from menu is a global ui action, as any other menu action should be,
    /// no direct calls please"</i>) — "Properties…" REQUESTS: the selection, and a <c>ShowEntityPropertiesEvent</c> on the map's bus; it
    /// opens no window itself. Then the inspector that ANSWERS it (on a host whose Details does not) shows itself on the next frame.
    /// ✅ Red-proof: the handler not publishing the event ⇒ the inspector stays closed.
    /// </summary>
    [Fact]
    public void BP515_Properties_IsARequest_AndTheInspectorAnswersIt()
    {
        var unit = Unit(9, Vector3.Zero);
        var windows = new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(IntPtr.Zero, 512, 512));
        var win = new Blank("t_fdp_inspector");
        windows.RegisterWindow(win);
        win.IsOpen = false;
        Hrot.Presentation.Windows.DiagnosticsWindowsBundle.AnswerProperties(windows, "t_fdp_inspector", () => _state);
        Assert.True(_map.Actions.TryGetHandler(GlobalActionIds.Properties, out var properties));

        properties(_w, unit);

        Assert.False(win.IsOpen, "the action opens no window itself");
        _w.Bus.SwapBuffers();
        var request = Assert.Single(_w.Bus.ReadManaged<SelectionChangeRequest>());
        Assert.Equal(unit, Assert.Single(request.Entities));
        _map.InteractionBus.SwapBuffers();
        var shown = Assert.Single(_map.InteractionBus.Read<Hrot.Common.Diagnostics.Gizmos.ShowEntityPropertiesEvent>().ToArray());
        Assert.Equal(unit, shown.Target);

        windows.RaiseFrameStarted();
        Assert.True(win.IsOpen, "the inspector answered the request");
    }

    private sealed class Blank : Fdp.Presentation.WindowManager.ManagedWindow
    {
        public Blank(string id) : base(id, "Inspector", "Scenario", Fdp.Presentation.WindowManager.WindowScope.PerspectiveBound) { }
        protected override void DrawClientArea() { }
    }
}
