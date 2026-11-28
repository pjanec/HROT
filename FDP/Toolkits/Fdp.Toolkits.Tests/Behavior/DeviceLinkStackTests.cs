using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S5b (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-119 / D-120, §3r D-56) — THE LINK and STACKING, at the toolkit:
    /// ① a message SENT is filed into its TARGET's inbox one frame later (the bus), by the device tick's ingest, and only there; the inbox keeps the NEWEST two;
    /// the receiver takes each once (a repeat seq is dropped — R3-10); 0 B on the steady path. ② the POSE CHAIN: a device mounted in another device's station
    /// points as its host does (host world pose ∘ station place ∘ own pose) and follows the host's look; a stacked device ignores its own look (D-56) and uses
    /// it again unstacked; the stack's root and operator.
    /// </summary>
    public class DeviceLinkStackTests
    {
        private struct Dto { public Vector3 Point; public float Range; }
        private const ushort Kind = 7;

        private static EntityRepository World()
        {
            var w = TestWorldFactory.Create();
            w.RegisterComponent<DeviceInfo>();
            w.RegisterComponent<PossessedBy>();
            w.RegisterComponent<DeviceInput>();
            w.RegisterComponent<DevicePose>();
            w.RegisterComponent<DeviceLinks>();
            w.RegisterComponent<DeviceInbox>();
            w.RegisterComponent<PersonCarrier>();
            w.RegisterComponent<CarrierSeats>();
            w.RegisterComponent<CarrierStations>();
            if (!w.IsComponentTypeRegistered<SimTransform>()) w.RegisterComponent<SimTransform>();
            if (!w.IsComponentTypeRegistered<NetworkIdentity>()) w.RegisterComponent<NetworkIdentity>();
            w.Bus.Register<DeviceMessageEvent>();
            return w;
        }

        private static Entity Device(EntityRepository w, Vector3 at = default)
        {
            var d = w.CreateEntity();
            w.AddComponent(d, new BehaviorState());
            w.SetAuthority<BehaviorState>(d, true);   // this node runs its logic (the ingest files only there)
            w.AddComponent(d, new DeviceInfo { DeviceType = 77 });
            w.AddComponent(d, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(d, new NetworkIdentity { Value = 5000 + d.Index });   // ⭐ a link and a message name a device by its NETWORK id (R-184)
            return d;
        }

        /// <summary>The frame boundary: last frame's events become readable, then the device tick's ingest files them.</summary>
        private static void Frame(EntityRepository w) { w.Bus.SwapBuffers(); DeviceMessages.Ingest(w); }

        private static int Received(EntityRepository w, Entity self, ref DeviceMessageTracker seen, out Dto last)
        {
            int n = 0; last = default;
            while (DeviceMessages.TryTake(w, self, Kind, ref seen, out Dto d, out _)) { last = d; n++; }
            return n;
        }

        [Fact]
        public void SDS5b_AMessage_IsFiledInItsTargetsInbox_OneFrameLate_AndTakenOnce_AndOnlyThere()
        {
            using var w = World();
            Entity a = Device(w), b = Device(w), c = Device(w);
            w.AddComponent(a, DeviceLinks.To(EntityRef.Of(w, b)));
            uint seq = 0;
            Assert.True(DeviceMessages.SendToPeer(w, a, 0, Kind, new Dto { Point = new Vector3(1, 2, 3), Range = 450f }, ref seq));
            Assert.Equal(1u, seq);
            DeviceMessages.Ingest(w);                                   // the same frame: nothing readable yet
            Assert.False(w.HasComponent<DeviceInbox>(b));
            Frame(w);                                                   // next frame
            Assert.True(w.HasComponent<DeviceInbox>(b));
            Assert.False(w.HasComponent<DeviceInbox>(c));               // ⭐ an unlinked device receives nothing
            Assert.False(w.HasComponent<DeviceInbox>(a));               // nor the sender
            var seenB = default(DeviceMessageTracker);
            Assert.Equal(1, Received(w, b, ref seenB, out var got));
            Assert.Equal(new Vector3(1, 2, 3), got.Point);
            Assert.Equal(450f, got.Range);
            Assert.Equal(0, Received(w, b, ref seenB, out _));          // taken once: a re-read inbox applies nothing
            Frame(w);
            Assert.Equal(0, Received(w, b, ref seenB, out _));          // nor the next frame
            Assert.False(DeviceMessages.SendToPeer(w, a, 1, Kind, new Dto(), ref seq));   // no second peer: nothing sent
        }

        [Fact]
        public void SDS5b_TheInbox_KeepsTheNewestTwo_AndARepeatSeq_IsDropped()
        {
            using var w = World();
            Entity a = Device(w), b = Device(w);
            uint seq = 0;
            for (int i = 1; i <= 3; i++) DeviceMessages.Send(w, a, EntityRef.Of(w, b), Kind, new Dto { Range = i }, ref seq);
            Frame(w);
            var inbox = w.GetComponentRO<DeviceInbox>(b);
            Assert.Equal(2, inbox.Count);
            Assert.Equal(3u, inbox.Filed);
            Assert.Equal(3u, inbox.Messages[0].Seq);                    // newest first
            Assert.Equal(2u, inbox.Messages[1].Seq);
            var seen = default(DeviceMessageTracker);
            Assert.Equal(2, Received(w, b, ref seen, out var last));    // oldest first, the newest last
            Assert.Equal(3f, last.Range);
            // a duplicate of seq 3 (a paused frame's event read again at resume, R3-10) is not applied
            var dup = new DeviceMessageEvent { Target = EntityRef.Of(w, b), From = EntityRef.Of(w, a), Seq = 3, Kind = Kind, Size = (ushort)System.Runtime.CompilerServices.Unsafe.SizeOf<Dto>() };
            w.Bus.Publish(dup);
            Frame(w);
            Assert.Equal(0, Received(w, b, ref seen, out _));
        }

        /// <summary>
        /// ⭐⭐ SD-S5b (R-184) — the link is AUTHORED as the peer's scenario GUID and resolved by the ONE load walker: the file's <c>{"Peer0": "&lt;guid&gt;"}</c>
        /// reads into <see cref="DeviceLinks"/> (the <see cref="EntityLink"/> converter, as the auto-serializer reads every non-handle field) and
        /// <see cref="EntityRefRemap.WalkObject"/> with this load's scope (<see cref="Fdp.Toolkit.Scenario.ScenarioRefScope"/>, GUID → the network id the
        /// entity will have — <c>StagingEntityExtractor</c>) names the peer's network id; an unset peer stays none; a save writes the GUID back.
        /// ⛔ No translator, no intent, no materialize step. 🔴 Red-proof: <c>DeviceLinks</c> back on <see cref="Entity"/> fields ⇒ the walker resolves
        /// nothing and the GUID does not deserialize ⇒ red.
        /// </summary>
        [Fact]
        public void SDS5b_ALinkAuthoredByGuid_IsResolvedByTheOneLoadWalker_ToThePeersNetworkId()
        {
            var peer = Guid.Parse("d5e10007-0000-4000-8000-000000000011");
            var opts = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            var links = System.Text.Json.JsonSerializer.Deserialize<DeviceLinks>($"{{\"Peer0\":\"{peer}\"}}", opts);
            Assert.True(links.Peer0.Ref.IsNone);                         // authored, not yet resolved
            var scope = new Fdp.Toolkit.Scenario.ScenarioRefScope("rail");
            scope.Add(new EntityRef(4242), null, peer, null);
            object boxed = links;
            Assert.True(EntityRefRemap.WalkObject(boxed, new RefResolve(scope)));
            links = (DeviceLinks)boxed;
            Assert.Equal(new EntityRef(4242), links.Peer0.Ref);
            Assert.True(links.Peer1.Ref.IsNone);
            Assert.True(links.Names(new EntityRef(4242)));
            Assert.Contains(peer.ToString(), System.Text.Json.JsonSerializer.Serialize(links, opts), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SDS5b_TheSteadyPath_SendIngestTake_AllocatesNothing()
        {
            using var w = World();
            Entity a = Device(w), b = Device(w);
            w.AddComponent(a, DeviceLinks.To(EntityRef.Of(w, b)));
            uint seq = 0;
            var seen = default(DeviceMessageTracker);
            void Step() { DeviceMessages.SendToPeer(w, a, 0, Kind, new Dto { Range = 1f }, ref seq); Frame(w); Received(w, b, ref seen, out _); }
            for (int i = 0; i < 40; i++) Step();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Step();
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // ── stacking ────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>A host device with one stacking station (seat 0) at <paramref name="z"/> above it, the stacked device in it.</summary>
        private static void Stack(EntityRepository w, Entity host, Entity top, float z)
        {
            var stations = new CarrierStations { StationMask = 1 };
            stations.Seats[0] = new StationSeat { Accepts = 32, OperatedBy = -1, Z = z };
            w.AddComponent(host, stations);
            var buffer = new PassengerBuffer();
            buffer.Passengers[0] = top;
            w.AddComponent(host, buffer);
            w.AddComponent(top, new IsEmbarkedTag { VehicleEntity = host, Seat = 0 });
        }

        private static void Look(EntityRepository w, Entity d, float yaw)
        {
            var c = new DeviceControl { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw), Trackers = DeviceControl.OrientationPresent };
            var input = new DeviceInput { Control = c };
            if (w.HasComponent<DeviceInput>(d)) w.SetComponent(d, input); else w.AddComponent(d, input);
        }

        private static Vector3 Forward(EntityRepository w, Entity d)
        {
            Assert.True(DevicePoses.TryWorld(w, d, out _, out var rot));
            return Vector3.Transform(Vector3.UnitX, rot);
        }

        [Fact]
        public void SDS5b_AStackedDevice_PointsAsItsRootLooks_IgnoresItsOwnLook_AndUsesItAgainUnstacked()
        {
            using var w = World();
            Entity root = Device(w, new Vector3(10f, 0f, 1.5f)), top = Device(w, new Vector3(10f, 0f, 1.5f));
            Stack(w, root, top, 0.08f);
            Assert.True(DeviceStacks.IsStacked(w, top));
            Assert.False(DeviceStacks.IsStacked(w, root));
            Assert.Equal(root, DeviceStacks.Root(w, top));
            Assert.Equal(root, DeviceStacks.Root(w, root));

            Look(w, root, MathF.PI / 2f);                 // the station's look turns north …
            Look(w, top, -MathF.PI / 2f);                 // … and the top device's own frame says south (a tracker on it, or the look relative to a stale mount)
            DeviceLooks.Follow(w, root, 0f);
            DeviceLooks.Follow(w, top, 0f);
            Assert.Equal(Quaternion.Identity, w.GetComponentRO<DevicePose>(top).Orientation);   // D-56: its own look ignored while stacked
            var fr = Forward(w, root);
            var ft = Forward(w, top);
            Assert.True(fr.Y > 0.999f, $"the root points north ({fr})");
            Assert.True(Vector3.Dot(fr, ft) > MathF.Cos(0.1f * MathF.PI / 180f), $"the stacked device points as its root ({ft} vs {fr})");
            Assert.True(DevicePoses.TryWorld(w, top, out var at, out _));
            Assert.Equal(1.58f, at.Z, 3);                 // the station place, in the root's frame

            // unstacked: its own look again
            w.RemoveComponent<IsEmbarkedTag>(top);
            DeviceLooks.Follow(w, top, 0f);
            Assert.True(Forward(w, top).Y < -0.999f, "unstacked, it points where its own look says");
        }

        [Fact]
        public void SDS5b_AStackedDevice_IsOperatedByItsRootsOperator()
        {
            using var w = World();
            var man = w.CreateEntity();
            w.AddComponent(man, new PersonCarrier());
            w.AddComponent(man, new PassengerBuffer());
            w.AddComponent(man, new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Fps, Epoch = 1 });
            Entity root = Device(w), top = Device(w);
            w.GetComponentRW<PassengerBuffer>(man).Passengers[4] = root;
            w.AddComponent(root, new IsEmbarkedTag { VehicleEntity = man, Seat = 4 });
            Stack(w, root, top, 0.08f);
            Assert.Equal(man, DeviceOperators.OperatorOf(w, top));
            DeviceOperators.Sync(w, top);
            Assert.Equal(PossessionMode.Operated, w.GetComponentRO<PossessedBy>(top).Mode);
            Assert.Equal((StationId)5, w.GetComponentRO<PossessedBy>(top).StationNodeId);
        }

        /// <summary>
        /// ⭐⭐ D-170 (user, `2026-11-28`) — a device CARRIED by a person is used at his EYE (THE eye rule, <c>TerrainWorldLosStrategy.EyeOf</c>: his feet +
        /// 1.7 m standing — the default mount — the rule sight and the rifle's shot use), not at his feet; its SimTransform stays the mount (D-169); a device
        /// stacked on it rises with it (the chain); a device NOT carried by a person stays where its SimTransform puts it.
        /// 🔴 Red-proof: drop the <c>CarriedBy</c> branch ⇒ the carried device's world Z is 0 (his feet) ⇒ red.
        /// </summary>
        [Fact]
        public void D170_ACarriedDevice_IsUsedAtItsCarriersEye_NotAtHisFeet()
        {
            using var w = World();
            if (!w.IsComponentTypeRegistered<IsEmbarkedTag>()) w.RegisterComponent<IsEmbarkedTag>();
            var man = w.CreateEntity();
            w.AddComponent(man, new PersonCarrier());
            w.AddComponent(man, new SimTransform { Position = new Vector3(5f, 0f, 0f), Rotation = Quaternion.Identity });
            Entity held = Device(w, new Vector3(5f, 0f, 0f)), top = Device(w), lying = Device(w, new Vector3(9f, 0f, 0f));
            w.AddComponent(held, new IsEmbarkedTag { VehicleEntity = man, Seat = 4 });
            Stack(w, held, top, 0.08f);

            Assert.True(DevicePoses.TryWorld(w, held, out var eye, out _));
            Assert.Equal(new Vector3(5f, 0f, Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding), eye);
            Assert.Equal(0f, w.GetComponentRO<SimTransform>(held).Position.Z);                                   // the mount point is unchanged (D-169)
            Assert.True(DevicePoses.TryWorld(w, top, out var stacked, out _));
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding + 0.08f, stacked.Z, 3);    // the stack rises with its root
            Assert.True(DevicePoses.TryWorld(w, lying, out var ground, out _));
            Assert.Equal(0f, ground.Z);                                                                          // not carried: its own place
        }
        /// <summary>
        /// ⭐⭐ D-171 — a device NOT carried by a person is used at its type's OPTIC above its base (<see cref="DeviceInfo.OpticOffset"/>, device frame, turned by
        /// its base): a tripod device on the ground 1.3 m above its feet (and 0.2 m ahead, turned with the tripod); its SimTransform stays its base (D-169); a
        /// device stacked on it starts from that place; a CARRIED device of the same type is at its carrier's eye instead (D-170 wins).
        /// 🔴 Red-proof: drop the <c>Optic</c> term ⇒ the tripod device's world Z is 0 ⇒ red.
        /// </summary>
        [Fact]
        public void D171_ADeviceNotCarried_IsUsedAtItsTypesOptic_AboveItsBase()
        {
            using var w = World();
            if (!w.IsComponentTypeRegistered<IsEmbarkedTag>()) w.RegisterComponent<IsEmbarkedTag>();
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);   // the tripod faces north
            var tripod = Device(w, new Vector3(20f, 0f, 0f));
            w.SetComponent(tripod, new SimTransform { Position = new Vector3(20f, 0f, 0f), Rotation = yaw });
            var info = w.GetComponentRO<DeviceInfo>(tripod); info.OpticOffset = new Vector3(0.2f, 0f, 1.3f); w.SetComponent(tripod, info);

            Assert.True(DevicePoses.TryWorld(w, tripod, out var lens, out _));
            Assert.Equal(20f, lens.X, 3); Assert.Equal(0.2f, lens.Y, 3); Assert.Equal(1.3f, lens.Z, 3);   // above its feet, ahead along its own facing
            Assert.Equal(0f, w.GetComponentRO<SimTransform>(tripod).Position.Z);                            // the base is unchanged (D-169)

            var top = Device(w);
            Stack(w, tripod, top, 0.08f);
            Assert.True(DevicePoses.TryWorld(w, top, out var stacked, out _));
            Assert.Equal(1.38f, stacked.Z, 3);                                                               // the stack starts at the optic

            var man = w.CreateEntity();
            w.AddComponent(man, new PersonCarrier());
            w.AddComponent(man, new SimTransform { Position = new Vector3(30f, 0f, 0f), Rotation = Quaternion.Identity });
            var held = Device(w, new Vector3(30f, 0f, 0f));
            var hi = w.GetComponentRO<DeviceInfo>(held); hi.OpticOffset = new Vector3(0.2f, 0f, 1.3f); w.SetComponent(held, hi);
            w.AddComponent(held, new IsEmbarkedTag { VehicleEntity = man, Seat = 4 });
            Assert.True(DevicePoses.TryWorld(w, held, out var eye, out _));
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding, eye.Z, 3);              // carried: his eye, not the type's optic
        }
    }
}
