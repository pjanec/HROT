using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.BTree;

/// <summary>
/// PU-102 compile-time / reflection test: BehaviorTreeAssetDto must NOT contain
/// any runtime-only fields listed in design §5.2.
///
/// Runtime-only fields excluded per §5.2:
///   Blob / Metadata, KernelBlobIndex / FlatIndex, derived *PinId,
///   _syncNodeMeta, _aliases runtime hydration, LoadDiagnosticMessage,
///   IsDirty, Changed, IsBreakpoint.
/// </summary>
public sealed class BTreeDtoRuntimeFieldExclusionTests
{
    private static readonly string[] RuntimeOnlyNames =
    {
        // Kernel/runtime data
        "Blob", "Metadata",
        "KernelBlobIndex", "FlatIndex",
        // Derived pin IDs
        "OutputPinId", "InputPinId",
        "HiddenOutputPinId", "HiddenInputPinId",
        // Internal sync metadata
        "_syncNodeMeta", "SyncNodeMeta",
        // Runtime hydration
        "_aliases",
        // Editor session state
        "LoadDiagnosticMessage", "IsDirty", "IsBreakpoint",
        // Events (delegates)
        "Changed",
    };

    [Fact]
    public void BehaviorTreeAssetDto_DoesNotContainRuntimeOnlyFields()
    {
        var members = typeof(BehaviorTreeAssetDto)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(m => m.Name)
            .ToHashSet();

        foreach (var forbidden in RuntimeOnlyNames)
        {
            members.Should().NotContain(forbidden,
                because: $"'{forbidden}' is a runtime-only field excluded per design §5.2");
        }
    }

    [Fact]
    public void BTreeNodeDto_DoesNotContainRuntimeOnlyFields()
    {
        var nodeTypes = new[]
        {
            typeof(BTreeNodeDto),
            typeof(BTreeActionNodeDto),
            typeof(BTreeConditionNodeDto),
            typeof(BTreeWaitNodeDto),
            typeof(BTreeSubtreeNodeDto),
            typeof(BTreeRepeaterNodeDto),
            typeof(BTreeCooldownNodeDto),
            typeof(BTreeRootNodeDto),
            typeof(BTreeSequenceNodeDto),
        };

        foreach (var t in nodeTypes)
        {
            var members = t
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(m => m.Name)
                .ToHashSet();

            foreach (var forbidden in RuntimeOnlyNames)
            {
                members.Should().NotContain(forbidden,
                    because: $"'{forbidden}' is runtime-only and must be excluded from {t.Name}");
            }
        }
    }

    [Fact]
    public void BTreePillDto_DoesNotContainIsBreakpoint()
    {
        var members = typeof(BTreePillDto)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(m => m.Name)
            .ToHashSet();

        members.Should().NotContain("IsBreakpoint",
            because: "IsBreakpoint is a session-only flag excluded per §5.2");
    }

    [Fact]
    public void BlackboardBlockDto_ContainsExpectedTypeRefFields()
    {
        // §5.4 type-ref must express TypeId + IsArray + FixedLength + DefaultValueJson
        var typeRef = typeof(BlackboardTypeRefDto);
        typeRef.GetProperty("TypeId").Should().NotBeNull("TypeId required per §5.4");
        typeRef.GetProperty("IsArray").Should().NotBeNull("IsArray required per §5.4");
        typeRef.GetProperty("FixedLength").Should().NotBeNull("FixedLength required per §5.4");

        var varDto = typeof(BlackboardVariableDto);
        varDto.GetProperty("DefaultValueJson").Should().NotBeNull("DefaultValueJson required per §5.4");
        varDto.GetProperty("Comment").Should().NotBeNull("Comment required per §5.4");
    }
}
