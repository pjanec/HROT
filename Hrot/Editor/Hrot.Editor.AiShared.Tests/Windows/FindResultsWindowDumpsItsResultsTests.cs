using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>FindResultsWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class FindResultsWindowDumpsItsResultsTests : IDisposable
{
    public FindResultsWindowDumpsItsResultsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static AssetReferenceInfo MakeRef(string targetKey = "Health")
        => new(Guid.NewGuid(), AssetKind.BTree, Guid.NewGuid(), "MyTree", targetKey, SubElementKind.BlackboardVariable, "/a.cs");

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "ai_find_results_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new FindResultsWindow("Blueprint", idOverride: id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(window);
    }

    [Fact]
    public void AfterShowingReferences_TheDumpCarriesTheResult()
    {
        const string id = "ai_find_results_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var window = new FindResultsWindow("Blueprint", idOverride: id);
        window.ShowReferences("Health", new[] { MakeRef("Health") });

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet(id);
        Assert.NotNull(vm);
        Assert.Equal(id, vm!.PanelId);
        Assert.Equal(FindResultsWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.Equal("results", dump["mode"]!.GetValue<string>());
        var rows = dump["results"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("Health", rows[0]!["targetKey"]!.GetValue<string>());
    }

    [Fact]
    public void AfterShowingARenamePreview_TheDumpCarriesTheEdits()
    {
        const string id = "ai_find_results_rail4";
        PanelSnapshot.CaptureEnabled = true;
        var window = new FindResultsWindow("Blueprint", idOverride: id);
        var preview = new RefactorPreview(
            "Old", "New",
            new[] { new RefactorFileEdit("/a.cs", Guid.NewGuid(), new[] { new RefactorLineEdit(3, "Old", "New", "ctx") }) },
            Array.Empty<RefactorIssue>());
        window.ShowRenamePreview(preview);

        window.SimulateDrawClientArea();

        var dump = PanelSnapshot.TryGet(id)!.Dump();
        Assert.Equal("rename-preview", dump["mode"]!.GetValue<string>());
        Assert.Equal("Old", dump["renameFromKey"]!.GetValue<string>());
        Assert.Equal("New", dump["renameToKey"]!.GetValue<string>());
        var edits = dump["renameEdits"]!.AsArray();
        Assert.Single(edits);
        Assert.Single(edits[0]!["lineEdits"]!.AsArray());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        const string id = "ai_find_results_rail3";
        var window = new FindResultsWindow("Blueprint", idOverride: id);
        window.ShowReferences("Health", new[] { MakeRef() });   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
        Assert.Equal("results", vm.Mode);
    }
}
