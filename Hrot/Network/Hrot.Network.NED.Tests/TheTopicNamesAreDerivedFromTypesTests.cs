using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using CycloneDDS.Runtime;
using CycloneDDS.Schema;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// ⭐ AQ107 A (R-4092 / R-4093) — every DDS topic name is DERIVED from its C# type by CycloneDDS.NET's one rule
    /// (<see cref="DdsTopicName"/>: namespace, containing types and type name joined with <c>_</c>); no HROT type writes a name,
    /// and no two types derive the same one. 📄 docs/reference/NED_DDS_Protocol.md §Concepts 15,
    /// docs/blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md item A.
    ///
    /// <para>Why it is a rail: a name written next to a type is a second copy of it, and two types that wrote the same string
    /// (<c>IGCapabilitiesAnnounce</c>, once in <c>Hrot.NED.Descriptors</c> and once in <c>Hrot.IG.Gizmos</c>) silently shared one topic.
    /// ⚠ The same rule makes a namespace or type RENAME a wire break — the doc's Inventory column and
    /// <c>scripts/ned-wire-inventory.py --check</c> pin the names; this class pins the invariants.</para>
    ///
    /// <para>⭐ The scan walks the assemblies this test project can reach (the NED, orchestration, BDC, animation, IG, FDP network and
    /// toolkit assemblies, GizmoMap's network assembly) — every production assembly that carries a <c>[DdsTopic]</c> type. Test
    /// assemblies are not scanned. SumoSharp (vendored, R-4046) is not an HROT assembly and keeps its <c>sumo/*</c> names.</para>
    /// </summary>
    public sealed class TheTopicNamesAreDerivedFromTypesTests
    {
        /// <summary>A private DDS domain (the NED suite's other tests use 120, 121, 150, 151, 200).</summary>
        private const uint PrivateDomain = 122;

        private static readonly Lazy<IReadOnlyList<Type>> TopicTypes = new(FindTopicTypes);

        private static bool IsHrotAssembly(string? name)
            => name is not null
               && (name.StartsWith("Hrot.", StringComparison.Ordinal) || name.StartsWith("Fdp.", StringComparison.Ordinal)
                   || name.StartsWith("GizmoMap.", StringComparison.Ordinal))
               && !name.EndsWith(".Tests", StringComparison.Ordinal);

        private static IReadOnlyList<Type> FindTopicTypes()
        {
            var seen = new Dictionary<string, Assembly>(StringComparer.Ordinal);
            void Visit(Assembly a)
            {
                var n = a.GetName().Name;
                if (!IsHrotAssembly(n) || !seen.TryAdd(n!, a)) return;
                foreach (var r in a.GetReferencedAssemblies())
                {
                    try { Visit(Assembly.Load(r)); } catch (Exception) { /* a reference that cannot load carries no topic we can name */ }
                }
            }

            // Anchors: one type from each production assembly family that holds [DdsTopic] types. The walk finds the rest.
            Visit(typeof(WorldPos).Assembly);                                          // Hrot.Network.NED
            Visit(typeof(Hrot.BDC.Messages.BdcWorldPos).Assembly);                      // Hrot.Network.BDC (not referenced by the NED project)
            Visit(typeof(Hrot.Animation.Replication.AnimationReplicationModule).Assembly);
            Visit(typeof(Hrot.IG.Gizmos.IGCapabilitiesAnnounce).Assembly);               // Hrot.IG — the second IGCapabilitiesAnnounce

            var types = new List<Type>();
            foreach (var a in seen.Values)
            {
                Type?[] all;
                try { all = a.GetTypes(); }
                catch (ReflectionTypeLoadException e) { all = e.Types; }
                types.AddRange(all.Where(t => t is not null && t.GetCustomAttribute<DdsTopicAttribute>() is not null)!);
            }
            return types;
        }

        /// <summary>The names that occur more than once (ordinal). Pure, so the rail can be proved on a list that does collide.</summary>
        internal static IReadOnlyList<string> Collisions(IEnumerable<string> names)
            => names.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();

        // ── the scan really reached the production assemblies (else (a) and (b) would pass on nothing) ──────────────────

        [Fact]
        public void TheScanReachesEveryProductionAssemblyThatCarriesTopics()
        {
            var types = TopicTypes.Value;
            Assert.True(types.Count >= 120, $"only {types.Count} [DdsTopic] types found — the assembly walk did not reach the production assemblies");
            foreach (var anchor in new[]
                     {
                         typeof(WorldPos), typeof(EntityMaster), typeof(Hrot.IG.Gizmos.IGCapabilitiesAnnounce), typeof(IGCapabilitiesAnnounce),
                         typeof(Hrot.BDC.Messages.BdcWorldPos), typeof(Hrot.Animation.Replication.DdsStanceIntent),
                         typeof(Fdp.Network.Cyclone.Topics.OwnershipUpdate), typeof(Fdp.Toolkit.Time.Messages.TimeSyncRequest),
                         typeof(GizmoMap.Network.GizmoInteractionBatch),
                         typeof(Hrot.NED.Messages.SquadReport), typeof(Hrot.NED.Messages.BehaviorFaultReport), typeof(Hrot.NED.Messages.TacticalIntentRequest),
                     })
                Assert.Contains(anchor, types);
        }

        // ── (a) no HROT topic type writes a name ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void NoTopicTypeWritesAnExplicitName()
        {
            var offenders = TopicTypes.Value
                .Where(t => !string.IsNullOrWhiteSpace(t.GetCustomAttribute<DdsTopicAttribute>()!.TopicName))
                .Select(t => $"{t.FullName} writes \"{t.GetCustomAttribute<DdsTopicAttribute>()!.TopicName}\"")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            Assert.True(offenders.Count == 0,
                "AQ107 A: the topic name is derived from the type — remove the name from [DdsTopic(...)]:\n  " + string.Join("\n  ", offenders));
        }

        // ── (b) the derived names are unique ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void TheDerivedNamesAreUniqueAcrossEveryTopicType()
        {
            var byName = TopicTypes.Value.GroupBy(t => DdsTopicName.Of(t), StringComparer.Ordinal)
                                         .Where(g => g.Count() > 1)
                                         .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.FullName))}")
                                         .ToList();

            Assert.True(byName.Count == 0, "two types derive one topic name (they would share a topic):\n  " + string.Join("\n  ", byName));
            // the same check through the pure helper — over the names themselves
            Assert.Empty(Collisions(TopicTypes.Value.Select(t => DdsTopicName.Of(t))));
        }

        [Fact]
        public void TheTwoIGCapabilitiesAnnounceTypesAreTwoTopicsNow()
        {
            // The pair AQ107 A's ruling names: before it both wrote "IGCapabilitiesAnnounce".
            Assert.Equal("Hrot_IG_Gizmos_IGCapabilitiesAnnounce", DdsTopicName.Of<Hrot.IG.Gizmos.IGCapabilitiesAnnounce>());
            Assert.Equal("Hrot_NED_Descriptors_IGCapabilitiesAnnounce", DdsTopicName.Of<IGCapabilitiesAnnounce>());
        }

        [Fact]
        public void TheCollisionCheckSeesADuplicateAndTheOneSpellingTheRuleCannotTellApart()
        {
            // proof the rail can go red: a list that DOES collide
            Assert.Equal(new[] { "A_B" }, Collisions(new[] { "A_B", "C", "A_B" }));
            Assert.Empty(Collisions(new[] { "A_B", "C", "A_C" }));

            // the one residual shape of the rule: a namespace+type spelling meets a nested/underscored one
            Assert.Equal(DdsTopicName.Of("A.B", new[] { "C" }), DdsTopicName.Of("A", new[] { "B_C" }));
            Assert.Equal(new[] { "A_B_C" }, Collisions(new[] { DdsTopicName.Of("A.B", new[] { "C" }), DdsTopicName.Of("A", new[] { "B_C" }) }));
        }

        [Fact]
        public void TheNamesAreTheTypesFullNameWithUnderscores()
        {
            // pins the wire spelling of the topics every external client reads first (a rename is a wire break — AQ107 A)
            Assert.Equal("Hrot_NED_Descriptors_WorldPos", DdsTopicName.Of<WorldPos>());
            Assert.Equal("Hrot_NED_Descriptors_EntityMaster", DdsTopicName.Of<EntityMaster>());
            Assert.Equal("Hrot_NED_Descriptors_Orchestration_GeoReference", DdsTopicName.Of<Hrot.NED.Descriptors.Orchestration.GeoReference>());
            Assert.Equal("Fdp_Network_Cyclone_Topics_OwnershipUpdate", DdsTopicName.Of<Fdp.Network.Cyclone.Topics.OwnershipUpdate>());
            // a type that used to be bound to a topic only by a translator string
            Assert.Equal("Hrot_NED_Messages_SquadReport", DdsTopicName.Of<Hrot.NED.Messages.SquadReport>());
        }

        // ── (c) a real writer/reader pair over DDS, name-less ──────────────────────────────────────────────────────────

        [Fact(Timeout = 30_000)]
        public void ANameLessWriterAndReaderExchangeASampleOnTheDerivedTopic()
        {
            using var participant = new DdsParticipant(PrivateDomain);
            using var writer = new DdsWriter<EntityMaster>(participant);                                          // no name: derived
            using var reader = new DdsReader<EntityMaster>(participant);                                          // no name: derived
            using var onTheName = new DdsReader<EntityMaster>(participant, DdsTopicName.Of<EntityMaster>());      // the same topic, by its derived name
            using var elsewhere = new DdsReader<EntityMaster>(
                participant, DdsTopicName.Of(typeof(EntityMaster).Namespace + ".Elsewhere", new[] { nameof(EntityMaster) }));   // a different topic
            Thread.Sleep(500);   // discovery

            writer.Write(new EntityMaster { EntityId = 12345, TkbType = 100, DisType = default, Flags = 0 });

            int viaNameless = 0, viaName = 0, viaElsewhere = 0;
            long idNameless = 0;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && (viaNameless == 0 || viaName == 0))
            {
                Thread.Sleep(50);
                Drain(reader, ref viaNameless, ref idNameless);
                long ignored = 0;
                Drain(onTheName, ref viaName, ref ignored);
            }
            Thread.Sleep(300);   // give a wrongly-routed sample time to show up on the other topic
            long ignored2 = 0;
            Drain(elsewhere, ref viaElsewhere, ref ignored2);

            Assert.Equal(1, viaNameless);
            Assert.Equal(12345, idNameless);
            Assert.Equal(1, viaName);          // the name-less pair IS on the derived name
            Assert.Equal(0, viaElsewhere);     // and on no other
        }

        private static void Drain(DdsReader<EntityMaster> reader, ref int count, ref long lastId)
        {
            using var loan = reader.Take();
            foreach (var s in loan)
            {
                if (!s.IsValid) continue;
                count++;
                lastId = s.Data.EntityId;
            }
        }
    }
}
