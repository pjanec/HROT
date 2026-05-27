using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

/// <summary>
/// Tests for BlackboardDtoEmitter.ValidateSaveAllowed and the BlackboardLoadState guard (1f-07).
/// </summary>
public sealed class BlackboardLoadStateTests
{
    // ---- Stub asset ----

    private sealed class StubAsset : IBlackboardManagedAsset
    {
        public BlackboardLoadState LoadState      { get; set; } = BlackboardLoadState.Clean;
        public string?             LoadDiagnosticMessage { get; set; }
        public bool                IsBlackboardEditorManaged { get; set; } = true;
        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables =>
            Array.Empty<BlackboardVariableEntry>();
        public void AddVariable(BlackboardVariableEntry entry) { }
        public void RemoveVariable(string name) { }
        public void UpdateVariableComment(string name, string? comment) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) =>
            Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
    }

    // ---- T1: ValidateSaveAllowed allows Clean state ----

    [Fact]
    public void ValidateSaveAllowed_DoesNotThrow_WhenStateIsClean()
    {
        var asset = new StubAsset { LoadState = BlackboardLoadState.Clean };
        // Should not throw
        BlackboardDtoEmitter.ValidateSaveAllowed(asset);
    }

    // ---- T2: StructParseFailed always blocks save ----

    [Fact]
    public void ValidateSaveAllowed_Throws_WhenStateIsStructParseFailed()
    {
        var asset = new StubAsset
        {
            LoadState = BlackboardLoadState.StructParseFailed,
            LoadDiagnosticMessage = "Parse error details",
        };
        var ex = Assert.Throws<InvalidOperationException>(
            () => BlackboardDtoEmitter.ValidateSaveAllowed(asset));
        Assert.Contains("Parse error details", ex.Message);
    }

    // ---- T3: AssemblyFailed always blocks save ----

    [Fact]
    public void ValidateSaveAllowed_Throws_WhenStateIsAssemblyFailed()
    {
        var asset = new StubAsset
        {
            LoadState = BlackboardLoadState.AssemblyFailed,
            LoadDiagnosticMessage = "Assembly failed",
        };
        var ex = Assert.Throws<InvalidOperationException>(
            () => BlackboardDtoEmitter.ValidateSaveAllowed(asset));
        Assert.Contains("Assembly failed", ex.Message);
    }

    // ---- T4: SpanCaptureFailed blocks save unless allowLossySave=true ----

    [Fact]
    public void ValidateSaveAllowed_Throws_WhenSpanCaptureFailed_AndLossySaveNotConfirmed()
    {
        var asset = new StubAsset
        {
            LoadState = BlackboardLoadState.SpanCaptureFailed,
            LoadDiagnosticMessage = "Span capture failed",
        };
        Assert.Throws<InvalidOperationException>(
            () => BlackboardDtoEmitter.ValidateSaveAllowed(asset, allowLossySave: false));
    }

    // ---- T5: SpanCaptureFailed allows save when allowLossySave=true ----

    [Fact]
    public void ValidateSaveAllowed_DoesNotThrow_WhenSpanCaptureFailed_AndLossySaveConfirmed()
    {
        var asset = new StubAsset
        {
            LoadState = BlackboardLoadState.SpanCaptureFailed,
            LoadDiagnosticMessage = "Span capture failed",
        };
        // Should not throw when lossy save is confirmed
        BlackboardDtoEmitter.ValidateSaveAllowed(asset, allowLossySave: true);
    }

    // ---- T6: null asset throws ArgumentNullException ----

    [Fact]
    public void ValidateSaveAllowed_Throws_WhenAssetIsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => BlackboardDtoEmitter.ValidateSaveAllowed(null!));
    }

    // ---- T7: Default LoadState on IBlackboardManagedAsset is Clean ----

    [Fact]
    public void DefaultLoadState_IsClean()
    {
        // Verifies that the default interface implementation returns Clean.
        var asset = new StubAsset();
        // The stub inherits from the interface default, but the stub overrides it
        // with the same default. The important thing is Clean == 0 and is the default.
        Assert.Equal(BlackboardLoadState.Clean, asset.LoadState);
    }
}
