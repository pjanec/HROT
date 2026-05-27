using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

/// <summary>
/// Tests for BlackboardAliasDropValidator.WouldCreateCrossRegionConflict.
/// TASK-BB-1f-02.
/// </summary>
public sealed class BlackboardAliasDropValidatorTests
{
    // ---- Stub ---------------------------------------------------------------

    private sealed class StubDropValidatorAsset : IBlackboardManagedAsset
    {
        private readonly bool _crossRegionAllowed;
        private readonly Dictionary<string, List<BlackboardAliasBinding>> _aliases = new();

        public StubDropValidatorAsset(bool crossRegionAllowed) =>
            _crossRegionAllowed = crossRegionAllowed;

        public void AddExistingAlias(string variableName, BlackboardAliasBinding binding)
        {
            if (!_aliases.TryGetValue(variableName, out var list))
            {
                list = new List<BlackboardAliasBinding>();
                _aliases[variableName] = list;
            }
            list.Add(binding);
        }

        public bool IsConflictSuppressed(string variableName, string writerPairKey) => _crossRegionAllowed;
        public bool IsUnusedWarningSuppressed(string variableName) => false;

        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) =>
            _aliases.TryGetValue(variableName, out var list)
                ? list.AsReadOnly()
                : Array.Empty<BlackboardAliasBinding>();

        // ---- Required interface members (unused in these tests) ----------------
        public bool IsBlackboardEditorManaged => false;
        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => Array.Empty<BlackboardVariableEntry>();
        public void AddVariable(BlackboardVariableEntry entry) { }
        public void RemoveVariable(string name) { }
        public void UpdateVariableComment(string name, string? comment) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
        public void SetCrossRegionWriteAllowed(string variableName, bool allowed) { }
    }

    // ---- Helpers ------------------------------------------------------------

    private static BlackboardAliasBinding MakeBinding(Guid requiringAssetId, Guid requiringElementId) =>
        new(
            RequiringAssetId:   requiringAssetId,
            RequiringElementId: requiringElementId,
            RequiringAssetName: "SomeAsset",
            RequiredByPath:     "SomeAsset > Node#1",
            DtoType:            typeof(float));

    private static StubDropValidatorAsset EmptyAsset() => new StubDropValidatorAsset(false);
    private static StubDropValidatorAsset AllowedAsset() => new StubDropValidatorAsset(true);

    // ---- T1: null region map -> false ---------------------------------------

    [Fact]
    public void Returns_False_WhenNoRegionMap()
    {
        var asset = EmptyAsset();
        var newBinding = MakeBinding(Guid.NewGuid(), Guid.NewGuid());

        var result = BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(
            asset, "speed", newBinding, regionIndexByStateId: null);

        Assert.False(result);
    }

    // ---- T2: empty region map -> false -------------------------------------

    [Fact]
    public void Returns_False_WhenEmptyRegionMap()
    {
        var asset = EmptyAsset();
        var newBinding = MakeBinding(Guid.NewGuid(), Guid.NewGuid());
        var emptyMap = new Dictionary<Guid, int>();

        var result = BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(
            asset, "speed", newBinding, emptyMap);

        Assert.False(result);
    }

    // ---- T3: new binding's element not in region map -> false ---------------

    [Fact]
    public void Returns_False_WhenNewBindingNotInAnyRegion()
    {
        var asset = EmptyAsset();
        var unknownElementId = Guid.NewGuid();
        var newBinding = MakeBinding(Guid.NewGuid(), unknownElementId);
        var regionMap = new Dictionary<Guid, int> { [Guid.NewGuid()] = 0 };  // different id

        var result = BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(
            asset, "speed", newBinding, regionMap);

        Assert.False(result);
    }

    // ---- T4: no existing aliases -> false ----------------------------------

    [Fact]
    public void Returns_False_WhenNoExistingAliases_InSameAsset()
    {
        var assetId   = Guid.NewGuid();
        var elementId = Guid.NewGuid();
        var asset     = EmptyAsset();    // no existing aliases
        var newBinding = MakeBinding(assetId, elementId);
        var regionMap = new Dictionary<Guid, int> { [elementId] = 0 };

        var result = BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(
            asset, "speed", newBinding, regionMap);

        Assert.False(result);
    }

    // ---- T5: existing alias in different region -> true (conflict) ----------

    [Fact]
    public void Returns_True_WhenExistingAlias_InDifferentRegion()
    {
        var assetId        = Guid.NewGuid();
        var existingElemId = Guid.NewGuid();   // region 0
        var newElemId      = Guid.NewGuid();   // region 1
        var existingBinding = MakeBinding(assetId, existingElemId);
        var newBinding      = MakeBinding(assetId, newElemId);

        var asset = new StubDropValidatorAsset(false);
        asset.AddExistingAlias("speed", existingBinding);

        var regionMap = new Dictionary<Guid, int>
        {
            [existingElemId] = 0,
            [newElemId]      = 1,
        };

        var result = BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(
            asset, "speed", newBinding, regionMap);

        Assert.True(result);
    }

    // ---- T6: existing alias in same region -> false (safe) -----------------

    [Fact]
    public void Returns_False_WhenExistingAlias_InSameRegion()
    {
        var assetId        = Guid.NewGuid();
        var existingElemId = Guid.NewGuid();   // region 1
        var newElemId      = Guid.NewGuid();   // region 1
        var existingBinding = MakeBinding(assetId, existingElemId);
        var newBinding      = MakeBinding(assetId, newElemId);

        var asset = new StubDropValidatorAsset(false);
        asset.AddExistingAlias("speed", existingBinding);

        var regionMap = new Dictionary<Guid, int>
        {
            [existingElemId] = 1,
            [newElemId]      = 1,
        };

        var result = BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(
            asset, "speed", newBinding, regionMap);

        Assert.False(result);
    }

    // ---- T7: cross-region write allowed -> false (override suppresses) ------

    [Fact]
    public void Returns_False_WhenCrossRegionWriteAllowed()
    {
        var assetId        = Guid.NewGuid();
        var existingElemId = Guid.NewGuid();   // region 0
        var newElemId      = Guid.NewGuid();   // region 1
        var existingBinding = MakeBinding(assetId, existingElemId);
        var newBinding      = MakeBinding(assetId, newElemId);

        // Asset has cross-region writes ALLOWED for "speed".
        var asset = AllowedAsset();
        asset.AddExistingAlias("speed", existingBinding);

        var regionMap = new Dictionary<Guid, int>
        {
            [existingElemId] = 0,
            [newElemId]      = 1,
        };

        var result = BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(
            asset, "speed", newBinding, regionMap);

        Assert.False(result);
    }
}

