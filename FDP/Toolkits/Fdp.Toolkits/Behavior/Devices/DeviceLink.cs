using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S5b (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-119, §3r D-52 / D-53) — a device's LINK: the devices it SENDS to
    /// (its peers). An ADDRESS, not a permission: the receiver accepts any message addressed to it (the address is the authorisation — D-53's "pairing is an
    /// address"); the sender sends only to its own peers. Unmanaged, recorded, scenario-authored (an entity reference per peer — the scenario's GUID); pairing in
    /// the sim is SD-S5's. ⭐⭐ An AUTHORED reference is an <see cref="EntityLink"/> — the network id plus how it was authored (R-184,
    /// DESIGN_Entity_Reference): the scenario names a peer by its entity GUID (or <c>@Key</c>), the ONE load walker resolves it
    /// (<c>StagingEntityExtractor</c> → <c>EntityRefRemap</c>), the editor offers the entity picker, and it means the same on every node. ⛔ Not a local
    /// <see cref="Entity"/> handle (node-local, recycled — R-184's "runtime state only"); it is resolved only where a message is FILED (the receiver's node, `DeviceMessages.Ingest`).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DeviceLinks)]
    [DataPolicy(DataPolicy.Default)]
    public struct DeviceLinks
    {
        public EntityLink Peer0;
        public EntityLink Peer1;

        /// <summary>Does the link name <paramref name="r"/>? (A compare of network ids — nothing is resolved.)</summary>
        public readonly bool Names(EntityRef r) => !r.IsNone && (Peer0.Ref == r || Peer1.Ref == r);

        /// <summary>A link to one peer (rails, tools).</summary>
        public static DeviceLinks To(EntityRef peer0, EntityRef peer1 = default) => new() { Peer0 = new EntityLink(peer0), Peer1 = new EntityLink(peer1) };
    }

    /// <summary>⭐ SD-S5b (D-58) — the 64 B binary body of a device message (an unmanaged DTO written by <see cref="DeviceMessages.Send{T}"/>).</summary>
    [InlineArray(Size)]
    public struct DeviceMessagePayload
    {
        public const int Size = 64;
#pragma warning disable IDE0044 // backing field required by InlineArray
        private byte _element;
#pragma warning restore IDE0044
    }

    /// <summary>
    /// ⭐⭐⭐ SD-S5b (D-119, the BINARY form of D-58) — ONE message from one device to another: published by the SENDER's behaviour (inside the device phase it
    /// rides the batch's command buffer and is played after the phase — <c>FdpEventBus.Publish</c>), filed by the RECEIVER's device tick ingest into its
    /// <see cref="DeviceInbox"/> the next frame, and applied by the receiver's own behaviour (self only). <see cref="Seq"/> is per sender: the receiver drops a
    /// repeat (R3-10, <see cref="DeviceMessageTracker"/>). Unmanaged, fixed size (~80 B), 0 B to send. ⭐ Both ends are NETWORK ids (<see cref="EntityRef"/>), so
    /// the same event crosses a node unchanged when SD-C carries it (the interaction transport delivers to the target's owner).
    /// </summary>
    [EventId(Fdp.Toolkit.Behavior.Possession.PossessionEventIds.DeviceMessage)]
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceMessageEvent
    {
        public EntityRef Target;
        public EntityRef From;
        public uint Seq;
        /// <summary>What the payload is — the DTO's kind (e.g. <c>TargetLocation.Kind</c>).</summary>
        public ushort Kind;
        /// <summary>The DTO's size in <see cref="Payload"/> (≤ 64).</summary>
        public ushort Size;
        public DeviceMessagePayload Payload;
    }

    /// <summary>⭐ SD-S5b — one filed message (the event without its target).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceMessage
    {
        public EntityRef From;
        public uint Seq;
        public ushort Kind;
        public ushort Size;
        public DeviceMessagePayload Payload;
    }

    /// <summary>Two <see cref="DeviceMessage"/>s.</summary>
    [InlineArray(Count)]
    public struct DeviceMessages2
    {
        public const int Count = 2;
#pragma warning disable IDE0044 // backing field required by InlineArray
        private DeviceMessage _element;
#pragma warning restore IDE0044
    }

    /// <summary>
    /// ⭐⭐ SD-S5b (D-119) — a device's INBOX: the NEWEST two messages addressed to it (<see cref="Messages"/>[0] the newest), written ONLY by the device tick's serial
    /// ingest (<see cref="DeviceMessages.Ingest"/>, beside the input merge — as <see cref="DeviceInput"/> is), read ONLY by the device's own behaviour
    /// (<see cref="DeviceMessages.TryTake{T}"/>), which copies what it keeps into its <c>[UnitMemory]</c>. Unmanaged, recorded (a debrief shows what arrived).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DeviceInbox)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DeviceInbox
    {
        public DeviceMessages2 Messages;
        /// <summary>How many of <see cref="Messages"/> are set (≤ 2).</summary>
        public byte Count;
        /// <summary>Messages ever filed (an older one past the second is dropped — the inbox keeps the newest two).</summary>
        public uint Filed;
    }

    /// <summary>⭐ SD-S5b (R3-10) — the last applied <c>Seq</c> per sender (two senders — a device has two peers), in the receiver's <c>[UnitMemory]</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceMessageTracker
    {
        public EntityRef From0;
        public uint Seq0;
        public EntityRef From1;
        public uint Seq1;

        /// <summary>True (and remembered) when (<paramref name="from"/>, <paramref name="seq"/>) is new — a later seq of a known sender, or a new sender (it takes
        /// the free slot, else the second); false for a repeat.</summary>
        public bool Accept(EntityRef from, uint seq)
        {
            if (!From0.IsNone && From0 == from) { if (seq <= Seq0) return false; Seq0 = seq; return true; }
            if (!From1.IsNone && From1 == from) { if (seq <= Seq1) return false; Seq1 = seq; return true; }
            if (From0.IsNone) { From0 = from; Seq0 = seq; return true; }
            From1 = from; Seq1 = seq;
            return true;
        }
    }

    /// <summary>
    /// ⭐⭐⭐ SD-S5b (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-119) — THE device message path, three calls and nothing else: a device behaviour
    /// <see cref="Send{T}"/>s an unmanaged DTO (≤ 64 B) to an address (one of its <see cref="DeviceLinks"/> — <see cref="SendToPeer{T}"/>); the device tick's serial
    /// pre-pass <see cref="Ingest"/>s every message of the frame into its target's <see cref="DeviceInbox"/> (only where this node runs the target's logic); the
    /// receiver's behaviour <see cref="TryTake{T}"/>s each new one (its own inbox, a per-sender seq — R3-10). ⭐ No device ever writes another: the message is an
    /// event, one tick late, applied by the receiver (thread-safe by construction, §3r). ⭐ A message to another device is NOT a world effect — it changes only
    /// the receiver's display state — so it is sent in pause too (the device tick runs every frame). 0 B.
    /// </summary>
    public static class DeviceMessages
    {
        /// <summary>Sends <paramref name="payload"/> of <paramref name="kind"/> from <paramref name="self"/> to <paramref name="target"/>: bumps the sender's
        /// <paramref name="seq"/> (its own memory) and publishes the event (inside the device phase it rides the batch's command buffer). False when there is no
        /// target, the sender has no network id (a device is always replicated; an unreplicated one cannot be answered), or the bus does not carry the event.</summary>
        public static bool Send<T>(EntityRepository w, Entity self, EntityRef target, ushort kind, in T payload, ref uint seq) where T : unmanaged
        {
            if (Unsafe.SizeOf<T>() > DeviceMessagePayload.Size)
                throw new ArgumentException($"a device message's payload is at most {DeviceMessagePayload.Size} B; {typeof(T).Name} is {Unsafe.SizeOf<T>()} B (D-58)");
            if (target.IsNone || !w.Bus.IsRegistered<DeviceMessageEvent>()) return false;
            var from = EntityRef.Of(w, self);
            if (from.IsNone || from == target) return false;
            var ev = new DeviceMessageEvent { Target = target, From = from, Seq = ++seq, Kind = kind, Size = (ushort)Unsafe.SizeOf<T>() };
            Span<byte> body = ev.Payload;
            MemoryMarshal.Write(body, in payload);
            w.Bus.Publish(ev);
            return true;
        }

        /// <summary>The device's peer <paramref name="index"/> (0 or 1) from its own <see cref="DeviceLinks"/> — a network id, never resolved here — or none.</summary>
        public static EntityRef Peer(EntityRepository w, Entity self, int index)
        {
            if (!w.IsComponentTypeRegistered<DeviceLinks>() || !w.HasComponent<DeviceLinks>(self)) return EntityRef.None;
            ref readonly var l = ref w.GetComponentRO<DeviceLinks>(self);
            return index == 0 ? l.Peer0.Ref : index == 1 ? l.Peer1.Ref : EntityRef.None;
        }

        /// <summary>Does <paramref name="self"/>'s own link name <paramref name="other"/>? (Network ids compared — nothing resolved, so a HUD may ask every frame.)</summary>
        public static bool IsLinked(EntityRepository w, Entity self, EntityRef other)
            => w.IsComponentTypeRegistered<DeviceLinks>() && w.HasComponent<DeviceLinks>(self) && w.GetComponentRO<DeviceLinks>(self).Names(other);

        /// <summary><see cref="Send{T}"/> to the device's own peer <paramref name="index"/>.</summary>
        public static bool SendToPeer<T>(EntityRepository w, Entity self, int index, ushort kind, in T payload, ref uint seq) where T : unmanaged
            => Send(w, self, Peer(w, self, index), kind, in payload, ref seq);

        /// <summary>
        /// The device tick's serial INGEST (<c>DeviceTickSystem</c>, beside the input merge): every <see cref="DeviceMessageEvent"/> of the frame aimed at a device
        /// whose logic this node runs is filed into its <see cref="DeviceInbox"/> (newest first, the newest two kept). ⭐ No operator gate — the address is the
        /// authorisation (D-53), and a device nobody operates still receives. Every frame, so each event is read once (no step window).
        /// </summary>
        public static void Ingest(EntityRepository repo)
        {
            if (!repo.Bus.IsRegistered<DeviceMessageEvent>() || !repo.IsComponentTypeRegistered<DeviceInbox>()) return;
            foreach (ref readonly var m in repo.ReadEvents<DeviceMessageEvent>())
            {
                var target = m.Target.Resolve(repo);   // ⭐ the one resolve of the path: a message is rare (a button press), so the lookup costs nothing
                if (!DeviceMarker.IsDevice(repo, target) || !Fdp.Toolkit.Behavior.Possession.PossessionSystem.OwnsBrain(repo, target)) continue;
                bool has = repo.HasComponent<DeviceInbox>(target);
                var inbox = has ? repo.GetComponentRO<DeviceInbox>(target) : default;
                inbox.Messages[1] = inbox.Messages[0];
                inbox.Messages[0] = new DeviceMessage { From = m.From, Seq = m.Seq, Kind = m.Kind, Size = m.Size, Payload = m.Payload };
                inbox.Count = (byte)Math.Min(inbox.Count + 1, DeviceMessages2.Count);
                inbox.Filed++;
                if (has) repo.SetComponent(target, inbox); else repo.AddComponent(target, inbox);
            }
        }

        /// <summary>
        /// The oldest message of <paramref name="kind"/> in <paramref name="self"/>'s own inbox that <paramref name="seen"/> has not applied (a later seq of its
        /// sender), as <typeparamref name="T"/>; marks it applied. Call until false. Reads only <paramref name="self"/>.
        /// </summary>
        public static bool TryTake<T>(EntityRepository w, Entity self, ushort kind, ref DeviceMessageTracker seen, out T payload, out EntityRef from) where T : unmanaged
        {
            payload = default; from = EntityRef.None;
            if (!w.IsComponentTypeRegistered<DeviceInbox>() || !w.HasComponent<DeviceInbox>(self)) return false;
            ref readonly var inbox = ref w.GetComponentRO<DeviceInbox>(self);
            for (int i = inbox.Count - 1; i >= 0; i--)
            {
                ref readonly var m = ref inbox.Messages[i];
                if (m.Kind != kind || m.Size != Unsafe.SizeOf<T>() || !seen.Accept(m.From, m.Seq)) continue;
                var body = m.Payload;
                payload = MemoryMarshal.Read<T>(body);
                from = m.From;
                return true;
            }
            return false;
        }
    }
}
