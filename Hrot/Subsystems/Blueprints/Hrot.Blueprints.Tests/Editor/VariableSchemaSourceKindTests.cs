using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor.AiShared.Blackboard;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// <b>U-4 + U-5 — the editor's turn at the three-list model.</b>
///
/// <para>
/// ⛔ <b><c>U-4</c>'s defect: a <c>bool isParams</c> over THREE lists.</b> Two values cannot name
/// three, so <c>Variables</c> — the <c>State</c> struct, the list every Instance blueprint actually
/// uses — was <b>not representable</b>. ⭐ Same shape as <c>U-3</c>'s untagged <c>int</c> at the other
/// end of the pipeline, and it takes the same carrier: <see cref="VariableKind"/>.
/// </para>
///
/// <para>
/// ⛔ <b><c>U-5</c>'s defects:</b> <c>CountNodesReferencingVariable</c> returned a hardcoded
/// <c>0</c> (<c>BP-230</c>) while the panel's delete confirmation was built on it; and the
/// <c>*Order</c> lists leaked ids on remove (<c>BP-231</c>).
/// </para>
/// </summary>
public sealed class VariableSchemaSourceKindTests
{
    private static BlueprintTypeRef Int() => new() { TypeId = "System.Int32" };

    private static BlueprintAsset Asset()
    {
        var a = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "SchemaHost",
            Dispatch = BlueprintDispatchKind.Instance, Header = new Header(),
        };
        a.Parameters.Add(new ParameterDecl { Id = Guid.NewGuid(), Name = "P0", Type = Int() });
        a.WorkingState.Add(new VariableDecl { Id = Guid.NewGuid(), Name = "W0", Type = Int() });
        a.Variables.Add(new VariableDecl { Id = Guid.NewGuid(), Name = "V0", Type = Int() });
        return a;
    }

    private static BlueprintVariableSchemaSource Source(BlueprintAsset a, VariableKind kind)
        => new(a, kind, () => { });

    private static void AddGet(Graph g, string variableId)
        => g.Nodes.Add(new GetVariableNode { Id = Guid.NewGuid(), VariableId = variableId });

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 U-4 — three kinds, three lists
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Each kind projects its own list</b> — and the <c>Variables</c> row is the one the
    /// <c>bool</c> could not express at all.
    /// </summary>
    [Theory]
    [InlineData(VariableKind.Parameter,    "P0")]
    [InlineData(VariableKind.WorkingState, "W0")]
    [InlineData(VariableKind.Variable,     "V0")]
    public void EachKindProjectsItsOwnList(VariableKind kind, string expected)
    {
        var projected = Source(Asset(), kind).Variables;
        Assert.Equal(expected, Assert.Single(projected).Name);
    }

    /// <summary>
    /// ⭐ <b><see cref="VariableKind.Unresolved"/> is rejected at construction.</b> It is the enum's
    /// default so a forgotten assignment is loud — ⛔ silently meaning "the first list" is the exact
    /// failure this task removes.
    /// </summary>
    [Fact]
    public void TheUnresolvedKindIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new BlueprintVariableSchemaSource(Asset(), VariableKind.Unresolved, () => { }));

    /// <summary>Add/remove land in the projected list and nowhere else.</summary>
    [Fact]
    public void MutationsTouchOnlyTheProjectedList()
    {
        var a = Asset();
        Source(a, VariableKind.Variable)
            .AddVariable(new BlackboardVariableEntry("Added", typeof(int), null));

        Assert.Equal(new[] { "V0", "Added" }, a.Variables.Select(v => v.Name).ToArray());
        Assert.Single(a.WorkingState);
        Assert.Single(a.Parameters);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 U-5 / BP-230 — a real count
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>0, 1 and 3 — the count is CORRECT, not merely non-zero.</b> ⚠ The hardcoded <c>0</c>
    /// this replaces would pass any test that only asserted *"returns an int"*, and would pass the
    /// zero case too — which is why all three points are asserted, and why the 3 is spread across
    /// <b>two graphs</b>.
    /// </summary>
    [Fact]
    public void TheReferenceCountIsRealAcrossGraphs()
    {
        var a = Asset();
        var v0 = a.Variables[0];
        a.Variables.Add(new VariableDecl { Id = Guid.NewGuid(), Name = "Unused", Type = Int() });

        var g1 = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function };
        var g2 = new Graph { Id = Guid.NewGuid(), Name = "Helper", Kind = GraphKind.Function };
        a.Graphs.Add(g1); a.Graphs.Add(g2);

        var src = Source(a, VariableKind.Variable);
        Assert.Equal(0, src.CountNodesReferencingVariable("V0"));
        Assert.Equal(0, src.CountNodesReferencingVariable("Unused"));

        AddGet(g1, v0.Id.ToString());
        Assert.Equal(1, src.CountNodesReferencingVariable("V0"));

        AddGet(g1, "var:" + v0.Id);          // ⭐ the My-Blueprint item-id form
        AddGet(g2, v0.Id.ToString());        // ⭐ and a second graph
        Assert.Equal(3, src.CountNodesReferencingVariable("V0"));
        Assert.Equal(0, src.CountNodesReferencingVariable("Unused"));
    }

    /// <summary>
    /// ⭐⭐ <b>The count follows the compiler's NAME fallback</b> — <c>Stage5.FindVariableRef</c>
    /// matches a bare name when the id does not parse, and hand-authored assets use one.
    /// ⚠ <b>This is why the count could not simply copy the locals source</b>, which counts by id only
    /// — correctly, because <c>FindLocalIndex</c> has no name fallback.
    /// </summary>
    [Fact]
    public void TheCountFollowsTheCompilersNameFallback()
    {
        var a = Asset();
        var g = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function };
        a.Graphs.Add(g);
        AddGet(g, "V0");                     // a bare NAME, not a guid

        Assert.Equal(1, Source(a, VariableKind.Variable).CountNodesReferencingVariable("V0"));
    }

    /// <summary>
    /// ⭐ <b>A reference the compiler resolves to a graph LOCAL is not a reference to the asset
    /// variable it shadows</b> (<c>Q27-C1</c>). ⚠ Counting it would over-report and block a legitimate
    /// delete.
    /// </summary>
    [Fact]
    public void AShadowingLocalsReferenceIsNotCounted()
    {
        var a = Asset();
        var g = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function };
        var local = new VariableDecl { Id = Guid.NewGuid(), Name = "V0", Type = Int() };
        g.LocalVariables.Add(local);
        a.Graphs.Add(g);
        AddGet(g, local.Id.ToString());

        Assert.Equal(0, Source(a, VariableKind.Variable).CountNodesReferencingVariable("V0"));
    }

    /// <summary>Each kind counts only references that resolve to ITS list.</summary>
    [Fact]
    public void TheCountIsScopedToTheSourcesOwnKind()
    {
        var a = Asset();
        var g = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function };
        a.Graphs.Add(g);
        AddGet(g, a.WorkingState[0].Id.ToString());

        Assert.Equal(1, Source(a, VariableKind.WorkingState).CountNodesReferencingVariable("W0"));
        Assert.Equal(0, Source(a, VariableKind.Variable).CountNodesReferencingVariable("V0"));
    }

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 U-5 / BP-231 — the order lists
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>Remove drops the id from the order list.</b> ⛔ It used to leak: <c>AddVariable</c> and
    /// <c>MoveVariable</c> maintained <c>*Order</c>, remove did not, so a deleted variable's id stayed
    /// forever. ✅ Benign today (<c>Stage5.GetOrdered</c> skips unknown ids) — ⚠ not after <c>U-9</c>.
    /// </summary>
    [Fact]
    public void RemoveDropsTheIdFromTheOrderList()
    {
        var a = Asset();
        var src = Source(a, VariableKind.Variable);
        src.AddVariable(new BlackboardVariableEntry("Second", typeof(int), null));

        Assert.Equal(2, a.VariableOrder!.Count);
        var survivorId = a.Variables.Single(v => v.Name == "V0").Id;

        src.RemoveVariable("Second");

        Assert.Equal(new[] { survivorId }, a.VariableOrder!.ToArray());
        Assert.Single(a.Variables);
    }

    /// <summary>
    /// ⭐ <b>Rename must NOT touch the order list</b> — it is keyed by id. ⚠ Asserted so a future
    /// "fix" cannot add a name-keyed rewrite that corrupts it.
    /// </summary>
    [Fact]
    public void RenameLeavesTheOrderListUntouched()
    {
        var a = Asset();
        var src = Source(a, VariableKind.Variable);
        src.AddVariable(new BlackboardVariableEntry("Second", typeof(int), null));
        var before = a.VariableOrder!.ToArray();

        src.RenameVariable("Second", "Renamed");

        Assert.Equal(before, a.VariableOrder!.ToArray());
        Assert.Contains(a.Variables, v => v.Name == "Renamed");
    }

    /// <summary>A batch remove drops every removed id, and only those.</summary>
    [Fact]
    public void BatchRemoveDropsExactlyTheRemovedIds()
    {
        var a = Asset();
        var src = Source(a, VariableKind.Variable);
        src.AddVariable(new BlackboardVariableEntry("A", typeof(int), null));
        src.AddVariable(new BlackboardVariableEntry("B", typeof(int), null));
        var keep = a.Variables.Single(v => v.Name == "V0").Id;

        src.RemoveVariables(new[] { "A", "B" });

        Assert.Equal(new[] { keep }, a.VariableOrder!.ToArray());
    }

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 U-5 / BP-230 — Q-k: read-only, said out loud
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The source SAYS it cannot edit Role/Scope, and the setter throws rather than
    /// discarding.</b>
    ///
    /// <para>
    /// ⛔ What this replaces: the interface shipped <c>{ }</c> default bodies, this source took the
    /// offer, and <c>VariablesPanelControl</c> gated its Role combo on <c>IsReadOnly</c> alone — which
    /// is <c>false</c> here. ⇒ the panel drew a live combo for a blueprint and the designer's change
    /// went nowhere. ⭐ <b>Established from the panel code, not from a screenshot</b>: the question of
    /// whether those columns were drawn-but-dead or hidden had been open since Batch 38 pending a
    /// visual check.
    /// </para>
    /// </summary>
    [Fact]
    public void RoleAndScopeAreDeclaredUneditable_AndTheSettersRefuse()
    {
        IVariablesSchemaSource src = Source(Asset(), VariableKind.Variable);

        Assert.False(src.SupportsRoleScopeEditing);
        Assert.Throws<NotSupportedException>(
            () => src.UpdateVariableRole("V0", Hrot.AiEditor.Persistence.BlackboardVariableRole.State));
        Assert.Throws<NotSupportedException>(
            () => src.UpdateVariableScope("V0", Hrot.AiEditor.Persistence.WorkingStateScope.Entity));
    }

    /// <summary>The locals source answers the same way, for the same reason.</summary>
    [Fact]
    public void TheLocalsSourceAlsoDeclaresRoleScopeUneditable()
    {
        var a = Asset();
        var g = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function };
        a.Graphs.Add(g);

        IVariablesSchemaSource src = new BlueprintLocalVariableSchemaSource(a, () => g, () => { });

        Assert.False(src.SupportsRoleScopeEditing);
        Assert.Throws<NotSupportedException>(
            () => src.UpdateVariableRole("x", Hrot.AiEditor.Persistence.BlackboardVariableRole.State));
    }
}
