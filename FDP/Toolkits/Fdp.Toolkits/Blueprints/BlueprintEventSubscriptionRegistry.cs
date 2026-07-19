namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Q#14 (3a) — indexes blueprint event subscriptions: <b>event type-id → the blueprint definitions whose
/// Event graphs handle it</b>. Built once from the registered <see cref="BlueprintDefinition"/>s (whose
/// <see cref="BlueprintDefinition.EventHandlers"/> are keyed by the event identity — the Event graph's name /
/// FQN). The per-tick dispatch pump queries this index, gated by the bus's <c>HasEvent</c>, so only handlers
/// for event-types actually present this frame are invoked — never a full per-event scan of every blueprint
/// (architect §7.2).
/// </summary>
public sealed class BlueprintEventSubscriptionRegistry
{
    /// <summary>One (definition, event-handler-key) subscription resolved to its runtime type-id.</summary>
    public readonly record struct Subscription(BlueprintDefinition Def, string EventKey, int TypeId);

    private readonly Dictionary<int, List<Subscription>> _byTypeId = new();

    private BlueprintEventSubscriptionRegistry() { }

    /// <summary>
    /// Builds the index from <paramref name="defs"/>. <paramref name="resolveTypeId"/> maps an event-handler
    /// key (the Event graph's name / event FQN) to its runtime bus type-id (e.g. <c>EventType&lt;T&gt;.Id</c>
    /// for a typed event, or the FQN hash for a custom carrier).
    /// </summary>
    public static BlueprintEventSubscriptionRegistry Build(
        IEnumerable<BlueprintDefinition> defs, Func<string, int> resolveTypeId)
    {
        if (defs is null) throw new ArgumentNullException(nameof(defs));
        if (resolveTypeId is null) throw new ArgumentNullException(nameof(resolveTypeId));

        var reg = new BlueprintEventSubscriptionRegistry();
        foreach (var def in defs)
        {
            if (def?.EventHandlers is null || def.EventHandlers.Count == 0) continue;
            foreach (var key in def.EventHandlers.Keys)
            {
                var typeId = resolveTypeId(key);
                if (!reg._byTypeId.TryGetValue(typeId, out var list))
                    reg._byTypeId[typeId] = list = new List<Subscription>();
                list.Add(new Subscription(def, key, typeId));
            }
        }
        return reg;
    }

    /// <summary>Subscriptions registered for <paramref name="typeId"/> (empty when none).</summary>
    public IReadOnlyList<Subscription> ForTypeId(int typeId)
        => _byTypeId.TryGetValue(typeId, out var list) ? list : Array.Empty<Subscription>();

    /// <summary>The distinct subscribed type-ids (the pump intersects these with events present on the bus).</summary>
    public IReadOnlyCollection<int> SubscribedTypeIds => _byTypeId.Keys;

    /// <summary>True when at least one blueprint subscribes to <paramref name="typeId"/>.</summary>
    public bool HasSubscribers(int typeId) => _byTypeId.ContainsKey(typeId);
}
