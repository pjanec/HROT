using Hrot.Blueprints.Core.Compiler.Determinism;
using System.Text;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Unit tests for the FnvHasher determinism utility.
/// </summary>
public sealed class BlueprintIdHashTests
{
    [Fact]
    public void Hash32_EmptyInput_ReturnsOffsetBasis()
    {
        // FNV32 offset basis: 2166136261
        var result = FnvHasher.Hash32(ReadOnlySpan<byte>.Empty);
        Assert.Equal(2166136261u, result);
    }

    [Fact]
    public void Hash64_EmptyInput_ReturnsOffsetBasis()
    {
        // FNV64 offset basis: 14695981039346656037
        var result = FnvHasher.Hash64(ReadOnlySpan<byte>.Empty);
        Assert.Equal(14695981039346656037UL, result);
    }

    [Fact]
    public void Hash32_IsDeterministic()
    {
        var data = Encoding.UTF8.GetBytes("MoveToAndFire;AiPrimitive;x|System.Single|0|4;");
        Assert.Equal(FnvHasher.Hash32(data), FnvHasher.Hash32(data));
    }

    [Fact]
    public void Hash64_IsDeterministic()
    {
        var data = Encoding.UTF8.GetBytes("HealthRegen;Instance;health|System.Single|0|4;");
        Assert.Equal(FnvHasher.Hash64(data), FnvHasher.Hash64(data));
    }

    [Fact]
    public void Hash32_DifferentInputs_ProduceDifferentHashes()
    {
        var a = FnvHasher.Hash32(Encoding.UTF8.GetBytes("foo"));
        var b = FnvHasher.Hash32(Encoding.UTF8.GetBytes("bar"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hash64_DifferentInputs_ProduceDifferentHashes()
    {
        var a = FnvHasher.Hash64(Encoding.UTF8.GetBytes("foo"));
        var b = FnvHasher.Hash64(Encoding.UTF8.GetBytes("bar"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hash32_KnownVector()
    {
        // FNV-1a of "a" (byte 97): expected value 0xe40c292c
        var result = FnvHasher.Hash32(new byte[] { 97 });
        Assert.Equal(0xe40c292cu, result);
    }

    [Fact]
    public void Hash64_KnownVector()
    {
        // FNV-1a 64 of "a" (byte 97): expected value 0xaf63dc4c8601ec8c
        var result = FnvHasher.Hash64(new byte[] { 97 });
        Assert.Equal(0xaf63dc4c8601ec8cUL, result);
    }
}
