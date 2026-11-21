using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ <c>CE-4660</c> (backend, a cross-lane test helper — NOTE_Backend_To_Ui_Wreck_Reads_Fire.md) — the W3 wreck flames' production chain in one system:
/// the simulation's <see cref="Fdp.Toolkit.Combat.Systems.BurnSystem"/> lights a destroyed fuelled hull (its <c>FireState</c>, on the Health owner — here),
/// then <see cref="Hrot.UI.Common.Effects.WreckSpawnSystem"/> shows the flames because it burns. A rail kills a vehicle by setting Health 0, as before.
/// </summary>
internal sealed class BurningWrecks : IEcsModuleSystem
{
    private readonly Fdp.Toolkit.Combat.Systems.BurnSystem _burn = new();
    public readonly Hrot.UI.Common.Effects.WreckSpawnSystem Wrecks = new();

    public BurningWrecks(EntityRepository world)
    {
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.FireState>()) world.RegisterComponent<Fdp.Toolkit.Perception.Signatures.FireState>();
    }

    public int Spawned => Wrecks.Spawned;

    public void Execute(ISimulationView view, float deltaTime)
    {
        _burn.Execute(view, deltaTime);
        Wrecks.Execute(view, deltaTime);
    }
}
