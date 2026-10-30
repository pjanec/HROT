using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared.Browser;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐⭐ <b>CE-4098 (AQ108 D19 / D16) — the RECORDING picker: <c>File › Open Recording</c>.</b> Lists the recordings the master offers
/// (the cluster read model's exercise inventory — <c>ClusterUiCache.AvailableExercises</c>, the same list <c>GET /exercises</c>
/// and the orchestrator's Replay section show), newest first, and on confirm hands the chosen exercise to the host's ONE opener.
///
/// <para>⭐ <b>ONE class for both hosts</b>, exactly as <c>AssetPickerLauncher</c> (and the since-removed terrain picker): each host passes
/// its inventory, its opener and the shared <c>PickerRegistry.OpenPicker</c> seam, so it is unit-testable without ImGui.</para>
///
/// <para>⭐ <b>The opener is not a second path.</b> In production <c>openRecording</c> is
/// <see cref="IScenarioSession.OpenRecording"/> — it publishes <c>ReplaySessionIntents.EnterReplay(exerciseId)</c>, the ONE construction of
/// the replay intent that <c>POST /replay {exerciseId}</c> (<c>DebugApiService.EnterReplay</c>) and the orchestrator's <i>Load Replay</i>
/// button also end in; the master plans the unload and the replay from any state (AQ108 D16: <i>Open recording → AAR</i>).</para>
///
/// <para>⭐ <b>Recent (D19):</b> an entry names the recording kind's list (<see cref="AssetRecents.ContextFor"/> of
/// <see cref="AssetKind.Recording"/>) and the entry id is the EXERCISE ID — never a path — so the list survives a content-folder move.
/// An exercise gone from the inventory is simply not listed, hence not shown as Recent.</para>
/// </summary>
public sealed class RecordingPickerLauncher
{
    /// <summary>The picker context key — the remembered query/selection of this picker.</summary>
    public const string ContextKey = "recording.open";

    /// <summary>⭐ CE-1442 — the id of the one placeholder entry shown while the cluster's list has not arrived (picking it does nothing: its tag is not an exercise).</summary>
    public const string WaitingEntryId = "recording.waiting-for-inventory";

    private readonly Action<PickerRequest, Action<PickerResult>> _openPicker;
    private readonly Func<IReadOnlyList<ExerciseInventoryItem>> _listExercises;
    private readonly Action<Guid> _openRecording;
    private readonly Func<bool>? _hasInventory;

    /// <param name="openPicker">The picker seam — production: the shell <c>PickerRegistry.OpenPicker</c>.</param>
    /// <param name="listExercises">The master's exercise inventory as this host reads it.</param>
    /// <param name="openRecording">The host's ONE recording opener, given the picked exercise id.</param>
    /// <param name="hasInventory">⭐ CE-1442 (§11.7 ⑤) — the master's list has arrived (<c>RunGate.HasInventory</c>, read through the host's run choice); until then the picker says <see cref="RunGate.WaitingForInventoryText"/> instead of an empty list that reads as "no recordings". <see langword="null"/> ⇒ always (a rail).</param>
    public RecordingPickerLauncher(
        Action<PickerRequest, Action<PickerResult>> openPicker,
        Func<IReadOnlyList<ExerciseInventoryItem>> listExercises,
        Action<Guid> openRecording,
        Func<bool>? hasInventory = null)
    {
        _hasInventory   = hasInventory;
        _openPicker     = openPicker     ?? throw new ArgumentNullException(nameof(openPicker));
        _listExercises  = listExercises  ?? throw new ArgumentNullException(nameof(listExercises));
        _openRecording  = openRecording  ?? throw new ArgumentNullException(nameof(openRecording));
    }

    /// <summary>The entries the picker shows — every recording the master offers, the newest first.</summary>
    public IReadOnlyList<PickerEntry> BuildEntries()
        => _hasInventory is not null && !_hasInventory()
            ? new[] { InventoryWaiting.Entry(WaitingEntryId) }
            : _listExercises()
            .OrderByDescending(x => x.StartTimeUtc)
            .Select(x => new PickerEntry(
                Id:            AssetRecents.RecordingKey(x.ExerciseId),
                Name:          Label(x),
                Description:   $"exercise {x.ExerciseId}",
                Category:      null,
                Keywords:      x.ScenarioId is { Length: > 0 } s ? new[] { s } : null,
                IconTextureId: null,
                Tag:           x,
                IconKey:       AssetKindIcons.GetIconKey(AssetKind.Recording),
                RecentContext: AssetRecents.ContextFor(AssetKind.Recording)))
            .ToList();

    /// <summary>The row label: the scenario it ran, when it started (local time) and how long it ran.</summary>
    public static string Label(ExerciseInventoryItem x)
    {
        var scenario = x.ScenarioId is { Length: > 0 } s ? s : "(unnamed scenario)";
        var start = x.StartTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var run = x.Duration >= TimeSpan.FromHours(1) ? x.Duration.ToString(@"h\:mm\:ss") : x.Duration.ToString(@"m\:ss");
        return $"{scenario} — {start} ({run})";
    }

    /// <summary>Opens the picker; a confirmed recording goes to the host's one opener, a cancel does nothing.</summary>
    public void Open()
    {
        var request = new PickerRequest
        {
            ContextKey    = ContextKey,
            Title         = "Open Recording",
            Layout        = PickerLayout.Standard,
            SelectionMode = PickerSelectionMode.Single,
            ItemsProvider = BuildEntries,
        };

        _openPicker(request, result =>
        {
            if (result.Cancelled) return;
            if (result.First?.Tag is ExerciseInventoryItem picked)
                _openRecording(picked.ExerciseId);
        });
    }
}
