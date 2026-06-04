using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Hsm;

/// <summary>
/// PU-103 compile-time / reflection test: HsmAssetDto must NOT contain
/// any runtime-only fields listed in design §5.2.
/// </summary>
public sealed class HsmDtoRuntimeFieldExclusionTests
{
    private static readonly string[] RuntimeOnlyNames =
    {
        "Blob", "Metadata",
        "FlatIndex", "KernelBlobIndex",
        "OutputPinId", "InputPinId",
        "HiddenOutputPinId", "HiddenInputPinId",
        "LoadDiagnosticMessage", "IsDirty", "IsBreakpoint",
        "Changed",
        "_aliases",
    };

    [Fact]
    public void HsmAssetDto_DoesNotContainRuntimeOnlyFields()
    {
        var members = typeof(HsmAssetDto)
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
    public void StateNodeDto_DoesNotContainFlatIndex()
    {
        typeof(StateNodeDto).GetProperty("FlatIndex").Should().BeNull(
            because: "FlatIndex is runtime-only and excluded per §5.2");
    }

    [Fact]
    public void TransitionNodeDto_DoesNotContainEventId_OrFlatIndex()
    {
        // EventId (ushort runtime-only) is excluded; EventName (string) is persisted
        typeof(TransitionNodeDto).GetProperty("EventId").Should().BeNull(
            because: "EventId is runtime-only; persist EventName instead");
        typeof(TransitionNodeDto).GetProperty("FlatIndex").Should().BeNull(
            because: "FlatIndex is runtime-only per §5.2");
    }

    [Fact]
    public void TransitionNodeDto_PersistsWaypoints()
    {
        typeof(TransitionNodeDto).GetProperty("Waypoints").Should().NotBeNull(
            because: "transition waypoints are persisted per §5.2");
    }

    [Fact]
    public void HsmBlackboardBlockDto_ContainsExpectedTypeRefFields()
    {
        var typeRef = typeof(HsmBlackboardTypeRefDto);
        typeRef.GetProperty("TypeId").Should().NotBeNull();
        typeRef.GetProperty("IsArray").Should().NotBeNull();
        typeRef.GetProperty("FixedLength").Should().NotBeNull();

        var varDto = typeof(HsmBlackboardVariableDto);
        varDto.GetProperty("DefaultValueJson").Should().NotBeNull();
        varDto.GetProperty("Comment").Should().NotBeNull();
    }
}
