using System;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Debug;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Debug;

/// <summary>
/// BPF-026: BTreeDebugSession.SetDebugMetadata enables node-index-to-VisualId symbolication.
/// BPF-045: Trace event VisualIds are populated from debug metadata (tested via symbolication path).
/// </summary>
public sealed class BTreeDebugSessionSymbolicationTests
{
    // ── BPF-026: SetDebugMetadata / TrySymbolicateIndex ──────────────────────

    [Fact]
    public void SetDebugMetadata_WithValidMetadata_SymbolicatesIndex0()
    {
        var expectedId = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        var sut = new BTreeDebugSession();
        var metadata = new NodeDebugMetadata[]
        {
            new() { VisualId = expectedId.ToString("D") },
        };

        sut.SetDebugMetadata(metadata, Guid.NewGuid());

        sut.TrySymbolicateIndex(0).Should().Be(expectedId);
    }

    [Fact]
    public void SetDebugMetadata_WithMultipleNodes_SymbolicatesCorrectIndex()
    {
        var id0 = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        var id1 = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
        var id2 = new Guid("cccccccc-0000-0000-0000-000000000003");
        var sut = new BTreeDebugSession();
        var metadata = new NodeDebugMetadata[]
        {
            new() { VisualId = id0.ToString("D") },
            new() { VisualId = id1.ToString("D") },
            new() { VisualId = id2.ToString("D") },
        };

        sut.SetDebugMetadata(metadata, Guid.NewGuid());

        sut.TrySymbolicateIndex(0).Should().Be(id0);
        sut.TrySymbolicateIndex(1).Should().Be(id1);
        sut.TrySymbolicateIndex(2).Should().Be(id2);
    }

    [Fact]
    public void TrySymbolicateIndex_WithoutMetadata_ReturnsNull()
    {
        var sut = new BTreeDebugSession();

        sut.TrySymbolicateIndex(0).Should().BeNull();
    }

    [Fact]
    public void TrySymbolicateIndex_OutOfRangeIndex_ReturnsNull()
    {
        var sut = new BTreeDebugSession();
        sut.SetDebugMetadata(new NodeDebugMetadata[] { new() { VisualId = Guid.NewGuid().ToString("D") } },
            Guid.NewGuid());

        sut.TrySymbolicateIndex(-1).Should().BeNull();
        sut.TrySymbolicateIndex(1).Should().BeNull();
        sut.TrySymbolicateIndex(100).Should().BeNull();
    }

    [Fact]
    public void TrySymbolicateIndex_EmptyVisualId_ReturnsNull()
    {
        var sut = new BTreeDebugSession();
        sut.SetDebugMetadata(new NodeDebugMetadata[] { new() { VisualId = string.Empty } },
            Guid.NewGuid());

        sut.TrySymbolicateIndex(0).Should().BeNull();
    }

    [Fact]
    public void TrySymbolicateIndex_MalformedVisualId_ReturnsNull()
    {
        var sut = new BTreeDebugSession();
        sut.SetDebugMetadata(new NodeDebugMetadata[] { new() { VisualId = "not-a-guid" } },
            Guid.NewGuid());

        sut.TrySymbolicateIndex(0).Should().BeNull();
    }

    [Fact]
    public void SetDebugMetadata_CalledTwice_UsesLatestMetadata()
    {
        var firstId  = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        var secondId = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
        var sut = new BTreeDebugSession();

        sut.SetDebugMetadata(new NodeDebugMetadata[] { new() { VisualId = firstId.ToString("D") } },
            Guid.NewGuid());
        sut.SetDebugMetadata(new NodeDebugMetadata[] { new() { VisualId = secondId.ToString("D") } },
            Guid.NewGuid());

        sut.TrySymbolicateIndex(0).Should().Be(secondId,
            because: "the second SetDebugMetadata call replaces the first");
    }

    [Fact]
    public void SetDebugMetadata_NullMetadata_ClearsSymbolication()
    {
        var id = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        var sut = new BTreeDebugSession();
        sut.SetDebugMetadata(new NodeDebugMetadata[] { new() { VisualId = id.ToString("D") } },
            Guid.NewGuid());

        sut.SetDebugMetadata(null, Guid.Empty);

        sut.TrySymbolicateIndex(0).Should().BeNull(
            because: "clearing metadata disables symbolication");
    }
}
