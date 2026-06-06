using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.ActionCatalog;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared.Blackboard;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// AN7 — proves the <b>live-style</b> composition-root wiring resolves end-to-end:
/// the real <see cref="BehaviorActionCatalog"/> (built from a channel-command catalog +
/// an <see cref="IActionSchemaExporter"/>) is threaded into both
/// <see cref="BlueprintEditorBootstrap.CreatePaletteRegistry"/> and
/// <see cref="BlueprintGraphModel"/> exactly as <c>EditorSubsystem</c> /
/// <c>BlueprintDocumentFactory</c> wire it in production.
///
/// <para>
/// These tests differ from the existing <c>AN4_PerActionPaletteTests</c> /
/// <c>NodePinSchemaEnrichmentTests</c> (which use a hand-rolled stub catalog and call
/// <c>NodePinSchema.GetCanonicalPins</c> directly): here the catalog is the concrete
/// production type and the projection goes through the real <see cref="BlueprintGraphModel"/>
/// projection path, mirroring the actual live call sites.
/// </para>
/// </summary>
public sealed class AN7_LiveWiringTests
{
    // ── Fake schema source producing a Blueprint-valid (Shared) non-channel action ─────

    /// <summary>Multi-field params DTO so the projected node has &gt; 0 data-IN pins.</summary>
    public struct LiveSharedParams
    {
        public int   Intensity;
        public float Radius;
    }

    private const string ActionFqn = "Live.Ns.LiveActions.DoThing";

    private static ActionSchemaEntry MakeSharedEntry() =>
        new(ActionFqn, typeof(LiveSharedParams),
            ActionHosting.BTree | ActionHosting.Hsm | ActionHosting.Shared,
            BlackboardAccess.Unknown, null);

    /// <summary>Builds the real catalog the same way EditorSubsystem does (channel + exporter).</summary>
    private static BehaviorActionCatalog BuildLiveCatalog()
    {
        var channel  = new FakeChannelCommandCatalog(); // no channel entries needed
        var exporter = new FakeActionSchemaExporter();
        exporter.SetEntries(MakeSharedEntry());
        return new BehaviorActionCatalog(channel, exporter);
    }

    // ── 1. Palette registry built with the real catalog ────────────────────────────────

    /// <summary>
    /// Live wiring: <see cref="BlueprintEditorBootstrap.CreatePaletteRegistry"/> fed the real
    /// catalog registers a non-channel <c>Action:{FQN}</c> kind that bakes <c>ActionFqn</c>.
    /// </summary>
    [Fact]
    public void CreatePaletteRegistry_WithLiveCatalog_ContainsNonChannelActionEntry_AN7()
    {
        using var catalog = BuildLiveCatalog();

        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry(
            channelCatalog:        null,
            behaviorActionCatalog: catalog);

        var descriptor = registry.TryGet($"Action:{ActionFqn}");
        Assert.NotNull(descriptor);

        var node = descriptor!.CreateInstance() as ChannelCommandNode;
        Assert.NotNull(node);
        Assert.Equal(ActionFqn, node!.ActionFqn);
    }

    // ── 2. BlueprintGraphModel built with the real catalog projects param pins ──────────

    /// <summary>
    /// Live wiring: a non-channel <see cref="ChannelCommandNode"/> (ActionFqn set, empty Pins →
    /// projection slow path) projects exec In/Out + one data-IN pin per params-DTO field when the
    /// real catalog is threaded into <see cref="BlueprintGraphModel"/> — exactly how
    /// <c>BlueprintDocumentFactory</c> threads it in production.
    /// </summary>
    [Fact]
    public void BlueprintGraphModel_WithLiveCatalog_ProjectsNonChannelParamPins_AN7()
    {
        using var catalog = BuildLiveCatalog();

        var node  = new ChannelCommandNode { Id = Guid.NewGuid(), ActionFqn = ActionFqn };
        var graph = new Graph
        {
            Id      = Guid.NewGuid(),
            Name    = "Live",
            Kind    = GraphKind.Event,
            Nodes   = new List<Node> { node },
            Links   = new List<Link>(),
            Inputs  = new(),
            Outputs = new(),
        };
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = "LiveWiring",
            Header  = new Header(),
            Graphs  = new List<Graph> { graph },
        };

        // Thread the catalog exactly as BlueprintDocumentFactory does (last ctor arg).
        var sut = new BlueprintGraphModel(asset, graph, behaviorActions: catalog);

        var projected = sut.Nodes.Single();
        var dataIn = projected.Pins
            .Where(p => p.Direction == PinDirection.Input && p.Kind == PinKind.Data)
            .Select(p => p.Label)
            .ToList();

        Assert.Equal(2, dataIn.Count);                 // one per DTO field
        Assert.Contains("Intensity", dataIn);
        Assert.Contains("Radius", dataIn);

        // Exec pins still present.
        Assert.Contains(projected.Pins, p => p.Kind == PinKind.Exec && p.Direction == PinDirection.Input);
        Assert.Contains(projected.Pins, p => p.Kind == PinKind.Exec && p.Direction == PinDirection.Output);
    }

    /// <summary>
    /// Control: the SAME non-channel node projected WITHOUT a catalog collapses to exec-only,
    /// confirming the data-IN pins above are a direct consequence of the threaded catalog
    /// (i.e. the wiring — not an unconditional projection — is what produces them).
    /// </summary>
    [Fact]
    public void BlueprintGraphModel_WithoutCatalog_NonChannelNode_IsExecOnly_AN7()
    {
        var node  = new ChannelCommandNode { Id = Guid.NewGuid(), ActionFqn = ActionFqn };
        var graph = new Graph
        {
            Id      = Guid.NewGuid(),
            Name    = "NoCatalog",
            Kind    = GraphKind.Event,
            Nodes   = new List<Node> { node },
            Links   = new List<Link>(),
            Inputs  = new(),
            Outputs = new(),
        };
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = "NoCatalog",
            Header  = new Header(),
            Graphs  = new List<Graph> { graph },
        };

        var sut = new BlueprintGraphModel(asset, graph); // behaviorActions == null

        var projected = sut.Nodes.Single();
        Assert.Empty(projected.Pins.Where(p =>
            p.Direction == PinDirection.Input && p.Kind == PinKind.Data));
    }
}
