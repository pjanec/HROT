using System.Collections.Generic;
using Hrot.ScenarioEditor;
using Fdp.ModuleHost.Core.Abstractions;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Smoke tests for <see cref="ScenarioEditorModule"/> (PACK2-E001).
/// </summary>
public class ScenarioEditorModuleTests
{
    // ── Stub ISystemRegistry ──────────────────────────────────────────────────

    private sealed class CapturingRegistry : ISystemRegistry
    {
        private readonly List<IEcsModuleSystem> _systems = new();
        public IReadOnlyList<IEcsModuleSystem> RegisteredSystems => _systems;

        public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => _systems.Add(system);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Name_IsScenarioEditor()
    {
        var module = new ScenarioEditorModule();

        Assert.Equal("ScenarioEditor", module.Name);
    }

    [Fact]
    public void RegisterSystems_DoesNotThrow_AndRegistersNoSystems()
    {
        var module   = new ScenarioEditorModule();
        var registry = new CapturingRegistry();

        var ex = Record.Exception(() => module.RegisterSystems(registry));

        Assert.Null(ex);
        Assert.Empty(registry.RegisteredSystems);
    }
}
