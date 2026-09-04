using System;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Resilience;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Controllers;
using Xunit;

namespace Fdp.ModuleHost.Tests;

/// <summary>
/// <b><c>CE-189</c> — a module fault must be impossible to ignore.</b>
///
/// <para>The kernel catches per-module exceptions so one faulty module cannot take down a distributed
/// simulation. That is right in production and exactly wrong while debugging: <c>CE-188</c>'s
/// <c>StatelessGizmoSystem</c> threw on every frame of every editor run, every system after it in its
/// phase group was skipped, the node still answered healthy — and the fault was <i>printed</i> 8 000 to
/// 16 000 times per run, which is its own way of hiding.</para>
///
/// <para>These pin the three properties that make that impossible to repeat: fail-fast rethrows,
/// the fault reaches the circuit breaker, and repeats are counted rather than reprinted.</para>
/// </summary>
public sealed class ModuleFaultReportingRails : IDisposable
{
    private readonly bool _originalFailFast = FdpConfig.FailFastOnModuleException;

    public void Dispose() => FdpConfig.FailFastOnModuleException = _originalFailFast;

    /// <summary>A synchronous module that throws the same exception on every tick.</summary>
    private sealed class AlwaysThrowsModule : IEcsModule
    {
        public int Ticks;

        public string Name => "AlwaysThrows";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        public void RegisterSystems(ISystemRegistry registry) { }

        public void Tick(ISimulationView view, float deltaTime)
        {
            Ticks++;
            throw new IndexOutOfRangeException("Index was outside the bounds of the array.");
        }
    }

    /// <summary>Mirrors <c>ModuleHostKernelTimeTests</c>'s setup so these rails exercise the real
    /// kernel rather than a shape invented for the test.</summary>
    private static (ModuleHostKernel Kernel, EntityRepository World) NewKernel()
    {
        var world = new EntityRepository();
        var kernel = new ModuleHostKernel(world, new EventAccumulator());

        world.RegisterComponent<GlobalTime>();
        world.SetSingletonUnmanaged(new GlobalTime());

        var controller = TimeControllerFactory.Create(
            new FdpEventBus(),
            new TimeControllerConfig { Role = TimeRole.Standalone, Mode = TimeMode.Continuous });
        kernel.SetTimeController(controller);

        return (kernel, world);
    }

    // ── ① fail fast ───────────────────────────────────────────────────────────

    /// <summary>
    /// With the switch on, the frame dies at the fault carrying the ORIGINAL exception type — not a
    /// wrapper, so a debugger breaks where the bug is.
    /// </summary>
    [Fact]
    public void WithFailFastOn_AModuleFaultIsRethrown()
    {
        FdpConfig.FailFastOnModuleException = true;

        var (kernel, world) = NewKernel();
        using var _ = world;

        var module = new AlwaysThrowsModule();
        kernel.RegisterModule(module);
        kernel.InitializeForTest();

        Assert.Throws<IndexOutOfRangeException>(() => kernel.Update());
    }

    /// <summary>
    /// And with it off the frame survives — the production behaviour this switch exists to suspend, not
    /// replace. A rail for the default matters: silently flipping it would turn every transient module
    /// fault into a dead node.
    /// </summary>
    [Fact]
    public void WithFailFastOff_TheFrameSurvivesTheFault()
    {
        FdpConfig.FailFastOnModuleException = false;

        var (kernel, world) = NewKernel();
        using var _ = world;

        kernel.RegisterModule(new AlwaysThrowsModule());
        kernel.InitializeForTest();

        kernel.Update();
        kernel.Update();   // still alive on the frame after a fault
    }

    // ── ② the fault reaches the breaker ───────────────────────────────────────

    /// <summary>
    /// <b>The synchronous path used to print to stderr and do nothing else</b> — no
    /// <c>RecordFailure</c>, so the circuit never opened and <c>GetExecutionStats</c> reported a healthy
    /// module however many times it threw. That contradicted this component's own design
    /// (<c>Fdp.ModuleHost.md</c> §5), which the async path implements and this one did not.
    /// </summary>
    [Fact]
    public void ARepeatedlyFaultingSyncModuleOpensItsCircuit()
    {
        FdpConfig.FailFastOnModuleException = false;

        var (kernel, world) = NewKernel();
        using var _ = world;

        kernel.RegisterModule(new AlwaysThrowsModule());
        kernel.InitializeForTest();

        for (int i = 0; i < 12; i++)
            kernel.Update();

        var stats = kernel.GetExecutionStats();
        var entry = Assert.Single(stats, s => s.ModuleName == "AlwaysThrows");

        Assert.NotEqual(CircuitState.Closed, entry.CircuitState);
    }

    // ── ③ repeats are counted, not reprinted ──────────────────────────────────

    /// <summary>
    /// <b>Volume is a form of hiding.</b> A module faulting every frame must not emit a stack per frame;
    /// the whole reason <c>CE-188</c> survived a working session is that 16 367 identical lines read as
    /// background noise. The first occurrence is reported in full and repeats are counted.
    /// </summary>
    [Fact]
    public void RepeatedIdenticalFaultsAreCountedRatherThanReprinted()
    {
        FdpConfig.FailFastOnModuleException = false;

        var (kernel, world) = NewKernel();
        using var _ = world;

        var original = Console.Error;
        var captured = new System.IO.StringWriter();
        Console.SetError(captured);
        try
        {
            kernel.RegisterModule(new AlwaysThrowsModule());
            kernel.InitializeForTest();

            for (int i = 0; i < 20; i++)
                kernel.Update();
        }
        finally
        {
            Console.SetError(original);
        }

        string log = captured.ToString();
        int stacks = System.Text.RegularExpressions.Regex.Matches(log, "IndexOutOfRangeException").Count;

        // 20 faults must not produce 20 full reports. One first-occurrence report plus the
        // power-of-ten reminder at 10 is the shape; the assertion is deliberately loose on the exact
        // number and tight on the property that matters.
        Assert.True(stacks < 20, $"expected repeats to be collapsed, saw {stacks} mentions in:\n{log}");
        Assert.Contains("AlwaysThrows", log);
        Assert.Contains("FDP_FAIL_FAST", log);   // the report tells you how to make it fatal
    }
}
