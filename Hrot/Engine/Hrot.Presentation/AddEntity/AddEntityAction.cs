using System.Text.Json;
using Fdp.Interfaces;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using Hrot.Core.Mission;
using Hrot.UI.Common.Facades;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ <c>CE-1017</c> S3 — the empty-map <c>Add Entity ▸ Friendly… · Hostile… · Neutral… · ─ · Map Graphics…</c>
/// submenu: each item opens the type picker and the pick ARMS that type's placement tool. 📄
/// docs/DESIGN_Add_Entity_Picker.md D4, D6, D9, sequence in §3.
///
/// <para>🔒 D9 — offered on CAPABILITY: a host that has a TKB, a picker (its <see cref="PickerRegistry.OpenPicker"/>) and an
/// <see cref="ISpawnController"/> (the shared adapter) gets the submenu; one without them gets nothing, not a dead
/// item. While authoring is suspended (Preview, loading) the submenu is shown greyed with its reason.</para>
///
/// <para>⭐ Built by <c>MapInteractionPack</c> from <c>MapInteractionContext.AddEntity</c>, so every map host gets the
/// same one. The three dependencies are resolved at CALL time — hosts build their spawn adapter after the map.</para>
/// </summary>
public sealed class AddEntityAction
{
    private readonly Func<ITkbDatabase?> _tkb;
    private readonly Func<Action<PickerRequest, Action<PickerResult>>?> _openPicker;
    private readonly Func<ISpawnController?> _spawn;
    private readonly Func<string?> _suspendedReason;

    /// <summary>Creates the action. <paramref name="suspendedReason"/> returns why authoring is suspended now
    /// (e.g. <c>"Authoring is suspended in Preview."</c>), or null when it is not.</summary>
    public AddEntityAction(Func<ITkbDatabase?> tkb, Func<Action<PickerRequest, Action<PickerResult>>?> openPicker,
                           Func<ISpawnController?> spawn, Func<string?>? suspendedReason = null)
    {
        _tkb = tkb ?? throw new ArgumentNullException(nameof(tkb));
        _openPicker = openPicker ?? throw new ArgumentNullException(nameof(openPicker));
        _spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
        _suspendedReason = suspendedReason ?? (static () => null);
    }

    /// <summary>True when this host can service the submenu right now (all three dependencies present).</summary>
    public bool IsAvailable => _tkb() is not null && _openPicker() is not null && _spawn() is not null;

    /// <summary>Why the submenu is greyed now, or null.</summary>
    public string? SuspendedReason => _suspendedReason();

    /// <summary>Registers the four action ids on <paramref name="actions"/>.</summary>
    public void RegisterOn(GlobalActionRegistry actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        foreach (int id in new[] { GlobalActionIds.AddEntityFriendly, GlobalActionIds.AddEntityHostile,
                                   GlobalActionIds.AddEntityNeutral, GlobalActionIds.AddMapGraphic })
        {
            int captured = id;
            actions.Register(captured, (_, _) => Open(captured));
        }
    }

    /// <summary>The side an action id stands for; null for <see cref="GlobalActionIds.AddMapGraphic"/>.</summary>
    public static eForceIdentifier? SideOf(int actionId) => actionId switch
    {
        GlobalActionIds.AddEntityFriendly => eForceIdentifier.FORCE_FRIENDLY,
        GlobalActionIds.AddEntityHostile  => eForceIdentifier.FORCE_OPPOSING,
        GlobalActionIds.AddEntityNeutral  => eForceIdentifier.FORCE_NEUTRAL,
        _                                 => null,
    };

    /// <summary>Opens the picker for <paramref name="actionId"/>; returns false when the host cannot service it
    /// or authoring is suspended (nothing opens).</summary>
    public bool Open(int actionId, System.Numerics.Vector2? anchorScreen = null)
    {
        if (SuspendedReason is not null) return false;
        var tkb = _tkb(); var openPicker = _openPicker(); var spawn = _spawn();
        if (tkb is null || openPicker is null || spawn is null) return false;

        var side = SideOf(actionId);
        openPicker(BuildRequest(tkb, side, anchorScreen), result =>
        {
            if (result.First?.Tag is EntityTypeEntry e)
                PlacementToolRegistry.Arm(spawn, e.TkbType, side ?? eForceIdentifier.FORCE_NEUTRAL);
        });
        return true;
    }

    /// <summary>The picker request for a side (or the map graphics when <paramref name="side"/> is null).</summary>
    public static PickerRequest BuildRequest(ITkbDatabase tkb, eForceIdentifier? side, System.Numerics.Vector2? anchorScreen = null)
    {
        var catalog = EntityTypeCatalog.Build(tkb);
        return new PickerRequest
        {
            ContextKey             = side is null ? "add-entity/graphics" : "add-entity/types",
            Title                  = side switch
            {
                eForceIdentifier.FORCE_FRIENDLY => "Add Friendly Entity",
                eForceIdentifier.FORCE_OPPOSING => "Add Hostile Entity",
                eForceIdentifier.FORCE_NEUTRAL  => "Add Neutral Entity",
                _                               => "Add Map Graphic",
            },
            Layout                 = PickerLayout.Tree,
            ItemsProvider          = () => EntityTypeCatalog.ToPickerEntries(catalog, sideBearing: side is not null),
            AnchorScreen           = anchorScreen,
            FoldSingleChildFolders = true,
            ShowPreview            = true,
        };
    }

    /// <summary>The submenu as a context-menu JSON item (the <c>ContextMenuAdapter</c> shape): greyed with
    /// <paramref name="suspendedReason"/> in its label when authoring is suspended.</summary>
    public static object MenuItem(string? suspendedReason)
    {
        bool enabled = suspendedReason is null;
        return new
        {
            label = enabled ? "Add Entity" : $"Add Entity ({suspendedReason})",
            enabled,
            children = new object[]
            {
                new { id = GlobalActionIds.AddEntityFriendly, label = "Friendly..." },
                new { id = GlobalActionIds.AddEntityHostile,  label = "Hostile..." },
                new { id = GlobalActionIds.AddEntityNeutral,  label = "Neutral..." },
                new { separator = true },
                new { id = GlobalActionIds.AddMapGraphic,     label = "Map Graphics..." },
            },
        };
    }

    /// <summary>Serializes a menu item list for <c>CanvasContextMenuState.MenuJson</c>.</summary>
    public static string ToJson(IEnumerable<object> items) => JsonSerializer.Serialize(items);
}
