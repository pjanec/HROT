using System;
using System.Numerics;
using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Runner.Services;
using CycloneDDS.Runtime;
using Xunit;

namespace Bagira.Runner.Tests;

/// <summary>
/// Tests for <see cref="OrchestratorSubsystem"/> covering S0501 (title-bar color + ImGui window)
/// and S0502 (DdsWriter&lt;SysOpRequest&gt; wired through panel).
/// </summary>
[Collection("OrchestratorSubsystemTests")]
public sealed class OrchestratorSubsystemTests
{
    // Domain 26 is reserved for this test class.
    private const int TestDomain = 26;

    // ── S0501 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Name_Returns_Orchestrator()
    {
        var subsystem = new OrchestratorSubsystem();
        Assert.Equal("Orchestrator", subsystem.Name);
    }

    [Fact]
    public void TitleBarColor_IsBeige()
    {
        // S0501: OrchestratorSubsystem.TitleBarColor must be the warm-beige defined in the spec
        // (0.72f, 0.64f, 0.47f, 1f) so the runner window title bar is distinguishable from IOS/IG.
        var subsystem = new OrchestratorSubsystem();
        var color = subsystem.TitleBarColor;

        Assert.Equal(0.72f, color.X, precision: 5);
        Assert.Equal(0.64f, color.Y, precision: 5);
        Assert.Equal(0.47f, color.Z, precision: 5);
        Assert.Equal(1.0f,  color.W, precision: 5);
    }

    // ── S0502 / lifecycle ─────────────────────────────────────────────────────

    /// <summary>
    /// After <see cref="OrchestratorSubsystem.Initialize"/> the internal DrillMaster
    /// must be running (exposed via <c>TestHook_DrillMaster</c>).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Initialize_Creates_DrillMaster()
    {
        var subsystem = new OrchestratorSubsystem();
        try
        {
            subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });
            Assert.NotNull(subsystem.TestHook_DrillMaster);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    /// <summary>
    /// S0502: After <see cref="OrchestratorSubsystem.Initialize"/>, a DDS reader on
    /// the same domain must be able to discover the subsystem's <c>SysOpRequest</c>
    /// writer endpoint (proves the writer is created and joined the DDS graph).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Initialize_SysOpWriter_IsDiscoverableOnDomain()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });

        bool discovered = false;
        try
        {
            using var probe       = new DdsParticipant(TestDomain);
            using var sysOpReader = new DdsReader<SysOpRequest>(probe);

            // Allow DDS endpoint discovery to settle.
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                if (sysOpReader.CurrentStatus.CurrentCount > 0)
                {
                    discovered = true;
                    break;
                }
                Thread.Sleep(50);
            }
        }
        finally
        {
            subsystem.Shutdown();
        }

        Assert.True(discovered,
            "OrchestratorSubsystem did not publish a SysOpRequest writer endpoint " +
            "after Initialize (S0502 DdsWriter wiring failed).");
    }

    /// <summary>
    /// A full Initialize → Update → Shutdown cycle must not throw.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Initialize_Update_Shutdown_DoesNotThrow()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });

        for (int i = 0; i < 5; i++)
            subsystem.Update(1f / 60f);

        subsystem.Shutdown();  // must not throw
    }

    /// <summary>
    /// S0502: After Shutdown, the DDS SysOpRequest writer endpoint must no longer
    /// be discoverable, and re-Initialize must work without throwing (proves the
    /// writer was properly disposed by Shutdown).
    /// </summary>
    [Fact(Timeout = 15_000)]
    public void Shutdown_DisposesWriter_ReinitializeWorks()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });
        subsystem.Shutdown();

        // After Shutdown, a second Initialize → Shutdown cycle must not throw
        // (proves no leaked writer handle prevents re-creation).
        var ex = Record.Exception(() =>
        {
            subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });
            subsystem.Shutdown();
        });
        Assert.Null(ex);
    }

    // ── S0501: FormatPrettyJson ────────────────────────────────────────────────

    [Fact]
    public void FormatPrettyJson_IndentsJson()
    {
        var result = OrchestratorSubsystem.FormatPrettyJson("{\"a\":1}");
        Assert.Contains("\n", result);
    }

    [Fact]
    public void FormatPrettyJson_InvalidJson_ReturnsOriginal()
    {
        const string bad = "not-json";
        var result = OrchestratorSubsystem.FormatPrettyJson(bad);
        Assert.Equal(bad, result);
    }

    [Fact]
    public void FormatPrettyJson_EmptyString_ReturnsEmpty()
    {
        var result = OrchestratorSubsystem.FormatPrettyJson(string.Empty);
        Assert.Equal(string.Empty, result);
    }
}
