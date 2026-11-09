using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Scenarios;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Editor.Tests.Browser;

/// <summary>
/// ⭐⭐ <c>CE-4098</c> (AQ108 D16 / D19) — the RECORDING picker behind <c>File › Open Recording</c>: it lists the exercises the master recorded, newest
/// first, shows the user's Recent recordings first, and opens the pick through the host's ONE opener
/// (<c>IScenarioSession.OpenRecording</c> → <c>ReplaySessionIntents.EnterReplay</c>; here a fake opener stands for it). A recording is remembered by
/// its EXERCISE ID in the <c>recent.Recording</c> list — never a path — so the content-folder move (CE-4089) cannot stale it.
/// </summary>
public sealed class RecordingPickerLauncherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rec-picker-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "recent-picks.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly Guid Old = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Mid = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid New = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private readonly List<Guid> _opened = new();

    private static ExerciseInventoryItem Ex(Guid id, int day, string? scenario = "uc-urban-battle")
        => new((Fdp.Core.ExerciseId)(id), new DateTime(2026, 10, day, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(5), scenario);

    private RecordingPickerLauncher Make(PickerRegistry registry, params ExerciseInventoryItem[] inventory)
        => new(registry.OpenPicker, () => inventory, id => _opened.Add(id));

    [Fact]
    public void Entries_ListEveryRecordingNewestFirst_ByExerciseId_InTheRecordingKindsList()
    {
        var launcher = Make(new PickerRegistry(), Ex(Old, 1), Ex(New, 3), Ex(Mid, 2));

        var entries = launcher.BuildEntries();

        Assert.Equal(new[] { New, Mid, Old }.Select(AssetRecents.RecordingKey), entries.Select(e => e.Id));
        Assert.All(entries, e =>
        {
            Assert.Equal(AssetRecents.ContextFor(AssetKind.Recording), e.RecentContext);
            Assert.DoesNotContain(Path.DirectorySeparatorChar, e.Id);   // an id, never a path
            Assert.Contains("uc-urban-battle", e.Name);
        });
    }

    /// <summary>
    /// ⭐ R-5 / V6 (docs/UX/DESIGN_Review_Perspective.md §3a) — a row says what opening it GIVES: <i>"… · 4 participants — opens MERGED"</i> / <i>"… · this editor's run"</i>; the notes are read ONCE when the picker
    /// opens (a directory scan, never per frame), and a row with no note (or a host with no source) reads as before.
    /// </summary>
    [Fact]
    public void V6_ARowSaysWhatYouGet_ReadOnceWhenThePickerOpens()
    {
        int reads = 0;
        var notes = new Dictionary<Guid, string> { [New] = "4 participants — opens MERGED", [Mid] = "this editor's run" };
        var registry = new PickerRegistry();
        var launcher = new RecordingPickerLauncher(registry.OpenPicker, () => new[] { Ex(Old, 1), Ex(New, 3), Ex(Mid, 2) }, id => _opened.Add(id),
                                                   sourceNotes: () => { reads++; return notes; });
        Assert.DoesNotContain(" · ", launcher.BuildEntries().First().Name);   // before the picker opens: no scan happened, no note

        launcher.Open();
        Assert.Equal(1, reads);
        var rows = launcher.BuildEntries().ToDictionary(e => e.Id);
        for (int i = 0; i < 5; i++) launcher.BuildEntries();                  // drawn every frame: no further scan
        Assert.Equal(1, reads);
        Assert.EndsWith(" · 4 participants — opens MERGED", rows[AssetRecents.RecordingKey(New)].Name);
        Assert.EndsWith(" · this editor's run", rows[AssetRecents.RecordingKey(Mid)].Name);
        Assert.DoesNotContain(" · ", rows[AssetRecents.RecordingKey(Old)].Name);   // an exercise the inventory does not know: the row as before
    }

    /// <summary>
    /// ⭐ CE-1442 (U1, S6; §11.7 ⑤) — until the master's list has arrived the picker says <i>"waiting for the cluster's list…"</i> (one disabled placeholder), never an empty list that reads as "no recordings";
    /// picking nothing opens nothing; once it arrived the real entries show (an empty list THEN is a true "none").
    /// 🔴 Mutations: <c>BuildEntries</c> ignoring <c>hasInventory</c> ⇒ the first cell reds (an empty list); the placeholder enabled / tagged with an exercise ⇒ the second cell reds.
    /// </summary>
    [Fact]
    public void CE1442_WithoutTheClustersList_ThePickerSaysItIsWaiting_NotNoRecordings()
    {
        var arrived = false;
        var launcher = new RecordingPickerLauncher(new PickerRegistry().OpenPicker, () => Array.Empty<ExerciseInventoryItem>(), id => _opened.Add(id), hasInventory: () => arrived);

        var waiting = launcher.BuildEntries();
        var only = Assert.Single(waiting);
        Assert.Equal("waiting for the cluster's list…", only.Name);
        Assert.False(only.IsEnabled);
        Assert.Null(only.Tag);

        arrived = true;
        Assert.Empty(launcher.BuildEntries());                                       // arrived and empty: a true "none"
        Assert.Empty(_opened);
    }

    /// <summary>
    /// ⭐ CE-4098 success 4 — a pick goes through the ONE opener, once, with the picked exercise id (the real one is
    /// <c>EditorScenarioSession.OpenRecording</c>, which publishes <c>ReplaySessionIntents.EnterReplay</c>); a cancel opens nothing.
    /// 🔴 Red-proof: call the opener with <c>Guid.Empty</c> instead of <c>picked.ExerciseId</c> ⇒ the id assertion fails.
    /// </summary>
    [Fact]
    public void APick_OpensThatExercise_ThroughTheOneOpener_Once_AndACancelOpensNothing()
    {
        var registry = new PickerRegistry { Recent = new RecentStore() };
        var launcher = Make(registry, Ex(Old, 1), Ex(New, 3));

        launcher.Open();
        Assert.True(registry.ConfirmEntry(AssetRecents.RecordingKey(Old)));
        Assert.Equal(new[] { Old }, _opened);

        _opened.Clear();
        launcher.Open();
        Assert.False(registry.ConfirmEntry("not-listed"));
        Assert.Empty(_opened);
    }

    /// <summary>
    /// ⭐ CE-4098 success 1+2 — the recording picker draws the Recent recordings first (last picked first), a pick pushes to the recording list, and
    /// after a "restart" (fresh store from the file, fresh registry, fresh launcher) the picked one is first. Kinds do not mix: a scenario pick
    /// is not a recent recording. Red-proof: give the entries no <c>RecentContext</c> ⇒ the pick lands in the picker's own context and the restart
    /// assertion (the section is empty under the recording list) fails.
    /// </summary>
    [Fact]
    public void ThePickedRecording_IsFirstInTheRecentSection_AfterARestart()
    {
        var inventory = new[] { Ex(Old, 1), Ex(Mid, 2), Ex(New, 3) };

        var first = new PickerRegistry { Recent = RecentStore.Load(FilePath) };
        first.Recent.Push(AssetRecents.ContextFor(AssetKind.Scenario), AssetRecents.RecordingKey(Mid));   // a different kind, same key: must not show
        var launcher = Make(first, inventory);
        launcher.Open();
        Assert.Empty(first.RecentSection());
        Assert.True(first.ConfirmEntry(AssetRecents.RecordingKey(Mid)));
        launcher.Open();
        Assert.True(first.ConfirmEntry(AssetRecents.RecordingKey(Old)));   // Old is now the newest pick, then Mid

        // "restart"
        var second = new PickerRegistry { Recent = RecentStore.Load(FilePath) };
        Make(second, inventory).Open();

        Assert.Equal(new[] { Old, Mid }.Select(AssetRecents.RecordingKey), second.RecentSection().Select(e => e.Id));
    }

    /// <summary>
    /// ⭐ CE-4098 success 3 — a remembered recording that the master no longer offers is not shown (no ghost row, no crash).
    /// 🔴 Red-proof: make <c>RecentFilteredIndices</c> add a row per remembered key ⇒ fails (see the NodeEditor.UI rail for the model).
    /// </summary>
    [Fact]
    public void ARecordingGoneFromTheInventory_IsNotShownAsRecent()
    {
        var store = RecentStore.Load(FilePath);
        store.Push(AssetRecents.ContextFor(AssetKind.Recording), AssetRecents.RecordingKey(Old));
        store.Push(AssetRecents.ContextFor(AssetKind.Recording), AssetRecents.RecordingKey(Mid));

        var registry = new PickerRegistry { Recent = RecentStore.Load(FilePath) };
        Make(registry, Ex(Mid, 2), Ex(New, 3)).Open();   // Old was archived away

        Assert.Equal(new[] { AssetRecents.RecordingKey(Mid) }, registry.RecentSection().Select(e => e.Id));
    }

    // ── ⭐ CE-4330 ui ⑤ (docs/DESIGN_Recording_Storage.md §8 ⑤) — where each run is ─────────────────────────────────────────────

    private static string Chips(PickerEntry e) => string.Join(" + ", e.Chips!.Select(c => c.Label));

    /// <summary>
    /// ⭐⭐ The row says where the run is and what opening it does, from the replicated facts: not archived ⇒ <i>on the nodes</i> (opens at once);
    /// archived and not here ⇒ <i>NAS</i> (imports first); archived and held here ⇒ <i>cached here</i> (opens at once); an incomplete archive ⇒
    /// its count AND <i>on the nodes</i>; a running import ⇒ <i>importing</i> (joins the import); a running archive ⇒ its file count.
    /// ✅ Red-proof: <c>RecordingLocation.Holds</c> answering false ⇒ the cached row reads "NAS" and "imports first".
    /// </summary>
    [Fact]
    public void CE4330_EachRowSaysWhereTheRunIs_AndWhatOpeningItDoes()
    {
        var a = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var b = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var c = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
        var d = Guid.Parse("dddddddd-0000-0000-0000-000000000004");
        var e = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");
        ExerciseInventoryItem At(Guid id, int day, ArchiveNasState nas, int files = 0, int total = 0)
            => Ex(id, day) with { NasState = nas, NasFiles = files, NasFilesTotal = total };
        var inventory = new[]
        {
            At(a, 5, ArchiveNasState.None),
            At(b, 4, ArchiveNasState.Complete),
            At(c, 3, ArchiveNasState.Complete),
            At(d, 2, ArchiveNasState.Incomplete, 3, 4),
            At(e, 1, ArchiveNasState.Complete),
        };
        var jobs = new[]
        {
            new StorageJob(Guid.Parse("70000000-0000-0000-0000-000000000000"), StorageJobKind.Archive, (Fdp.Core.ExerciseId)a, StorageJobState.Running, FilesDone: 2, FilesTotal: 4),
            new StorageJob(Guid.Parse("71111111-0000-0000-0000-000000000000"), StorageJobKind.Import, (Fdp.Core.ExerciseId)e, StorageJobState.Running),
            new StorageJob(Guid.NewGuid(), StorageJobKind.Import, (Fdp.Core.ExerciseId)b, StorageJobState.Done),   // ended: says nothing
        };
        var local = new List<string> { c.ToString() + ":1234" };
        var launcher = new RecordingPickerLauncher(new PickerRegistry().OpenPicker, () => inventory, id => _opened.Add(id),
                                                   storageJobs: () => jobs, localRecordings: () => local);

        var rows = launcher.BuildEntries();

        Assert.Equal(("on the nodes + archiving 2 / 4", "opens at once"), (Chips(rows[0]), rows[0].Hint));
        Assert.Equal(("NAS", "imports first"), (Chips(rows[1]), rows[1].Hint));
        Assert.Equal(("NAS + cached here", "opens at once"), (Chips(rows[2]), rows[2].Hint));
        Assert.Equal(("NAS incomplete 3 / 4 + on the nodes", "opens at once"), (Chips(rows[3]), rows[3].Hint));
        Assert.Equal(("NAS + importing — job #71111111", "joins the import"), (Chips(rows[4]), rows[4].Hint));
        Assert.Equal(new[] { "storage/onnodes", "storage/archive" }, rows[0].Chips!.Select(x => x.IconKey));
    }

    /// <summary>⭐ CE-4330 ④ (§5) — picking a run this station must IMPORT (NAS only, or an import of it running) goes to the host's import, never the plain open;
    /// a run on the nodes / cached here opens at once; a host whose import declines (false) falls back to the plain open.
    /// 🔴 Mutation: the confirm calling <c>_openRecording</c> first ⇒ the NAS row opens without an import ⇒ red.</summary>
    [Fact]
    public void CE4330_PickingANasOnlyRun_ImportsFirst()
    {
        Guid G(int n) => Guid.Parse($"{n:x8}-0000-0000-0000-000000000000");
        ExerciseInventoryItem Row(int n, ArchiveNasState nas) => new((Fdp.Core.ExerciseId)G(n), new DateTime(2026, 11, 8, 18, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(5), "s" + n, NasState: nas);
        var nasOnly = Row(1, ArchiveNasState.Complete);
        var onNodes = Row(2, ArchiveNasState.None);
        var cached = Row(3, ArchiveNasState.Complete);
        Action<PickerResult>? chosen = null;
        var opened = new List<Guid>();
        var imported = new List<Guid>();
        bool accepts = true;
        var launcher = new RecordingPickerLauncher((req, cb) => chosen = cb, () => new[] { nasOnly, onNodes, cached }, opened.Add,
            localRecordings: () => new List<string> { G(3) + ":10" }, import: x => { imported.Add(x.ExerciseId.Value); return accepts; });
        PickerEntry E(ExerciseInventoryItem x) => new("id", "n", null, null, null, null, x);

        launcher.Open(); chosen!(new PickerResult(new[] { E(nasOnly) }));
        launcher.Open(); chosen!(new PickerResult(new[] { E(onNodes) }));
        launcher.Open(); chosen!(new PickerResult(new[] { E(cached) }));
        Assert.Equal(new[] { G(1) }, imported);
        Assert.Equal(new[] { G(2), G(3) }, opened);

        accepts = false;                                                     // a host that opens no recording: the plain open
        launcher.Open(); chosen!(new PickerResult(new[] { E(nasOnly) }));
        Assert.Equal(G(1), opened[^1]);
    }

    /// <summary>⭐ CE-4330 ⑤ — every cluster host's picker is handed BOTH facts (the silent-default rule: a host that has them passes them).
    /// ⚠ The Replay Browser lists its own disk (no cluster) and passes neither.</summary>
    [Theory]
    [InlineData("Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs")]
    [InlineData("Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs")]
    [InlineData("Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs")]
    public void CE4330_EveryClusterHostsPicker_IsHandedTheJobsAndTheLocalRecordings(string file)
    {
        var src = Hrot.Editor.Tests.HostSource.ReadRelative(file.Split('/'));
        int at = src.IndexOf("new Hrot.Editor.AiShared.Scenarios.RecordingPickerLauncher(", StringComparison.Ordinal);
        Assert.True(at >= 0, file);
        var call = src.Substring(at, Math.Min(1600, src.Length - at));
        Assert.Contains("storageJobs:", call);
        Assert.Contains("localRecordings:", call);
        Assert.Contains("actions: x =>", call);            // ⭐ CE-4330 ⑦ — the row's Delete from archive…, through the run choice's transfers
        Assert.Contains("RunChoice?.Transfers?.ActionsFor(x)", call);
        Assert.Contains("RunChoice?.Transfers?.OpenOrImport(x)", call);   // ⭐ CE-4330 ④ — a NAS-only run imports first
    }
}
