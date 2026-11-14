using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Editor.AiShared.Review;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Review;

/// <summary>
/// ⭐ R-1 / V1 (docs/UX/DESIGN_Review_Perspective.md §4) — the Review bundle composes exactly the four analysis windows, under the host's id prefix and
/// perspective: Frame diff <c>&lt;p&gt;diff</c>, Replay search <c>&lt;p&gt;search</c>, Recording info and JSON export (fixed ids). 🔴 Red-proof (compiles): drop a
/// <c>RegisterWindow</c> from <see cref="ReviewBundle.RegisterInto"/> ⇒ the window-set assertion goes red.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheReviewBundleTests
{
    private sealed class NoExport : Fdp.Toolkit.ReplayBrowser.IRecordingExportService
    {
        public void ExportToJson(string input, string output, Fdp.Toolkit.ReplayBrowser.JsonExportOptions opts) { }
    }

    private sealed class NoDialogs : Fdp.Presentation.Abstractions.IFileDialogService
    {
        public Task<string?> ShowSaveAsDialogAsync(string callSiteId, string defaultFileName, string extensionFilter) => Task.FromResult<string?>(null);
        public Task<string?> ShowOpenFileDialogAsync(string callSiteId, string extensionFilter) => Task.FromResult<string?>(null);
        public Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter) => Task.FromResult<string[]?>(null);
    }

    private sealed class NoEdit : StructEdit.Core.IComponentEditService
    {
        public StructEdit.Core.IEditSession Open(object component, Type componentType, StructEdit.Core.EditScope? scope = null, StructEdit.Core.EditContext? context = null)
            => throw new NotSupportedException();
    }

    private sealed class NoSearch : IRecordingSearchService
    {
        public IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto root, TargetEntityFilter? entityFilter = null, CancellationToken ct = default) => Array.Empty<SearchResultDto>();
        public IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(string fdpPath, LifecyclePredicateDto criteria, TargetEntityFilter? entityFilter = null, CancellationToken ct = default) => Array.Empty<LifecycleSearchResultDto>();
    }

    private static ReviewHostServices Host(string prefix) => new(
        Perspective: "Review", IdPrefix: prefix, Colour: System.Numerics.Vector4.One,
        DiffPanel: new ComponentDiffPanel(),
        SearchPanel: new ReplaySearchPanel(new NoEdit(), new NoSearch(), _ => { }, _ => { }, (_, _) => { }),
        TimelinePanel: new ReplayTimelinePanel(null, () => 0, new NoExport(), new NoDialogs(), new Fdp.Toolkit.ReplayBrowser.PlaybackHistoryTracker(), new Fdp.Presentation.Abstractions.InspectorState()));

    [Fact]
    public void R1_TheBundleRegistersExactlyTheFourAnalysisWindows_UnderTheHostsPrefix()
    {
        var wm = new WindowManager(new IconAtlas(IntPtr.Zero, 1, 1));
        var bundle = new ReviewBundle(Host("t_"));
        Fdp.Toolkit.Runner.UiBundleHost.Compose(new Fdp.Toolkit.Runner.IUiBundle[] { bundle }, new Fdp.Toolkit.Runner.UiBundleContext(wm));

        var windows = (Dictionary<string, ManagedWindow>)typeof(WindowManager).GetField("_windows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wm)!;
        Assert.Equal(new[] { "rb_json_export", "rb_recording_info", "t_diff", "t_search" }, windows.Keys.Where(k => k != "shell_keyboard_shortcuts").OrderBy(k => k, StringComparer.Ordinal).ToArray());   // the shell registers its own shortcuts window
        Assert.All(windows.Where(kv => kv.Key != "shell_keyboard_shortcuts").Select(kv => kv.Value), w => Assert.Equal("Review", w.OwningPerspective));
        Assert.Equal("t_diff", bundle.FrameDiff.Id);
        Assert.Equal("t_search", bundle.ReplaySearch.Id);
    }
}
