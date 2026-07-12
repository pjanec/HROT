using System;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

/// <summary>
/// S3-1 editor tests: VariablesPanel scope-selector gating flag.
/// Verifies that VariableViewModel.ShowScopeSelector is true only when Role == State.
/// Uses the view-model (headless) — no ImGui required.
/// </summary>
public sealed class RoleScopePanelTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static VariableViewModel MakeVar(
        string name,
        BlackboardVariableRole role  = BlackboardVariableRole.Input,
        WorkingStateScope      scope = WorkingStateScope.Node) =>
        new VariableViewModel(
            Name:         name,
            TypeName:     "float",
            ByteSize:     4,
            FieldType:    typeof(float),
            Comment:      null,
            AliasedBy:    Array.Empty<(string, Guid, Guid)>(),
            IsUnused:     false,
            IsAutoManaged: false,
            IsReadOnly:   false,
            Role:         role,
            Scope:        scope);

    // ── ShowScopeSelector gating ─────────────────────────────────────────────

    [Fact]
    public void VariablesPanel_ShowsScopeSelector_OnlyForState_Input()
    {
        var vm = MakeVar("inputVar", role: BlackboardVariableRole.Input);
        vm.ShowScopeSelector.Should().BeFalse(
            "scope selector must be hidden when Role == Input");
    }

    [Fact]
    public void VariablesPanel_ShowsScopeSelector_OnlyForState_State()
    {
        var vm = MakeVar("stateVar", role: BlackboardVariableRole.State);
        vm.ShowScopeSelector.Should().BeTrue(
            "scope selector must be shown when Role == State");
    }

    [Fact]
    public void VariablesPanel_ShowsScopeSelector_Default_IsInput()
    {
        // Default-constructed entry must have Role=Input → ShowScopeSelector=false.
        var vm = MakeVar("defaultVar");
        vm.Role.Should().Be(BlackboardVariableRole.Input, "default Role must be Input");
        vm.ShowScopeSelector.Should().BeFalse("default entry must not show scope selector");
    }

    [Fact]
    public void VariablesPanel_ShowsScopeSelector_State_AllScopes()
    {
        foreach (var scope in new[] { WorkingStateScope.Node, WorkingStateScope.Behavior, WorkingStateScope.Entity })
        {
            var vm = MakeVar("stateVar", role: BlackboardVariableRole.State, scope: scope);
            vm.ShowScopeSelector.Should().BeTrue(
                $"ShowScopeSelector must be true for State role regardless of Scope={scope}");
            vm.Scope.Should().Be(scope, "Scope value must be preserved in view-model");
        }
    }

    // ── BuildViewModel populates Role/Scope from entry ───────────────────────

    [Fact]
    public void BuildViewModel_PopulatesRoleAndScope_FromEntry()
    {
        var asset = new FakeBlackboardAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("inputVar",  typeof(float), null,
                Role: BlackboardVariableRole.Input,
                Scope: WorkingStateScope.Node),
            new BlackboardVariableEntry("stateVar",  typeof(float), null,
                Role: BlackboardVariableRole.State,
                Scope: WorkingStateScope.Behavior),
        });

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        var inputRow = vm.Variables[0];
        inputRow.Role.Should().Be(BlackboardVariableRole.Input);
        inputRow.Scope.Should().Be(WorkingStateScope.Node);
        inputRow.ShowScopeSelector.Should().BeFalse("Input role → no scope selector");

        var stateRow = vm.Variables[1];
        stateRow.Role.Should().Be(BlackboardVariableRole.State);
        stateRow.Scope.Should().Be(WorkingStateScope.Behavior);
        stateRow.ShowScopeSelector.Should().BeTrue("State role → scope selector shown");
    }
}
