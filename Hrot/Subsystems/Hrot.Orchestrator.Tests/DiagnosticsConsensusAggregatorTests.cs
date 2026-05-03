using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Unit tests for <see cref="DiagnosticsConsensusAggregator"/>.
/// </summary>
public sealed class DiagnosticsConsensusAggregatorTests
{
    private static string SerializeEntries(IEnumerable<(string SourceUnc, string RelativeDest)> entries)
    {
        var list = entries
            .Select(e => new FileManifestEntry { SourceUnc = e.SourceUnc, RelativeDest = e.RelativeDest })
            .ToList();
        return JsonSerializer.Serialize(list, new JsonSerializerOptions { PropertyNamingPolicy = null });
    }

    // ── SC1: Multiple nodes, valid manifests ─────────────────────────────────

    [Fact]
    public void Aggregate_ThreeNodesWithTwoEntriesEach_Returns6StrippedEntries()
    {
        var aggregator = new DiagnosticsConsensusAggregator();

        var responses = new Dictionary<int, Dictionary<NodeOpType, string>>
        {
            [1] = new() { [NodeOpType.CollectDiagnostics] = SerializeEntries(new[]
            {
                (@"\\NODE01\c$\diag\events.json", @"diag\events.json"),
                (@"\\NODE01\c$\diag\entities.json", @"diag\entities.json"),
            }) },
            [2] = new() { [NodeOpType.CollectDiagnostics] = SerializeEntries(new[]
            {
                (@"\\NODE02\c$\diag\events.json", @"diag\events.json"),
                (@"\\NODE02\c$\diag\entities.json", @"diag\entities.json"),
            }) },
            [3] = new() { [NodeOpType.CollectDiagnostics] = SerializeEntries(new[]
            {
                (@"\\NODE03\c$\diag\events.json", @"diag\events.json"),
                (@"\\NODE03\c$\diag\entities.json", @"diag\entities.json"),
            }) },
        };

        var result = aggregator.Aggregate(responses) as List<FileManifestEntry>;

        Assert.NotNull(result);
        Assert.Equal(6, result!.Count);
    }

    // ── SC2: TargetOp maps to CollectDiagnostics ─────────────────────────────

    [Fact]
    public void TargetOp_IsCollectDiagnostics()
    {
        var aggregator = new DiagnosticsConsensusAggregator();
        Assert.Equal(NodeOpType.CollectDiagnostics, aggregator.TargetOp);
    }

    // ── SC3: Stripped manifest has SourceUnc absent; full manifest preserved ─

    [Fact]
    public void Aggregate_StripsSourceUnc_ButFullManifestRetainedInternally()
    {
        var aggregator = new DiagnosticsConsensusAggregator();

        var responses = new Dictionary<int, Dictionary<NodeOpType, string>>
        {
            [1] = new() { [NodeOpType.CollectDiagnostics] = SerializeEntries(new[]
            {
                (@"\\NODE01\c$\diag\events.json", @"diag\events.json"),
            }) },
        };

        var stripped = aggregator.Aggregate(responses) as List<FileManifestEntry>;
        var full     = aggregator.TakeFullManifest();

        // Stripped manifest passed to ExCon: SourceUnc must be absent/empty.
        Assert.NotNull(stripped);
        Assert.All(stripped!, e => Assert.True(string.IsNullOrEmpty(e.SourceUnc)));

        // Full manifest used for PullToNasAsync: SourceUnc must be present.
        Assert.NotNull(full);
        Assert.All(full!, e => Assert.False(string.IsNullOrEmpty(e.SourceUnc)));
    }

    // ── SC4: Empty / missing node responses → null ────────────────────────────

    [Fact]
    public void Aggregate_EmptyResponses_ReturnsNull()
    {
        var aggregator = new DiagnosticsConsensusAggregator();

        var result = aggregator.Aggregate(new Dictionary<int, Dictionary<NodeOpType, string>>());

        Assert.Null(result);
        Assert.Null(aggregator.TakeFullManifest());
    }

    // ── SC5: Malformed JSON is skipped without throw ──────────────────────────

    [Fact]
    public void Aggregate_MalformedJson_SkipsNodeWithoutThrowing()
    {
        var aggregator = new DiagnosticsConsensusAggregator();

        var responses = new Dictionary<int, Dictionary<NodeOpType, string>>
        {
            [1] = new() { [NodeOpType.CollectDiagnostics] = "not valid json" },
            [2] = new() { [NodeOpType.CollectDiagnostics] = SerializeEntries(new[]
            {
                (@"\\NODE02\c$\diag\events.json", @"diag\events.json"),
            }) },
        };

        var result = aggregator.Aggregate(responses) as List<FileManifestEntry>;

        Assert.NotNull(result);
        Assert.Equal(1, result!.Count);
    }

    // ── SC6: TakeFullManifest drains the stored manifest ─────────────────────

    [Fact]
    public void TakeFullManifest_CalledTwice_SecondCallReturnsNull()
    {
        var aggregator = new DiagnosticsConsensusAggregator();

        var responses = new Dictionary<int, Dictionary<NodeOpType, string>>
        {
            [1] = new() { [NodeOpType.CollectDiagnostics] = SerializeEntries(new[]
            {
                (@"\\NODE01\c$\diag\events.json", @"diag\events.json"),
            }) },
        };

        aggregator.Aggregate(responses);
        var first  = aggregator.TakeFullManifest();
        var second = aggregator.TakeFullManifest();

        Assert.NotNull(first);
        Assert.Null(second);
    }
}
