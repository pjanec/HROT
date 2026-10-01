using System.Collections.Generic;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;
using F = Hrot.AiEditor.Persistence.Emit.BTreeBlackboardPackHelper.PackedField;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// ⭐ <c>CE-455</c> — the root block's layout hash that the BTree and HSM registrars now emit into
/// <c>BehaviorDefinition.BlueprintStructureHash</c>. A hot reload restarts a running behaviour when it changes, so it must
/// change whenever a SAME-WIDTH re-layout would reinterpret the bytes, and stay put when nothing moved.
/// </summary>
public sealed class LayoutHashTests
{
    private static readonly F[] IntThenFloat   = { new("A", "System.Int32", 0, 4), new("B", "System.Single", 4, 4) };
    private static readonly F[] FloatThenInt   = { new("B", "System.Single", 0, 4), new("A", "System.Int32", 4, 4) };
    private static readonly F[] RetypedA       = { new("A", "System.Single", 0, 4), new("B", "System.Single", 4, 4) };
    private static readonly F[] RenamedA       = { new("C", "System.Int32", 0, 4), new("B", "System.Single", 4, 4) };

    [Fact]
    public void TheSameLayout_HashesTheSame_AcrossCalls()
        => Assert.Equal(BTreeBlackboardPackHelper.LayoutHash(IntThenFloat),
                        BTreeBlackboardPackHelper.LayoutHash(new[] { new F("A", "System.Int32", 0, 4), new F("B", "System.Single", 4, 4) }));

    [Fact]
    public void ASameWidthReorder_RetypeOrRename_ChangesTheHash()
    {
        ulong h = BTreeBlackboardPackHelper.LayoutHash(IntThenFloat);
        Assert.NotEqual(h, BTreeBlackboardPackHelper.LayoutHash(FloatThenInt));
        Assert.NotEqual(h, BTreeBlackboardPackHelper.LayoutHash(RetypedA));
        Assert.NotEqual(h, BTreeBlackboardPackHelper.LayoutHash(RenamedA));
    }

    [Fact]
    public void TheStateHalf_IsPartOfTheLayout()
    {
        ulong noState = BTreeBlackboardPackHelper.LayoutHash(IntThenFloat);
        var state = new[] { new KeyValuePair<string, string>("Phase", "System.Int32") };
        var stateRetyped = new[] { new KeyValuePair<string, string>("Phase", "System.Single") };
        Assert.NotEqual(noState, BTreeBlackboardPackHelper.LayoutHash(IntThenFloat, state));
        Assert.NotEqual(BTreeBlackboardPackHelper.LayoutHash(IntThenFloat, state),
                        BTreeBlackboardPackHelper.LayoutHash(IntThenFloat, stateRetyped));
    }

    [Fact]
    public void NeverZero_ZeroMeansNoLayout()
        => Assert.NotEqual(0UL, BTreeBlackboardPackHelper.LayoutHash(null));
}
