using System;
using System.Collections.Generic;
using Bagira.IG.Components;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.IG.UI;

/// <summary>
/// Pure-logic form state driving the Mini-IOS spawner panel (IG.5.3).
///
/// Holds user-supplied form data (TKB type, affiliation, spawn coordinates)
/// and exposes a <see cref="Submit"/> method that constructs and publishes a
/// <see cref="SpawnEntityCommand"/> onto the event bus — mirroring the
/// <see cref="Bagira.IG.Tools.CreationTool"/> click path without requiring a
/// canvas interaction.
///
/// The <see cref="OnCommandPublished"/> event allows tests and integrators to
/// observe emitted commands without subscribing to the bus.
/// </summary>
public class MiniIosPanelState
{
    // ── Form fields ───────────────────────────────────────────────────────────

    /// <summary>
    /// TKB template type to spawn.  Defaults to <see cref="MiniIosPanelConstants.DefaultTkbType"/>.
    /// </summary>
    public long TkbType { get; set; } = MiniIosPanelConstants.DefaultTkbType;

    /// <summary>Force affiliation to assign to the spawned entity.</summary>
    public ForceId Affiliation { get; set; } = ForceId.Unknown;

    /// <summary>Initial world-space X position (metres) for the spawned entity.</summary>
    public float PositionX { get; set; }

    /// <summary>Initial world-space Y position (metres) for the spawned entity.</summary>
    public float PositionY { get; set; }

    /// <summary>Filter text for the TKB type browser list.</summary>
    public string SearchText { get; set; } = string.Empty;

    // ── Testability hook ──────────────────────────────────────────────────────

    /// <summary>
    /// Raised synchronously inside <see cref="Submit"/> immediately after the
    /// <see cref="SpawnEntityCommand"/> is published, so tests can inspect the
    /// command without consuming it from the bus.
    /// </summary>
    public event Action<SpawnEntityCommand>? OnCommandPublished;

    // ── Submit ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a <see cref="SpawnEntityCommand"/> from the current form fields
    /// and publishes it to <paramref name="eventBus"/>.
    ///
    /// The command mirrors the <c>CreationTool</c> contract:
    /// <list type="bullet">
    ///   <item><see cref="SpawnEntityCommand.NetworkId"/> = 0 (SimHost allocates).</item>
    ///   <item><see cref="SpawnEntityCommand.OwnerNodeId"/> = <see cref="IgNetworkConstants.LocalNodeId"/>.</item>
    ///   <item><see cref="SpawnEntityCommand.InitType"/> = <see cref="ReliableInitType.None"/>.</item>
    ///   <item><c>InitialComponents</c> contains a <see cref="SimTransform"/> at the form position
    ///         and an <see cref="Bagira.IG.Components.IgSymbolOverride"/> carrying the chosen affiliation.</item>
    /// </list>
    /// </summary>
    /// <param name="eventBus">The application event bus; must not be <c>null</c>.</param>
    public void Submit(FdpEventBus eventBus)
    {
        if (eventBus is null) throw new ArgumentNullException(nameof(eventBus));

        var transform = new SimTransform
        {
            Position = new System.Numerics.Vector3(PositionX, PositionY, 0f),
            Rotation = SimMath.FacingEast,
        };

        var symbolOverride = new IgSymbolOverride
        {
            StyleSetId = AffiliationToStyleSetId(Affiliation),
        };

        var cmd = new SpawnEntityCommand
        {
            NetworkId         = 0,
            TkbType           = TkbType,
            OwnerNodeId       = IgNetworkConstants.LocalNodeId,
            InitType          = ReliableInitType.None,
            InitialComponents = new List<object> { transform, symbolOverride },
            RequestId         = Guid.NewGuid(),
        };

        eventBus.PublishManaged(cmd);
        OnCommandPublished?.Invoke(cmd);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string AffiliationToStyleSetId(ForceId affiliation) =>
        affiliation switch
        {
            ForceId.Friend  => IgSymbolOverride.StyleSetFriend,
            ForceId.Hostile => IgSymbolOverride.StyleSetHostile,
            ForceId.Neutral => IgSymbolOverride.StyleSetNeutral,
            _               => IgSymbolOverride.StyleSetUnknown,
        };
}
