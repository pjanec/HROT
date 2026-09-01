using System;
using Xunit;
using Xunit.Abstractions;

namespace HrotStrideApp.Tests;

/// <summary>
/// ⭐⭐⭐ <b>DIAGNOSTIC PROBE — does the Stride host's own world get the bootstrap singletons?</b>
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
/// <para>📐 <b>The static measurement that motivated this probe</b> — <c>EditorStrideSubsystem.cs</c>
/// contains <b>zero</b> mentions of <c>IGeographicTransform</c> / <c>WGS84Transform</c>, while
/// <c>SimHostApp.cs:509</c>, <c>CgfSubsystem.cs:544</c> and <c>EditorSubsystem.cs:1025</c> all publish
/// it. And the exposure is <b>mode-dependent</b>:
/// <list type="bullet">
///   <item><b>hosted</b> (<c>hostRealEditor: true</c>, env <c>STRIDE_HOST_REAL_EDITOR=1</c>) —
///     <c>:943</c> repoints <c>World = _editor.World</c>, i.e. the real
///     <c>EditorSubsystem</c>'s world, which <b>does</b> publish the singleton ⇒ SAFE;</item>
///   <item><b>standalone</b> (the <b>default</b>) — <c>:525</c> creates its own
///     <c>EntityRepository</c> and no <c>EditorSubsystem</c> exists ⇒ <b>exposed</b>.</item>
/// </list></para>
///
/// <para>⚠⚠ <b>This probe exists because the claim could not be measured where it was found.</b> The
/// Stride tree does not build on Linux — measured, not assumed:
/// <c>NETSDK1073: The FrameworkReference 'Microsoft.WindowsDesktop.App' was not recognized</c>. ⇒ the
/// finding above is <b>a static read, not a verdict</b>, and this probe is how the Windows lane turns
/// it into one.</para>
///
/// <para>⭐ <b>It asserts nothing about the singleton on purpose</b> — the same discipline the
/// <c>WhyDoesTheMissionNotMoveProbe</c> / <c>WhyDoesTheVehicleNotMoveProbe</c> rails follow. Handing
/// another lane a knowingly-RED test would read as a regression; handing it a green probe that
/// <b>prints the answer</b> does not. ⇒ <b>run it and read the output.</b> If it reports ABSENT, the
/// fix is one line at the Stride composition root and the real assertion lands <i>with</i> that fix.</para>
/// </summary>
public sealed class WhichSingletonsDoesTheStrideWorldGetProbe : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly EditorStrideSubsystem _sut;

    public WhichSingletonsDoesTheStrideWorldGetProbe(ITestOutputHelper output)
    {
        _out = output;
        _sut = new EditorStrideSubsystem();
        _sut.Initialize();          // default => hostRealEditor: false => the STANDALONE path
    }

    public void Dispose() => _sut.Dispose();

    [Fact]
    [Trait("Category", "Diagnostic")]
    public void DumpWhetherTheBootstrapSingletonsArePresent()
    {
        var world = _sut.World;

        _out.WriteLine("── WHICH MODE IS THIS? ─────────────────────────────────────");
        _out.WriteLine($"  HostRealEditor = {_sut.HostRealEditor}"
                     + (_sut.HostRealEditor
                        ? "   (hosted — World is the real EditorSubsystem's, which publishes the singleton)"
                        : "   (STANDALONE — its own world, created at EditorStrideSubsystem.cs:525)"));

        _out.WriteLine("── THE BOOTSTRAP SINGLETON ─────────────────────────────────");
        bool hasGeo = world.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>();
        _out.WriteLine($"  HasSingletonManaged<IGeographicTransform> = {hasGeo}");
        if (!hasGeo)
        {
            _out.WriteLine("  ⛔⛔ ABSENT — CE-151 CONFIRMED on the Stride host.");
            _out.WriteLine("     ⇒ every behaviour parameter resolver that reaches for the transform");
            _out.WriteLine("       gets null, and CgfNodes.cs:205 silently drops TargetLat/TargetLon.");
            _out.WriteLine("     ⇒ a MoveToLocation mission on this host yields NavigationIntent with");
            _out.WriteLine("       the right Speed/ArrivalRadius and FinalDestination = (0,0).");
            _out.WriteLine("     FIX: publish it at the composition root, as the other hosts do —");
            _out.WriteLine("       world.SetSingletonManaged<IGeographicTransform>(<the transform>);");
            _out.WriteLine("       (SimHostApp.cs:509 / CgfSubsystem.cs:544 / EditorSubsystem.cs:1025)");
        }
        else
        {
            _out.WriteLine("  ⭐ PRESENT — CE-151's Stride row is REFUTED; correct the tracker row.");
        }

        // ⭐ The one thing worth asserting: that this probe actually exercised the EXPOSED mode.
        //   If Initialize() ever starts defaulting to hosted, the reading above becomes vacuous and
        //   this rail must be revisited rather than silently reporting "PRESENT" about a world that
        //   was never the one at issue.
        Assert.False(_sut.HostRealEditor,
            "probe precondition: Initialize() must default to the STANDALONE path — the mode CE-151 is about. "
          + "If this fails, hostRealEditor's default changed and the probe needs updating.");
    }
}
