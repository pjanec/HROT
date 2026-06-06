using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Tests for <see cref="BlueprintNodeCatalog"/>.
/// All tests are headless.
/// </summary>
public sealed class BlueprintNodeCatalogTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates the palette registry pre-populated via BlueprintEditorBootstrap.</summary>
    private static NodeKindRegistry MakePaletteRegistry()
        => BlueprintEditorBootstrap.CreatePaletteRegistry();

    private static BlueprintNodeCatalog MakeSut(NodeKindRegistry? registry = null)
        => new(registry ?? MakePaletteRegistry());

    // ── All entries include palette kinds ─────────────────────────────────────

    [Fact]
    public void All_IncludesPaletteKinds_WhenNode()
    {
        var sut = MakeSut();
        Assert.Contains(sut.All, e => e.Kind.Id == "When");
    }

    [Fact]
    public void All_IncludesPaletteKinds_ReadEqsResult()
    {
        var sut = MakeSut();
        Assert.Contains(sut.All, e => e.Kind.Id == "ReadEqsResult");
    }

    [Fact]
    public void All_IncludesPaletteKinds_SpawnEqsSensor()
    {
        var sut = MakeSut();
        Assert.Contains(sut.All, e => e.Kind.Id == "SpawnEqsSensor");
    }

    [Fact]
    public void All_Count_MatchesRegistryEntryCount_WhenNoAsset()
    {
        var registry = MakePaletteRegistry();
        var sut      = MakeSut(registry);

        Assert.Equal(registry.EnumerateAll().Count, sut.All.Count);
    }

    // ── Query: text filter ────────────────────────────────────────────────────

    [Fact]
    public void Query_EmptyText_ReturnsAll()
    {
        var sut = MakeSut();
        var results = sut.Query(new NodeSearchQuery(""));
        Assert.Equal(sut.All.Count, results.Count);
    }

    [Fact]
    public void Query_MatchingText_ReturnsMatchingEntries()
    {
        var sut = MakeSut();
        var results = sut.Query(new NodeSearchQuery("When"));
        Assert.True(results.Count >= 1);
        Assert.All(results, e =>
            Assert.True(
                e.DisplayName.Contains("When", StringComparison.OrdinalIgnoreCase)
                || e.Kind.Id.Contains("When", StringComparison.OrdinalIgnoreCase)
                || (e.CategoryPath?.Contains("When", StringComparison.OrdinalIgnoreCase) ?? false),
                $"Entry '{e.Kind.Id}' does not match 'When'"));
    }

    [Fact]
    public void Query_NonMatchingText_ReturnsEmpty()
    {
        var sut = MakeSut();
        var results = sut.Query(new NodeSearchQuery("ZZZ_TOTALLY_NONEXISTENT_9999"));
        Assert.Empty(results);
    }

    [Fact]
    public void Query_CaseInsensitive()
    {
        var sut = MakeSut();
        var lower = sut.Query(new NodeSearchQuery("when"));
        var upper = sut.Query(new NodeSearchQuery("WHEN"));
        Assert.Equal(lower.Count, upper.Count);
    }

    // ── Query: category filter ────────────────────────────────────────────────

    [Fact]
    public void Query_CategoryFilter_ReturnsOnlyMatchingCategory()
    {
        var sut     = MakeSut();
        var results = sut.Query(new NodeSearchQuery("", CategoryFilter: "EQS"));
        // All returned entries must be in the EQS category.
        Assert.All(results, e =>
            Assert.True(
                e.CategoryPath?.StartsWith("EQS", StringComparison.OrdinalIgnoreCase) ?? false,
                $"Entry '{e.Kind.Id}' is not in EQS category, has '{e.CategoryPath}'"));
    }

    // ── QueryForPinContext ────────────────────────────────────────────────────

    [Fact]
    public void QueryForPinContext_ExecSource_ReturnsEntriesWithExecPin()
    {
        var sut = MakeSut();
        var q   = new PinContextQuery(
            SourcePin:       PinId.Empty,
            SourceDirection: PinDirection.Output,
            SourceKind:      PinKind.Exec,
            SourceType:      null,
            Text:            "");

        var results = sut.QueryForPinContext(q);

        // Every returned entry must have at least one exec Input pin.
        Assert.All(results, e =>
            Assert.True(
                e.Inputs.Any(p => p.Kind == PinKind.Exec),
                $"Entry '{e.Kind.Id}' has no exec input pin"));
    }

    [Fact]
    public void QueryForPinContext_DataSource_ExcludesExecOnlyNodes()
    {
        var sut    = MakeSut();
        var floatT = new TypeKey(BlueprintTypeSystem.Single);
        var q      = new PinContextQuery(
            SourcePin:       PinId.Empty,
            SourceDirection: PinDirection.Output,
            SourceKind:      PinKind.Data,
            SourceType:      floatT,
            Text:            "");

        var results = sut.QueryForPinContext(q);

        // No result should have only exec input pins; each must have a float data input.
        foreach (var e in results)
        {
            Assert.True(
                e.Inputs.Any(p => p.Kind == PinKind.Data && p.Type == floatT),
                $"Entry '{e.Kind.Id}' returned for float data context but has no float data input");
        }
    }

    // ── BCP-BATCH-02-FIX3 Task 1: wire-drop picker offers the full compatible set ──

    /// <summary>
    /// Regression for the "wire-drop picker shows only 3 kinds" bug. The 24 FIX2 palette
    /// kinds construct nodes with empty <c>Pins</c>; DescriptorToEntry must derive pin
    /// signatures from <c>NodePinSchema</c> so an exec-output source returns MANY compatible
    /// kinds (Branch / Sequence / per-action ChannelCommand / …), each with a real exec-input
    /// pin — not just the 3 hand-authored When/EQS entries.
    /// <para>
    /// AN4: The generic "ChannelCommand" kind no longer exists; per-action kinds such as
    /// "ChannelCommand:LocomotionChannel:MoveTo" take its place.  The catalog must be passed
    /// to <see cref="BlueprintEditorBootstrap.CreatePaletteRegistry"/> so those entries appear.
    /// </para>
    /// </summary>
    [Fact]
    public void QueryForPinContext_ExecOutputSource_ReturnsFullFlowSet_WithCompatibleExecInput()
    {
        // AN4: pass BuiltInChannelCommandCatalog so per-action ChannelCommand entries are registered.
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry(BuiltInChannelCommandCatalog.Instance);
        var sut      = MakeSut(registry);
        var q   = new PinContextQuery(
            SourcePin:       PinId.Empty,
            SourceDirection: PinDirection.Output,
            SourceKind:      PinKind.Exec,
            SourceType:      null,
            Text:            "");

        var results = sut.QueryForPinContext(q);

        // Far more than the 3 hand-authored kinds.
        Assert.True(results.Count > 3,
            $"Expected the full compatible flow set (>3), got {results.Count}.");

        // The exec-flow staples must be present (Branch + Sequence).
        foreach (var kind in new[] { "Branch", "Sequence" })
        {
            var entry = results.SingleOrDefault(e => e.Kind.Id == kind);
            Assert.True(entry != null,
                $"Exec-output wire-drop picker is missing compatible kind '{kind}'.");
            Assert.Contains(entry!.Inputs, p => p.Kind == PinKind.Exec);
        }

        // AN4: per-action ChannelCommand entry must appear (e.g. the MoveTo action).
        var ccEntry = results.SingleOrDefault(
            e => e.Kind.Id == "ChannelCommand:LocomotionChannel:MoveTo");
        Assert.True(ccEntry != null,
            "Exec-output wire-drop picker is missing per-action kind 'ChannelCommand:LocomotionChannel:MoveTo'.");
        Assert.Contains(ccEntry!.Inputs, p => p.Kind == PinKind.Exec);

        // Every returned entry must genuinely have an exec input pin.
        Assert.All(results, e =>
            Assert.Contains(e.Inputs, p => p.Kind == PinKind.Exec));
    }

    /// <summary>
    /// A node kind that only has a data-OUTPUT (a literal-style node) must NOT appear for an
    /// exec-output source — proves the pin-derived signatures actually filter by compatibility
    /// rather than blindly returning everything.
    /// </summary>
    [Fact]
    public void QueryForPinContext_ExecOutputSource_ExcludesPureDataOutputKinds()
    {
        var sut = MakeSut();
        var q   = new PinContextQuery(
            PinId.Empty, PinDirection.Output, PinKind.Exec, null, "");

        var results = sut.QueryForPinContext(q);

        // GetVariable projects a single Value data-OUTPUT pin (pure, no exec) → must be excluded.
        Assert.DoesNotContain(results, e => e.Kind.Id == "GetVariable");
    }

    /// <summary>
    /// The pin signatures are derived for the empty-Pins palette kinds: a freshly built
    /// catalog entry for "Branch" exposes exec In + two exec Outs (True/False), proving
    /// DescriptorToEntry now goes through NodePinSchema rather than reading defaultNode.Pins
    /// (which would be empty for these kinds).
    /// </summary>
    [Fact]
    public void DescriptorToEntry_EmptyPinsPaletteKind_DerivesCanonicalPinSignatures()
    {
        var sut    = MakeSut();
        var branch = sut.All.Single(e => e.Kind.Id == "Branch");

        // exec input "In"
        Assert.Contains(branch.Inputs, p => p.Kind == PinKind.Exec);
        // exec outputs True/False
        Assert.True(branch.Outputs.Count(p => p.Kind == PinKind.Exec) >= 2,
            "Branch should project two exec output pins (True/False).");

        // Sequence: exec in + at least one exec out.
        var seq = sut.All.Single(e => e.Kind.Id == "Sequence");
        Assert.Contains(seq.Inputs,  p => p.Kind == PinKind.Exec);
        Assert.Contains(seq.Outputs, p => p.Kind == PinKind.Exec);
    }

    // ── Dynamic entries: callable peers ──────────────────────────────────────

    [Fact]
    public void IncludesCallablePeers_AfterCatalogChanged()
    {
        var registry = MakePaletteRegistry();
        var sut      = new BlueprintNodeCatalog(registry);

        var peerGuid = Guid.NewGuid();
        var asset    = BlueprintAssetBuilder.Instance("Host")
            .WithCallablePeer(peerGuid.ToString())
            .Build();

        int changedFired = 0;
        sut.CatalogChanged += () => changedFired++;

        sut.Asset = asset;

        // CatalogChanged must have fired.
        Assert.True(changedFired >= 1, "CatalogChanged was not fired after setting Asset");

        // A peer entry should appear.
        var peerEntries = sut.All.Where(e => e.Kind.Id.StartsWith("CallPeer.")).ToList();
        Assert.Equal(asset.CallablePeers.Count, peerEntries.Count);
    }

    [Fact]
    public void IncludesCustomEvents_AfterAssetSet()
    {
        var registry = MakePaletteRegistry();
        var sut      = new BlueprintNodeCatalog(registry);

        var asset = BlueprintAssetBuilder.Instance("EvtHost")
            .WithCustomEvent("OnAttacked", ("Damage", typeof(float)))
            .Build();

        sut.Asset = asset;

        var evtEntries = sut.All.Where(e => e.Kind.Id.StartsWith("CustomEvent.OnAttacked")).ToList();
        Assert.Single(evtEntries);

        var evtEntry = evtEntries[0];
        Assert.Equal("CustomEvents", evtEntry.CategoryPath);
        // The custom event entry should have an input pin for the Damage parameter.
        Assert.Contains(evtEntry.Inputs, p => p.Label == "Damage");
    }

    [Fact]
    public void CatalogHasNoDynamicEntries_WithNoAsset()
    {
        var registry = MakePaletteRegistry();
        var sut      = new BlueprintNodeCatalog(registry);
        // No asset set — no peer/event entries.
        Assert.DoesNotContain(sut.All, e => e.Kind.Id.StartsWith("CallPeer."));
        Assert.DoesNotContain(sut.All, e => e.Kind.Id.StartsWith("CustomEvent."));
    }

    [Fact]
    public void Refresh_WithNullAsset_FiresCatalogChanged()
    {
        var sut = MakeSut();
        int fired = 0;
        sut.CatalogChanged += () => fired++;
        sut.Refresh();
        Assert.Equal(1, fired);
    }

    // ── Categories ────────────────────────────────────────────────────────────

    [Fact]
    public void Categories_IsNonEmpty()
    {
        var sut = MakeSut();
        Assert.True(sut.Categories.Count > 0);
    }
}
