using System;
using System.Collections.Concurrent;
using System.Reflection;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Q#14 (3b) — per-slot custom-event dispatch. For one blueprint instance's <see cref="BlueprintDefinition"/>,
/// invokes each Event-graph handler whose event is present on the bus this frame, passing the event's raw
/// payload bytes (pump-passes-payload, per the architect). Called from <c>BlueprintTickSystem</c>'s slot loop
/// so it reuses that system's entity/slot iteration. <c>HasEvent</c>-gated — absent events cost nothing.
/// </summary>
public static class BlueprintEventDispatch
{
    // Event-key (Event-graph name = event identity) → runtime bus type-id. Resolution is reflection-based
    // (find the type, read [EventId]) so it is cached; the set of event types is small + stable.
    private static readonly ConcurrentDictionary<string, int> _typeIdCache = new();

    /// <summary>
    /// Invokes <paramref name="def"/>'s event handlers for every present event, once per event instance,
    /// with the instance's <paramref name="stateBytes"/> and the event's payload byte-span.
    /// </summary>
    public static void DispatchForSlot(
        BlueprintDefinition def, Span<byte> stateBytes, FdpEventBus bus,
        ISimulationView view, IEntityCommandBuffer ecb, Entity self, float time, float deltaTime)
    {
        if (bus is null || def?.EventHandlers is null || def.EventHandlers.Count == 0) return;

        foreach (var kv in def.EventHandlers)
        {
            int typeId = ResolveTypeId(kv.Key);
            if (!bus.HasEvent(typeId)) continue;

            var raw = bus.ReadRawByTypeId(typeId, out int elementSize);
            if (elementSize <= 0 || raw.Length < elementSize) continue;

            int count = raw.Length / elementSize;
            for (int i = 0; i < count; i++)
                kv.Value(stateBytes, view, ecb, self, time, deltaTime, raw.Slice(i * elementSize, elementSize));
        }
    }

    /// <summary>
    /// Resolves an event key (the Event graph's name = the event identity — an event-type FQN, or a custom
    /// event name) to its runtime bus type-id: <c>[EventId].Id</c> for a typed event (matching
    /// <c>EventType&lt;T&gt;.Id</c>), else the FQN hash (matching the bus's custom/untyped fallback).
    /// </summary>
    public static int ResolveTypeId(string eventKey) => _typeIdCache.GetOrAdd(eventKey, ResolveUncached);

    private static int ResolveUncached(string eventKey)
    {
        var type = FindType(eventKey);
        var attr = type?.GetCustomAttribute<EventIdAttribute>();
        if (attr != null) return attr.Id;
        return eventKey.GetHashCode() & 0x7FFFFFFF;
    }

    private static Type? FindType(string fqn)
    {
        var t = Type.GetType(fqn);
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { t = asm.GetType(fqn); } catch { continue; }
            if (t != null) return t;
        }
        return null;
    }
}
