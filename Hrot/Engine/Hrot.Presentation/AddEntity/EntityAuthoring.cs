using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Modules.Geographic;
using Hrot.Core.Network;
using Hrot.UI.Common.Adapters;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// What a map host supplies so the shared map pack can build its entity-authoring surface. Every member is resolved at
/// CALL time — hosts compose their creation pack, TKB and picker shell after the map.
/// </summary>
/// <param name="Tkb">The host's TKB.</param>
/// <param name="Requests">⭐ <c>CE-3228</c> — the bus the host's creation pack reads (its world's bus), once that pack is composed; null
/// before. Every authoring affordance publishes an <c>EntityCreationRequest</c> on it (Entity Creation §3.4d).</param>
public sealed record EntityAuthoringInputs(
    Func<ITkbDatabase?> Tkb,
    Func<FdpEventBus?> Requests)
{
    /// <summary>Why authoring is suspended now (Preview, loading), or null. Greys the Add Entity submenu.</summary>
    public Func<string?>? SuspendedReason { get; init; }

    /// <summary>The host's own shell picker registry, when it already has one it draws each frame (Editor, CGF: their
    /// asset pickers live there). Null ⇒ the pack makes one and the host draws it with <see cref="EntityAuthoring.DrawFrame"/>.</summary>
    public Func<PickerRegistry?>? HostPickers { get; init; }
}

/// <summary>
/// ⭐ <c>CE-1017</c> — ONE entity-authoring surface per map, built by <c>MapInteractionPack</c> for every map host (Editor,
/// CGF, SimHost, IG): the shared <see cref="ScenarioSpawnAdapter"/> (the tools that place entities), the picker registry the
/// Add Entity picker opens in, the <see cref="AddEntityAction"/> (registered on the map's actions) and the canvas menu system
/// that offers it. 📄 docs/DESIGN_Add_Entity_Picker.md §5 "One wiring".
///
/// <para>🔴 Why it exists — measured `2026-10-07`: each host hand-built the same four objects (3 spawn adapters, 5 picker
/// registries with two different icon setups, 4 canvas menu registrations, 3 service blocks) and IG was on none of them.
/// 🔒 User: "Do we share unified code across simhost and cgf and ig and editor? We should." ⇒ the pack CONSTRUCTS, the
/// host SCHEDULES <see cref="CanvasMenu"/> and DRAWS <see cref="DrawFrame"/> (the 2026-08-28 ruling, unchanged).</para>
/// </summary>
public sealed class EntityAuthoring
{
    private readonly EntityAuthoringInputs _in;
    private readonly GlobalGizmoManager _gizmos;
    private readonly Hrot.ScenarioEditor.Tools.ToolController _tools;
    private readonly PickerRegistry? _ownPickers;
    private ScenarioSpawnAdapter? _spawn;

    internal EntityAuthoring(EntityAuthoringInputs inputs, FdpEventBus worldBus, GlobalGizmoManager gizmos,
                             Hrot.ScenarioEditor.Tools.ToolController tools)
    {
        _in = inputs ?? throw new ArgumentNullException(nameof(inputs));
        _ = worldBus;   // ⭐ CE-3228 — requests go on the creation pack's bus (Requests), which is this world's
        _gizmos = gizmos;
        _tools = tools;
        if (inputs.HostPickers is null) _ownPickers = CreatePickers();
        AddEntity = new AddEntityAction(_in.Tkb, () => Pickers is { } p ? p.OpenPicker : null, () => Spawn, _in.SuspendedReason);
        CanvasMenu = new Hrot.Presentation.Systems.CanvasMenuUpdateSystem(AddEntity);
    }

    /// <summary>The shared spawn adapter, built on first use once the host's creation pack exists; null before that.</summary>
    public ScenarioSpawnAdapter? Spawn
    {
        get
        {
            if (_spawn != null) return _spawn;
            var requests = _in.Requests();
            if (requests is null) return null;
            // ⭐ CE-4073 — the attribute compiler needs no geographic transform (local ENU metres), so it is built on every host:
            //   `geo is null ? null : …` used to leave a host with no terrain without one, silently dropping every request's attributes.
            var json = Fdp.Toolkit.Replication.Attributes.AttributeCompilerFactory.Build();
            _spawn = new ScenarioSpawnAdapter(requests, json, _in.Tkb(), _gizmos, _tools);
            return _spawn;
        }
    }

    /// <summary>The picker registry the Add Entity picker opens in — the host's shell, or the pack's own.</summary>
    public PickerRegistry? Pickers => _in.HostPickers?.Invoke() ?? _ownPickers;

    /// <summary>The Add Entity action, already registered on the map's actions.</summary>
    public AddEntityAction AddEntity { get; }

    /// <summary>The canvas (empty-map) menu system — the host schedules it.</summary>
    public Hrot.Presentation.Systems.CanvasMenuUpdateSystem CanvasMenu { get; }

    /// <summary>Draws the pack's own picker registry; a no-op when the host supplied its shell (it draws that itself).
    /// Call once per ImGui frame. ⛔ OpenPicker only queues — without this nothing shows.</summary>
    public void DrawFrame() => _ownPickers?.DrawFrame();

    /// <summary>⭐ The ONE way a host makes a picker registry that can show entity icons: <c>entity/&lt;name&gt;</c> keys
    /// from <see cref="EntityIconLibrary"/>, every other key from <paramref name="hostIcons"/> (the silk atlas, when the
    /// host has one), with <paramref name="theme"/> or the picker's default theme.</summary>
    public static PickerRegistry CreatePickers(IIconProvider? hostIcons = null, IEditorTheme? theme = null)
    {
        var pickers = new PickerRegistry();
        var icons = new EntityIconLibrary(hostIcons);
        if (theme is not null) pickers.SetServices(icons, theme);
        else pickers.SetIcons(icons);
        return pickers;
    }
}
