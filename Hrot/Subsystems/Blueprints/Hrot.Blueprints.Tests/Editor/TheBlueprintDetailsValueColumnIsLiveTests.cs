using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 90 (<c>90b</c>) — Blueprint's Details Value column goes live *(<c>BP-334</c>)*.</b>
///
/// <para>🔴🔴 <b>The defect.</b> <c>BlueprintMyBlueprintWindow.ResolveVariableSelection</c> builds a
/// <c>SectionVariableRowSource</c> at TWO sites — the graph-scoped locals arm and the asset-scoped arm
/// — and 📐 <b>neither passed a live reader</b>. ⇒ every blueprint Details cell read <c>(pending)</c>,
/// however the sim was running. ⚠ <c>88a</c> made the standalone <i>Blackboard Variables</i> window
/// live and left this dark, because they sit on <b>two different seams</b>.</para>
///
/// <para>⭐⭐⭐ <b>These ask the CELL TEXT.</b> 📌 Batch 88: <i>"a rail on the provider's return value
/// proves NOTHING."</i> ⇒ every assertion runs the resolved row through
/// <see cref="VariableValueFormatter"/> — the object the control calls — ⛔ never the projection's own
/// dictionary.</para>
///
/// <para>⭐⭐ <b>BOTH sites are railed separately</b>, because they are two constructor calls and
/// 📌 gate 10 requires three distinct reds. ⚠ Wiring one and not the other would show live values on
/// locals and <c>(pending)</c> on globals, which reads as a broken feature rather than as two seams.</para>
/// </summary>
public sealed class TheBlueprintDetailsValueColumnIsLiveTests
{
    private static VariableValueFormatter Formatter() => new(RawValueDecoder.Instance);

    // ══ site 2 — the ASSET-SCOPED arm ════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail for the asset-scoped arm.</b> 🔴 RED before this batch: the cell read
    /// <c>(pending)</c> because <c>liveObjects</c> did not exist and nothing was passed.
    /// </summary>
    [Fact]
    public void AGlobalVariablesCellRendersItsLiveValue()
    {
        var (window, asset) = MakeOutline(live: new Dictionary<string, object> { ["Health"] = 7 });
        BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Int32");

        var selection = window.ResolveVariableSelection(Item(BlueprintMyBlueprintModel.SectionVariables));
        var row       = Assert.Single(selection.Source!.GetRows());

        Assert.Equal("7", Formatter().Cell(row));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Guide row <c>C9</c>, on the real path.</b> A declared variable the run never wrote is
    /// absent from the map ⇒ <c>(pending)</c>. ⛔ <b>A zero here would be a REGRESSION, not a fix</b> —
    /// this is the assertion that separates the two.
    /// </summary>
    [Fact]
    public void ADeclaredButUnwrittenVariableStillReadsPending()
    {
        var (window, asset) = MakeOutline(live: new Dictionary<string, object> { ["Health"] = 7 });
        BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Int32");
        BlueprintDocumentFactory.CreateVariable(asset, "Ammo",   "System.Int32");

        var selection = window.ResolveVariableSelection(Item(BlueprintMyBlueprintModel.SectionVariables));
        var rows      = selection.Source!.GetRows().ToDictionary(r => r.ShortName);
        var f         = Formatter();

        Assert.Equal("7", f.Cell(rows["Health"]));
        Assert.Equal(VariableValueFormatter.PendingFirstWrite, f.Cell(rows["Ammo"]));
    }

    // ══ site 3 — the GRAPH-SCOPED locals arm ═════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail for the locals arm — a SECOND constructor call, and it was equally dark.</b>
    /// </summary>
    [Fact]
    public void ALocalVariablesCellRendersItsLiveValue()
    {
        var tick = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function };
        tick.LocalVariables.Add(Decl("Scratch"));

        var (window, _) = MakeOutline(
            live:      new Dictionary<string, object> { ["Scratch"] = 42 },
            configure: a => a.Graphs.Add(tick),
            graphId:   () => tick.Id);

        var selection = window.ResolveVariableSelection(
            Item(BlueprintMyBlueprintModel.SectionLocalVariables));
        var row = Assert.Single(selection.Source!.GetRows());

        Assert.Equal("42", Formatter().Cell(row));
    }

    // ══ the negative controls ════════════════════════════════════════════════

    /// <summary>⛔ No projection installed ⇒ <c>(pending)</c>, unchanged. ⚠ This is the headless case
    /// AND the state the editor shipped in.</summary>
    [Fact]
    public void WithNoProjectionTheCellIsPending()
    {
        var (window, asset) = MakeOutline(live: null);
        BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Int32");

        var selection = window.ResolveVariableSelection(Item(BlueprintMyBlueprintModel.SectionVariables));

        Assert.False(window.HasLiveProjection);
        Assert.Equal(VariableValueFormatter.PendingFirstWrite,
                     Formatter().Cell(Assert.Single(selection.Source!.GetRows())));
    }

    /// <summary>⭐ A projection that cannot serve THIS asset returns null ⇒ <c>(pending)</c>, ⛔ never a
    /// throw and never a zero.</summary>
    [Fact]
    public void AProjectionThatDeclinesTheAssetLeavesTheCellPending()
    {
        var (window, asset) = MakeOutline(live: null, installDeclining: true);
        BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Int32");

        var selection = window.ResolveVariableSelection(Item(BlueprintMyBlueprintModel.SectionVariables));

        Assert.True(window.HasLiveProjection);
        Assert.Equal(VariableValueFormatter.PendingFirstWrite,
                     Formatter().Cell(Assert.Single(selection.Source!.GetRows())));
    }

    /// <summary>
    /// ⭐⭐ <b>The map is re-read per <c>GetRows()</c>, i.e. per frame.</b> ⛔ Without this the arm could
    /// capture the first frame's map and look correct forever while the sim moved on.
    /// </summary>
    [Fact]
    public void TheLiveMapIsReReadEveryFrame()
    {
        var live = new Dictionary<string, object>();
        var (window, asset) = MakeOutline(live: live);
        BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Int32");

        var source = window
            .ResolveVariableSelection(Item(BlueprintMyBlueprintModel.SectionVariables)).Source!;
        var f = Formatter();

        Assert.Equal(VariableValueFormatter.PendingFirstWrite, f.Cell(source.GetRows()[0]));

        live["Health"] = 99;

        Assert.Equal("99", f.Cell(source.GetRows()[0]));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static (BlueprintMyBlueprintWindow Window, BlueprintAsset Asset) MakeOutline(
        IReadOnlyDictionary<string, object>? live,
        Action<BlueprintAsset>? configure = null,
        Func<Guid>? graphId = null,
        bool installDeclining = false)
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "LiveHost",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = new List<Graph>(),
            Header   = new Header(),
        };
        configure?.Invoke(asset);

        var window = new BlueprintMyBlueprintWindow();
        // ⚠ The EDITABLE asset matters: the live projection is keyed on IEditableAsset, which is the
        //   interface both providers take. A harness that passes null here would silently prove nothing.
        window.Retarget(new FakeEditable(), asset, null, new EditorCommandsImpl(), null,
                        graphId ?? (() => Guid.Empty));

        if (live != null || installDeclining)
            window.SetLiveProjection(new FakeProjection { Objects = live });

        return (window, asset);
    }

    private sealed class FakeProjection : ILiveVariableProjection
    {
        public IReadOnlyDictionary<string, object>? Objects { get; set; }
        public IReadOnlyDictionary<string, object>? GetLiveObjects(Hrot.Editor.AiShared.IEditableAsset a)
            => Objects;
    }

    private sealed class FakeEditable : Hrot.Editor.AiShared.IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "LiveHost";
        public Hrot.Editor.AiShared.AssetKind Kind => Hrot.Editor.AiShared.AssetKind.Blueprint;
        public string SourceFilePath => "/live.bp.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public event Action? Changed { add { } remove { } }
    }

    private static MyBlueprintItem Item(string sectionId)
        => new(ItemId: $"var:{Guid.NewGuid():D}", SectionId: sectionId, DisplayName: "x",
               CategoryPath: null, IconKey: null, BadgeText: null, AccentColor: null,
               Children: null, IsRenamable: true, IsDeletable: true, IsHostDefined: false,
               Tooltip: null);

    private static VariableDecl Decl(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, Type = new BlueprintTypeRef { TypeId = "System.Int32" },
    };
}
