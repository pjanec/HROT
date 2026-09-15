using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor.AiShared.Blackboard;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-57 — <b>delete must not orphan its references, and every gesture must be undoable.</b>
///
/// <para>
/// ⛔ What these replace: a naive <c>RemoveAll</c> that dropped the declaration and left every
/// <c>Get</c>/<c>Set</c> pointing at nothing — <c>BP1670</c> then refuses the asset at Stage 2, so a
/// one-click gesture reliably made the blueprint uncompilable. And <b>no undo at all</b>: every
/// mutation called <c>_onChanged()</c> and recorded nothing.
/// </para>
/// </summary>
public sealed class LocalVariableDeleteAndUndoTests
{
    private static Pin P(string name, string dir, bool isExec, string typeId = "") => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = isExec,
        TypeRef = new BlueprintTypeRef { TypeId = typeId },
    };

    private static VariableDecl Decl(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name,
        Type = new BlueprintTypeRef { TypeId = "System.Int32" }, DefaultValueJson = "",
    };

    private static Graph NewGraph(string name, GraphKind kind = GraphKind.Function) => new()
    {
        Id = Guid.NewGuid(), Name = name, Kind = kind,
    };

    private static void AddGet(Graph g, string variableId)
    {
        var n = new GetVariableNode { Id = Guid.NewGuid(), VariableId = variableId };
        n.Pins.Add(P("Value", "Out", false, "System.Int32"));
        g.Nodes.Add(n);
    }

    private static BlueprintAsset Asset(params Graph[] graphs) => new()
    {
        AssetId = Guid.NewGuid(), Name = "DeleteHost",
        Dispatch = BlueprintDispatchKind.Instance,
        Graphs = graphs.ToList(), Header = new Header(),
    };

    /// <summary>A recorder that keeps the inverse, so a test can undo without a canvas.</summary>
    private sealed class FakeUndo
    {
        public readonly List<string> Labels = new();
        private readonly List<Action> _inverses = new();
        private readonly BlueprintAsset _asset;

        public FakeUndo(BlueprintAsset asset) => _asset = asset;

        public Action<string, Func<bool>> Recorder => (label, mutate) =>
        {
            var before = Snapshot();
            if (!mutate()) return;          // ⭐ no entry for a no-op gesture
            Labels.Add(label);
            _inverses.Add(() => Restore(before));
        };

        public void UndoLast()
        {
            if (_inverses.Count == 0) return;
            _inverses[^1]();
            _inverses.RemoveAt(_inverses.Count - 1);
            Labels.RemoveAt(Labels.Count - 1);
        }

        // Deep, because rename mutates the decl in place.
        private Dictionary<Guid, List<VariableDecl>> Snapshot()
            => _asset.Graphs.ToDictionary(
                g => g.Id,
                g => g.LocalVariables.Select(v => new VariableDecl
                {
                    Id = v.Id, Name = v.Name,
                    Type = new BlueprintTypeRef { TypeId = v.Type.TypeId },
                    DefaultValueJson = v.DefaultValueJson, Comment = v.Comment,
                }).ToList());

        private void Restore(Dictionary<Guid, List<VariableDecl>> snap)
        {
            foreach (var g in _asset.Graphs)
                if (snap.TryGetValue(g.Id, out var d))
                {
                    g.LocalVariables.Clear();
                    g.LocalVariables.AddRange(d);
                }
        }
    }

    private static (BlueprintLocalVariableSchemaSource Source, FakeUndo Undo, List<string> Refusals)
        Build(BlueprintAsset asset, Func<Graph?> current)
    {
        var undo     = new FakeUndo(asset);
        var refusals = new List<string>();
        var source   = new BlueprintLocalVariableSchemaSource(
            asset, current, () => { }, undo.Recorder, refusals.Add);
        return (source, undo, refusals);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 Delete
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The defect, in one assertion.</b> Deleting a referenced local used to succeed and leave
    /// the references dangling — the asset then failed to compile with <c>BP1670</c>. It now refuses.
    /// </summary>
    [Fact]
    public void DeletingAReferencedLocalIsRefused_AndTheDeclarationSurvives()
    {
        var g = NewGraph("Tick");
        var local = Decl("Scratch");
        g.LocalVariables.Add(local);
        AddGet(g, local.Id.ToString());

        var (source, undo, refusals) = Build(Asset(g), () => g);
        source.RemoveVariable("Scratch");

        Assert.Single(g.LocalVariables);                 // ⛔ nothing was dropped
        Assert.Single(refusals);
        Assert.Empty(undo.Labels);                       // ⭐ a refusal is not an undo entry
    }

    /// <summary>
    /// ⭐ <b>The refusal names the count and the graph.</b> Silence was never an option (Q26-B2), and a
    /// bare "cannot delete" would leave the designer hunting.
    /// </summary>
    [Fact]
    public void TheRefusalNamesTheCountAndWhere()
    {
        var owner = NewGraph("Tick");
        var other = NewGraph("Helper");
        var local = Decl("Scratch");
        owner.LocalVariables.Add(local);
        AddGet(owner, local.Id.ToString());
        AddGet(other, local.Id.ToString());              // ⭐ the cross-graph reference

        var (source, _, refusals) = Build(Asset(owner, other), () => owner);
        source.RemoveVariable("Scratch");

        var msg = Assert.Single(refusals);
        Assert.Contains("2", msg);
        Assert.Contains("Tick", msg);
        Assert.Contains("Helper", msg);                  // ⭐ the graph they cannot see from here
    }

    /// <summary>An unreferenced local deletes cleanly, and that is one undo entry.</summary>
    [Fact]
    public void AnUnreferencedLocalDeletesAndIsUndoable()
    {
        var g = NewGraph("Tick");
        g.LocalVariables.Add(Decl("Scratch"));

        var (source, undo, refusals) = Build(Asset(g), () => g);
        source.RemoveVariable("Scratch");

        Assert.Empty(g.LocalVariables);
        Assert.Empty(refusals);
        Assert.Single(undo.Labels);

        undo.UndoLast();
        Assert.Equal("Scratch", g.LocalVariables.Single().Name);
    }

    /// <summary>
    /// ⚠ <b>Gathered before mutating.</b> A batch where one entry is referenced must not half-delete —
    /// a partial delete followed by a refusal is the worst of both.
    /// </summary>
    [Fact]
    public void ABatchWithOneReferencedEntryDeletesNothing()
    {
        var g = NewGraph("Tick");
        var free = Decl("Free");
        var used = Decl("Used");
        g.LocalVariables.AddRange(new[] { free, used });
        AddGet(g, used.Id.ToString());

        var (source, undo, refusals) = Build(Asset(g), () => g);
        source.RemoveVariables(new[] { "Free", "Used" });

        Assert.Equal(2, g.LocalVariables.Count);
        Assert.Single(refusals);
        Assert.Empty(undo.Labels);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 Undo — every gesture
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ Add is one entry, and undo removes exactly what it added.</summary>
    [Fact]
    public void AddIsOneUndoableEntry()
    {
        var g = NewGraph("Tick");
        var (source, undo, _) = Build(Asset(g), () => g);

        source.AddVariable(new BlackboardVariableEntry("Scratch", typeof(int), null));

        Assert.Single(g.LocalVariables);
        Assert.Single(undo.Labels);

        undo.UndoLast();
        Assert.Empty(g.LocalVariables);
    }

    /// <summary>
    /// ⭐⭐ <b>Rename undo is the one a shallow snapshot gets wrong.</b> The declaration is mutated in
    /// place, so a snapshot holding the same object on both sides would "restore" the new name.
    /// </summary>
    [Fact]
    public void RenameUndoRestoresTheOldName()
    {
        var g = NewGraph("Tick");
        g.LocalVariables.Add(Decl("Scratch"));
        var (source, undo, _) = Build(Asset(g), () => g);

        source.RenameVariable("Scratch", "Carry");
        Assert.Equal("Carry", g.LocalVariables.Single().Name);
        Assert.Single(undo.Labels);

        undo.UndoLast();
        Assert.Equal("Scratch", g.LocalVariables.Single().Name);
    }

    /// <summary>Reorder is one entry and undoes to the prior order.</summary>
    [Fact]
    public void MoveIsUndoable()
    {
        var g = NewGraph("Tick");
        g.LocalVariables.AddRange(new[] { Decl("A"), Decl("B"), Decl("C") });
        var (source, undo, _) = Build(Asset(g), () => g);

        source.MoveVariable(0, 2);
        Assert.Equal(new[] { "B", "C", "A" }, g.LocalVariables.Select(v => v.Name).ToArray());

        undo.UndoLast();
        Assert.Equal(new[] { "A", "B", "C" }, g.LocalVariables.Select(v => v.Name).ToArray());
    }

    /// <summary>
    /// ⭐ <b>A gesture that changes nothing records nothing</b> — BP-204's "one entry per gesture, not
    /// one per keystroke", in its degenerate case. An undo stack full of no-ops is its own defect.
    /// </summary>
    [Fact]
    public void ANoOpGestureRecordsNoUndoEntry()
    {
        var g = NewGraph("Tick");
        g.LocalVariables.Add(Decl("Scratch"));
        var (source, undo, _) = Build(Asset(g), () => g);

        source.RenameVariable("Scratch", "Scratch");   // same name
        source.RenameVariable("Nope", "Other");        // no such local
        source.MoveVariable(0, 0);                     // same slot
        source.RemoveVariable("Nope");                 // nothing to remove

        Assert.Empty(undo.Labels);
    }

    /// <summary>
    /// ⭐ A macro graph refuses the add <b>out loud</b>, and the message says why rather than the
    /// button silently doing nothing (<c>BP1664</c>).
    /// </summary>
    [Fact]
    public void AMacroGraphRefusesTheAddWithAReason()
    {
        var macro = NewGraph("Mac", GraphKind.Macro);
        var (source, undo, refusals) = Build(Asset(macro), () => macro);

        source.AddVariable(new BlackboardVariableEntry("Scratch", typeof(int), null));

        Assert.Empty(macro.LocalVariables);
        Assert.Empty(undo.Labels);
        Assert.Contains("macro", Assert.Single(refusals), StringComparison.OrdinalIgnoreCase);
    }
}
