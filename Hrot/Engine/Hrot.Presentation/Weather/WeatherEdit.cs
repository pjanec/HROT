using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Weather;

namespace Hrot.UI.Common.Weather;

/// <summary>
/// ⭐ CE-4346 (X-3; 📄 docs/UX/DESIGN_Details_Forms.md Q8 + §E2 weather, docs/designs/weather/DESIGN_Weather_And_Time.md §7) — edits a WHOLE
/// <see cref="WeatherConditions"/> (the global weather on the World Environment, or a Weather Region's): the scalars have attributes, but the wind
/// levels and cloud layers are LISTS, and a list travels to its owner whole (Q8 — the route's precedent, CE-4284).
/// <para>⭐ The one path: write it here (an owner is done; a non-owner shows it at once) and publish ONE <see cref="UpdateEntityCommand"/> — the NED
/// egress (<c>UpdateEntityCommandEgressTranslator.ProcessWeatherUpdate</c>) sends it to the owner as <c>UpdateEntityDescriptorRequest(dtWeather)</c>
/// when this node does not own the weather, the owner applies it and its weather egress republishes it to every node. ⭐ The networkless editor
/// owns everything: the local write IS the edit.</para>
/// <para>⭐ Change a level / layer with <see cref="WeatherConditions.AddWind"/> · <see cref="WeatherConditions.RemoveWind"/> ·
/// <see cref="WeatherConditions.SetWind"/> (and the cloud twins) on a copy, then <see cref="Set"/> it.</para>
/// </summary>
public static class WeatherEdit
{
    /// <summary>The weather <paramref name="e"/> carries; false when it has none.</summary>
    public static bool TryRead(EntityRepository world, Entity e, out WeatherConditions weather)
    {
        weather = default;
        if (!world.IsAlive(e) || !world.IsComponentTypeRegistered<WeatherConditions>() || !world.HasComponent<WeatherConditions>(e)) return false;
        weather = world.GetComponentRO<WeatherConditions>(e);
        return true;
    }

    /// <summary>
    /// Sets <paramref name="e"/>'s whole weather to <paramref name="weather"/>: written here and sent to its owner when this node is not it.
    /// False when the entity has no weather (nothing is written).
    /// </summary>
    public static bool Set(EntityRepository world, Entity e, in WeatherConditions weather)
    {
        if (!TryRead(world, e, out _)) return false;
        world.SetComponent(e, weather);
        if (world.IsComponentTypeRegistered<NetworkIdentity>() && world.HasComponent<NetworkIdentity>(e))
            world.Bus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId          = world.GetComponentRO<NetworkIdentity>(e).Value,
                ComponentsToUpdate = new List<object> { weather },   // boxed once per edit (a user action, not a frame)
                RequestId          = Guid.NewGuid(),
            });
        return true;
    }
}
