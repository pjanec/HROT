using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using CycloneDDS.Schema;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4079</c> ② — every DESCRIPTOR topic's writer lets go of an instance WITHOUT disposing it
    /// (<c>[DdsQos(AutodisposeUnregisteredInstances = false)]</c>), and every message topic keeps the library default.
    /// 📄 docs/DESIGN_Ownership_Groups_And_Grants.md §5.3 (the giver's unregister after the grantee confirmed, F7);
    /// docs/reference/NED_DDS_Protocol.md §Ownership (handoff: *"the old owner stops writing without dispose"*).
    ///
    /// <para>Two halves. ① <b>The attribute</b> — a rail over every <c>[DdsTopic]</c> type in the production assemblies, by the inventory's
    /// own rule (<c>keyed ∧ TransientLocal ⇒ descriptor</c>, plus the two forced overrides; NED_DDS_Protocol.md §Not determinable 10).
    /// ② <b>What the attribute does on the wire</b>, over real DDS and three participants — the giver, the new owner and a reader —
    /// on a real descriptor type: the giver's unregister leaves the instance ALIVE for the reader (written by the new owner), and the new
    /// owner's departure then shows as <c>NotAliveNoWriters</c>, never <c>NotAliveDisposed</c>.</para>
    ///
    /// <para>🔴 Red-proofs (each compiles): ① set one descriptor's attribute back to <c>true</c> ⇒ the rail names it; ② set
    /// <c>EntityLean</c>'s back to the default ⇒ the first wire test sees <c>NotAliveDisposed</c> at the giver's unregister and the second
    /// never reaches <c>NotAliveNoWriters</c>.</para>
    /// </summary>
    public sealed class TheDescriptorTopicsDoNotAutodisposeTests
    {
        // NED.Tests' other DDS tests use 120, 121, 122, 150, 151, 171, 200.
        private const uint WireDomain = 124;

        // ── ① the attribute ─────────────────────────────────────────────────────────────────────────

        /// <summary>The inventory's two forced overrides (NED_DDS_Protocol.md §Not determinable 10): a descriptor / a message regardless of QoS.</summary>
        private static readonly HashSet<string> ForcedDescriptors = new() { "EntityStateTopic", "SensorTargets" };
        private static readonly HashSet<string> ForcedMessages = new() { "NodeIdRequest", "NodeIdGrant" };

        private static bool IsKeyed(Type t)
            => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .Any(m => m.GetCustomAttribute<DdsKeyAttribute>() is not null);

        /// <summary>The inventory's rule: keyed ∧ TransientLocal ⇒ descriptor (plus the forced ones, minus the forced messages).</summary>
        internal static bool IsDescriptor(Type t)
        {
            // GizmoMap is vendored (FDP/ExtDeps, outside the inventory — scripts/ned-wire-inventory.py skips ExtDeps): not ours to change.
            if (t.Assembly.GetName().Name?.StartsWith("GizmoMap.", StringComparison.Ordinal) == true) return false;
            if (ForcedMessages.Contains(t.Name)) return false;
            if (ForcedDescriptors.Contains(t.Name)) return true;
            var qos = t.GetCustomAttribute<DdsQosAttribute>();
            return qos is { Durability: DdsDurability.TransientLocal } && IsKeyed(t);
        }

        [Fact]
        public void TheScanFindsTheDescriptorTopics()
        {
            int descriptors = TheTopicNamesAreDerivedFromTypesTests.TopicTypes.Value.Count(IsDescriptor);
            // The inventory (docs/reference/NED_DDS_Protocol.md) lists 66 (67 before R-3067 / AQ109 X3 retired MapInteractionConfig); a few more may be added, never silently lost.
            Assert.True(descriptors >= 66, $"only {descriptors} descriptor topics found — the scan did not reach the production assemblies");
        }

        [Fact]
        public void EveryDescriptorTopic_ReleasesAnUnregisteredInstanceWithoutDisposingIt()
        {
            var wrong = TheTopicNamesAreDerivedFromTypesTests.TopicTypes.Value
                .Where(IsDescriptor)
                .Where(t => t.GetCustomAttribute<DdsQosAttribute>() is not { AutodisposeUnregisteredInstances: false })
                .Select(t => t.FullName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            Assert.True(wrong.Count == 0,
                "descriptor topics whose writer would DISPOSE an instance it unregisters (set AutodisposeUnregisteredInstances = false):\n  "
                + string.Join("\n  ", wrong));
        }

        [Fact]
        public void EveryMessageTopic_KeepsTheLibraryDefault()
        {
            // A message has no owner and is never handed over: its writers keep the default, so closing a writer disposes what it sent.
            var wrong = TheTopicNamesAreDerivedFromTypesTests.TopicTypes.Value
                .Where(t => !IsDescriptor(t))
                .Where(t => t.GetCustomAttribute<DdsQosAttribute>() is { AutodisposeUnregisteredInstances: false })
                .Select(t => t.FullName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            Assert.True(wrong.Count == 0,
                "message topics that set AutodisposeUnregisteredInstances = false (only descriptors are handed over):\n  " + string.Join("\n  ", wrong));
        }

        // ── ② the wire ──────────────────────────────────────────────────────────────────────────────

        private const int GiverNode = 41, NewOwnerNode = 42, ReaderNode = 99;
        private const long Entity = 7;

        private sealed record Seen(DdsInstanceState State, bool Valid, int? Sender, long EntityId, byte Side);

        private static DdsParticipant Node(int appInstanceId)
        {
            var p = new DdsParticipant(WireDomain);
            p.EnableSenderTracking(new SenderIdentityConfig { AppDomainId = (int)WireDomain, AppInstanceId = appInstanceId });
            return p;
        }

        /// <summary>Takes what the reader holds, noting each sample's instance state, validity, writer and key.</summary>
        private static List<Seen> Drain(DdsReader<EntityLean> reader)
        {
            var seen = new List<Seen>();
            using var loan = reader.Take();
            int i = 0;
            foreach (var s in loan)
            {
                int? sender = loan.GetSender(i)?.AppInstanceId;
                if (s.IsValid) seen.Add(new Seen(s.Info.InstanceState, true, sender, s.Data.EntityId, s.Data.Side));
                else
                {
                    var key = DdsTypeSupport.FromNative<EntityLean>(s.NativePtr);   // a not-alive sample carries only its key
                    seen.Add(new Seen(s.Info.InstanceState, false, sender, key.EntityId, 0));
                }
                i++;
            }
            return seen;
        }

        /// <summary>Re-polls until <paramref name="done"/> holds over everything seen so far (or the time is up). Returns all that was seen.</summary>
        private static List<Seen> PollUntil(DdsReader<EntityLean> reader, Func<List<Seen>, bool> done, int seconds = 10, Action? eachRound = null)
        {
            var all = new List<Seen>();
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                eachRound?.Invoke();
                all.AddRange(Drain(reader));
                if (done(all)) break;
                Thread.Sleep(50);
            }
            return all;
        }

        /// <summary>Giver and new owner both write the instance, the reader has seen both, then the giver lets go (an unregister).</summary>
        private static (DdsParticipant giver, DdsParticipant owner, DdsParticipant observer,
                        DdsWriter<EntityLean> giverWriter, DdsWriter<EntityLean> ownerWriter, DdsReader<EntityLean> reader,
                        List<Seen> seen) HandOver()
        {
            var giver = Node(GiverNode);
            var owner = Node(NewOwnerNode);
            var observer = Node(ReaderNode);
            var reader = new DdsReader<EntityLean>(observer);
            reader.EnableSenderTracking(observer.SenderRegistry!);
            var giverWriter = new DdsWriter<EntityLean>(giver);
            var ownerWriter = new DdsWriter<EntityLean>(owner);

            // The giver wrote it first (it created the entity) — re-written each round: a sample sent before discovery matched is lost.
            var seen = PollUntil(reader, s => s.Any(x => x.Sender == GiverNode && x.State == DdsInstanceState.Alive),
                eachRound: () => giverWriter.Write(new EntityLean { EntityId = Entity, Side = 1 }));
            Assert.Contains(seen, x => x.Sender == GiverNode && x.State == DdsInstanceState.Alive);

            // The new owner confirms by writing it.
            seen.AddRange(PollUntil(reader, s => s.Any(x => x.Sender == NewOwnerNode && x.State == DdsInstanceState.Alive),
                eachRound: () => ownerWriter.Write(new EntityLean { EntityId = Entity, Side = 2 })));
            Assert.Contains(seen, x => x.Sender == NewOwnerNode && x.State == DdsInstanceState.Alive);

            // ⭐ The giver lets go — what the creator's translator does once the grantee confirmed (F7).
            giverWriter.UnregisterInstance(new EntityLean { EntityId = Entity });
            return (giver, owner, observer, giverWriter, ownerWriter, reader, seen);
        }

        [Fact]
        public void TheGiversUnregister_LeavesTheInstanceAliveForReaders_WrittenByTheNewOwner()
        {
            var (giver, owner, observer, giverWriter, ownerWriter, reader, before) = HandOver();
            using (giver) using (owner) using (observer) using (giverWriter) using (ownerWriter) using (reader)
            {
                // ⭐ Right after the unregister — before the new owner writes again — the reader must see NOTHING happen to the instance. The
                //   library default would DISPOSE it here (a NotAliveDisposed the next write would hide again, so the window is watched).
                var quiet = PollUntil(reader, _ => false, seconds: 1);
                Assert.DoesNotContain(quiet, x => x.State != DdsInstanceState.Alive);

                // The new owner keeps writing; what the reader sees from now on is the instance, alive, from the new owner only.
                var after = PollUntil(reader, s => s.Any(x => x.Valid && x.Side == 3), seconds: 10,
                    eachRound: () => ownerWriter.Write(new EntityLean { EntityId = Entity, Side = 3 }));
                after.AddRange(Drain(reader));
                after.InsertRange(0, quiet);
                var everything = before.Concat(after).ToList();

                // The sample the new owner wrote after the giver let go: alive, from the new owner.
                var latest = after.Last(x => x.Valid && x.Side == 3);
                Assert.Equal(DdsInstanceState.Alive, latest.State);
                Assert.Equal(NewOwnerNode, latest.Sender);

                // ⭐ And the instance was never deleted along the way (the library default would have disposed it at the unregister).
                Assert.DoesNotContain(everything, x => x.State == DdsInstanceState.NotAliveDisposed);
                Assert.DoesNotContain(everything, x => x.State == DdsInstanceState.NotAliveNoWriters);   // the new owner is still a writer
            }
        }

        [Fact]
        public void ALateJoinerAfterTheHandover_GetsTheInstanceFromTheNewOwner()
        {
            var (giver, owner, observer, giverWriter, ownerWriter, reader, _) = HandOver();
            using (giver) using (owner) using (observer) using (giverWriter) using (ownerWriter) using (reader)
            {
                // A node that starts reading after the giver let go (TransientLocal): the new owner's writer hands it the instance, alive.
                // ⚠ Measured (CycloneDDS.NET 1.2.20): the GIVER's own copy is gone once it unregisters — a descriptor with no other
                //   writer is not handed to a late joiner; that is why the new owner must be a writer BEFORE the giver lets go (F7).
                using var lateParticipant = Node(ReaderNode + 1);
                using var late = new DdsReader<EntityLean>(lateParticipant);
                late.EnableSenderTracking(lateParticipant.SenderRegistry!);
                var seen = PollUntil(late, s => s.Any(x => x.Valid), seconds: 10);

                var got = seen.Last(x => x.Valid);
                Assert.Equal(DdsInstanceState.Alive, got.State);
                Assert.Equal(Entity, got.EntityId);
                Assert.Equal(NewOwnerNode, got.Sender);
            }
        }

        [Fact]
        public void WhenTheLastWriterGoesAway_TheReaderSeesNoWriters_NotDisposed()
        {
            var (giver, owner, observer, giverWriter, ownerWriter, reader, before) = HandOver();
            using (giver) using (observer) using (giverWriter) using (reader)
            {
                // The new owner (now the SOLE writer) goes away — a crash or a clean exit reaches the same state.
                ownerWriter.Dispose();
                owner.Dispose();

                var after = PollUntil(reader, s => s.Any(x => x.State != DdsInstanceState.Alive), seconds: 15);
                var everything = before.Concat(after).ToList();

                // ⭐ The spec's partial-owner mechanism: a descriptor whose owner left reads "no writers", not "deleted".
                Assert.Contains(everything, x => x.State == DdsInstanceState.NotAliveNoWriters && x.EntityId == Entity);
                Assert.DoesNotContain(everything, x => x.State == DdsInstanceState.NotAliveDisposed);
            }
        }
    }
}
