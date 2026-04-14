using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Modules;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Systems;
using Fdp.ModuleHost_Core.Abstractions;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Tests for <see cref="IgGroundClampingModule"/> (MOD1-P7T5).
/// </summary>
public sealed class IgGroundClampingModuleTests
{
    // ── Stub ISystemRegistry ──────────────────────────────────────────────────

    private sealed class CapturingRegistry : ISystemRegistry
    {
        private readonly List<IEcsModuleSystem> _systems = new();
        public IReadOnlyList<IEcsModuleSystem> RegisteredSystems => _systems;

        public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => _systems.Add(system);
    }

    // ── Stub ITerrainProvider ─────────────────────────────────────────────────

    private sealed class NullTerrainProvider : ITerrainProvider
    {
        public void QueryBatch(
            NativeArray<TerrainQueryRequest> requests, int count,
            NativeArray<TerrainQueryResult>  results) { }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>RegisterSystems</c> must register all 4 terrain-query phase systems.
    /// </summary>
    [Fact]
    public void RegisterSystems_RegistersAllFourSystems()
    {
        var registry = new CapturingRegistry();
        var module   = new IgGroundClampingModule(new NullTerrainProvider());

        module.RegisterSystems(registry);

        Assert.Equal(4, registry.RegisteredSystems.Count);
        Assert.Contains(registry.RegisteredSystems, s => s is TerrainQueryInitializationSystem);
        Assert.Contains(registry.RegisteredSystems, s => s is TerrainQuerySubmitSystem);
        Assert.Contains(registry.RegisteredSystems, s => s is TerrainQuerySolverSystem);
        Assert.Contains(registry.RegisteredSystems, s => s is TerrainQueryResolutionSystem);
    }

    /// <summary>
    /// Module name must be non-empty and policy must be synchronous.
    /// </summary>
    [Fact]
    public void Module_Metadata_IsValid()
    {
        var module = new IgGroundClampingModule(new NullTerrainProvider());

        Assert.NotEmpty(module.Name);
        Assert.Equal(ExecutionPolicy.Synchronous(), module.Policy);
    }
}
