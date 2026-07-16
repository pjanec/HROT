using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

/// <summary>
/// B-4 headless tests for node-owned variable presentation and alias drop predicate.
/// </summary>
public sealed class NodeOwnedVariableTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static VariableViewModel MakeVar(string name, bool isAutoManaged = false, Type? type = null) =>
        new VariableViewModel(
            Name:         name,
            TypeName:     (type ?? typeof(float)).Name,
            ByteSize:     4,
            FieldType:    type ?? typeof(float),
            Comment:      null,
            AliasedBy:    Array.Empty<(string, Guid, Guid)>(),
            IsUnused:     false,
            IsAutoManaged: isAutoManaged);

    private static BlackboardWindowViewModel BuildVm(IReadOnlyList<VariableViewModel> rows) =>
        new BlackboardWindowViewModel(
            HasActiveAsset:            true,
            IsBlackboardEditorManaged: true,
            TotalInlineBytes:          0,
            TotalHeavyBytes:           0,
            InlineBudget:              512,
            HeavyBudget:               0,
            RequiresHeavyComponent:    false,
            Warning:                   PackWarning.None,
            Variables:                 rows,
            KnownTypeNames:            Array.Empty<VariableTypeChoice>(),
            UnboundRequirements:       Array.Empty<UnboundRequirementViewModel>());

    // ── VariableViewModel carries IsAutoManaged ──────────────────────────────

    [Fact]
    public void VariableViewModel_IsAutoManaged_DefaultsFalse()
    {
        var vm = MakeVar("hand");
        vm.IsAutoManaged.Should().BeFalse("hand-authored variables are not auto-managed");
    }

    [Fact]
    public void VariableViewModel_IsAutoManaged_TrueWhenSet()
    {
        var vm = MakeVar("_auto_nodeA", isAutoManaged: true);
        vm.IsAutoManaged.Should().BeTrue("auto-managed flag must be propagated");
    }

    // ── BuildViewModel populates IsAutoManaged from entry ────────────────────

    [Fact]
    public void BuildViewModel_PopulatesIsAutoManaged_FromEntry()
    {
        var asset = new FakeBlackboardAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("handVar",       typeof(float), null, IsAutoManaged: false),
            new BlackboardVariableEntry("_auto_nodeA",   typeof(float), null, IsAutoManaged: true),
        });

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        var hand = vm.Variables.First(v => v.Name == "handVar");
        hand.IsAutoManaged.Should().BeFalse("hand-authored var must have IsAutoManaged=false");

        var auto = vm.Variables.First(v => v.Name == "_auto_nodeA");
        auto.IsAutoManaged.Should().BeTrue("auto-managed entry must yield IsAutoManaged=true in VM");
    }

    // ── Panel section split: auto vars are in node-owned group ───────────────

    [Fact]
    public void Variables_AutoManaged_Are_InNodeOwnedGroup_NotMainList()
    {
        var rows = new List<VariableViewModel>
        {
            MakeVar("sharedVar",       isAutoManaged: false),
            MakeVar("_auto_nodeA",     isAutoManaged: true),
            MakeVar("anotherShared",   isAutoManaged: false),
        };

        var mainVars     = rows.Where(v => !v.IsAutoManaged).ToList();
        var nodeOwnedVars = rows.Where(v =>  v.IsAutoManaged).ToList();

        mainVars.Should().HaveCount(2, "two hand-authored vars belong in main list");
        mainVars.Should().AllSatisfy(v => v.IsAutoManaged.Should().BeFalse());

        nodeOwnedVars.Should().HaveCount(1, "one auto-managed var belongs in node-owned group");
        nodeOwnedVars[0].Name.Should().Be("_auto_nodeA");
    }

    [Fact]
    public void Variables_NoAutoManaged_NodeOwnedGroupIsEmpty()
    {
        var rows = new List<VariableViewModel>
        {
            MakeVar("alpha", isAutoManaged: false),
            MakeVar("beta",  isAutoManaged: false),
        };

        var nodeOwnedVars = rows.Where(v => v.IsAutoManaged).ToList();
        nodeOwnedVars.Should().BeEmpty("no auto-managed vars → node-owned group is empty");
    }

    // ── Alias drop predicate ─────────────────────────────────────────────────

    [Fact]
    public void IsAliasDropAccepted_NormalVar_MatchingType_ReturnsTrue()
    {
        var target = MakeVar("sharedVar", isAutoManaged: false, type: typeof(float));
        VariablesPanelControl.IsAliasDropAccepted(target, typeof(float))
            .Should().BeTrue("hand-authored var with matching DTO type accepts alias drop");
    }

    [Fact]
    public void IsAliasDropAccepted_NormalVar_MismatchedType_ReturnsFalse()
    {
        var target = MakeVar("sharedVar", isAutoManaged: false, type: typeof(float));
        VariablesPanelControl.IsAliasDropAccepted(target, typeof(int))
            .Should().BeFalse("type mismatch rejects alias drop");
    }

    [Fact]
    public void IsAliasDropAccepted_AutoManagedVar_MatchingType_ReturnsFalse()
    {
        // B-4 §3.7: auto-managed vars must never be alias targets.
        var target = MakeVar("_auto_nodeA", isAutoManaged: true, type: typeof(float));
        VariablesPanelControl.IsAliasDropAccepted(target, typeof(float))
            .Should().BeFalse("auto-managed var must NOT accept alias drop even if types match");
    }

    [Fact]
    public void IsAliasDropAccepted_AutoManagedVar_MismatchedType_ReturnsFalse()
    {
        var target = MakeVar("_auto_nodeA", isAutoManaged: true, type: typeof(float));
        VariablesPanelControl.IsAliasDropAccepted(target, typeof(int))
            .Should().BeFalse("auto-managed var must NOT accept alias drop");
    }

    // ── Unused diagnostic does not fire for auto-managed while node lives ─────

    [Fact]
    public void AutoManagedVar_IsUnused_WhenNodeSetsRefCountToZero_ButStillExistsInMainList()
    {
        // The 'isUnused' flag is set when CountNodesReferencing == 0.
        // Before node deletion, the node's ETF keeps the ref count at 1 → not unused.
        // After node deletion the var is removed, so it can't appear as unused.
        // This test verifies that an auto-managed var WITH a live node reference is NOT marked unused.
        var asset = new FakeBlackboardAsset(refCountOverride: 1);
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("_auto_nodeA", typeof(float), null, IsAutoManaged: true),
        });

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        var autoVar = vm.Variables.Single(v => v.Name == "_auto_nodeA");
        autoVar.IsUnused.Should().BeFalse(
            "auto-managed var must NOT be flagged as unused while its owning node is alive");
    }
}

// ── Fake asset for BuildViewModel tests ──────────────────────────────────────

internal sealed class FakeBlackboardAsset : IEditableAsset, IBlackboardManagedAsset
{
    private readonly List<BlackboardVariableEntry> _vars = new();
    private readonly int _refCountOverride;

    public FakeBlackboardAsset(int refCountOverride = 0) => _refCountOverride = refCountOverride;

    public Guid AssetId { get; } = Guid.NewGuid();
    public string Name { get; set; } = "FakeAsset";
    public AssetKind Kind => AssetKind.BTree;
    public string SourceFilePath => "/fake.cs";
    public bool IsDirty => false;
    public bool IsEditorOwned => true;
    public bool IsBlackboardEditorManaged { get; set; } = true;
    public void SetBlackboardEditorManaged(bool managed) => IsBlackboardEditorManaged = managed;
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;
    public BlackboardLoadState LoadState => BlackboardLoadState.Clean;
    public string? LoadDiagnosticMessage => null;

    public event Action? Changed;

    public void SetBlackboardVariables(IEnumerable<BlackboardVariableEntry> vars)
    {
        _vars.Clear();
        _vars.AddRange(vars);
        Changed?.Invoke();
    }

    public void AddVariable(BlackboardVariableEntry entry) { _vars.Add(entry); Changed?.Invoke(); }
    public void RemoveVariable(string name) { _vars.RemoveAll(v => v.Name == name); Changed?.Invoke(); }
    public void RemoveVariables(IReadOnlyList<string> names) { foreach (var n in names) _vars.RemoveAll(v => v.Name == n); Changed?.Invoke(); }
    public void UpdateVariableComment(string name, string? comment) { Changed?.Invoke(); }
    public void UpdateVariableDefaultValueJson(string name, string? json) { Changed?.Invoke(); }
    public void MoveVariable(int src, int dst) { Changed?.Invoke(); }
    public void RenameVariable(string o, string n) { Changed?.Invoke(); }
    public int CountNodesReferencingVariable(string name) => _refCountOverride;

    // ── S3-1 authorability: real (non-no-op) Role/Scope updates, mirroring
    // BehaviorTreeAsset.UpdateVariableRole/UpdateVariableScope, so tests can prove the
    // schema call actually persists on the model — including for IsAutoManaged rows,
    // since the real asset applies the update purely by name lookup with no auto-managed gate.
    public int UpdateVariableScopeCallCount { get; private set; }
    public int UpdateVariableRoleCallCount { get; private set; }

    public void UpdateVariableRole(string name, BlackboardVariableRole role)
    {
        UpdateVariableRoleCallCount++;
        int idx = _vars.FindIndex(v => v.Name == name);
        if (idx < 0) return;
        _vars[idx] = _vars[idx] with { Role = role };
        Changed?.Invoke();
    }

    public void UpdateVariableScope(string name, WorkingStateScope scope)
    {
        UpdateVariableScopeCallCount++;
        int idx = _vars.FindIndex(v => v.Name == name);
        if (idx < 0) return;
        _vars[idx] = _vars[idx] with { Scope = scope };
        Changed?.Invoke();
    }

    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string v) => Array.Empty<BlackboardAliasBinding>();
    public void AddAlias(string v, BlackboardAliasBinding b) { }
    public void RemoveAlias(string v, Guid a, Guid e) { }
    public void PruneStaleAliasBindings(IReadOnlyCollection<Guid> knownIds) { }
    public IReadOnlyCollection<Guid> GetKnownSubAssetIds() => Array.Empty<Guid>();
    public bool IsConflictSuppressed(string v, string w) => false;
    public void SetConflictSuppressed(string v, string w, bool s) { }
    public bool IsUnusedWarningSuppressed(string v) => false;
    public void SetUnusedWarningSuppressed(string v, bool s) { }
    public IEnumerable<(string, string)> GetConflictSuppressions() => Array.Empty<(string, string)>();
    public IEnumerable<string> GetUnusedSuppressions() => Array.Empty<string>();
    public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;
}
