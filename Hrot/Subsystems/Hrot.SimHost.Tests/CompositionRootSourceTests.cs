using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-260</c> — rails for the source-scan helper itself.</b>
/// </summary>
/// <remarks>
/// <para><b>📌 Why the SCANNER needs its own rails.</b> 🔎 The "H - ui" session found
/// <c>TheViewportInteractionIsSharedTests</c> asserting the literal string
/// <c>"new EntityRotatorGizmo"</c> while the file wrote
/// <c>new Hrot.ScenarioEditor.Gizmos.EntityRotatorGizmo(</c> — fully qualified. ⇒ that rail had been
/// <b>GREEN over the duplicate it exists to forbid, since <c>CE-051</c></b>. 📐 Re-measured here: two
/// rails in this project had the same blindness, both in the dangerous NEGATIVE direction.</para>
///
/// <para>⛔⛔ <b>And the fix was wrong the first time.</b> The replacement pattern was authored with a
/// stray <c>0x08</c> byte in it — a real backspace, not <c>\b</c> — so it matched NOTHING and the rail
/// stayed green while looking fixed. ⚠ That is the same failure one level up: <b>a repaired rail that
/// cannot fail is worth less than the broken one, because it now carries confidence.</b> ⇒ these tests
/// exist so the helper cannot silently stop matching again.</para>
///
/// <para>⭐ The red-proof that closed it: with a qualified <c>new Fdp.Core.FdpEventBus()</c> injected into
/// <c>SimHostNodeBootstrapper</c>, <c>AnEcsNodeDoesNotBuildASecondOrchestrationBus</c> went
/// <b>1 failed / 2 passed</b> — the injected host red, the other two green. The old literal check passed
/// all three.</para>
/// </remarks>
public sealed class CompositionRootSourceTests
{
    [Fact]
    public void MatchesAFullyQualifiedConstruction()
        => Assert.True(CompositionRootSource.ConstructsType(
               "var bus = new Fdp.Core.FdpEventBus();", "FdpEventBus"));

    [Fact]
    public void MatchesAnUnqualifiedConstruction()
        => Assert.True(CompositionRootSource.ConstructsType(
               "var bus = new FdpEventBus();", "FdpEventBus"));

    [Fact]
    public void MatchesAGenericConstruction()
        => Assert.True(CompositionRootSource.ConstructsType(
               "var r = new Fdp.Net.DdsReader<Foo>(p);", "DdsReader"));

    /// <summary>⛔ The word boundary is load-bearing: <c>renew</c> must not read as <c>new</c>.</summary>
    [Fact]
    public void DoesNotMatchAWordEndingInNew()
        => Assert.False(CompositionRootSource.ConstructsType(
               "var x = renew FdpEventBus();", "FdpEventBus"));

    /// <summary>⛔ A different type of a similar name must not match.</summary>
    [Fact]
    public void DoesNotMatchADifferentType()
        => Assert.False(CompositionRootSource.ConstructsType(
               "var bus = new FdpEventBusFactory();", "FdpEventBus"));

    /// <summary>
    /// ⚠ The documented LIMIT, asserted so nobody reads more coverage into this than it has: an alias
    /// still slips past. ⭐ Closing that needs Roslyn, not a better regex.
    /// </summary>
    [Fact]
    public void DoesNotSeeThroughAUsingAlias()
        => Assert.False(CompositionRootSource.ConstructsType(
               "using Bus = Fdp.Core.FdpEventBus; var b = new Bus();", "FdpEventBus"));
}
