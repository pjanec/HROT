using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.HotReload;
using Hrot.Hsm.Editor.HotReload;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmQuickReloadHasherTests
{
    private static HsmDefinitionBlob BlobWithHashes(uint structureHash, uint paramHash)
    {
        var blob = new HsmDefinitionBlob();
        blob.Header.StructureHash = structureHash;
        blob.Header.ParameterHash = paramHash;
        return blob;
    }

    [Fact]
    public void Classify_identical_blobs_returns_cosmetic()
    {
        var blob = BlobWithHashes(structureHash: 42, paramHash: 7);
        HsmQuickReloadHasher.Classify(blob, blob).Should().Be(HotReloadTier.Cosmetic);
    }

    [Fact]
    public void Classify_different_param_hash_returns_soft()
    {
        var previous = BlobWithHashes(structureHash: 10, paramHash: 1);
        var next     = BlobWithHashes(structureHash: 10, paramHash: 2);
        HsmQuickReloadHasher.Classify(previous, next).Should().Be(HotReloadTier.Soft);
    }

    [Fact]
    public void Classify_different_structure_hash_returns_hard()
    {
        var previous = BlobWithHashes(structureHash: 1, paramHash: 5);
        var next     = BlobWithHashes(structureHash: 2, paramHash: 5);
        HsmQuickReloadHasher.Classify(previous, next).Should().Be(HotReloadTier.Hard);
    }

    [Fact]
    public void Classify_both_hashes_different_returns_hard()
    {
        var previous = BlobWithHashes(structureHash: 1, paramHash: 1);
        var next     = BlobWithHashes(structureHash: 2, paramHash: 2);
        HsmQuickReloadHasher.Classify(previous, next).Should().Be(HotReloadTier.Hard);
    }

    [Fact]
    public void Classify_zero_hashes_returns_cosmetic()
    {
        var previous = new HsmDefinitionBlob();
        var next     = new HsmDefinitionBlob();
        HsmQuickReloadHasher.Classify(previous, next).Should().Be(HotReloadTier.Cosmetic);
    }
}
