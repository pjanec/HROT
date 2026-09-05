using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Smoke tests for PACK2-R003 harness scaffolding.
/// </summary>
public class HarnessSmokeTests
{
    [Fact]
    public void EditorHarness_Initializes_WithoutException()
    {
        using var h = new EditorHarness();
        Assert.NotNull(h.Repo);
        Assert.NotNull(h.Bus);
        Assert.NotNull(h.Kernel);
    }

    [Fact]
    public void EditorHarness_PumpFrames_WithoutException()
    {
        using var h = new EditorHarness();
        h.PumpFrames(5);
        Assert.True(true);
    }

    [Fact]
    public void CgfHarness_TwoInstances_HaveDifferentDomainIds()
    {
        using var h1 = new CgfHarness();
        using var h2 = new CgfHarness();
        Assert.NotEqual(h1.DomainId, h2.DomainId);
    }

    [Fact]
    public void CgfHarness_SharedDomainCtor_UsesSuppledDomainId()
    {
        using var h = new CgfHarness(domainId: 150);
        Assert.Equal(150, h.DomainId);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-036</c> — un-skipped <c>2026-08-25</c>. The skip reason was WRONG.</b>
    ///
    /// <para>⛔ It said *"Requires CycloneDDS"*, which reads as *"this environment has no DDS"* — and the
    /// other tests in this very assembly boot a real CycloneDDS domain and pass. 📐 The real cause is the
    /// DOMAIN ID: CycloneDDS derives its UDP ports as <c>7400 + 250 × domainId + …</c>, so
    /// <c>domainId = 250</c> asks for port <c>69900</c>, which does not exist. ⇒ the harness could never
    /// have come up on 250, on any machine, with or without DDS installed.</para>
    ///
    /// <para>⭐ The usable ceiling is therefore ≈ <c>(65535 − 7400) / 250 ≈ 232</c>. ⚠ <c>200</c> is chosen
    /// to stay clear of it AND of the auto-counter, which starts at
    /// <c>HrotRunnerHarness.DomainIdBase = 100</c> and increments per harness.</para>
    ///
    /// <para>📌 <c>R-131</c>: the fix is the domain id, not a filter around the test.</para>
    /// </summary>
    [Fact]
    public void HrotRunnerHarness_SharedDomainCtor_UsesSuppledDomainId()
    {
        using var h = new HrotRunnerHarness("simhost", domainId: 200);
        Assert.Equal(200, h.DomainId);
    }
}
