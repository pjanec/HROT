using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

/// <summary>
/// ⭐⭐⭐ <b>The editor-owned variable lifecycle, pinned ONCE — because it is now implemented once.</b>
/// 🔒 User, <c>2026-09-28</c>: <i>"If btree does something right, hsm should reuse it by sharing
/// wherever possible, not by duplication."</i>
///
/// <para>📐 <b>What these replace.</b> The same four operations lived in FIVE places:
/// <c>BTreeCommandSink.ComposeAiPrimitiveAction</c> and <c>…Condition</c> (near-identical to each
/// other), <c>BTreeCommandSink.GenerateUniqueVariableName</c>,
/// <c>HsmFacetDispatcher.ComposeBlueprintParams</c>+<c>UniqueVariableName</c>, and both picker drawers'
/// <c>Promote</c> (character-for-character identical). ⇒ the rules were pinned only indirectly, per
/// host, and a fix to one spelling could not reach the others.</para>
///
/// <para>⚠ <b>Honest limit:</b> these pin the BEHAVIOUR in one place. ⛔ They do not machine-check that
/// nobody re-duplicates it — a future site could re-spell the rules and stay green. The per-host rails
/// (<c>CE414_R1</c>–<c>R4</c>, <c>BTreeDynamicCatalogTests</c>, <c>BTreeNodeAutoVarDeleteTests</c>) now
/// run THROUGH this code, so the coupling is real; the no-second-copy property is a review matter.</para>
/// </summary>
public sealed class AutoManagedVariablesTests
{
    // A stand-in for a generated AiPrimitive: {Name}_{Id:X8}_Bp with its sibling nested structs.
#pragma warning disable CS0649
    private static class Patrol_0000ABCD_Bp
    {
        public struct Params { public float DestX; public float DestY; }
        public struct WorkingState { public int Phase; }
    }

    // …and one with NO WorkingState, which is a legal generated shape.
    private static class Stateless_0000BEEF_Bp
    {
        public struct Params { public int Threshold; }
    }
#pragma warning restore CS0649

    private static ActionSchemaEntry EntryFor(Type paramsType) => new(
        paramsType.DeclaringType!.FullName + ".TickCore",
        paramsType, ActionHosting.Shared, BlackboardAccess.ReadWrite,
        IsCondition: false, DtoFields: null, IsAiPrimitive: true);

    // ── UniqueName ───────────────────────────────────────────────────────────

    [Fact]
    public void UniqueName_ReturnsTheBaseNameWhenFree_ThenSuffixesFromTwo()
    {
        var asset = new StubAsset();

        Assert.Equal("bpParams", AutoManagedVariables.UniqueName(asset, "bpParams"));

        AutoManagedVariables.Create(asset, "bpParams", typeof(int));
        Assert.Equal("bpParams_2", AutoManagedVariables.UniqueName(asset, "bpParams"));

        AutoManagedVariables.Create(asset, "bpParams", typeof(int));
        Assert.Equal("bpParams_3", AutoManagedVariables.UniqueName(asset, "bpParams"));
    }

    // ── RemoveIfAutoManaged ──────────────────────────────────────────────────

    /// <summary>
    /// ⛔⛔ <b>The guard that keeps a node operation from deleting AUTHORED data.</b> ⚠ This is the one
    /// rule in this file whose absence would be destructive rather than merely untidy.
    /// </summary>
    [Fact]
    public void RemoveIfAutoManaged_RefusesAVariableTheAuthorOwns()
    {
        var asset = new StubAsset();
        asset.AddVariable(new BlackboardVariableEntry("MineNotYours", typeof(float), null));
        string auto = AutoManagedVariables.Create(asset, "bpParams", typeof(int));

        Assert.False(AutoManagedVariables.RemoveIfAutoManaged(asset, "MineNotYours"));
        Assert.False(AutoManagedVariables.RemoveIfAutoManaged(asset, "NoSuchVariable"));
        Assert.False(AutoManagedVariables.RemoveIfAutoManaged(asset, null));

        Assert.True(AutoManagedVariables.RemoveIfAutoManaged(asset, auto));
        Assert.Equal(new[] { "MineNotYours" }, asset.BlackboardVariables.Select(v => v.Name));
    }

    // ── PromoteForSite ───────────────────────────────────────────────────────

    [Fact]
    public void PromoteForSite_IsStableAndIdempotentPerSite_AndRefusesANonGuid()
    {
        var asset = new StubAsset();
        var site  = new Guid("11111111-2222-3333-4444-555555555555");

        string? first = AutoManagedVariables.PromoteForSite(asset, site.ToString(), typeof(float));
        Assert.Equal($"_auto_{site:N}", first);

        // ⭐ Calling again for the same site returns the SAME row rather than a duplicate.
        Assert.Equal(first, AutoManagedVariables.PromoteForSite(asset, site.ToString(), typeof(float)));
        Assert.Single(asset.BlackboardVariables);

        // ⛔ A non-GUID site id creates nothing: a name that cannot be matched back to its site is
        //    worse than no variable.
        Assert.Null(AutoManagedVariables.PromoteForSite(asset, "not-a-guid", typeof(float)));
        Assert.Null(AutoManagedVariables.PromoteForSite(asset, null, typeof(float)));
        Assert.Single(asset.BlackboardVariables);
    }

    // ── ComposeForAiPrimitive ────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The BTree host's shape: params variable + a SEPARATE <c>Role=State</c> working-state
    /// variable, so the designer can widen its <c>Scope</c> to <c>Behavior</c>.</b>
    /// </summary>
    [Fact]
    public void ComposeForAiPrimitive_BTreeShape_CreatesBothVariablesWithTheRightRoles()
    {
        var asset = new StubAsset();

        var composed = AutoManagedVariables.ComposeForAiPrimitive(
            asset, EntryFor(typeof(Patrol_0000ABCD_Bp.Params)));

        var p  = asset.BlackboardVariables.Single(v => v.Name == composed.ParamsVariable);
        var ws = asset.BlackboardVariables.Single(v => v.Name == composed.WorkingStateVariable);

        // ⭐⭐ THE PROPERTY THAT MATTERS: the params variable IS the generated Params struct, so a
        //    hosting site's seed is that one variable and the field offsets come from the DTO.
        Assert.Equal(typeof(Patrol_0000ABCD_Bp.Params), p.FieldType);
        Assert.Equal(BlackboardVariableRole.Input, p.Role);
        Assert.True(p.IsAutoManaged);

        Assert.Equal(typeof(Patrol_0000ABCD_Bp.WorkingState), ws.FieldType);
        Assert.Equal(BlackboardVariableRole.State, ws.Role);
        Assert.Equal(WorkingStateScope.Node, ws.Scope);
        Assert.Equal(typeof(Patrol_0000ABCD_Bp.WorkingState), composed.WorkingStateType);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The HSM host's shape: the params variable ONLY.</b>
    ///
    /// <para>⛔⛔ This asymmetry is CORRECT, not unfinished. An HSM-hosted occurrence's working state
    /// lives in its occurrence slot, keyed <c>(machine, region, state, childAsset)</c> — there is no
    /// variable to bind, and inventing one would be a second storage story. ⚠ The rail exists so a
    /// future "let's finish the unification" pass has to argue with it.</para>
    /// </summary>
    [Fact]
    public void ComposeForAiPrimitive_HsmShape_CreatesOnlyTheParamsVariable()
    {
        var asset = new StubAsset();

        var composed = AutoManagedVariables.ComposeForAiPrimitive(
            asset, EntryFor(typeof(Patrol_0000ABCD_Bp.Params)),
            paramsBaseName: "bpActivityParams", workingStateBaseName: null);

        Assert.Equal("bpActivityParams", composed.ParamsVariable);
        Assert.Null(composed.WorkingStateVariable);
        Assert.Single(asset.BlackboardVariables);

        // ⚠ …and the TYPE is still reported, because a caller may record it without binding a variable.
        Assert.Equal(typeof(Patrol_0000ABCD_Bp.WorkingState), composed.WorkingStateType);
    }

    /// <summary>⭐ A generated shape with no <c>WorkingState</c> binds none, on either host.</summary>
    [Fact]
    public void ComposeForAiPrimitive_NoWorkingStateInTheGeneratedShape_BindsNone()
    {
        var asset = new StubAsset();

        var composed = AutoManagedVariables.ComposeForAiPrimitive(
            asset, EntryFor(typeof(Stateless_0000BEEF_Bp.Params)));

        Assert.Null(composed.WorkingStateType);
        Assert.Null(composed.WorkingStateVariable);
        Assert.Single(asset.BlackboardVariables);
    }

    /// <summary>⭐ Composing twice on one asset does not collide — the unique-name rule applies.</summary>
    [Fact]
    public void ComposeForAiPrimitive_Twice_ProducesDistinctVariables()
    {
        var asset = new StubAsset();
        var entry = EntryFor(typeof(Patrol_0000ABCD_Bp.Params));

        var a = AutoManagedVariables.ComposeForAiPrimitive(asset, entry);
        var b = AutoManagedVariables.ComposeForAiPrimitive(asset, entry);

        Assert.NotEqual(a.ParamsVariable, b.ParamsVariable);
        Assert.NotEqual(a.WorkingStateVariable, b.WorkingStateVariable);
        Assert.Equal(4, asset.BlackboardVariables.Count);
    }

    // ── the stub ─────────────────────────────────────────────────────────────

    private sealed class StubAsset : IBlackboardManagedAsset
    {
        private readonly List<BlackboardVariableEntry> _vars = new();

        public bool IsBlackboardEditorManaged { get; private set; } = true;
        public void SetBlackboardEditorManaged(bool managed) => IsBlackboardEditorManaged = managed;

        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;
        public void AddVariable(BlackboardVariableEntry entry) => _vars.Add(entry);
        public void RemoveVariable(string name) => _vars.RemoveAll(v => v.Name == name);

        public void UpdateVariableComment(string name, string? comment) { }
        public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName)
            => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
        public void RemoveVariables(IReadOnlyList<string> names)
        { foreach (var n in names) RemoveVariable(n); }
    }
}
