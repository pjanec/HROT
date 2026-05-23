using Hrot.Editor.AiShared.Identity;
using System.Text;

namespace Hrot.Editor.AiShared.Tests.Identity;

public sealed class AssetIdHashTests
{
    [Fact]
    public void Fnv1a32_EmptyInput_ReturnsOffsetBasis()
    {
        int result = AssetIdHash.Fnv1a32(ReadOnlySpan<byte>.Empty);
        // FNV-1a for empty input is the offset basis (2166136261) interpreted as int
        Assert.Equal(unchecked((int)2166136261u), result);
    }

    [Fact]
    public void Fnv1a32_SingleByte_DeterministicResult()
    {
        byte[] input = [0x61]; // 'a'
        int r1 = AssetIdHash.Fnv1a32(input);
        int r2 = AssetIdHash.Fnv1a32(input);
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void Fnv1a32_SameBytes_SameHash()
    {
        byte[] a = Encoding.UTF8.GetBytes("Hello");
        byte[] b = Encoding.UTF8.GetBytes("Hello");
        Assert.Equal(AssetIdHash.Fnv1a32(a), AssetIdHash.Fnv1a32(b));
    }

    [Fact]
    public void Fnv1a32_DifferentBytes_DifferentHash()
    {
        byte[] a = Encoding.UTF8.GetBytes("Hello");
        byte[] b = Encoding.UTF8.GetBytes("World");
        Assert.NotEqual(AssetIdHash.Fnv1a32(a), AssetIdHash.Fnv1a32(b));
    }

    [Fact]
    public void Fnv1a32_GuidBytes_DeterministicResult()
    {
        var guid = Guid.NewGuid();
        byte[] bytes = guid.ToByteArray();
        int r1 = AssetIdHash.Fnv1a32(bytes);
        int r2 = AssetIdHash.Fnv1a32(bytes);
        Assert.Equal(r1, r2);
    }
}
