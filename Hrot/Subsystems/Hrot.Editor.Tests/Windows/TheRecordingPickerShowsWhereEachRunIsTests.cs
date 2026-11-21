using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared.Scenarios;
using Hrot.Editor.UiFrameRail;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-4330 ui ⑤ (docs/DESIGN_Recording_Storage.md §8 ⑤; mockup <c>docs/UX/recording-storage-visual.svg</c>) — the REAL recording picker drawn on a
/// real frame with the mockup's five rows (archiving · importing · cached here · NAS only · incomplete): every row draws its chips and its hint
/// without throwing. The frame is written to <c>HROT_RAIL_SHOTS</c> (default: the temp folder's <c>hrot-frames</c>) for the side-by-side with the mockup.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheRecordingPickerShowsWhereEachRunIsTests
{
    [SkippableFact]
    public void ThePickerDrawsEachRowsChipsAndHint_OnARealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Guid G(int n) => Guid.Parse($"{n:x8}-0000-0000-0000-000000000000");
        ExerciseInventoryItem Row(int n, string scenario, int minutes, ArchiveNasState nas, int files = 0, int total = 0)
            => new((Fdp.Core.ExerciseId)G(n), new DateTime(2026, 11, 8, 18, 40 - n, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(minutes), scenario,
                   NasState: nas, NasFiles: files, NasFilesTotal: total);
        var inventory = new[]
        {
            Row(1, "Night attack", 18, ArchiveNasState.None),
            Row(2, "Recon", 22, ArchiveNasState.Complete),
            Row(3, "Bridgehead", 7, ArchiveNasState.Complete),
            Row(4, "Ambush", 12, ArchiveNasState.Complete),
            Row(5, "Crossfire", 31, ArchiveNasState.Incomplete, 3, 4),
        };
        var jobs = new[]
        {
            new StorageJob(G(0x70), StorageJobKind.Archive, (Fdp.Core.ExerciseId)G(1), StorageJobState.Running, FilesDone: 2, FilesTotal: 4),
            new StorageJob(G(0x71), StorageJobKind.Import, (Fdp.Core.ExerciseId)G(2), StorageJobState.Running),
        };
        var local = new List<string> { G(3) + ":1024" };
        var registry = new PickerRegistry();
        var launcher = new RecordingPickerLauncher(registry.OpenPicker, () => inventory, _ => { }, storageJobs: () => jobs, localRecordings: () => local);
        Assert.All(launcher.BuildEntries(), e => Assert.NotEmpty(e.Chips!));
        launcher.Open();

        using var frame = UiFrameHarness.Begin(1000, 520);
        frame.StepN(8, () => registry.DrawFrame());
        var dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");
        Directory.CreateDirectory(dir);
        frame.Screenshot(Path.Combine(dir, "ce4330-recording-picker-chips.png"));
    }
}
