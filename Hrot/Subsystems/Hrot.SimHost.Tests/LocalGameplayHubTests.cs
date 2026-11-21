using System.Collections.Generic;
using Hrot.Gameplay;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-11027</c> S12-2 (<c>docs/DESIGN_Player_Join.md</c> §12.2 rule table, Q114 C / D) — the semantics of the editor's in-process gameplay transport that the readers rely on. The deploy rails themselves run over this hub
    /// through <see cref="GameplayDeployServerTests"/> / <see cref="GameplayDeployFlowTests"/>; this class pins the hub's OWN rules, one rail per rule.
    /// </summary>
    public sealed class LocalGameplayHubTests
    {
        private static DeployOfferTopic Offer(int unit, string name = "u") => new() { UnitNetId = unit, SlotName = name, Side = 1 };
        private static PlayerSlotTopic Slot(int station, byte state = 1) => new() { StationNodeId = station, Name = "s", State = state };
        private static List<GameplaySample<T>> Take<T>(IGameplayIn<T> reader) { var l = new List<GameplaySample<T>>(); reader.TakeInto(l); return l; }

        /// <summary>Offers, status and slots BROADCAST: every client end reads every sample, each from its own queue (one end taking them does not take them from the other). 🔴 Mutation: delivered to only one end ⇒ red.</summary>
        [Fact]
        public void CE11027_HasServer_IsTrueOnlyWhileAServerEndIsOpen_AndReadingItOpensNothing()
        {
            var hub = new LocalGameplayHub();
            Assert.False(hub.HasServer);
            Assert.False(hub.HasServer);   // asking twice opens nothing
            hub.ClientEnd();
            Assert.False(hub.HasServer);   // a client end is no server
            hub.ServerEnd();
            Assert.True(hub.HasServer);
        }

        [Fact]
        public void OffersStatusAndSlots_AreBroadcastToEveryClientEnd()
        {
            var hub = new LocalGameplayHub();
            var server = hub.ServerEnd();
            var a = hub.ClientEnd(); var b = hub.ClientEnd();
            server.Offers!.Write(Offer(1)); server.Status!.Write(new DeployStatusTopic { StationNodeId = 7, RequestId = "r", Accepted = true }); server.SlotWriter!.Write(Slot(7));
            foreach (var end in new[] { a, b })
            {
                Assert.Equal(1, Assert.Single(Take(end.OfferReader!)).Data.UnitNetId);
                Assert.Equal("r", Assert.Single(Take(end.StatusReader!)).Data.RequestId);
                Assert.Equal(7, Assert.Single(Take(end.SlotReader!)).Data.StationNodeId);
            }
            Assert.Equal(7, Assert.Single(Take(server.SlotReader!)).Data.StationNodeId);   // the server's own slot reader sees them too (as on DDS)
        }

        /// <summary>Requests go to the SERVER end only; a client end has no reader for them. 🔴 Mutation: a request also delivered to a client's queue ⇒ red.</summary>
        [Fact]
        public void Requests_GoToTheServerEndOnly()
        {
            var hub = new LocalGameplayHub();
            var server = hub.ServerEnd();
            var a = hub.ClientEnd(); var b = hub.ClientEnd();
            a.RequestWriter!.Write(new DeployRequestTopic { RequestId = "x", StationNodeId = 5 });
            Assert.Null(a.Requests); Assert.Null(b.Requests);
            var got = Assert.Single(Take(server.Requests!));
            Assert.Equal(("x", 5), (got.Data.RequestId, got.Data.StationNodeId));
            Assert.Empty(Take(server.Requests!));   // taken once
            Assert.Empty(Take(b.StatusReader!));
        }

        /// <summary>A client end made LATE receives the CURRENT last values first (a replace of a key shows only the latest; a disposed key is not shown). 🔴 Mutation: the last values not replayed to a new end ⇒ red.</summary>
        [Fact]
        public void ALateClientEnd_ReceivesTheLastValuePerKey()
        {
            var hub = new LocalGameplayHub();
            var server = hub.ServerEnd();
            server.Offers!.Write(Offer(1, "old")); server.Offers.Write(Offer(1, "new")); server.Offers.Write(Offer(2)); server.Offers.DisposeInstance(Offer(2));
            server.Status!.Write(new DeployStatusTopic { StationNodeId = 7, RequestId = "r1" }); server.Status.Write(new DeployStatusTopic { StationNodeId = 7, RequestId = "r2" });
            server.SlotWriter!.Write(Slot(7)); server.SlotWriter.Write(Slot(8));

            var late = hub.ClientEnd();
            var offers = Take(late.OfferReader!);
            var only = Assert.Single(offers);
            Assert.Equal(("new", true), (only.Data.SlotName, only.Alive));
            Assert.Equal("r2", Assert.Single(Take(late.StatusReader!)).Data.RequestId);
            Assert.Equal(2, Take(late.SlotReader!).Count);
        }

        /// <summary>A dispose reaches every receiver NOT ALIVE with its KEY in <c>Data</c>, and removes the key from the last values. 🔴 Mutation: the dispose sample loses its key (default data) ⇒ red.</summary>
        [Fact]
        public void ADispose_CarriesItsKey_ToEveryEnd_AndLeavesNothingForALateOne()
        {
            var hub = new LocalGameplayHub();
            var server = hub.ServerEnd(); var a = hub.ClientEnd();
            server.SlotWriter!.Write(Slot(7)); Take(a.SlotReader!);
            server.SlotWriter.DisposeInstance(new PlayerSlotTopic { StationNodeId = 7 });
            var ended = Assert.Single(Take(a.SlotReader!));
            Assert.False(ended.Alive);
            Assert.Equal(7, ended.Data.StationNodeId);
            Assert.Empty(Take(hub.ClientEnd().SlotReader!));
        }

        /// <summary>Closing the SERVER end ends every slot (the writer is gone): each receiver gets it not-alive with its key; the slots are gone for a late end; writes after the close are ignored. 🔴 Mutation: the close ends nothing ⇒ red.</summary>
        [Fact]
        public void ClosingTheServerEnd_EndsEverySlot_ForEveryReceiver()
        {
            var hub = new LocalGameplayHub();
            var server = hub.ServerEnd(); var a = hub.ClientEnd(); var b = hub.ClientEnd();
            server.SlotWriter!.Write(Slot(7)); server.SlotWriter.Write(Slot(8));
            Take(a.SlotReader!); Take(b.SlotReader!);
            server.Dispose();
            foreach (var end in new[] { a, b })
            {
                var ended = Take(end.SlotReader!);
                Assert.Equal(2, ended.Count);
                Assert.All(ended, s => Assert.False(s.Alive));
                Assert.Equal(new[] { 7, 8 }, new[] { ended[0].Data.StationNodeId, ended[1].Data.StationNodeId }.OrderBy());
            }
            Assert.Empty(Take(hub.ClientEnd().SlotReader!));
            server.SlotWriter.Write(Slot(9));                                   // a closed end writes nothing
            Assert.Empty(Take(a.SlotReader!));
            Assert.False(server.IsOpen);
            Assert.True(hub.ServerEnd().IsOpen);                                // a new server end can open afterwards
        }

        /// <summary>HOT PATH (R-2084): nothing pending ⇒ <c>TakeInto</c> allocates 0 bytes, on every reader of both ends. 🔴 Mutation: a per-call <c>new</c> / copy on the empty path ⇒ red.</summary>
        [Fact]
        public void AnEmptyTakeInto_AllocatesNothing()
        {
            var hub = new LocalGameplayHub();
            var server = hub.ServerEnd(); var client = hub.ClientEnd();
            var req = new List<GameplaySample<DeployRequestTopic>>(); var off = new List<GameplaySample<DeployOfferTopic>>();
            var st = new List<GameplaySample<DeployStatusTopic>>(); var sl = new List<GameplaySample<PlayerSlotTopic>>();
            void Round() { server.Requests!.TakeInto(req); client.OfferReader!.TakeInto(off); client.StatusReader!.TakeInto(st); client.SlotReader!.TakeInto(sl); server.SlotReader!.TakeInto(sl); }
            for (int i = 0; i < 100; i++) Round();
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++) Round();
            Assert.Equal(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>A closed client end stops receiving and costs the topics nothing (a gateway seat that left).</summary>
        [Fact]
        public void AClosedClientEnd_StopsReceiving()
        {
            var hub = new LocalGameplayHub();
            var server = hub.ServerEnd(); var a = hub.ClientEnd(); var b = hub.ClientEnd();
            Assert.Equal(2, hub.ClientEndCount);
            a.Dispose();
            Assert.Equal(1, hub.ClientEndCount);
            server.Offers!.Write(Offer(1));
            Assert.Single(Take(b.OfferReader!));
        }
    }

    internal static class SortExt { public static IEnumerable<int> OrderBy(this IEnumerable<int> e) { var l = new List<int>(e); l.Sort(); return l; } }
}
