using Fdp.Core.Serialization.Migrations.Internal;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations.Internal;

/// <summary>
/// Tests for <see cref="HashUtilities"/> (T1-290..T1-293).
/// </summary>
public sealed class HashUtilitiesTests
{
    // ---------------------------------------------------------------
    // T1-290: Known input produces known SHA-256 first-16-hex
    // ---------------------------------------------------------------
    [Fact]
    public void ComputeContentHash_ProducesExpectedHash()
    {
        // SHA-256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
        // First 16 hex chars (8 bytes): 2cf24dba5fb0a30e
        Assert.Equal("2cf24dba5fb0a30e", HashUtilities.ComputeContentHash("hello"));
    }

    // ---------------------------------------------------------------
    // T1-291: Identical inputs produce identical outputs (determinism)
    // ---------------------------------------------------------------
    [Fact]
    public void ComputeContentHash_IdenticalInputs_IdenticalOutputs()
    {
        var hash1 = HashUtilities.ComputeContentHash("some content");
        var hash2 = HashUtilities.ComputeContentHash("some content");

        Assert.Equal(hash1, hash2);
    }

    // ---------------------------------------------------------------
    // T1-292: Different inputs produce different outputs (sensitivity)
    // ---------------------------------------------------------------
    [Fact]
    public void ComputeContentHash_DifferentInputs_DifferentOutputs()
    {
        var hash1 = HashUtilities.ComputeContentHash("hello");
        var hash2 = HashUtilities.ComputeContentHash("world");

        Assert.NotEqual(hash1, hash2);
    }

    // ---------------------------------------------------------------
    // T1-293: Non-ASCII input uses UTF-8 bytes (not platform-dependent)
    // ---------------------------------------------------------------
    [Fact]
    public void ComputeContentHash_Utf8Bytes_NotPlatformDependent()
    {
        // U+00E9 (e with acute) encodes to two UTF-8 bytes: 0xC3 0xA9
        // SHA-256(0xC3 0xA9) first 16 hex = known value
        // Pre-computed: SHA-256 of {0xC3, 0xA9} = 4a99557e4033c353...
        var hash = HashUtilities.ComputeContentHash("\u00e9");

        // Must be 16 lowercase hex characters
        Assert.Equal(16, hash.Length);
        Assert.Matches("^[0-9a-f]{16}$", hash);

        // Must be stable: same result every call
        Assert.Equal(hash, HashUtilities.ComputeContentHash("\u00e9"));

        // Must differ from the ASCII 'e' hash
        Assert.NotEqual(hash, HashUtilities.ComputeContentHash("e"));

        // D-011: pin the exact expected hash value
        Assert.Equal("4a99557e4033c353", HashUtilities.ComputeContentHash("\u00e9"));
    }
}
