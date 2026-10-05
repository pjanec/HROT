using System;
using System.Collections.Generic;
using Fdp.Toolkit.Behavior.Shared;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐ <c>CE-3040</c> — THE ONE assignment of an HSM asset's event ids, used by the emitter (<c>HsmEmitCore</c>) and the
/// editor's mapper so the two cannot disagree. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.3b.
/// <list type="number">
/// <item>an event named after an engine-raised event (<c>Sensor.FirstThreat</c> …) gets its reserved id;</item>
/// <item>otherwise its stored id when non-zero;</item>
/// <item>otherwise the next sequential id from 1 (assets without stored ids).</item>
/// </list>
/// </summary>
public static class HsmEventIds
{
    /// <summary>The id of every event, by name, in declaration order.</summary>
    public static Dictionary<string, ushort> Assign(IEnumerable<(string Name, ushort StoredId)> events)
    {
        var ids = new Dictionary<string, ushort>(StringComparer.Ordinal);
        ushort fallbackId = 1;
        foreach (var (name, stored) in events)
        {
            ushort id;
            if (BuiltInHsmEvents.TryGetId(name, out var reserved)) id = reserved;
            else if (stored != 0) id = stored;
            else id = fallbackId++;
            ids[name] = id;
        }
        return ids;
    }

    /// <summary>⭐ CE-2088 — every engine-raised event, by name and reserved id (what the HSM editor offers).</summary>
    public static IReadOnlyList<(string Name, ushort Id)> BuiltIns { get; } = BuildBuiltIns();

    private static IReadOnlyList<(string Name, ushort Id)> BuildBuiltIns()
    {
        var list = new List<(string, ushort)>();
        foreach (var name in BuiltInHsmEvents.SensorNames)
            if (BuiltInHsmEvents.TryGetId(name, out var id)) list.Add((name, id));
        return list;
    }

    /// <summary>The reserved id of an engine-raised event, if <paramref name="name"/> names one.</summary>
    public static bool TryGetBuiltIn(string? name, out ushort id) => BuiltInHsmEvents.TryGetId(name, out id);
}
