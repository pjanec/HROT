using System;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Windows;
using BlueprintTypeRef = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 99 (<c>99b</c>) — <c>BP-367</c>'s SIBLINGS, enumerated and then GATED.</b>
///
/// <para>🔴 <b>What <c>BP-367</c> was.</b> <c>BlueprintVariableSchemaSource</c> projected a declaration
/// into a <c>VariableViewModel</c> and <b>silently dropped <c>DefaultValueJson</c></b>. ⛔ Harmless
/// while OK refused; <b>DESTRUCTIVE</b> once <c>98a</c> made the write land — the dialog opened on
/// <c>0</c> and OK overwrote an authored <c>1</c>.</para>
///
/// <para>⭐⭐⭐ <b>THE MECHANISM, which is what makes this a whole class of defect rather than one
/// mistake:</b> <c>DefaultValueJson</c> is a <b>TRAILING OPTIONAL parameter defaulting to
/// <c>null</c></b> on BOTH carriers. <c>VariableViewModel</c>'s own comment says it out loud —
/// <i>"Trailing and optional: every existing construction site is unchanged"</i>. ⇒ ⛔ <b>a projection
/// that forgets it does not fail to compile, does not warn, and reads correctly.</b> Nothing but a
/// rail can see it.</para>
///
/// <para>⭐⭐ <b>THE ENUMERATION</b> *(📌 <c>R-74</c> — the graph, ⛔ not a grep alone)*.
/// <c>query_graph</c>: <c>MATCH (c)-[:CALLS]-&gt;(t) WHERE t.name IN
/// ['BlackboardVariableEntry','VariableViewModel']</c> ⇒ <b>total 54</b>, of which <b>11</b> are
/// production. ⚠⚠ <b>And the graph MISSED four</b> — <c>BTreeCommandSink</c>'s four
/// <c>AddVariable(new BlackboardVariableEntry(...))</c> sites carry no <c>CALLS</c> edge to the record
/// constructor. ⇒ ⭐ <b>the enumeration is graph ∪ grep, and saying so is the honest form</b>: the graph
/// found the callers grep's line-matching would have had to be told to look for, and grep found four
/// the graph could not see. ⭐ <b>16 production sites total.</b></para>
///
/// <list type="table">
///   <listheader><term>projection</term><description>verdict</description></listheader>
///   <item><term><c>BlueprintVariableSchemaSource.Variables</c> + <c>.Entries</c></term>
///     <description>⭐ <b>the <c>BP-367</c> site itself</b> — fixed in Batch 98, railed by
///     <c>TheBlueprintPlanningEditLandsTests</c>.</description></item>
///   <item><term><c>BlueprintLocalVariableSchemaSource.Variables</c></term>
///     <description>✅ carries it — ⛔ <b>and was NOT railed for it</b> ⇒ <see cref="AGraphLocalsDeclaredDefaultSurvivesTheProjection"/>.</description></item>
///   <item><term><c>BlackboardAuthoringWindow.BuildViewModel</c></term>
///     <description>✅ carries it — ⛔ <b>and was NOT railed for it</b>. ⭐ This is the projection every
///     BTree/HSM row is built from: <c>BTreeHsmSchemaSource.Variables</c> is a pass-through of the
///     <c>BlackboardWindowViewModel</c> this builds ⇒ <see cref="AnAiHostsDeclaredDefaultSurvivesTheProjection"/>.</description></item>
///   <item><term><c>HsmAssetMapper.BlackboardFromDto</c> · <c>BehaviorTreeAssetMapper.BlackboardFromDto</c></term>
///     <description>✅ carry it, and ⭐ <b>already railed</b> — <c>DefaultValueJsonRoundTripTests</c>,
///     14 tests over both hosts (round-trip · null-omitted-from-JSON · back-compat when the key is
///     absent). ⚠ <b>The highest-stakes pair</b>: dropping it there loses an authored default on
///     RELOAD, silently. ⛔ Not re-railed here — 📌 ruling 9.</description></item>
///   <item><term><c>BlackboardAuthoringWindow.BuildHardcodedDtoFields</c></term>
///     <description>⛔ <b>does NOT carry it, correctly</b> — these are sub-tree DTO field
///     REQUIREMENTS, not declarations. There is no declaration to have authored a default, and they
///     ship <c>IsReadOnly: true</c>.</description></item>
///   <item><term><c>VariablesPanelControl</c> :710</term>
///     <description>⛔ <b>drops it, correctly</b> — a name-only projection whose sole consumer is
///     <c>BlackboardNameValidator.Validate</c>, which reads <c>Name</c> and nothing else.</description></item>
///   <item><term>the remaining <b>8</b> — <c>BTreeCommandSink</c> ×4 · the two pickers' <c>Promote</c> ·
///     <c>BehaviorTreeAsset.GetAutoAllocatedVariables</c> · <c>VariablesPanelControl</c> :720</term>
///     <description>⛔ <b>not projections at all</b> — every one CREATES a variable that did not exist.
///     ⭐ There is no declaration behind them to carry a default from.</description></item>
/// </list>
///
/// <para>⭐⭐⭐ <b>THE ANSWER: <c>BP-367</c> has NO unfixed sibling.</b> 📌 The handoff allows this
/// verdict explicitly — <i>"a count of 'one, and it is fixed' is a fine answer"</i> — ⛔ <b>but an
/// enumeration that ends in a sentence rots.</b> ⇒ the two carriers that were correct-but-ungated are
/// gated below, so the next projection that forgets the trailing argument goes red instead of
/// shipping.</para>
///
/// <para>⚠ <b>WHICH LAYER IS FAKED</b> *(📌 <c>M-29</c>)*: ⛔ <b>none</b> — both rails drive the real
/// production projection over a real declaration and read the real result. ⭐ What they do NOT cover:
/// a projection written AFTER today. 📌 That is the standing limit of a per-site rail, and it is why
/// the enumeration above is recorded rather than only the two assertions.</para>
/// </summary>
public sealed class EveryDeclarationProjectionCarriesTheDefaultTests
{
    private const string AuthoredDefault = "42";

    private static VariableDecl Decl(string name) => new()
    {
        Id               = Guid.NewGuid(),
        Name             = name,
        Type             = new BlueprintTypeRef { TypeId = "System.Int32" },
        DefaultValueJson = AuthoredDefault,
    };

    /// <summary>
    /// ⭐⭐ <b>Graph LOCALS — the sibling source <c>BP-367</c>'s fix did not touch.</b>
    ///
    /// <para>📐 <c>BlueprintLocalVariableSchemaSource.Variables</c> builds its own
    /// <c>VariableViewModel</c> list, with its own argument list, from the graph's
    /// <c>LocalVariables</c> — ⛔ <b>a second projection, not a caller of the first</b>. ⇒ the Batch 98
    /// fix could not have covered it, and nothing asserted it did.</para>
    /// </summary>
    [Fact]
    public void AGraphLocalsDeclaredDefaultSurvivesTheProjection()
    {
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function };
        graph.LocalVariables.Add(Decl("Scratch"));

        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "Locals", Dispatch = BlueprintDispatchKind.Instance,
            Header  = new Header(),
        };
        asset.Graphs.Add(graph);

        var source = new BlueprintLocalVariableSchemaSource(
            asset, currentGraph: () => graph, onChanged: () => { });

        var projected = Assert.Single(source.Variables);
        Assert.Equal("Scratch",       projected.Name);
        Assert.Equal(AuthoredDefault, projected.DefaultValueJson);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>BTree/HSM — and this one is the WIDEST, which is why it is worth its own rail.</b>
    ///
    /// <para>📐 <c>BTreeHsmSchemaSource.Variables</c> is <c>=&gt; _vm.Variables</c>, a pass-through of a
    /// <c>BlackboardWindowViewModel</c> built elsewhere. ⇒ ⭐ <b>the projection that actually decides
    /// whether an AI host's row knows its declared default is
    /// <c>BlackboardAuthoringWindow.BuildViewModel</c></b>, and every BTree and HSM variable row in the
    /// editor comes through it.</para>
    ///
    /// <para>⚠ Driven through the REAL static builder over a real managed asset — ⛔ not through a
    /// hand-made view model, which would assert the test's own construction call.</para>
    /// </summary>
    [Fact]
    public void AnAiHostsDeclaredDefaultSurvivesTheProjection()
    {
        var asset = new TestManagedAsset(
            AssetKind.BTree,
            new BlackboardVariableEntry(
                "Health", typeof(int), Comment: null, DefaultValueJson: AuthoredDefault));

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        var projected = Assert.Single(vm.Variables, v => v.Name == "Health");
        Assert.Equal(AuthoredDefault, projected.DefaultValueJson);
    }
}
