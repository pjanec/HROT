using System;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Windows;
using NodeEditor.Core.Action;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-77 / BP-224 — creating a macro by hand, and listing it in the right section.
///
/// <para>
/// ⭐ <b>BP-224 became reachable the moment collapse shipped.</b> <c>BuildGraphItems</c> took a
/// <c>bool functionGraphs</c> and kept <i>everything that is not a Function</i> in the Graphs
/// section — Event, Construction <b>and Macro</b> — while the Macros section was hardcoded empty.
/// Before Batches 33-34 no macro graph existed in any ordinary workflow, so a boolean standing in
/// for a three-way choice cost nothing. It does now.
/// </para>
/// </summary>
public sealed class MacroSectionAndCreateTests
{
    private static BlueprintAsset AssetWithEveryGraphKind()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "SectionAsset",
            Dispatch = BlueprintDispatchKind.Instance,
            Header   = new Header(),
        };
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "Tick",    Kind = GraphKind.Function });
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "OnSpawn", Kind = GraphKind.Event });
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "Build",   Kind = GraphKind.Construction });
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "AimFire", Kind = GraphKind.Macro });
        return asset;
    }

    private static BlueprintMyBlueprintModel ModelFor(BlueprintAsset asset)
    {
        var model = new BlueprintMyBlueprintModel();
        model.Retarget(null, asset);
        return model;
    }

    private static string[] Names(BlueprintMyBlueprintModel model, string sectionId)
        => model.GetItems(sectionId).Select(i => i.DisplayName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    // ────────────────────────────────────────────────────────────────────────
    // BP-224 — section membership
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The regression test for the boolean.</b> A macro must appear under Macros and, just as
    /// importantly, <b>not</b> under Graphs — the old filter put it in both places' worth of wrong:
    /// listed under Graphs, absent from the section named after it.
    /// </summary>
    [Fact]
    public void MacroGraph_ListsUnderMacros_AndNotUnderGraphs()
    {
        var model = ModelFor(AssetWithEveryGraphKind());

        Assert.Equal(new[] { "AimFire" }, Names(model, BlueprintMyBlueprintModel.SectionMacros));
        Assert.DoesNotContain("AimFire", Names(model, BlueprintMyBlueprintModel.SectionGraphs));
    }

    [Fact]
    public void FunctionGraph_StillListsUnderFunctionsOnly()
    {
        var model = ModelFor(AssetWithEveryGraphKind());

        Assert.Equal(new[] { "Tick" }, Names(model, BlueprintMyBlueprintModel.SectionFunctions));
        Assert.DoesNotContain("Tick", Names(model, BlueprintMyBlueprintModel.SectionGraphs));
    }

    /// <summary>
    /// ⭐ The handoff asked where <c>Construction</c> lands, because the same filter governed it and
    /// nobody had looked. <b>Answer: under "Graphs", alongside Event bodies</b> — where it was before
    /// this change and where it belongs; it is a graph the designer opens, not a callable. Pinned here
    /// so the answer stops being folklore.
    /// </summary>
    [Fact]
    public void ConstructionAndEventGraphs_ListUnderGraphs()
    {
        var model = ModelFor(AssetWithEveryGraphKind());

        Assert.Equal(new[] { "Build", "OnSpawn" }, Names(model, BlueprintMyBlueprintModel.SectionGraphs));
    }

    /// <summary>Every graph lands in exactly one of the three sections — no drops, no duplicates.</summary>
    [Fact]
    public void EveryGraph_LandsInExactlyOneSection()
    {
        var asset = AssetWithEveryGraphKind();
        var model = ModelFor(asset);

        var listed = Names(model, BlueprintMyBlueprintModel.SectionGraphs)
            .Concat(Names(model, BlueprintMyBlueprintModel.SectionFunctions))
            .Concat(Names(model, BlueprintMyBlueprintModel.SectionMacros))
            .ToList();

        Assert.Equal(asset.Graphs.Count, listed.Count);
        Assert.Equal(listed.Count, listed.Distinct().Count());
    }

    // ────────────────────────────────────────────────────────────────────────
    // BP-77 — editor.create-macro
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The id and the <i>"Macros +"</i> button have existed since BP-12e; <b>only the handler was
    /// missing</b>, so the item rendered permanently greyed — the same shape as BP-23a's clipboard
    /// commands and BP-74's collapse.
    /// </summary>
    [Fact]
    public void CreateMacro_AppendsAMacroGraph_AndThePanelListsIt()
    {
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "CreateAsset",
            Dispatch = BlueprintDispatchKind.Instance, Header = new Header(),
        };
        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterCreateMacroCommand(commands, asset);

        var result = commands.Invoke(NodeEditor.Core.CommandCatalog.CreateMacro);

        Assert.True(result.Success, result.Message);
        var macro = Assert.Single(asset.Graphs, g => g.Kind == GraphKind.Macro);
        Assert.Equal(new[] { macro.Name }, Names(ModelFor(asset), BlueprintMyBlueprintModel.SectionMacros));
    }

    /// <summary>
    /// BP-126's reason applies to macros unchanged: a graph born with a bare entry costs a palette
    /// trip to find Return, and missing that wire reports <c>BP3010</c> + <c>BP1657</c>.
    /// </summary>
    [Fact]
    public void CreateMacro_BornWithAWiredEntryAndReturn()
    {
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "BornWiredAsset",
            Dispatch = BlueprintDispatchKind.Instance, Header = new Header(),
        };

        var macro = BlueprintDocumentFactory.CreateMacroGraph(asset, "AimFire");

        Assert.NotNull(macro);
        Assert.Equal(GraphKind.Macro, macro!.Kind);
        Assert.Single(macro.Nodes.OfType<EventEntryNode>());
        Assert.Single(macro.Nodes.OfType<ReturnNode>());
        Assert.Single(macro.Links);
        // ⚠ N=0 is the wireable degenerate case, not an omission — NodePinSchema projects the single
        // default Out/In pin, so a fresh macro is callable before anything is declared.
        Assert.Empty(macro.ExecInputs);
        Assert.Empty(macro.ExecOutputs);
    }

    /// <summary>Repeated quick-adds must not collide, and must not overwrite.</summary>
    [Fact]
    public void CreateMacro_TwiceYieldsTwoDistinctlyNamedMacros()
    {
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "TwiceAsset",
            Dispatch = BlueprintDispatchKind.Instance, Header = new Header(),
        };
        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterCreateMacroCommand(commands, asset);

        commands.Invoke(NodeEditor.Core.CommandCatalog.CreateMacro);
        commands.Invoke(NodeEditor.Core.CommandCatalog.CreateMacro);

        var macros = asset.Graphs.Where(g => g.Kind == GraphKind.Macro).ToList();
        Assert.Equal(2, macros.Count);
        Assert.Equal(2, macros.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>A name an existing graph already holds is rejected, not silently duplicated.</summary>
    [Fact]
    public void CreateMacro_RejectsADuplicateName()
    {
        var asset = AssetWithEveryGraphKind();

        Assert.Null(BlueprintDocumentFactory.CreateMacroGraph(asset, "Tick"));
        Assert.Null(BlueprintDocumentFactory.CreateMacroGraph(asset, "class"));   // C# keyword
    }
}
