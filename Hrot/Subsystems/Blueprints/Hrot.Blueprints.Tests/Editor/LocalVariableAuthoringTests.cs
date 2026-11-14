using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor.AiShared.Blackboard;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-57 §1 — the locals <see cref="IVariablesSchemaSource"/>.
///
/// <para>
/// ⭐⭐ <b>The reference count is the test that matters.</b> `BP-230` records that the existing
/// asset-variable source returns a hardcoded <c>0</c> from
/// <c>CountNodesReferencingVariable</c> — trap #5, a member reporting success while doing nothing — and
/// the delete gesture is built on that number. A locals source that inherited the stub would report
/// "no references" for every local and delete anyway.
/// </para>
/// </summary>
public sealed class LocalVariableAuthoringTests
{
    private static Pin P(string name, string dir, bool isExec, string typeId = "") => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = isExec,
        TypeRef = new BlueprintTypeRef { TypeId = typeId },
    };

    private static VariableDecl Decl(string name, string typeId = "System.Int32") => new()
    {
        Id = Guid.NewGuid(), Name = name,
        Type = new BlueprintTypeRef { TypeId = typeId },
        DefaultValueJson = "",
    };

    private static Graph NewGraph(string name, GraphKind kind = GraphKind.Function) => new()
    {
        Id = Guid.NewGuid(), Name = name, Kind = kind,
    };

    /// <summary>A <c>Get</c> node in <paramref name="graph"/> aimed at <paramref name="variableId"/>.</summary>
    private static Node AddGet(Graph graph, string variableId)
    {
        var get = new GetVariableNode { Id = Guid.NewGuid(), VariableId = variableId };
        get.Pins.Add(P("Value", "Out", false, "System.Int32"));
        graph.Nodes.Add(get);
        return get;
    }

    private static Node AddSet(Graph graph, string variableId)
    {
        var set = new SetVariableNode { Id = Guid.NewGuid(), VariableId = variableId };
        set.Pins.AddRange(new[] { P("In", "In", true), P("Out", "Out", true), P("Value", "In", false, "System.Int32") });
        graph.Nodes.Add(set);
        return set;
    }

    private static BlueprintAsset Asset(params Graph[] graphs) => new()
    {
        AssetId = Guid.NewGuid(), Name = "LocalsAuthoring",
        Dispatch = BlueprintDispatchKind.Instance,
        Graphs = graphs.ToList(), Header = new Header(),
    };

    // ────────────────────────────────────────────────────────────────────────
    // ⭐⭐ The reference count
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>0, 1 and 3 — and the 3 spread across two graphs.</b> The cross-graph reference is not a
    /// curiosity: a node in another graph carrying this id resolves to nothing
    /// (<c>FindLocalIndex</c> is per-graph) and is exactly the dangling case <c>BP1670</c> refuses. A
    /// delete gesture that could not see it would leave the asset uncompilable while reporting itself
    /// clean.
    /// </summary>
    [Fact]
    public void TheReferenceCountIsReal_AtZeroOneAndThree_AcrossTwoGraphs()
    {
        var owner = NewGraph("Tick");
        var other = NewGraph("Helper");

        var unused   = Decl("Unused");
        var usedOnce = Decl("UsedOnce");
        var usedThree = Decl("UsedThrice");
        owner.LocalVariables.AddRange(new[] { unused, usedOnce, usedThree });

        AddGet(owner, usedOnce.Id.ToString());

        AddGet(owner, usedThree.Id.ToString());
        AddSet(owner, "var:" + usedThree.Id);          // ⭐ the var: prefix must count too
        AddGet(other, usedThree.Id.ToString());        // ⭐ the cross-graph one

        var asset  = Asset(owner, other);
        var source = new BlueprintLocalVariableSchemaSource(asset, () => owner, () => { });

        Assert.Equal(0, source.CountNodesReferencingVariable("Unused"));
        Assert.Equal(1, source.CountNodesReferencingVariable("UsedOnce"));
        Assert.Equal(3, source.CountNodesReferencingVariable("UsedThrice"));
    }

    /// <summary>
    /// ⭐ <b>By ID, never by name — the same rule the compiler follows.</b> <c>FindLocalIndex</c> has
    /// no name fallback, so a node carrying the local's NAME is not a reference to it. Counting names
    /// would inflate the number and make a safe delete refuse for no reason.
    /// </summary>
    [Fact]
    public void ANodeCarryingTheNameIsNotCountedAsAReference()
    {
        var owner = NewGraph("Tick");
        var local = Decl("Scratch");
        owner.LocalVariables.Add(local);
        AddGet(owner, "Scratch");   // the NAME, not the id

        var source = new BlueprintLocalVariableSchemaSource(Asset(owner), () => owner, () => { });
        Assert.Equal(0, source.CountNodesReferencingVariable("Scratch"));
    }

    /// <summary>⭐ <c>IsUnused</c> is computed from that count, not hardcoded <c>false</c>.</summary>
    [Fact]
    public void IsUnusedReflectsTheRealCount()
    {
        var owner = NewGraph("Tick");
        var used = Decl("Used");
        var free = Decl("Free");
        owner.LocalVariables.AddRange(new[] { used, free });
        AddGet(owner, used.Id.ToString());

        var source = new BlueprintLocalVariableSchemaSource(Asset(owner), () => owner, () => { });
        var rows = source.Variables;

        Assert.False(rows.Single(r => r.Name == "Used").IsUnused);
        Assert.True(rows.Single(r => r.Name == "Free").IsUnused);
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐ It follows the canvas
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b><c>BP-72</c>'s lesson, applied.</b> The source resolves the graph at CALL time, so
    /// switching the canvas changes what it projects. A source that captured the graph at construction
    /// would edit the graph the designer is not looking at.
    /// </summary>
    [Fact]
    public void TheProjectionFollowsTheCurrentGraph()
    {
        var first  = NewGraph("Tick");
        var second = NewGraph("Helper");
        first.LocalVariables.Add(Decl("A"));
        second.LocalVariables.AddRange(new[] { Decl("B"), Decl("C") });

        Graph current = first;
        var source = new BlueprintLocalVariableSchemaSource(Asset(first, second), () => current, () => { });

        Assert.Equal(new[] { "A" }, source.Variables.Select(v => v.Name).ToArray());

        current = second;
        Assert.Equal(new[] { "B", "C" }, source.Variables.Select(v => v.Name).ToArray());
    }

    /// <summary>⭐ Present and EMPTY when the graph declares none — not absent, not null.</summary>
    [Fact]
    public void AGraphWithNoLocals_ProjectsAnEmptySetRatherThanFailing()
    {
        var g = NewGraph("Tick");
        var source = new BlueprintLocalVariableSchemaSource(Asset(g), () => g, () => { });

        Assert.Empty(source.Variables);
        Assert.False(source.IsReadOnly);
    }

    /// <summary>
    /// ⭐ <b>A <see cref="GraphKind.Macro"/> graph is read-only, and <c>BP1664</c> is the reason.</b> A
    /// macro is spliced into its call sites, so after expansion it is not a graph and a macro-local has
    /// nothing to be scoped to. ⚠ Read-only rather than absent — the surface has to stay visible to say
    /// why.
    /// </summary>
    [Fact]
    public void AMacroGraphIsReadOnly_AndRefusesTheAdd()
    {
        var macro = NewGraph("Mac", GraphKind.Macro);
        bool changed = false;
        var source = new BlueprintLocalVariableSchemaSource(Asset(macro), () => macro, () => changed = true);

        Assert.True(source.IsReadOnly);

        source.AddVariable(new BlackboardVariableEntry("Scratch", typeof(int), null));
        Assert.Empty(macro.LocalVariables);
        Assert.False(changed);
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐ Mutation
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>Rename is safe, proven rather than asserted in a comment.</b> A local resolves by id, so
    /// every reference survives the rename pointing at the same declaration. ⚠ The opposite of
    /// <c>BP-225</c>'s exec pins, where identity is the name.
    /// </summary>
    [Fact]
    public void RenameKeepsEveryReferenceResolving()
    {
        var g = NewGraph("Tick");
        var local = Decl("Scratch");
        g.LocalVariables.Add(local);
        AddGet(g, local.Id.ToString());
        AddSet(g, local.Id.ToString());

        var source = new BlueprintLocalVariableSchemaSource(Asset(g), () => g, () => { });
        source.RenameVariable("Scratch", "Carry");

        Assert.Equal("Carry", g.LocalVariables.Single().Name);
        // ⭐ The id did not move, so the count is unchanged — the references still point at it.
        Assert.Equal(2, source.CountNodesReferencingVariable("Carry"));
        Assert.Equal(0, source.CountNodesReferencingVariable("Scratch"));
    }

    /// <summary>A rename to a blank name changes nothing rather than producing an unnamed local.</summary>
    [Fact]
    public void RenameToBlankIsRefused()
    {
        var g = NewGraph("Tick");
        g.LocalVariables.Add(Decl("Scratch"));
        var source = new BlueprintLocalVariableSchemaSource(Asset(g), () => g, () => { });

        source.RenameVariable("Scratch", "   ");
        Assert.Equal("Scratch", g.LocalVariables.Single().Name);
    }

    /// <summary>Add and remove go to the CURRENT graph's list, and fire the change notification once.</summary>
    [Fact]
    public void AddAndRemoveTargetTheCurrentGraph()
    {
        var first  = NewGraph("Tick");
        var second = NewGraph("Helper");
        Graph current = second;
        int changes = 0;
        var source = new BlueprintLocalVariableSchemaSource(Asset(first, second), () => current, () => changes++);

        source.AddVariable(new BlackboardVariableEntry("Scratch", typeof(float), "note"));

        Assert.Empty(first.LocalVariables);
        Assert.Equal("Scratch", second.LocalVariables.Single().Name);
        Assert.Equal("System.Single", second.LocalVariables.Single().Type.TypeId);
        Assert.Equal(1, changes);

        source.RemoveVariable("Scratch");
        Assert.Empty(second.LocalVariables);
        Assert.Equal(2, changes);
    }

    /// <summary>
    /// ⚠ <b>Reordering is not cosmetic for a suspending graph</b> — the declaration list IS the order,
    /// and it feeds the slot layout and therefore <c>StructureHash</c>. Asserted here as ordinary list
    /// behaviour; the consequence is documented on the source.
    /// </summary>
    [Fact]
    public void MoveReordersTheDeclarationList()
    {
        var g = NewGraph("Tick");
        g.LocalVariables.AddRange(new[] { Decl("A"), Decl("B"), Decl("C") });
        var source = new BlueprintLocalVariableSchemaSource(Asset(g), () => g, () => { });

        source.MoveVariable(0, 2);
        Assert.Equal(new[] { "B", "C", "A" }, g.LocalVariables.Select(v => v.Name).ToArray());

        source.MoveVariable(5, 0);   // out of range ⇒ no-op, not a throw
        Assert.Equal(new[] { "B", "C", "A" }, g.LocalVariables.Select(v => v.Name).ToArray());
    }

    /// <summary>
    /// ⚠ <b>The refactor key is graph-qualified.</b> Two graphs may each declare <c>Scratch</c>; a key
    /// naming only the asset and the variable would collide and a refactor would rename the wrong one.
    /// </summary>
    [Fact]
    public void TheRefactorKeyDistinguishesTwoGraphsSameNamedLocals()
    {
        var first  = NewGraph("Tick");
        var second = NewGraph("Helper");
        first.LocalVariables.Add(Decl("Scratch"));
        second.LocalVariables.Add(Decl("Scratch"));
        var asset = Asset(first, second);

        Graph current = first;
        var source = new BlueprintLocalVariableSchemaSource(asset, () => current, () => { });
        var keyA = source.GetRefactorKey("Scratch");

        current = second;
        var keyB = source.GetRefactorKey("Scratch");

        Assert.NotNull(keyA);
        Assert.NotEqual(keyA, keyB);
    }
}
