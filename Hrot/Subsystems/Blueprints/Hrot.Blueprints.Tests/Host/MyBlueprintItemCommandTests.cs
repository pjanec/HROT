using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-12b — My Blueprint items could not be renamed, duplicated or deleted.
///
/// <para>
/// <c>MyBlueprintContextMenu</c> has always invoked <c>editor.rename-item</c>,
/// <c>editor.duplicate-item</c> and <c>editor.delete-item</c>; nothing ever registered them. The
/// visible consequence: a variable could be <b>created but never renamed or removed</b>.
/// </para>
/// </summary>
public sealed class MyBlueprintItemCommandTests
{
    private sealed record Sut(EditorCommandsImpl Commands, GraphView View, BlueprintAsset Asset);

    private static Sut MakeSut(Action<BlueprintAsset>? configure = null)
    {
        var asset = BlueprintAssetBuilder.Instance("ItemCmdAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();
        configure?.Invoke(asset);

        var graph      = asset.Graphs[0];
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(new NodeKindRegistry()) { Asset = asset };
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };

        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, markDirty: _ => { });

        var host = new StubHostServices(catalog, typeSystem, validator, sink);
        var view = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);

        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterMyBlueprintItemCommands(commands, asset, view);

        return new Sut(commands, view, asset);
    }

    private static EditorCommandContext Ctx(string itemId, string? newName = null)
        => new(ScreenPos: null, CanvasPos: null,
               Args: newName is null
                   ? new Dictionary<string, object?> { ["itemId"] = itemId }
                   : new Dictionary<string, object?> { ["itemId"] = itemId, ["newName"] = newName });

    private static string VarId(VariableDecl decl) => $"var:{decl.Id:D}";
    private static string EvtId(CustomEventDecl decl) => $"evt:{decl.Id:D}";

    // ── Registration (the bug itself) ─────────────────────────────────────────

    [Theory]
    [InlineData("editor.rename-item")]
    [InlineData("editor.delete-item")]
    [InlineData("editor.duplicate-item")]
    public void EachItemCommand_IsRegistered(string commandId)
    {
        Assert.NotNull(MakeSut().Commands.Get(commandId));
    }

    // ── Variables ─────────────────────────────────────────────────────────────

    [Fact]
    public void AVariable_CanBeRenamed()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;

        sut.Commands.Invoke("editor.rename-item", Ctx(VarId(decl), "Hitpoints"));

        Assert.Equal("Hitpoints", decl.Name);
    }

    /// <summary>The gap the tracker names: created, then never removable.</summary>
    [Fact]
    public void AVariable_CanBeDeleted()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;

        sut.Commands.Invoke("editor.delete-item", Ctx(VarId(decl)));

        Assert.Empty(sut.Asset.Variables);
    }

    [Fact]
    public void AVariable_CanBeDuplicated_UnderAFreeName_KeepingItsType()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(
            sut.Asset, "Health", "System.Int32", capacity: 4, initialLength: 2)!;
        decl.Category = "Combat";

        sut.Commands.Invoke("editor.duplicate-item", Ctx(VarId(decl)));

        Assert.Equal(2, sut.Asset.Variables.Count);
        var copy = sut.Asset.Variables[1];
        Assert.Equal("Health1", copy.Name);
        Assert.Equal("System.Int32", copy.Type.TypeId);
        Assert.Equal(4, copy.Type.Capacity);
        Assert.Equal(2, copy.Type.InitialLength);
        Assert.Equal("Combat", copy.Category);
        Assert.NotEqual(decl.Id, copy.Id);
    }

    /// <summary>
    /// Nodes bind to a variable by GUID, so a rename must not disturb them — that is the whole
    /// reason ids are stored rather than names.
    /// </summary>
    [Fact]
    public void RenamingAVariable_LeavesNodesBound()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;
        var node = new GetVariableNode { Id = Guid.NewGuid(), VariableId = $"var:{decl.Id:D}" };
        sut.Asset.Graphs[0].Nodes.Add(node);

        sut.Commands.Invoke("editor.rename-item", Ctx(VarId(decl), "Hitpoints"));

        Assert.Equal($"var:{decl.Id:D}", node.VariableId);
    }

    // ══ Inputs and Variables — the C-sections (Batch 81) ═════════════════════
    //
    // 🔴 <b>What the user's first visual check found</b>, verbatim: <i>"variable record in Working
    // State shows just 'Delete' (does nothing) and 'Rename' (shows rename dialog but does not cause
    // name to change) items in the context menu."</i>
    //
    // 📐 BuildDeclarationItems emitted the same "var:" prefix for EVERY kind, while FindVariable
    // resolved only DeclarationKind.Variable — so the id parsed, the lookup returned null, and every
    // command fell through to `return false`. ⛔ Duplicate was broken the same way and untested.
    //
    // ⭐ One test per KIND per GESTURE, not one loop: a loop that happened to run Variable first
    //   would have passed on the kind that already worked.
    //
    // ⭐⭐ <b>Batch 86 — RESTATED, not deleted.</b> The theories asserted (Parameter, WorkingState);
    //   R-01 collapses WorkingState into Variable, so they now assert (Parameter, Variable) — which is
    //   still EVERY kind, which is what "OfAnyKind" has always meant. ⛔ Dropping the second InlineData
    //   would have shrunk the gate to one kind and hidden exactly the fall-through it was written for.

    private static string DeclId(BlueprintDeclaration decl) => $"var:{decl.Id:D}";

    /// <summary>⭐ The kind this one is NOT — so "lands in its OWN list" stays checkable with two kinds.</summary>
    private static DeclarationKind OtherKind(DeclarationKind kind)
        => kind == DeclarationKind.Parameter ? DeclarationKind.Variable : DeclarationKind.Parameter;

    private static BlueprintDeclaration Declare(
        BlueprintAsset asset, DeclarationKind kind, string name)
        => BlueprintDocumentFactory.CreateDeclaration(asset, kind, name, "System.Int32")!;

    /// <summary>🔴 RED before Batch 81 — the rename dialog opened and changed nothing.</summary>
    [Theory]
    [InlineData(DeclarationKind.Parameter)]
    [InlineData(DeclarationKind.Variable)]   // Batch 86 — RESTATED (was WorkingState; R-01)
    public void ADeclarationOfAnyKind_CanBeRenamed(DeclarationKind kind)
    {
        var sut  = MakeSut();
        var decl = Declare(sut.Asset, kind, "Health");

        sut.Commands.Invoke("editor.rename-item", Ctx(DeclId(decl), "Hitpoints"));

        Assert.Equal("Hitpoints", sut.Asset.Declarations.Single(d => d.Kind == kind).Name);
    }

    /// <summary>
    /// ⚠ The dialog opened EMPTY because ItemDisplayName resolved to null — which is what made the
    /// rename look like it had "no current name" before it silently failed.
    /// </summary>
    [Theory]
    [InlineData(DeclarationKind.Parameter)]
    [InlineData(DeclarationKind.Variable)]   // Batch 86 — RESTATED (was WorkingState; R-01)
    public void ADeclarationOfAnyKind_ReportsItsDisplayName(DeclarationKind kind)
    {
        var asset = MakeSut().Asset;
        var decl  = Declare(asset, kind, "Health");

        Assert.Equal("Health", BlueprintDocumentFactory.ItemDisplayName(asset, DeclId(decl)));
    }

    /// <summary>🔴 RED before Batch 81 — "Delete (does nothing)", verbatim.</summary>
    [Theory]
    [InlineData(DeclarationKind.Parameter)]
    [InlineData(DeclarationKind.Variable)]   // Batch 86 — RESTATED (was WorkingState; R-01)
    public void ADeclarationOfAnyKind_CanBeDeleted(DeclarationKind kind)
    {
        var sut  = MakeSut();
        var decl = Declare(sut.Asset, kind, "Health");
        Assert.Equal(1, sut.Asset.Declarations.CountIn(kind));

        sut.Commands.Invoke("editor.delete-item", Ctx(DeclId(decl)));

        Assert.Equal(0, sut.Asset.Declarations.CountIn(kind));
    }

    /// <summary>
    /// 🔴 RED before Batch 81, and ⚠ <b>the user never tested this one</b> — it was broken by the
    /// same fall-through. ⭐ The copy must land in the SAME kind's list, not in Variables.
    /// </summary>
    [Theory]
    [InlineData(DeclarationKind.Parameter)]
    [InlineData(DeclarationKind.Variable)]   // Batch 86 — RESTATED (was WorkingState; R-01)
    public void ADeclarationOfAnyKind_CanBeDuplicated_IntoItsOwnList(DeclarationKind kind)
    {
        var sut  = MakeSut();
        var decl = Declare(sut.Asset, kind, "Health");

        sut.Commands.Invoke("editor.duplicate-item", Ctx(DeclId(decl)));

        Assert.Equal(2, sut.Asset.Declarations.CountIn(kind));
        // ⭐ Batch 86 — RESTATED. This asserted `CountIn(Variable) == 0`, which read as "did not leak
        //   into Variables". With R-01's two kinds, Variable IS one of the cases, so the claim becomes
        //   the OTHER kind's count — same assertion, now true for every case rather than for one.
        Assert.Equal(0, sut.Asset.Declarations.CountIn(OtherKind(kind)));
        var copy = sut.Asset.Declarations.Of(kind).Last();
        Assert.Equal("Health1",      copy.Name);
        Assert.Equal("System.Int32", copy.Type.TypeId);
        Assert.NotEqual(decl.Id,     copy.Id);
    }

    /// <summary>
    /// ⭐ A Parameter is backed by <c>ParameterDecl</c>, which carries no Category / IsEditable /
    /// IsExposedOnSpawn. ⛔ Duplicating one must ASK the capability rather than write and throw.
    /// </summary>
    [Fact]
    public void DuplicatingAParameter_DoesNotAttemptTheMembersItDoesNotCarry()
    {
        var sut  = MakeSut();
        var decl = Declare(sut.Asset, DeclarationKind.Parameter, "Health");
        Assert.False(decl.CarriesEditorPresentation);

        sut.Commands.Invoke("editor.duplicate-item", Ctx(DeclId(decl)));   // ⛔ must not throw

        Assert.Equal(2, sut.Asset.Declarations.CountIn(DeclarationKind.Parameter));
    }

    /// <summary>
    /// ⭐ And a state declaration DOES carry them, so the copy keeps its category.
    /// ⭐ <b>Batch 86 — RESTATED</b> from <c>WorkingState</c> to <c>Variable</c> *(<c>R-01</c>)*; the
    /// claim — "the kind that carries editor presentation carries it through a duplicate" — is the
    /// paired opposite of the Parameter fact above and is untouched.
    /// </summary>
    [Fact]
    public void DuplicatingAStateVariable_CarriesItsPresentation()
    {
        var sut  = MakeSut();
        var decl = Declare(sut.Asset, DeclarationKind.Variable, "Cursor");
        decl.Category = "Runtime";

        sut.Commands.Invoke("editor.duplicate-item", Ctx(DeclId(decl)));

        Assert.Equal("Runtime",
            sut.Asset.Declarations.Of(DeclarationKind.Variable).Last().Category);
    }

    /// <summary>
    /// ⭐⭐ The uniqueness rule spans all kinds (U-14), so a rename may not collide with another
    /// declaration — ⛔ Stage5's name fallback would otherwise let list order pick the target.
    ///
    /// <para>⚠ <b>Batch 86 — RESTATED, and the COLLIDING PARTNER had to move.</b> This renamed a
    /// <c>WorkingState</c> onto a <c>Variable</c> — two kinds. With one state kind that would be a
    /// within-kind collision, which <c>CrossKindUniquenessTests</c> already owns. ⭐ So the partner is
    /// now a <b>Parameter</b>: the claim is still "a rename may not take a name another KIND holds",
    /// asserted over the only cross-kind pair that exists.</para>
    /// </summary>
    [Fact]
    public void RenamingAStateVariable_CannotCollideWithAParameter()
    {
        var sut  = MakeSut();
        Declare(sut.Asset, DeclarationKind.Parameter, "Health");
        var state = Declare(sut.Asset, DeclarationKind.Variable, "Cursor");

        sut.Commands.Invoke("editor.rename-item", Ctx(DeclId(state), "Health"));

        Assert.Equal("Cursor", state.Name);
    }

    // ── Custom events ─────────────────────────────────────────────────────────

    [Fact]
    public void ACustomEvent_CanBeRenamedDuplicatedAndDeleted()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(
            sut.Asset, "OnHit", new[] { ("Damage", "System.Single") })!;

        sut.Commands.Invoke("editor.rename-item", Ctx(EvtId(decl), "OnStruck"));
        Assert.Equal("OnStruck", decl.Name);

        sut.Commands.Invoke("editor.duplicate-item", Ctx(EvtId(decl)));
        Assert.Equal(2, sut.Asset.CustomEvents.Count);
        Assert.Equal("OnStruck1", sut.Asset.CustomEvents[1].Name);
        Assert.Equal("Damage", sut.Asset.CustomEvents[1].Parameters.Single().Name);

        sut.Commands.Invoke("editor.delete-item", Ctx(EvtId(decl)));
        Assert.Single(sut.Asset.CustomEvents);
    }

    /// <summary>
    /// The compiler emits <c>Event_{Name}</c> from the <b>graph</b>, so renaming the declaration
    /// alone would break the pairing and produce a BP1407 on the next compile.
    /// </summary>
    [Fact]
    public void RenamingACustomEvent_RenamesItsHandlerGraph()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(sut.Asset, "OnHit")!;
        sut.Asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "OnHit", Kind = GraphKind.Event });

        sut.Commands.Invoke("editor.rename-item", Ctx(EvtId(decl), "OnStruck"));

        Assert.Contains(sut.Asset.Graphs, g => g.Kind == GraphKind.Event && g.Name == "OnStruck");
    }

    /// <summary>
    /// The editor writes GUIDs, which survive a rename untouched — but Stage5 accepts a bare name
    /// and hand-authored assets use one. Leaving those behind would turn a rename into a silent
    /// BP1403.
    /// </summary>
    [Fact]
    public void RenamingACustomEvent_RewritesNameKeyedCalls_AndLeavesGuidCallsAlone()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(sut.Asset, "OnHit")!;

        var byName = new CallCustomEventNode { Id = Guid.NewGuid(), EventId = "OnHit" };
        var byGuid = new CallCustomEventNode { Id = Guid.NewGuid(), EventId = decl.Id.ToString("D") };
        sut.Asset.Graphs[0].Nodes.Add(byName);
        sut.Asset.Graphs[0].Nodes.Add(byGuid);

        sut.Commands.Invoke("editor.rename-item", Ctx(EvtId(decl), "OnStruck"));

        Assert.Equal("OnStruck", byName.EventId);
        Assert.Equal(decl.Id.ToString("D"), byGuid.EventId);
    }

    /// <summary>A custom event's name is emitted verbatim as <c>Event_{Name}</c>.</summary>
    [Theory]
    [InlineData("On Struck")]
    [InlineData("1st")]
    [InlineData("class")]
    public void ACustomEvent_CannotBeRenamedToANonIdentifier(string newName)
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(sut.Asset, "OnHit")!;

        sut.Commands.Invoke("editor.rename-item", Ctx(EvtId(decl), newName));

        Assert.Equal("OnHit", decl.Name);
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankRename_ChangesNothing(string newName)
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;

        sut.Commands.Invoke("editor.rename-item", Ctx(VarId(decl), newName));

        Assert.Equal("Health", decl.Name);
        Assert.Equal(0, sut.View.Undo.UndoCount);
    }

    [Fact]
    public void RenamingToATakenName_ChangesNothing()
    {
        var sut = MakeSut();
        var a   = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;
        BlueprintDocumentFactory.CreateVariable(sut.Asset, "Armour", "System.Int32");

        sut.Commands.Invoke("editor.rename-item", Ctx(VarId(a), "armour"));

        Assert.Equal("Health", a.Name);
    }

    [Theory]
    [InlineData("editor.rename-item")]
    [InlineData("editor.delete-item")]
    [InlineData("editor.duplicate-item")]
    public void AnUnknownItemId_ChangesNothing(string commandId)
    {
        var sut = MakeSut();
        BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32");

        sut.Commands.Invoke(commandId, Ctx($"var:{Guid.NewGuid():D}", "Whatever"));

        Assert.Single(sut.Asset.Variables);
        Assert.Equal("Health", sut.Asset.Variables[0].Name);
        Assert.Equal(0, sut.View.Undo.UndoCount);
    }

    /// <summary>
    /// The prefix is what distinguishes a variable id from an event id; a variable id must never
    /// resolve against the custom-event list.
    /// </summary>
    [Fact]
    public void ItemIdPrefixes_AreNotInterchangeable()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(sut.Asset, "OnHit")!;

        sut.Commands.Invoke("editor.delete-item", Ctx($"var:{decl.Id:D}"));

        Assert.Single(sut.Asset.CustomEvents);
    }

    /// <summary>
    /// Deleting a declaration leaves the nodes that referenced it alone. They render as dangling
    /// and the compiler names them, which is recoverable; silently deleting a designer's wired-up
    /// nodes is not.
    /// </summary>
    [Fact]
    public void DeletingAVariable_LeavesItsNodesInPlace()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;
        sut.Asset.Graphs[0].Nodes.Add(
            new GetVariableNode { Id = Guid.NewGuid(), VariableId = $"var:{decl.Id:D}" });

        sut.Commands.Invoke("editor.delete-item", Ctx(VarId(decl)));

        Assert.Single(sut.Asset.Graphs[0].Nodes);
    }

    // ── Undo ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("editor.rename-item")]
    [InlineData("editor.delete-item")]
    [InlineData("editor.duplicate-item")]
    public void EveryItemEdit_IsUndoable(string commandId)
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;

        sut.Commands.Invoke(commandId, Ctx(VarId(decl), "Hitpoints"));
        Assert.Equal(1, sut.View.Undo.UndoCount);

        sut.View.UndoLast();

        var restored = Assert.Single(sut.Asset.Variables);
        Assert.Equal("Health", restored.Name);
    }

    [Fact]
    public void RedoingAnItemEdit_ReappliesItExactlyOnce()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;

        sut.Commands.Invoke("editor.duplicate-item", Ctx(VarId(decl)));
        sut.View.UndoLast();
        sut.View.RedoLast();

        // Not three: redo restores the recorded end state rather than repeating the duplication.
        Assert.Equal(2, sut.Asset.Variables.Count);
    }

    // ── Test host services ───────────────────────────────────────────────────

    private sealed class StubHostServices : IEditorHostServices
    {
        public StubHostServices(INodeCatalog catalog, ITypeSystem typeSystem,
            ILinkValidator validator, IGraphCommandSink sink)
        {
            NodeCatalog = catalog; TypeSystem = typeSystem; LinkValidator = validator; CommandSink = sink;
        }

        public INodeCatalog      NodeCatalog   { get; }
        public ITypeSystem       TypeSystem    { get; }
        public ILinkValidator    LinkValidator { get; }
        public IGraphCommandSink CommandSink   { get; }
        public IPickerRegistry   Pickers       => null!;
        public IClipboard        Clipboard     => null!;
        public IIconProvider     Icons         => null!;
        public IDiagnosticsSink? Diagnostics   => null;
        public IDebugSession?    Debug         => null;
        public IInputSource      Input         => null!;
        public IEditorTheme      Theme         => null!;
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }
}
