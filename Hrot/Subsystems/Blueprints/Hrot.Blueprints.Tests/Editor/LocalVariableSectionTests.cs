using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Windows;
using NodeEditor.Core.Action;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-57 — <b>the Local Variables section</b>: the surface the compiler half, the schema source, the
/// picker, the delete refusal and the undo were all built behind.
///
/// <para>
/// ⛔ <b>What these replace: nothing constructed <c>BlueprintLocalVariableSchemaSource</c> outside its
/// own tests.</b> Every gesture below it worked and none of them was reachable — there was nowhere in
/// the editor to declare a local at all.
/// </para>
///
/// <para>
/// ⭐ <b>The section is the only GRAPH-scoped one</b>, so most of what is asserted here is about
/// following the canvas rather than about the rows themselves.
/// </para>
/// </summary>
public sealed class LocalVariableSectionTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    private static VariableDecl Decl(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name,
        Type = new BlueprintTypeRef { TypeId = "System.Int32" }, DefaultValueJson = "",
    };

    private static Graph NewGraph(string name, GraphKind kind = GraphKind.Function) => new()
    {
        Id = Guid.NewGuid(), Name = name, Kind = kind,
    };

    private static BlueprintAsset Asset(params Graph[] graphs) => new()
    {
        AssetId = Guid.NewGuid(), Name = "SectionHost",
        Dispatch = BlueprintDispatchKind.Instance,
        Graphs = graphs.ToList(), Header = new Header(),
    };

    private static void AddGet(Graph g, VariableDecl target)
    {
        var n = new GetVariableNode { Id = Guid.NewGuid(), VariableId = target.Id.ToString() };
        n.Pins.Add(new Pin
        {
            Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" },
        });
        g.Nodes.Add(n);
    }

    /// <summary>A model pointed at <paramref name="asset"/>, with a settable current graph.</summary>
    private static (BlueprintMyBlueprintModel Model, Action<Guid> Switch) Model(BlueprintAsset asset, Graph? initial)
    {
        var current = initial?.Id ?? Guid.Empty;
        var model = new BlueprintMyBlueprintModel();
        // No IEditableAsset: the locals section reads the graph, never the dirty-tracking wrapper.
        model.Retarget(null, asset, () => current);
        return (model, id => current = id);
    }

    private static IReadOnlyList<string> LocalNames(BlueprintMyBlueprintModel m)
        => m.GetItems(BlueprintMyBlueprintModel.SectionLocalVariables)
            .Select(i => i.DisplayName).ToList();

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 The descriptor
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The section exists at all.</b> This is the assertion that was missing for two batches:
    /// everything else about locals was built and none of it had a surface.
    /// </summary>
    [Fact]
    public void TheSectionIsDeclared_WithACreateCommand()
    {
        var section = new BlueprintMyBlueprintModel().Sections
            .Single(s => s.Id == BlueprintMyBlueprintModel.SectionLocalVariables);

        Assert.Equal("Local Variables", section.DisplayName);
        Assert.Equal(5, section.SortOrder);                 // ⭐ appended, below Variables
        Assert.True(section.CanCreateItems);
        Assert.Equal("editor.create-local-variable", section.CreateCommandId);
        Assert.Equal(BlueprintMyBlueprintModel.CommandCreateLocalVariable, section.CreateCommandId);
    }

    /// <summary>
    /// ⭐ <b>Present and EMPTY, never absent.</b> A section that appears when a graph happens to have
    /// a local and vanishes when it does not reads as a broken feature — and it would leave the
    /// designer no way to declare the first one.
    /// </summary>
    [Fact]
    public void AGraphWithNoLocals_StillHasTheSection_Empty()
    {
        var g = NewGraph("Tick");
        var (model, _) = Model(Asset(g), g);

        Assert.Contains(model.Sections, s => s.Id == BlueprintMyBlueprintModel.SectionLocalVariables);
        Assert.Empty(model.GetItems(BlueprintMyBlueprintModel.SectionLocalVariables));
    }

    /// <summary>A macro graph is read-only for locals, but its section is still there (BP1664).</summary>
    [Fact]
    public void AMacroGraph_StillHasTheSection()
    {
        var macro = NewGraph("Mac", GraphKind.Macro);
        var (model, _) = Model(Asset(macro), macro);

        Assert.Contains(model.Sections, s => s.Id == BlueprintMyBlueprintModel.SectionLocalVariables);
        Assert.Empty(model.GetItems(BlueprintMyBlueprintModel.SectionLocalVariables));
    }

    // ══ the "+" says WHY, before the work (2026-08-17 user ruling) ═══════════
    //
    // 📌 User, verbatim: "Disabling/graying a [+] on variable section but showing explanatory tooltip
    //    would be better than allowing user to click the button and then saying that it is not
    //    possible — same information value, no false expectations."
    //
    // ⭐ A REFINEMENT of Q26-B2 (which forbids the "+" VANISHING), not a reversal.

    /// <summary>
    /// ⭐ On a Function graph the "+" is usable and says nothing — ⛔ a reason that is always present
    /// is a reason that teaches nothing.
    /// </summary>
    [Fact]
    public void OnAFunctionGraph_TheLocalsCreateButtonHasNoReason()
    {
        var g = NewGraph("Tick");
        var (model, _) = Model(Asset(g), g);

        Assert.Null(LocalsSection(model).CreateDisabledReason);
    }

    /// <summary>
    /// ⭐⭐ 🔴 On a Macro graph it greys and names the graph. ⛔ The section and its "+" both STAY —
    /// <c>Q26-B2</c> forbids vanishing, and this test asserts that too.
    /// </summary>
    [Fact]
    public void OnAMacroGraph_TheLocalsCreateButtonGreysAndSaysWhy()
    {
        var macro = NewGraph("Blend", GraphKind.Macro);
        var (model, _) = Model(Asset(macro), macro);

        var section = LocalsSection(model);
        Assert.True(section.CanCreateItems);                   // ⛔ still declared creatable
        Assert.NotNull(section.CreateCommandId);               // ⛔ the button still exists
        Assert.NotNull(section.CreateDisabledReason);
        Assert.Contains("Blend",  section.CreateDisabledReason!, StringComparison.Ordinal);
        Assert.Contains("macro",  section.CreateDisabledReason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The reason FOLLOWS the canvas.</b> 🔴 This is the half that a <c>static readonly</c>
    /// descriptor list could not express, and whose impossibility the section's own comment cited —
    /// ⛔ one model instance, two graphs, two answers.
    /// </summary>
    [Fact]
    public void SwitchingFromAMacroToAFunction_ClearsTheReason()
    {
        var macro    = NewGraph("Blend", GraphKind.Macro);
        var function = NewGraph("Tick");
        var asset    = Asset(macro, function);

        var current = macro.Id;
        var model   = new BlueprintMyBlueprintModel();
        model.Retarget(null, asset, () => current);

        Assert.NotNull(LocalsSection(model).CreateDisabledReason);

        current = function.Id;
        Assert.Null(LocalsSection(model).CreateDisabledReason);
    }

    /// <summary>⭐ And with no canvas at all it says so, rather than inviting a click into nothing.</summary>
    [Fact]
    public void WithNoGraphOpen_TheLocalsCreateButtonSaysToOpenOne()
    {
        var model = new BlueprintMyBlueprintModel();
        model.Retarget(null, Asset(NewGraph("Tick")));

        Assert.NotNull(LocalsSection(model).CreateDisabledReason);
    }

    /// <summary>
    /// ⚠ <b>Only the locals section carries a reason today.</b> ⛔ Asserted so a future reason added
    /// somewhere else is a deliberate change rather than a side effect of the projection.
    /// </summary>
    [Fact]
    public void NoOtherSectionCarriesAReason()
    {
        var g = NewGraph("Tick");
        var (model, _) = Model(Asset(g), g);

        Assert.All(
            model.Sections.Where(s => s.Id != BlueprintMyBlueprintModel.SectionLocalVariables),
            s => Assert.Null(s.CreateDisabledReason));
    }

    private static NodeEditor.Core.Interfaces.MyBlueprintSectionDescriptor LocalsSection(
        BlueprintMyBlueprintModel model)
        => model.Sections.Single(s => s.Id == BlueprintMyBlueprintModel.SectionLocalVariables);

    /// <summary>No canvas provider at all ⇒ empty, not a throw. The other five sections still work.</summary>
    [Fact]
    public void WithNoCurrentGraphProvider_TheSectionIsEmptyAndTheOthersAreNot()
    {
        var g = NewGraph("Tick");
        g.LocalVariables.Add(Decl("Scratch"));
        var asset = Asset(g);
        asset.Variables.Add(Decl("Health"));

        var model = new BlueprintMyBlueprintModel();
        model.Retarget(null, asset);

        Assert.Empty(model.GetItems(BlueprintMyBlueprintModel.SectionLocalVariables));
        Assert.Single(model.GetItems(BlueprintMyBlueprintModel.SectionVariables));
    }

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 Following the canvas — the reason this section needed real work
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Switch graphs ⇒ the contents change.</b> <c>BP-72</c>: a panel editing the graph you are
    /// not looking at is a defect, and locals are the second surface to hit it.
    /// </summary>
    [Fact]
    public void SwitchingGraphs_ChangesTheContents()
    {
        var tick   = NewGraph("Tick");
        var helper = NewGraph("Helper");
        tick.LocalVariables.Add(Decl("Scratch"));
        helper.LocalVariables.AddRange(new[] { Decl("Carry"), Decl("Temp") });

        var (model, switchTo) = Model(Asset(tick, helper), tick);
        Assert.Equal(new[] { "Scratch" }, LocalNames(model));

        switchTo(helper.Id);
        Assert.Equal(new[] { "Carry", "Temp" }, LocalNames(model));
    }

    /// <summary>
    /// ⭐ <b><c>Changed</c> fires on a switch</b> — and only on a real one. ⚠ Note the section does not
    /// depend on this to be correct: <c>MyBlueprintPanel.DrawSections</c> calls <c>GetItems</c> every
    /// frame and its <c>Changed</c> handler is an empty lambda. This is the <c>IMyBlueprintModel</c>
    /// contract, kept for any consumer that caches.
    /// </summary>
    [Fact]
    public void SyncCurrentGraph_FiresChangedOncePerSwitch()
    {
        var tick   = NewGraph("Tick");
        var helper = NewGraph("Helper");
        var (model, switchTo) = Model(Asset(tick, helper), tick);

        var fired = 0;
        model.Changed += () => fired++;

        Assert.True(model.SyncCurrentGraph());       // first observation after Retarget
        Assert.Equal(1, fired);

        Assert.False(model.SyncCurrentGraph());      // ⭐ idempotent — no spurious refresh
        Assert.Equal(1, fired);

        switchTo(helper.Id);
        Assert.True(model.SyncCurrentGraph());
        Assert.Equal(2, fired);
    }

    /// <summary>Rows mirror the asset-variable rows: id form, renamable, deletable, not host-defined.</summary>
    [Fact]
    public void RowsCarryTheLocalIdForm_AndAreEditable()
    {
        var g = NewGraph("Tick");
        var decl = Decl("Scratch");
        g.LocalVariables.Add(decl);

        var (model, _) = Model(Asset(g), g);
        var item = Assert.Single(model.GetItems(BlueprintMyBlueprintModel.SectionLocalVariables));

        Assert.Equal($"local:{decl.Id}", item.ItemId);
        Assert.Equal(BlueprintMyBlueprintModel.SectionLocalVariables, item.SectionId);
        Assert.True(item.IsRenamable);
        Assert.True(item.IsDeletable);
        Assert.False(item.IsHostDefined);
        Assert.NotNull(item.AccentColor);
        Assert.Contains("Tick", item.Tooltip);       // the scope the canvas cannot show
    }

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 The "+" — registered, not merely declared
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b><c>BP-12c</c>'s lesson, asserted.</b> A section that declares a create command nothing
    /// registers is an INERT BUTTON — that shipped twice (Custom Events, Macros) before it was
    /// caught. ⚠ Driven through the <b>production</b> construction site, not a stand-in.
    /// </summary>
    [Fact]
    public void TheCreateCommandIsRegisteredByTheProductionRetarget()
    {
        var g = NewGraph("Tick");
        var window   = new BlueprintMyBlueprintWindow();
        var commands = new EditorCommandsImpl();

        Assert.False(commands.Invoke(BlueprintMyBlueprintModel.CommandCreateLocalVariable).Success);

        window.Retarget(null, Asset(g), null, commands, null, () => g.Id);

        Assert.True(commands.Invoke(BlueprintMyBlueprintModel.CommandCreateLocalVariable).Success);
    }

    /// <summary>⭐ The create path reaches <c>AddVariable</c> on the <b>current</b> graph.</summary>
    [Fact]
    public void CreatingALocalLandsOnTheCurrentGraph()
    {
        var tick   = NewGraph("Tick");
        var helper = NewGraph("Helper");
        var current = helper.Id;                       // ⭐ NOT the first graph

        var window = new BlueprintMyBlueprintWindow();
        window.Retarget(null, Asset(tick, helper), null, new EditorCommandsImpl(), null, () => current);

        window.Locals!.AddVariable(
            new Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry("Scratch", typeof(int), null));

        Assert.Empty(tick.LocalVariables);
        Assert.Equal("Scratch", helper.LocalVariables.Single().Name);
    }

    /// <summary>
    /// ⭐⭐ <b>A macro graph refuses OUT LOUD.</b> ⛔ The one outcome <c>Q26-B2</c> rules out is silence,
    /// and a test asserting only "nothing was added" would pass against that silence too.
    /// </summary>
    [Fact]
    public void OnAMacroGraph_TheCreateRefusesAndSaysWhy()
    {
        var macro = NewGraph("Mac", GraphKind.Macro);
        var window = new BlueprintMyBlueprintWindow();
        window.Retarget(null, Asset(macro), null, new EditorCommandsImpl(), null, () => macro.Id);

        window.Locals!.AddVariable(
            new Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry("Scratch", typeof(int), null));

        Assert.Empty(macro.LocalVariables);
        Assert.NotNull(window.LastRefusal);
        Assert.Contains("macro", window.LastRefusal!, StringComparison.OrdinalIgnoreCase);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 Rename / delete / duplicate route to the source
    // ────────────────────────────────────────────────────────────────────────

    private static (BlueprintMyBlueprintWindow Window, EditorCommandsImpl Commands)
        WiredWindow(BlueprintAsset asset, Func<Guid> current)
    {
        var window   = new BlueprintMyBlueprintWindow();
        var commands = new EditorCommandsImpl();
        window.Retarget(null, asset, null, commands, null, current);
        return (window, commands);
    }

    private static EditorCommandContext Item(string itemId, string? newName = null)
    {
        var args = new Dictionary<string, object?> { ["itemId"] = itemId };
        if (newName is not null) args["newName"] = newName;
        return new EditorCommandContext(null, null, args);
    }

    /// <summary>Rename through the panel's command reaches the source, which renames by id.</summary>
    [Fact]
    public void RenameRoutesToTheLocalsSource()
    {
        var g = NewGraph("Tick");
        var decl = Decl("Scratch");
        g.LocalVariables.Add(decl);

        var (_, commands) = WiredWindow(Asset(g), () => g.Id);
        commands.Invoke("editor.rename-item", Item($"local:{decl.Id}", "Carry"));

        Assert.Equal("Carry", g.LocalVariables.Single().Name);
    }

    /// <summary>
    /// ⭐⭐ <b>Delete refuses while referenced — through the panel.</b> Batch 42 built the refusal; this
    /// is the assertion that the panel's Delete actually reaches it rather than the asset-variable
    /// path, which deletes and leaves the references dangling on purpose.
    /// </summary>
    [Fact]
    public void DeleteOfAReferencedLocalIsRefused_ThroughTheCommand()
    {
        var g = NewGraph("Tick");
        var decl = Decl("Scratch");
        g.LocalVariables.Add(decl);
        AddGet(g, decl);

        var (window, commands) = WiredWindow(Asset(g), () => g.Id);
        commands.Invoke("editor.delete-item", Item($"local:{decl.Id}"));

        Assert.Single(g.LocalVariables);                       // ⛔ nothing dropped
        Assert.NotNull(window.LastRefusal);
        Assert.Contains("1", window.LastRefusal!);             // the count
        Assert.Contains("Tick", window.LastRefusal!);          // and where
    }

    /// <summary>An unreferenced local deletes through the same command.</summary>
    [Fact]
    public void DeleteOfAnUnreferencedLocalSucceeds()
    {
        var g = NewGraph("Tick");
        var decl = Decl("Scratch");
        g.LocalVariables.Add(decl);

        var (_, commands) = WiredWindow(Asset(g), () => g.Id);
        commands.Invoke("editor.delete-item", Item($"local:{decl.Id}"));

        Assert.Empty(g.LocalVariables);
    }

    /// <summary>
    /// ⭐ <b>Duplicate is not a silent no-op.</b> <c>MyBlueprintContextMenu</c> offers "Duplicate" for
    /// every <c>IsRenamable</c> item, so without a locals arm the entry would appear and do nothing —
    /// trap #5, the shape <c>BP-12b</c> was filed for.
    /// </summary>
    [Fact]
    public void DuplicateAppendsACopyUnderAFreeName()
    {
        var g = NewGraph("Tick");
        var decl = Decl("Scratch");
        decl.Tooltip = "carries the hit count";
        g.LocalVariables.Add(decl);

        var (_, commands) = WiredWindow(Asset(g), () => g.Id);
        commands.Invoke("editor.duplicate-item", Item($"local:{decl.Id}"));

        Assert.Equal(new[] { "Scratch", "Scratch1" }, g.LocalVariables.Select(v => v.Name).ToArray());
        var copy = g.LocalVariables[1];
        Assert.NotEqual(decl.Id, copy.Id);                     // a copy, not an alias
        Assert.Equal("carries the hit count", copy.Tooltip);
        Assert.Equal(decl.Type.TypeId, copy.Type.TypeId);
    }

    /// <summary>
    /// ⚠ An asset variable and a local of the SAME name must not cross wires: the two id prefixes are
    /// what keeps the gestures apart, and <c>Q27-C1</c> makes that pairing legal on purpose.
    /// </summary>
    [Fact]
    public void AnAssetVariableAndALocalOfTheSameNameAreDeletedIndependently()
    {
        var g = NewGraph("Tick");
        var local = Decl("Scratch");
        g.LocalVariables.Add(local);
        var asset = Asset(g);
        var shared = Decl("Scratch");
        asset.Variables.Add(shared);

        var (_, commands) = WiredWindow(asset, () => g.Id);
        commands.Invoke("editor.delete-item", Item($"local:{local.Id}"));

        Assert.Empty(g.LocalVariables);
        Assert.Single(asset.Variables);                        // ⭐ the shadowed one survives
    }
}
