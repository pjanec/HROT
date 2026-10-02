using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐ S7a (<c>DESIGN_Unified_Behaviour_Run</c> "S7 design" D8) — the Behaviour Task's Details editor: a picker over the
/// host's REGISTERED behaviour names, and the registry forwards those names to the drawer it builds (the silent-default
/// rule: a host that has a behaviour registry must reach the picker with it).
/// </summary>
public sealed class BehaviorTaskNodeDrawerTests
{
    private sealed class RecordingEditService : IEditService
    {
        public int Edits;
        public Action? LastUndo;
        public void MarkDirty(BlueprintAsset asset) { }
        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
        { apply(); Edits++; LastUndo = undo; }
        public void NotifyStructureChanged(BlueprintAsset asset) { }
    }

    private sealed class NullPredicateCompiler : Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler
    {
        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileComponentPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto root) => (_, _) => false;
        public IReadOnlyList<Type> ExtractMandatoryComponents(Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto root) => [];
    }

    private static readonly string[] Names = { "Patrol", "Guard", "PatrolFast" };
    private static BlueprintAsset Asset() => new() { AssetId = Guid.NewGuid(), Name = "Host", Dispatch = BlueprintDispatchKind.Behavior };

    private static BehaviorTaskNodeSession Session(RunBehaviorNode node, IEditService edits, Func<IReadOnlyList<string>>? names = null)
        => (BehaviorTaskNodeSession)new BehaviorTaskNodeDrawer(edits, names ?? (() => Names)).CreateSession(node, Asset());

    [Fact]
    public void TheDrawer_HandlesABehaviourTask_Only()
    {
        var drawer = new BehaviorTaskNodeDrawer(new RecordingEditService());
        Assert.True(drawer.Handles(new RunBehaviorNode()));
        Assert.False(drawer.Handles(new BranchNode()));
    }

    /// <summary>⭐ The picker lists the registered names, sorted, filtered by the shared picker's matcher.</summary>
    [Fact]
    public void ThePicker_ListsTheRegisteredBehaviours_SortedAndFiltered()
    {
        var s = Session(new RunBehaviorNode(), new RecordingEditService());
        Assert.Equal(new[] { "Guard", "Patrol", "PatrolFast" }, s.GetFilteredBehavioursForTest(""));
        Assert.Equal(new[] { "Patrol", "PatrolFast" }, s.GetFilteredBehavioursForTest("patrol"));
    }

    /// <summary>⭐ Picking is ONE undoable edit; undo restores the previous name.</summary>
    [Fact]
    public void Picking_IsOneUndoableEdit()
    {
        var node = new RunBehaviorNode { BehaviorName = "Guard" };
        var edits = new RecordingEditService();
        var s = Session(node, edits);
        s.SetBehaviourForTest("Patrol");
        Assert.Equal("Patrol", node.BehaviorName);
        Assert.True(s.IsDirty);
        Assert.Equal(1, edits.Edits);
        edits.LastUndo!();
        Assert.Equal("Guard", node.BehaviorName);
        s.SetBehaviourForTest("Guard");                 // the same name is not an edit
        Assert.Equal(1, edits.Edits);
    }

    /// <summary>⚠ A name the host does not list now is KEPT and flagged (a child may register later), never dropped.</summary>
    [Fact]
    public void AnUnlistedName_IsKeptAndFlagged()
    {
        var node = new RunBehaviorNode { BehaviorName = "LoadedLater" };
        var s = Session(node, new RecordingEditService());
        Assert.True(s.IsCurrentUnlistedForTest());
        Assert.Equal("LoadedLater", node.BehaviorName);
        Assert.False(Session(new RunBehaviorNode { BehaviorName = "Guard" }, new RecordingEditService()).IsCurrentUnlistedForTest());
    }

    /// <summary>
    /// ⭐⭐ The forwarding rail (silent-default rule): <c>CreateNodeDrawerRegistry(behaviourNames:)</c> reaches the
    /// CONSTRUCTED drawer — asserted on the drawer the registry hands out, not on the bootstrap's source.
    /// <para>✅ Red-proof: drop <c>behaviourNames</c> from the registration and the picker lists nothing.</para>
    /// </summary>
    [Fact]
    public void TheRegistry_ForwardsTheBehaviourNames_ToTheDrawerItBuilds()
    {
        var registry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            BuiltInChannelCommandCatalog.Instance, BuiltInEngineEventCatalog.Instance, new RecordingEditService(),
            new NullPredicateCompiler(), new EqsTemplateRegistry(), behaviourNames: () => Names);
        var drawer = registry.GetDrawerFor(new RunBehaviorNode());
        Assert.IsType<BehaviorTaskNodeDrawer>(drawer);
        var s = (BehaviorTaskNodeSession)drawer!.CreateSession(new RunBehaviorNode(), Asset());
        Assert.Equal(3, s.GetFilteredBehavioursForTest("").Count);
    }
}
