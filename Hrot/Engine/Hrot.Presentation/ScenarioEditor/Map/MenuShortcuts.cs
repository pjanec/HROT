using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Common.Interactions;
using VisKey = Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey;
using VisButton = Fdp.Toolkit.Vis2D.Abstractions.MapMouseButton;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐ CE-1076 (docs/DESIGN_Entity_Control.md §13.6) — THE KEYS THE UNIT MENU SHOWS NOW WORK. 🔒 User, 2026-10-14: "the keys shown next
    /// to the order name in the entity context menu (M = move, A = attack …) i supposed they would work plainly for the currently selected
    /// entity". A key typed on the map runs the item of the PRIMARY selected unit's menu that shows it — read from the one menu source
    /// (<see cref="Hrot.Common.Diagnostics.Gizmos.ContextMenuProjectorGizmo.MenuJsonFor"/>, so a disabled item stays disabled) and sent
    /// as the SAME <see cref="GizmoMenuActionEvent"/> a click sends: one path to the action, its follow-up pick and its Esc.
    /// <para>Not while a tool holds the map (a pick, a placement — its keys are its own), except the RTS command tool; not with Ctrl or
    /// Alt held. Several selected: the primary one, as a menu click on it would (plain orders stay one unit — DESIGN_Orders §13.12 O6).</para>
    /// </summary>
    public sealed class MenuShortcuts : IEcsModuleSystem
    {
        private readonly EntityRepository _world;
        private readonly FdpEventBus _bus;
        private readonly Selection.EcsSelectionState _selection;
        private readonly Tools.ToolController _tools;

        public MenuShortcuts(EntityRepository world, FdpEventBus bus, Selection.EcsSelectionState selection, Tools.ToolController tools)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        }

        /// <summary>Shortcuts run (diagnostics, rails).</summary>
        public int Fired { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (!_bus.IsRegistered<MapShortcutKeyEvent>()) return;
            foreach (ref readonly var k in _bus.Read<MapShortcutKeyEvent>())
            {
                if (k.Modified || Blocked()) continue;
                if (Resolve(k.Key) is not { } hit) continue;
                _bus.Publish(new GizmoMenuActionEvent { AnchorId = hit.NetworkId, ActionId = hit.ActionId });
                Fired++;
            }
        }

        /// <summary>A tool holds the map's keys — every armed modal tool but the RTS command tool (its gestures are clicks).</summary>
        private bool Blocked()
            => _tools.ActiveModal is { } tool && tool.Id != Tools.ScenarioToolIds.RtsCommand;

        /// <summary>The unit a shortcut means (the primary selection, else the only selected one) and the action its menu binds to
        /// <paramref name="key"/>; null when there is none.</summary>
        public (long NetworkId, int ActionId)? Resolve(int key)
        {
            var unit = _selection.PrimarySelected;
            if (unit is null || unit.Value.IsNull)
            {
                var all = _selection.SelectedEntities;
                if (all.Count != 1) return null;
                foreach (var e in all) unit = e;
            }
            var target = unit!.Value;
            if (!_world.IsAlive(target) || !_world.IsComponentTypeRegistered<NetworkIdentity>() || !_world.HasComponent<NetworkIdentity>(target))
                return null;
            long net = _world.GetComponentRO<NetworkIdentity>(target).Value;
            if (net == 0) return null;
            var items = JsonSerializer.Deserialize<ContextMenuItemDto[]>(
                Hrot.Common.Diagnostics.Gizmos.ContextMenuProjectorGizmo.MenuJsonFor(_world, target), Options);
            return items != null && ActionFor(items, key) is int action ? (net, action) : null;
        }

        private static readonly JsonSerializerOptions Options = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;

        /// <summary>The first ENABLED item (submenus searched too) whose shortcut is <paramref name="key"/>.</summary>
        public static int? ActionFor(IEnumerable<ContextMenuItemDto> items, int key)
        {
            foreach (var item in items)
            {
                if (item.IsSeparator == true) continue;
                if (item.Children is { Length: > 0 } children)
                {
                    if (ActionFor(children, key) is int inner) return inner;
                    continue;
                }
                if (item.Enabled != false && item.Id != 0 && KeyOf(item.Shortcut) == key) return item.Id;
            }
            return null;
        }

        /// <summary>The key a menu's shortcut text names: one letter or digit ("M"), or a key name ("Space", "Delete", "F2").</summary>
        public static int? KeyOf(string? shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut)) return null;
            var s = shortcut.Trim();
            if (s.Length == 1 && char.IsLetterOrDigit(s[0])) return char.ToUpperInvariant(s[0]);
            if (s.Equals("Esc", StringComparison.OrdinalIgnoreCase)) return (int)VisKey.Escape;
            if (s.Equals("Del", StringComparison.OrdinalIgnoreCase)) return (int)VisKey.Delete;
            // the canvas' key enum names only a few keys; the rest are Raylib's (the canvas passes Raylib's codes through)
            if (Enum.TryParse<VisKey>(s, ignoreCase: true, out var named)) return (int)named;
            return Enum.TryParse<Raylib_cs.KeyboardKey>(s, ignoreCase: true, out var raylib) ? (int)raylib : null;
        }
    }

    /// <summary>
    /// ⭐ CE-1076 — the canvas' half: every key typed on the map (the canvas routes only keys no panel took) becomes a
    /// <see cref="MapShortcutKeyEvent"/> for <see cref="MenuShortcuts"/>. Silent while a camera driver (first person) holds the map —
    /// there W, A, S, D and C drive the unit. Consumes nothing.
    /// </summary>
    public sealed class MenuShortcutKeyLayer : IMapLayer
    {
        private readonly FdpEventBus _bus;
        private readonly Func<Fdp.Toolkit.Vis2D.Components.MapCamera?> _camera;
        private readonly Func<bool> _modified;

        public MenuShortcutKeyLayer(FdpEventBus bus, Func<Fdp.Toolkit.Vis2D.Components.MapCamera?> camera, Func<bool>? modified = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _modified = modified ?? CtrlOrAltHeld;
        }

        public string Name => "Menu shortcuts";
        public int LayerBitIndex => -1;
        public bool Has3D => true;

        public bool HandleKeyInput(VisKey key)
        {
            if (_camera() is Fdp.Toolkit.Vis3D.MapCamera3D { Driver: not null }) return false;
            if (_bus.IsRegistered<MapShortcutKeyEvent>())
                _bus.Publish(new MapShortcutKeyEvent { Key = (int)key, Modified = _modified() });
            return false;
        }

        private static bool CtrlOrAltHeld()
        {
            try
            {
                return Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.LeftControl) || Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.RightControl)
                    || Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.LeftAlt) || Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.RightAlt);
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        public void Update(float dt) { }
        public void Draw(RenderContext ctx) { }
        public void Draw3D(RenderContext ctx) { }
        public bool HandleInput(System.Numerics.Vector2 worldPos, VisButton button, bool isPressed) => false;
        public Entity? PickEntity(System.Numerics.Vector2 worldPos) => null;
    }
}
