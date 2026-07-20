using Fdp.Core;
using Hrot.Editor.AiShared;

namespace Hrot.AI.Behaviors
{
    /// <summary>
    /// Q#14 minimal custom-event demo carrier. A blittable, <c>[EventId]</c>-registered struct that is BOTH
    /// publishable from a blueprint (the <c>CustomEventPublisherDemo</c> Tick graph does
    /// <c>world.Bus.Publish(new PingEvent{ Value = 42, Target = self })</c>) AND subscribable via a named-event
    /// <c>EventEntry</c> (the <c>CustomEventSubscriberDemo</c> Event graph mirrors <see cref="Value"/> into a
    /// WorkingState field). Because it carries a real <c>[EventId]</c>, the publisher's typed publish
    /// (<c>EventType&lt;PingEvent&gt;.Id</c>) and the dispatch pump's FQN→<c>[EventId].Id</c> resolution agree,
    /// so the event routes end-to-end through <c>BlueprintTickSystem</c> — the runtime the editor ticks live.
    ///
    /// <para>
    /// <see cref="BlueprintEventAttribute"/> makes it discoverable in the editor's grouped picker (no FQN typing);
    /// the single <see cref="EventTargetAttribute"/> field designates the recipient for the future Self/Any filter
    /// (not yet enforced at dispatch — broadcast for now). All fields are blittable (§7.3): no managed refs.
    /// </para>
    /// </summary>
    [EventId(7401)]
    [BlueprintEvent("Demo", DisplayName = "Ping (custom-event demo)")]
    public struct PingEvent
    {
        /// <summary>Recipient entity (Self/Any filter target). Broadcast until the filter lands (Q#14 3d).</summary>
        [EventTarget]
        public Entity Target;

        /// <summary>Arbitrary payload the subscriber mirrors into WorkingState — proves full payload delivery.</summary>
        public int Value;
    }
}
