using System.Collections.Generic;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.BTree;

/// <summary>
/// ⭐⭐ <c>CE-3193</c> — a variable carries no authored kind. Whether it is working state (the block's <c>St</c> half) is
/// derived from the bindings — a binding that names it as its <c>WorkingStateTargetField</c> — on load and at each generator
/// entry (<see cref="WorkingStateVariables"/>). ⛔ HISTORY: <c>RoleScopeRoundTripTests</c> pinned the authored
/// <c>Role</c>/<c>Scope</c> pair this replaced. 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10 ⑦.
/// </summary>
public sealed class WorkingStateDerivationTests
{
    // A legacy file: both variables still carry the old keys. "Shared" is named as a node's working state; "Cursor" is not.
    private const string LegacyBTree = """
    {
      "$meta": { "docType": "Hrot.BTree", "schemaVersion": 2 },
      "AssetId": "00000000-0000-0000-0000-00000000c193",
      "Name": "T",
      "Blackboard": { "Managed": true, "TypeName": "T_Blackboard", "Variables": [
        { "Name": "Cursor", "Type": { "TypeId": "System.Int32" }, "Role": "State", "Scope": "Behavior" },
        { "Name": "Shared", "Type": { "TypeId": "System.Int32" }, "Role": "State", "Scope": "Node" }
      ] },
      "Nodes": [
        { "kind": "Action", "Action": { "MethodFqn": "M", "WorkingStateTargetField": "Shared" },
          "VisualId": "00000000-0000-0000-0000-000000000001" }
      ]
    }
    """;

    [Fact]
    public void ALegacyFilesRoleAndScopeAreIgnored_AndWorkingStateComesFromTheBindings()
    {
        var dto = BTreeJsonServices.Deserialize(LegacyBTree)!;

        var vars = dto.Blackboard.Variables;
        vars.Should().HaveCount(2, "the old keys must not stop the file loading");
        vars.Single("Cursor").IsWorkingState.Should().BeFalse("nothing names Cursor as working state, whatever its old Role said");
        vars.Single("Shared").IsWorkingState.Should().BeTrue("a binding names Shared as its working state");
    }

    [Fact]
    public void SavingWritesNoKind()
    {
        var dto = BTreeJsonServices.Deserialize(LegacyBTree)!;
        string json = BTreeJsonServices.Serialize(dto);

        json.Should().NotContain("\"Role\"").And.NotContain("\"Scope\"").And.NotContain("IsWorkingState",
            "the kind is derived, so nothing about it is persisted");
    }

    [Fact]
    public void AnHsmBindingNamesItsWorkingStateTheSameWay()
    {
        var dto = new HsmAssetDto
        {
            Blackboard = new HsmBlackboardBlockDto
            {
                Managed = true,
                Variables = new List<HsmBlackboardVariableDto>
                {
                    new() { Name = "steps", Type = new HsmBlackboardTypeRefDto { TypeId = "System.Int32" } },
                    new() { Name = "speed", Type = new HsmBlackboardTypeRefDto { TypeId = "System.Single" } },
                },
            },
        };
        dto.States.Add(new StateNodeDto { Name = "A", Activity = new BehaviorActionBindingDto { WorkingStateTargetField = "steps" } });

        WorkingStateVariables.Mark(dto);

        dto.Blackboard.Variables[0].IsWorkingState.Should().BeTrue();
        dto.Blackboard.Variables[1].IsWorkingState.Should().BeFalse();
    }
}

internal static class VariableListExtensions
{
    public static BlackboardVariableDto Single(this List<BlackboardVariableDto> vars, string name)
        => vars.Find(v => v.Name == name) ?? throw new KeyNotFoundException(name);
}
