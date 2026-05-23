using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.HotReload;
using Hrot.Editor.AiShared.HotReload;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// Tests for BT-S1-19: BTreeQuickReloadHasher.
/// </summary>
public sealed class BTreeQuickReloadHasherTests
{
    private static BehaviorTreeBlob MakeBlob(int structureHash, int paramHash) =>
        new BehaviorTreeBlob
        {
            TreeName      = "T",
            StructureHash = structureHash,
            ParamHash     = paramHash,
        };

    [Fact]
    public void Classify_identical_blobs_returns_cosmetic()
    {
        var blob = MakeBlob(structureHash: 42, paramHash: 7);
        BTreeQuickReloadHasher.Classify(blob, blob).Should().Be(HotReloadTier.Cosmetic);
    }

    [Fact]
    public void Classify_different_structure_hash_returns_hard()
    {
        var previous = MakeBlob(structureHash: 1, paramHash: 5);
        var next     = MakeBlob(structureHash: 2, paramHash: 5);
        BTreeQuickReloadHasher.Classify(previous, next).Should().Be(HotReloadTier.Hard);
    }

    [Fact]
    public void Classify_same_structure_different_param_hash_returns_soft()
    {
        var previous = MakeBlob(structureHash: 10, paramHash: 1);
        var next     = MakeBlob(structureHash: 10, paramHash: 2);
        BTreeQuickReloadHasher.Classify(previous, next).Should().Be(HotReloadTier.Soft);
    }
}
