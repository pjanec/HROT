using System;
using System.Collections.Generic;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

/// <summary>
/// <b>U-5 / <c>BP-230</c> — the contract half, tested where the contract lives.</b>
///
/// <para>
/// ⛔ <b>The defect was written into the interface itself.</b> <c>UpdateVariableRole</c> and
/// <c>UpdateVariableScope</c> shipped as <c>{ }</c> default bodies — <i>"so existing mock
/// implementations continue to compile"</i> — and <c>BlueprintVariableSchemaSource</c> took the offer.
/// <c>VariablesPanelControl</c> gates its Role combo on <c>IsReadOnly</c> alone, which is
/// <c>false</c> there, so the panel drew a live combo and discarded the designer's edit.
/// ⭐ <b>A default body is the interface volunteering to lie on an implementer's behalf</b> — trap #5,
/// one level up from where it usually appears.
/// </para>
///
/// <para>
/// ⚠ <b>These tests exist because the first run of Batch 46 left <c>Hrot.Editor.AiShared</c> at 1213 —
/// unmoved — after changing this very contract.</b> The handoff predicted the gate would move; it did
/// not, and the reason was that the change had no coverage in the assembly it landed in. A contract
/// change tested only through its consumers is a contract change nobody is watching.
/// </para>
/// </summary>
public sealed class RoleScopeCapabilityTests
{
    /// <summary>A source that answers "no" and inherits the interface's defaults.</summary>
    private sealed class CannotEditSource : IVariablesSchemaSource
    {
        public bool IsReadOnly => false;
        public bool SupportsRoleScopeEditing => false;
        public IReadOnlyList<VariableViewModel> Variables => Array.Empty<VariableViewModel>();
        public string? GetRefactorKey(string variableName) => null;
        public void AddVariable(BlackboardVariableEntry entry) { }
        public void RemoveVariable(string name) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
        public void RenameVariable(string oldName, string newName) { }
        // ⭐ 98a — the interface has NO default body on purpose (U-5/BP-230: "a default body is
        //   the interface volunteering to lie on an implementer's behalf"), so every double must
        //   answer. ⚠ These doubles do not exercise the write, so they RECORD rather than no-op —
        //   a silent { } here would be the very shape the rule exists to stop.
        public System.Collections.Generic.List<(string Name, string? Json)> DefaultWrites { get; } = new();
        public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
            => DefaultWrites.Add((name, defaultValueJson));
        // ⭐ 99a — the interface has NO default body on purpose (U-5/BP-230: "a default body is
        //   the interface volunteering to lie on an implementer's behalf"), so every double answers.
        //   ⚠ These doubles do not exercise the Properties form, so they RECORD rather than no-op —
        //   a silent { } here is the very shape the rule exists to stop.
        public System.Collections.Generic.List<(string Name, Hrot.Editor.AiShared.Variables.VariablePropertyValues Values)> PropertyWrites { get; } = new();
        public void UpdateVariableProperties(
            string name, Hrot.Editor.AiShared.Variables.VariablePropertyValues values)
            => PropertyWrites.Add((name, values));
        public Hrot.Editor.AiShared.Variables.DeclarationPropertySnapshot? ReadVariableProperties(string name)
            => null;
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements
            => Array.Empty<UnboundRequirementViewModel>();
        public void AddAlias(string name, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId) { }
        public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;
    }

    /// <summary>A source that answers "yes" and implements both.</summary>
    private sealed class CanEditSource : CannotEditSourceBase
    {
        public override bool SupportsRoleScopeEditing => true;
        public BlackboardVariableRole? LastRole { get; private set; }
        public WorkingStateScope? LastScope { get; private set; }
        public override void UpdateVariableRole(string name, BlackboardVariableRole role) => LastRole = role;
        public override void UpdateVariableScope(string name, WorkingStateScope scope) => LastScope = scope;
    }

    public abstract class CannotEditSourceBase : IVariablesSchemaSource
    {
        public bool IsReadOnly => false;
        public virtual bool SupportsRoleScopeEditing => false;
        public IReadOnlyList<VariableViewModel> Variables => Array.Empty<VariableViewModel>();
        public string? GetRefactorKey(string variableName) => null;
        public void AddVariable(BlackboardVariableEntry entry) { }
        public void RemoveVariable(string name) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
        public void RenameVariable(string oldName, string newName) { }
        // ⭐ 98a — the interface has NO default body on purpose (U-5/BP-230: "a default body is
        //   the interface volunteering to lie on an implementer's behalf"), so every double must
        //   answer. ⚠ These doubles do not exercise the write, so they RECORD rather than no-op —
        //   a silent { } here would be the very shape the rule exists to stop.
        public System.Collections.Generic.List<(string Name, string? Json)> DefaultWrites { get; } = new();
        public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
            => DefaultWrites.Add((name, defaultValueJson));
        // ⭐ 99a — the interface has NO default body on purpose (U-5/BP-230: "a default body is
        //   the interface volunteering to lie on an implementer's behalf"), so every double answers.
        //   ⚠ These doubles do not exercise the Properties form, so they RECORD rather than no-op —
        //   a silent { } here is the very shape the rule exists to stop.
        public System.Collections.Generic.List<(string Name, Hrot.Editor.AiShared.Variables.VariablePropertyValues Values)> PropertyWrites { get; } = new();
        public void UpdateVariableProperties(
            string name, Hrot.Editor.AiShared.Variables.VariablePropertyValues values)
            => PropertyWrites.Add((name, values));
        public Hrot.Editor.AiShared.Variables.DeclarationPropertySnapshot? ReadVariableProperties(string name)
            => null;
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public int CountNodesReferencingVariable(string name) => 0;
        // ⚠ Declaring these implicitly means the interface's default body is NOT used for this type,
        // so they must refuse here themselves. (Delegating via a cast to the interface would dispatch
        // straight back to this member — an infinite recursion, not a fallback.)
        public virtual void UpdateVariableRole(string name, BlackboardVariableRole role)
            => throw new NotSupportedException(nameof(UpdateVariableRole));
        public virtual void UpdateVariableScope(string name, WorkingStateScope scope)
            => throw new NotSupportedException(nameof(UpdateVariableScope));
        public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements
            => Array.Empty<UnboundRequirementViewModel>();
        public void AddAlias(string name, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId) { }
        public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;
    }

    /// <summary>
    /// ⭐⭐ <b>The default bodies REFUSE rather than discarding.</b> ⛔ Before U-5 both of these
    /// returned silently, and the caller had no way to tell a successful edit from a swallowed one.
    /// </summary>
    [Fact]
    public void ASourceThatCannotEdit_RefusesInsteadOfSwallowing()
    {
        IVariablesSchemaSource src = new CannotEditSource();

        src.SupportsRoleScopeEditing.Should().BeFalse();
        src.Invoking(s => s.UpdateVariableRole("x", BlackboardVariableRole.State))
           .Should().Throw<NotSupportedException>();
        src.Invoking(s => s.UpdateVariableScope("x", WorkingStateScope.Entity))
           .Should().Throw<NotSupportedException>();
    }

    /// <summary>A source that claims the capability and implements it is called normally.</summary>
    [Fact]
    public void ASourceThatCanEdit_AppliesTheChange()
    {
        var src = new CanEditSource();

        ((IVariablesSchemaSource)src).SupportsRoleScopeEditing.Should().BeTrue();
        src.UpdateVariableRole("x", BlackboardVariableRole.State);
        src.UpdateVariableScope("x", WorkingStateScope.Entity);

        src.LastRole.Should().Be(BlackboardVariableRole.State);
        src.LastScope.Should().Be(WorkingStateScope.Entity);
    }

    /// <summary>
    /// ⭐ <b><c>SupportsRoleScopeEditing</c> has NO default body, on purpose.</b> ⚠ Asserted by
    /// reflection rather than trusted: if someone gives it a default *"so mocks keep compiling"* — the
    /// exact sentence that produced this defect — silence becomes available again.
    /// </summary>
    [Fact]
    public void TheCapabilityMemberIsAbstract_SoEveryImplementerMustAnswer()
    {
        var prop = typeof(IVariablesSchemaSource)
            .GetProperty(nameof(IVariablesSchemaSource.SupportsRoleScopeEditing));

        prop.Should().NotBeNull();
        prop!.GetMethod!.IsAbstract.Should().BeTrue(
            "a default body would let an implementer stay silent about whether the edit lands");
    }
}
