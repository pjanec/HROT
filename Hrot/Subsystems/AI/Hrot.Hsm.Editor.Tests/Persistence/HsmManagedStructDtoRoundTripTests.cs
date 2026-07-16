using System;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Persistence;

/// <summary>
/// Mirrors <c>Hrot.BTree.Editor.Tests.Persistence.ManagedStructDtoLoadTests</c>: proves that a
/// struct-typed HSM blackboard variable (added by a designer via the Add-Variable dropdown, see
/// <see cref="Hrot.Editor.AiShared.Blackboard.BlackboardTypeChoiceBuilder"/>) survives the
/// mapper's ToDto -&gt; FromDto round trip -- i.e. a save + re-open of the asset -- without its
/// CLR type collapsing to <see cref="object"/>.
/// <see cref="HsmAssetMapper.ResolveClrType"/> already searches loaded assemblies by full name
/// (mirroring <c>BehaviorTreeAssetMapper.ResolveFromLoadedAssemblies</c>), so this locks in that
/// existing behavior for HSM assets too.
/// </summary>
public sealed class HsmManagedStructDtoRoundTripTests
{
    /// <summary>Top-level struct in the test assembly, used as a managed-variable DTO type.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HsmProbeDto
    {
        public int A;
        public int B;
    }

    private static HsmAssetDto MakeManagedDto(string typeId) => new()
    {
        AssetId            = new Guid("dd000001-0000-0000-0000-000000000000"),
        Name               = "HsmManagedStructDtoProbe",
        TargetNamespace    = "Hrot.AI.Behaviors.Hsms",
        BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
        Blackboard = new HsmBlackboardBlockDto
        {
            Managed   = true,
            TypeName  = "",
            Variables =
            {
                new HsmBlackboardVariableDto { Name = "counter", Type = new HsmBlackboardTypeRefDto { TypeId = typeId } },
            },
        },
    };

    [Fact]
    public void FromDto_StructDtoVariable_ResolvesRealType_NotObject()
    {
        var asset = HsmAssetMapper.FromDto(MakeManagedDto(typeof(HsmProbeDto).FullName!));

        var v = asset.BlackboardVariables.Single();
        v.FieldType.Should().Be(typeof(HsmProbeDto),
            "the struct DTO type must resolve from a loaded assembly, not fall back to object");
        v.FieldType.Should().NotBe(typeof(object));
    }

    /// <summary>
    /// A struct-typed blackboard variable added by a designer via the Add-Variable dropdown must
    /// round-trip through the mapper's ToDto -> FromDto cycle without losing the struct's
    /// identity, for BOTH Input and State roles.
    /// </summary>
    [Theory]
    [InlineData(BlackboardVariableRole.Input)]
    [InlineData(BlackboardVariableRole.State)]
    public void ToDtoThenFromDto_StructTypedVariable_PreservesType_ForBothRoles(BlackboardVariableRole role)
    {
        var loaded = HsmAssetMapper.FromDto(MakeManagedDto(typeof(HsmProbeDto).FullName!));
        loaded.UpdateVariableRole("counter", role);

        var dto = HsmAssetMapper.ToDto(loaded);
        var reloaded = HsmAssetMapper.FromDto(dto);

        var roundTripped = reloaded.BlackboardVariables.Single(v => v.Name == "counter");
        roundTripped.FieldType.Should().Be(typeof(HsmProbeDto),
            "the struct type must survive a save/reload round-trip, not fall back to object");
        roundTripped.FieldType.Should().NotBe(typeof(object));
        roundTripped.Role.Should().Be(role, "role (Input/State) must also survive the round-trip");
    }
}
