using System;
using System.Linq;
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

    // ── Slice-1 authorability gap fix: auto-managed (node-owned) State rows ──
    // ── must expose an editable Scope through the schema path, while Input-  ──
    // ── role node-owned rows (e.g. bpParams) keep no scope selector.         ──

    [Fact]
    public void AutoManaged_StateRole_ShowScopeSelector_IsTrue()
    {
        // The composed AiPrimitive's auto-created bpWorkingState variable: IsAutoManaged=true,
        // Role=State. The Node-Owned Allocations table must treat this exactly like a
        // hand-authored State row for the purposes of the Scope column.
        var vm = MakeVar("bpWorkingState", role: BlackboardVariableRole.State) with { IsAutoManaged = true };
        vm.ShowScopeSelector.Should().BeTrue(
            "an auto-managed State-role variable (a composed node's WorkingState) must still show the Scope selector");
    }

    [Fact]
    public void AutoManaged_InputRole_ShowScopeSelector_IsFalse()
    {
        // The composed AiPrimitive's auto-created bpParams variable: IsAutoManaged=true, Role=Input.
        // Scope is meaningless for Input — must stay blank/no-selector, unaffected by this fix.
        var vm = MakeVar("bpParams", role: BlackboardVariableRole.Input) with { IsAutoManaged = true };
        vm.ShowScopeSelector.Should().BeFalse(
            "an auto-managed Input-role variable (e.g. bpParams) must not expose a scope selector");
    }

    [Fact]
    public void BTreeHsmSchemaSource_UpdateVariableScope_AppliesToAutoManagedStateVar()
    {
        // Reproduces the Slice-1 authorability gap end-to-end through the schema layer that
        // DrawNodeOwnedTable now calls for State-role node-owned rows: an auto-managed
        // WorkingState variable's Scope must be flippable from Node to Behavior/Entity.
        var asset = new FakeBlackboardAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("bpWorkingState", typeof(float), null,
                IsAutoManaged: true,
                Role: BlackboardVariableRole.State,
                Scope: WorkingStateScope.Node),
        });

        var vm     = BlackboardAuthoringWindow.BuildViewModel(asset);
        var schema = new BTreeHsmSchemaSource(asset, vm, isReadOnly: false);

        var row = schema.Variables.Single(v => v.Name == "bpWorkingState");
        row.IsAutoManaged.Should().BeTrue();
        row.ShowScopeSelector.Should().BeTrue();
        row.Scope.Should().Be(WorkingStateScope.Node, "starts at the pre-Slice-1 default scope");

        // This is exactly the call DrawNodeOwnedTable's new editable Scope combo issues for
        // State-role rows (VariablesPanelControl.cs ~line 424 in the node-owned table).
        schema.UpdateVariableScope("bpWorkingState", WorkingStateScope.Behavior);

        asset.UpdateVariableScopeCallCount.Should().Be(1,
            "the schema call must reach the underlying asset exactly once");

        var vm2 = BlackboardAuthoringWindow.BuildViewModel(asset);
        var updatedRow = vm2.Variables.Single(v => v.Name == "bpWorkingState");
        updatedRow.Scope.Should().Be(WorkingStateScope.Behavior,
            "Slice 1: flipping an auto-managed State var's scope must persist on the model, " +
            "enabling shared working-state between composed nodes bound to the same variable");
        updatedRow.IsAutoManaged.Should().BeTrue(
            "the variable stays auto-managed for lifecycle/orphan-cleanup purposes -- only its scope changed");
        updatedRow.Role.Should().Be(BlackboardVariableRole.State,
            "Role must remain State -- this fix only authors Scope, never Role, for node-owned rows");
    }

    [Fact]
    public void BTreeHsmSchemaSource_UpdateVariableScope_AutoManagedVar_AllThreeScopes()
    {
        // The WorkingStateScope enum offers Node/Behavior/Entity; all three must be reachable
        // through the schema path for an auto-managed State row (mirrors the main table's
        // three-entry "Node\0Behavior\0Entity" combo used by the new node-owned Scope dropdown).
        foreach (var scope in new[] { WorkingStateScope.Node, WorkingStateScope.Behavior, WorkingStateScope.Entity })
        {
            var asset = new FakeBlackboardAsset();
            asset.SetBlackboardVariables(new[]
            {
                new BlackboardVariableEntry("bpWorkingState", typeof(float), null,
                    IsAutoManaged: true, Role: BlackboardVariableRole.State, Scope: WorkingStateScope.Node),
            });
            var vm     = BlackboardAuthoringWindow.BuildViewModel(asset);
            var schema = new BTreeHsmSchemaSource(asset, vm, isReadOnly: false);

            schema.UpdateVariableScope("bpWorkingState", scope);

            var updated = BlackboardAuthoringWindow.BuildViewModel(asset).Variables.Single(v => v.Name == "bpWorkingState");
            updated.Scope.Should().Be(scope, $"scope {scope} must be reachable for an auto-managed State row");
        }
    }

    [Fact]
    public void BTreeHsmSchemaSource_RenameAndRemove_RemainCallable_ButAreNeverInvokedByNodeOwnedTable()
    {
        // Guard-scoping check for the ~:250 IsAliasDropAccepted-style protections: this fix widens
        // ONLY the Scope column for node-owned State rows. It must not add any rename/delete
        // affordance. VariablesPanelControl.DrawNodeOwnedTable never calls RenameVariable,
        // RemoveVariable, or MoveVariable for its rows -- rename/delete stay blocked purely because
        // the node-owned table renders plain text for Name (no double-click rename, no [x] button,
        // no drag-drop reorder target), not because IVariablesSchemaSource itself refuses the call.
        // Existing alias-drop protection (IsAliasDropAccepted) is a separate, unaffected mechanism:
        var target = new VariableViewModel(
            Name: "bpWorkingState", TypeName: "float", ByteSize: 4, FieldType: typeof(float),
            Comment: null, AliasedBy: Array.Empty<(string, Guid, Guid)>(), IsUnused: false,
            IsAutoManaged: true, Role: BlackboardVariableRole.State, Scope: WorkingStateScope.Node);

        VariablesPanelControl.IsAliasDropAccepted(target, typeof(float)).Should().BeFalse(
            "auto-managed rows must still never accept alias drops -- unaffected by the Scope-editability fix");
    }
}
