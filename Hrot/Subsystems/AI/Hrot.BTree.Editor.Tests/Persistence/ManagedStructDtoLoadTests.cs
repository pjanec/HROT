using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Persistence;

/// <summary>
/// Regression (editor crash on opening a managed asset whose variable is a struct DTO).
/// `ResolveClrType` previously used only `Type.GetType(typeId)`, which cannot see types in
/// behavior assemblies (e.g. an action's param DTO), so a managed variable's CLR type fell back
/// to <see cref="object"/>. The Variables panel then called `BlackboardBinPacker.Pack` →
/// `Marshal.SizeOf(typeof(object))`, which throws ArgumentException and crashed the editor.
/// The fix searches loaded assemblies for the DTO struct type.
/// </summary>
public sealed class ManagedStructDtoLoadTests
{
    /// <summary>Top-level struct in the test assembly, used as a managed-variable DTO type.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ProbeDto
    {
        public int A;
        public int B;
    }

    private static BehaviorTreeAssetDto MakeManagedDto(string typeId) => new()
    {
        AssetId            = new System.Guid("cc000001-0000-0000-0000-000000000000"),
        Name               = "ManagedStructDtoProbe",
        TargetNamespace    = "Hrot.AI.Behaviors.Trees",
        BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
        ContextTypeName    = "Fdp.Toolkit.Behavior.BTreeContext",
        Nodes              = new System.Collections.Generic.List<BTreeNodeDto>(),
        Pills              = new System.Collections.Generic.List<BTreePillDto>(),
        Canvas             = new CanvasDto(),
        SubtreeSyncBindings = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<SubtreeSyncBindingDto>>(),
        Suppressions       = new SuppressionsDto(),
        Blackboard = new BlackboardBlockDto
        {
            Managed   = true,
            TypeName  = "",
            Variables = new System.Collections.Generic.List<BlackboardVariableDto>
            {
                new() { Name = "counter", Type = new BlackboardTypeRefDto { TypeId = typeId } },
            },
        },
    };

    [Fact]
    public void FromDto_StructDtoVariable_ResolvesRealType_NotObject()
    {
        // TypeId in the FullName form the editor/mapper writes (top-level here; nested uses '+').
        var asset = BehaviorTreeAssetMapper.FromDto(MakeManagedDto(typeof(ProbeDto).FullName!));

        var v = asset.BlackboardVariables.Single();
        v.FieldType.Should().Be(typeof(ProbeDto),
            "the struct DTO type must resolve from a loaded assembly, not fall back to object");
        v.FieldType.Should().NotBe(typeof(object));
    }

    [Fact]
    public void BuildViewModel_ManagedStructDtoVariable_DoesNotThrow()
    {
        // Reproduces the exact crash path: BuildViewModel -> BlackboardBinPacker.Pack -> GetManagedSize.
        var asset = BehaviorTreeAssetMapper.FromDto(MakeManagedDto(typeof(ProbeDto).FullName!));

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        vm.Should().NotBeNull();
        vm.Variables.Should().ContainSingle(r => r.Name == "counter");
    }
}
