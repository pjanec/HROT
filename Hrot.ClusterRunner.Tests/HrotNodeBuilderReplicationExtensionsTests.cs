using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Network.Infrastructure;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Unit tests for <see cref="HrotNodeBuilderReplicationExtensions"/> (S202).
///
/// Verifies the OCP-compliant extension that wires NedReplicationModule into
/// HrotNodeContext via .WithReplication().Build() without requiring Hrot.Common
/// to reference Hrot.Network.
///
/// Design note: <see cref="HrotNodeBuilderReplicationExtensions.WithReplication"/> returns a
/// <see cref="HrotNodeBuilderWithReplication"/> rather than <see cref="HrotNodeBuilder"/>.
/// This ensures the subsequent .Build() call resolves to
/// <see cref="HrotNodeBuilderWithReplication.Build"/> (which creates NedReplicationModule)
/// rather than the native <see cref="HrotNodeBuilder.Build"/> (which returns null NedReplication).
/// C# instance methods always take precedence over extension methods with the same name,
/// so returning a distinct type is required to achieve the expected fluent-chain behavior.
/// </summary>
public sealed class HrotNodeBuilderReplicationExtensionsTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HrotNodeConfig HeadlessConfig() => new()
    {
        DomainId = 0,
        NodeId   = 1,
        Headless = true,
    };

    // ── SC1 — Build with WithReplication returns non-null NedReplication ─────

    /// <summary>
    /// API surface test: builder with .WithReplication() returns a context with
    /// a non-null NedReplication property.
    /// </summary>
    [Fact]
    public void Build_WithReplication_ReturnsNonNullNedReplication()
    {
        var config = HeadlessConfig();

        var context = new HrotNodeBuilder(config)
            .WithRole("TestNode", NodeRole.AllInOne)
            .WithReplication(NodeRole.AllInOne)
            .Build();

        Assert.NotNull(context.NedReplication);
    }

    // ── SC2 — Build without WithReplication returns null NedReplication ──────

    /// <summary>
    /// Guard test: the native HrotNodeBuilder.Build() (without .WithReplication())
    /// returns a context where NedReplication is null.
    /// This documents that NedReplication is an opt-in via the
    /// HrotNodeBuilderWithReplication pathway, not always populated by the native build.
    /// </summary>
    [Fact]
    public void Build_WithoutWithReplication_NedReplicationIsNull()
    {
        var config = HeadlessConfig();

        // Native HrotNodeBuilder.Build() — no WithReplication() in the chain.
        // The C# instance method takes precedence over any extension method of the same name,
        // so this calls HrotNodeBuilder.Build() directly.
        var context = new HrotNodeBuilder(config)
            .WithRole("TestNode", NodeRole.AllInOne)
            .Build();

        Assert.Null(context.NedReplication);
    }

    // ── SC3 — AllInOne role → DriveFromNetwork == false ──────────────────────

    /// <summary>
    /// Role contract test: AllInOne role configures DriveFromNetwork = false
    /// (local entities must not be overridden by dead-reckoning).
    /// </summary>
    [Fact]
    public void Build_AllInOneRole_DriveFromNetworkIsFalse()
    {
        var config = HeadlessConfig();

        var context = new HrotNodeBuilder(config)
            .WithRole("TestNode", NodeRole.AllInOne)
            .WithReplication(NodeRole.AllInOne)
            .Build();

        Assert.NotNull(context.NedReplication);

        // Cast to concrete type to access DriveFromNetwork property
        var ned = context.NedReplication as Hrot.Network.Replication.NedReplicationModule;
        Assert.NotNull(ned);
        Assert.False(ned.DriveFromNetwork,
            "AllInOne role must use DriveFromNetwork=false (local entities must not be overridden).");
    }

    // ── SC4 — GhostCreationSystem is populated on context ────────────────────

    /// <summary>
    /// Verifies that the extension Build() populates HrotNodeContext.GhostCreationSystem
    /// (wired from NedReplicationModule.GhostCreationSystem for replay handler use).
    /// </summary>
    [Fact]
    public void Build_WithReplication_PopulatesGhostCreationSystem()
    {
        var config = HeadlessConfig();

        var context = new HrotNodeBuilder(config)
            .WithRole("TestNode", NodeRole.Brain)
            .WithReplication(NodeRole.Brain)
            .Build();

        Assert.NotNull(context.GhostCreationSystem);
    }
}
