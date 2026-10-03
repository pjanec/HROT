using Fdp.Toolkit.Behavior;
using Hrot.Editor.AiComposition;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐ S8b-2 / <c>CE-2025</c> (<c>DESIGN_Unified_Behaviour_Run</c> "S8b design" U2) — the ONE editor answer to "what does child X
/// take?", shared by the BTree, HSM and Behaviour Task pickers: it reads the runtime registry and spells the type the way an
/// asset stores a type id.
/// </summary>
public sealed class ChildInputTypesTests
{
    public struct Outer { public struct NestedInputs { public float Speed; } }

    private static BehaviorRegistry Registry()
    {
        var r = new BehaviorRegistry();
        // a generated child: a manifest + its Inputs struct (nested, so the spelling is exercised)
        r.Register("Generated", new BehaviorDefinition
        {
            Name = "Generated", BrainTier = BehaviorConstants.BrainTierBTree,
            ManagedBlackboardVariables = new[] { new ManagedBlackboardVariable("Speed", typeof(float), 0) },
            JsonParamsDtoType = typeof(Outer.NestedInputs),
        });
        // a generated child with no Inputs half
        r.Register("NoInputs", new BehaviorDefinition
        {
            Name = "NoInputs", BrainTier = BehaviorConstants.BrainTierBTree,
            ManagedBlackboardVariables = System.Array.Empty<ManagedBlackboardVariable>(),
        });
        return r;
    }

    [Fact]
    public void ALookup_SpellsTheChildsInputsType_AsAnAssetTypeId()
    {
        var reg = Registry();
        var lookup = ChildInputTypes.Lookup(() => reg);
        Assert.Equal("Hrot.Editor.Tests.ChildInputTypesTests.Outer.NestedInputs", lookup("Generated"));
        Assert.Null(lookup("NoInputs"));
        Assert.Null(lookup("NotRegistered"));
        Assert.Null(lookup(""));
    }

    /// <summary>⭐ The registry is read when asked: a child registered after the lookup was built is found.</summary>
    [Fact]
    public void ALookup_ReadsTheRegistryWhenAsked()
    {
        BehaviorRegistry? reg = null;
        var lookup = ChildInputTypes.Lookup(() => reg);
        Assert.Null(lookup("Generated"));
        reg = Registry();
        Assert.NotNull(lookup("Generated"));
    }
}
