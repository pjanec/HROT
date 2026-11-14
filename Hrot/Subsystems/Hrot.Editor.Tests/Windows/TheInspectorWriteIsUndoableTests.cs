using System;
using Fdp.Core;
using Fdp.Presentation.Adapters;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Hrot.Presentation.Edits;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-3386 (docs/DESIGN_Struct_Editor.md §10.4) — the raw inspector's OK wrote with <c>SetComponent</c>: no undo. The inspector's commit (<see cref="RepositoryAdapter.SetComponentAsUserStep"/>) is now bracketed as ONE step of the editor's history where one is
/// recorded: in Edit mode edit a field → <c>EditHistory.Undo</c> → the old value (unmanaged AND managed component); in Live / Preview / replay (the recorder is off) the same write leaves NO history entry.
/// 🔴 Red-proof (compile): <c>SetComponentAsUserStep</c> without the <c>EditSteps</c> bracket ⇒ <c>CanUndo</c> false ⇒ red.
/// </summary>
public sealed class TheInspectorWriteIsUndoableTests
{
    private static (EntityRepository Repo, Entity E, RepositoryAdapter Session, EditHistory History) Fixture(Func<bool> editMode)
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<EntityInfo>();
        repo.RegisterManagedComponent<MacroInstance>();
        var e = repo.CreateEntity();
        repo.AddComponent(e, new EntityInfo { Name = new FixedString64("Alpha") });
        repo.SetManagedComponent(e, new MacroInstance { Macro = "m", ArgsJson = "{\"a\":1}" });
        var history = new EditHistory();
        EditSteps.Install(repo, new WorldUndoRecorder(history, new WorldSnapshotRing(maxDepth: 8), editMode));
        return (repo, e, new RepositoryAdapter(repo), history);
    }

    [Fact]
    public void InEditMode_AnUnmanagedWrite_IsOneStep_UndoBringsTheOldValueBack()
    {
        var (repo, e, session, history) = Fixture(() => true);
        using var _r = repo;
        session.SetComponentAsUserStep(e, typeof(EntityInfo), new EntityInfo { Name = new FixedString64("Bravo") }, "Edit EntityInfo");
        Assert.Equal("Bravo", repo.GetComponentRO<EntityInfo>(e).Name.ToString());
        Assert.Equal(1, history.Count);
        Assert.True(history.CanUndo);
        history.Undo();
        Assert.Equal("Alpha", repo.GetComponentRO<EntityInfo>(e).Name.ToString());
        history.Redo();
        Assert.Equal("Bravo", repo.GetComponentRO<EntityInfo>(e).Name.ToString());
    }

    [Fact]
    public void InEditMode_AManagedWrite_IsOneStep_UndoBringsTheOldValueBack()
    {
        var (repo, e, session, history) = Fixture(() => true);
        using var _r = repo;
        session.SetComponentAsUserStep(e, typeof(MacroInstance), new MacroInstance { Macro = "m", ArgsJson = "{\"a\":2}" }, "Edit MacroInstance");
        Assert.Equal(1, history.Count);
        history.Undo();
        Assert.Equal("{\"a\":1}", ((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetManagedComponentRO<MacroInstance>(e).ArgsJson);
    }

    [Fact]
    public void NotInEditMode_TheSameWrite_LeavesNoHistoryEntry()
    {
        var (repo, e, session, history) = Fixture(() => false);   // Live / Preview / replay: the recorder is off
        using var _r = repo;
        session.SetComponentAsUserStep(e, typeof(EntityInfo), new EntityInfo { Name = new FixedString64("Bravo") }, "Edit EntityInfo");
        Assert.Equal("Bravo", repo.GetComponentRO<EntityInfo>(e).Name.ToString());
        Assert.Equal(0, history.Count);
        Assert.False(history.CanUndo);
    }

    /// <summary>⭐ CE-3386 — the hazard the N-10 review found: a PLAIN write (no user step) records nothing, and a write made INSIDE another history step's apply / undo / redo
    /// (a parameter rename rewriting its bindings through the same adapter) is part of THAT step — one entry, and Undo / Redo keep the list intact. Red-proof: drop the
    /// <c>_history.Applying</c> test in <c>WorldUndoRecorder.Begin</c> ⇒ Count 2 after the Do, and the Undo cuts the redo tail ⇒ red.</summary>
    [Fact]
    public void AWriteInsideAnotherSteps_ApplyOrUndo_IsPartOfThatStep_TheHistoryStaysIntact()
    {
        var (repo, e, session, history) = Fixture(() => true);
        using var _r = repo;
        session.SetComponent(e, typeof(EntityInfo), new EntityInfo { Name = new FixedString64("Plain") });
        Assert.Equal(0, history.Count);                                         // a plain write is not a user step
        // the inner writes are BRACKETED (as an Edits.Submit or a user-step write would be) — the guard is what keeps them inside the outer step
        history.Do("Rename",
            () => session.SetComponentAsUserStep(e, typeof(EntityInfo), new EntityInfo { Name = new FixedString64("Bravo") }, "inner"),
            () => session.SetComponentAsUserStep(e, typeof(EntityInfo), new EntityInfo { Name = new FixedString64("Plain") }, "inner"));
        Assert.Equal(1, history.Count);
        history.Undo();
        Assert.Equal("Plain", repo.GetComponentRO<EntityInfo>(e).Name.ToString());
        Assert.Equal(1, history.Count);
        Assert.True(history.CanRedo);
        history.Redo();
        Assert.Equal("Bravo", repo.GetComponentRO<EntityInfo>(e).Name.ToString());
    }
}
