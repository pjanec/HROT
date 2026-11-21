using System;
using Xunit;
using Xunit.Abstractions;

namespace HrotStrideApp.Tests;

/// <summary>
/// ⭐⭐⭐ <b>The Stride host's world carries the bootstrap singletons.</b>
///
/// <para>🔴 <b>Why this exists</b> (<c>CE-151</c>, <c>2026-09-01</c>). <c>CE-150</c> found that
/// <c>SimHostInstance</c> never published <c>IGeographicTransform</c> as a world singleton, and the
/// consequence was silent and severe: <c>CgfNodes.ResolveMoveToParams</c> (<c>:163</c>) reads
/// <c>HasSingletonManaged&lt;IGeographicTransform&gt;()</c>, gets <see langword="null"/>, and
/// <c>ParseMoveToParams</c>'s guard at <c>CgfNodes.cs:205</c> —
/// <c>if ((dto.TargetLat != 0 || dto.TargetLon != 0) &amp;&amp; geoTransform != null)</c> — then
/// <b>drops the destination while <c>Speed</c> and <c>ArrivalRadius</c>, assigned above the guard,
/// survive</b>. A <c>MoveToLocation</c> mission produces a perfectly-shaped
/// <c>NavigationIntent</c> aimed at <c>(0,0)</c>: an order to drive to where the vehicle already
/// stands. Nothing logs, nothing throws.</para>
///
/// <para>⭐⭐⭐ <b>CE-209 / R-S9 ANSWERED THIS BY DELETION, and that is worth stating plainly.</b> This
/// file used to be <c>WhichSingletonsDoesTheStrideWorldGetProbe</c> — a deliberately assertion-free
/// diagnostic, because the exposure it measured was <b>mode-dependent</b>:
/// <list type="bullet">
///   <item><b>hosted</b> — <c>World</c> is repointed to the real <c>EditorSubsystem</c>'s world,
///     which <b>does</b> publish the singleton ⇒ SAFE;</item>
///   <item><b>standalone</b> (then the DEFAULT) — its own <c>EntityRepository</c>, no
///     <c>EditorSubsystem</c> anywhere ⇒ <b>exposed</b>.</item>
/// </list>
/// The self-contained arm is retired, so the exposed mode does not exist any more. ⇒ the probe's
/// question has one answer for every caller, which is exactly the condition its own header set for
/// becoming a real assertion: <i>"the real assertion lands with that fix."</i></para>
///
/// <para>⚠ <b>Kept as a rail rather than deleted</b>, because the claim it now guarantees is
/// load-bearing and cheap: if the Stride composition root ever stops repointing <c>World</c> at a
/// world that publishes the transform, every <c>MoveToLocation</c> on this host silently aims at
/// (0,0) again — the failure that has no log line.</para>
/// </summary>
public sealed class TheStrideWorldGetsTheBootstrapSingletonsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly EditorStrideSubsystem _sut;

    public TheStrideWorldGetsTheBootstrapSingletonsTests(ITestOutputHelper output)
    {
        _out = output;
        _sut = new EditorStrideSubsystem();
        _sut.Initialize();          // CE-209: the ONLY composition — the hosted editor
    }

    public void Dispose() => _sut.Dispose();

    /// <summary>
    /// The world this subsystem exposes publishes <c>IGeographicTransform</c>, so behaviour
    /// parameter resolvers can convert lat/lon rather than silently dropping it.
    /// </summary>
    [Fact]
    public void TheWorldPublishesTheGeographicTransform()
    {
        var world = _sut.World;

        // ⭐ The anti-vacuity half: assert we are looking at the composition that is actually used,
        //   not a bare repository some future refactor left behind. HostedEditor is what CE-209 made
        //   unconditional; if it is ever null again there is a second composition back in the tree.
        Assert.NotNull(_sut.HostedEditor);

        bool hasGeo = world.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>();
        _out.WriteLine($"  HasSingletonManaged<IGeographicTransform> = {hasGeo}");

        Assert.True(hasGeo,
            "The Stride host's world must publish IGeographicTransform. Without it "
          + "CgfNodes.cs:205 drops TargetLat/TargetLon while keeping Speed and ArrivalRadius, so a "
          + "MoveToLocation mission yields a well-formed NavigationIntent aimed at (0,0) — an order "
          + "to drive to where the vehicle already stands, with nothing logged and nothing thrown "
          + "(CE-150 / CE-151). The other hosts publish it at SimHostApp.cs:509, "
          + "CgfSubsystem.cs:544 and EditorSubsystem.cs:1025.");
    }
}
