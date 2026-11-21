using System;
using System.IO;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Presentation.Adapters;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.Scenario;
using Hrot.Presentation.Edits;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-3384 (c) (docs/DESIGN_Struct_Editor.md §10.4) — a macro instance's args were written with <c>SetComponent</c>: no undo. Now each write is bracketed as ONE step of the editor's history
/// (<see cref="EditSteps"/>, as every <c>Edits.Submit</c> is): edit an arg → <c>EditHistory.Undo</c> → the old value; Redo → the new. A node that does not own the instance refuses and records nothing.
/// 🔴 Red-proof (compile): <c>MacroArgs.Write</c> without the bracket ⇒ <c>CanUndo</c> false ⇒ red.
/// </summary>
public sealed class TheMacroArgsAreUndoableTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "macro-undo-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private (EntityRepository Repo, Entity E, MacroInstanceDetailsView View, EditHistory History) Fixture(bool owned)
    {
        var repo = new EntityRepository();
        repo.RegisterManagedComponent<MacroInstance>();
        var e = repo.CreateEntity();
        repo.SetManagedComponent(e, new MacroInstance { Macro = "test/fire-team", ArgsJson = "{\"speed\":2.5}" });
        if (owned) repo.SetAuthority(e, ComponentTypeRegistry.GetId(typeof(MacroInstance)), true);
        var history = new EditHistory();
        EditSteps.Install(repo, new WorldUndoRecorder(history, new WorldSnapshotRing(maxDepth: 8), () => true));
        var view = new MacroInstanceDetailsView(() => repo, () => new RepositoryAdapter(repo), () => _root,
            () => Hrot.Editor.AiShared.Inspector.ActionBinding.AiFacetEditService.Build());
        return (repo, e, view, history);
    }

    private static string Args(EntityRepository repo, Entity e) => ((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetManagedComponentRO<MacroInstance>(e).ArgsJson;

    [Fact]
    public void AnArgEdit_IsOneUndoStep_UndoBringsTheOldValueBack_RedoTheNew()
    {
        var (repo, e, view, history) = Fixture(owned: true);
        using var _r = repo;
        Assert.Equal(MacroArgs.WriteResult.Written, view.SetArg(e, "speed", "7"));
        Assert.Equal("{\"speed\":7}", Args(repo, e));
        Assert.True(history.CanUndo);
        Assert.Equal("Edit macro argument 'speed'", history.UndoLabel);
        history.Undo();
        Assert.Equal("{\"speed\":2.5}", Args(repo, e));              // the old value
        Assert.True(history.CanRedo);
        history.Redo();
        Assert.Equal("{\"speed\":7}", Args(repo, e));
    }

    [Fact]
    public void ANodeThatDoesNotOwnTheInstance_RefusesAndRecordsNothing()
    {
        var (repo, e, view, history) = Fixture(owned: false);
        using var _r = repo;
        Assert.Equal(MacroArgs.WriteResult.NotOwned, view.SetArg(e, "speed", "7"));
        Assert.Equal("{\"speed\":2.5}", Args(repo, e));
        Assert.False(history.CanUndo);
    }
}
