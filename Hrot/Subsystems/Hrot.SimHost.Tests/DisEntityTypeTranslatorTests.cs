using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Tkb;
using Hrot.SimHost.Serializers;

namespace Hrot.SimHost.Tests
{
    public sealed class DisEntityTypeTranslatorTests
    {
        private const string SubsystemType = "Test.Scenario";

        // ── (a) Direct round-trip ─────────────────────────────────────────────

        /// <summary>
        /// Entity with explicit non-zero DisType: serialise → deserialise into a fresh repo,
        /// assert that GetDisType returns a value whose .Value equals the original.
        /// </summary>
        [Fact]
        public void RoundTrip_ExplicitDisType_PreservesAllFields()
        {
            using var repo = new EntityRepository();
            var entity = repo.CreateEntity();

            var original = new DISEntityType
            {
                Kind        = 1,
                Domain      = 2,
                Country     = 225,
                Category    = 3,
                Subcategory = 4,
                Specific    = 5,
                Extra       = 6,
            };
            repo.SetDisType(entity, original);

            var serializer = new ScenarioSerializerBuilder(SubsystemType)
                .RegisterTranslator(new DisEntityTypeTranslator())
                .Build();

            var dom = serializer.Serialize(repo, new ScenarioHeader(SubsystemType));

            using var freshRepo = new EntityRepository();
            serializer.Deserialize(freshRepo, dom);

            // Locate the first live entity in the fresh repository.
            Entity loaded = Entity.Null;
            for (int i = 0; i <= freshRepo.MaxEntityIndex; i++)
            {
                var candidate = new Entity(i, freshRepo.GetEntityIndex().GetMetadata(i).Generation);
                if (freshRepo.IsAlive(candidate))
                {
                    loaded = candidate;
                    break;
                }
            }

            Assert.NotEqual(Entity.Null, loaded);
            var result = freshRepo.GetDisType(loaded);
            Assert.Equal(original.Value, result.Value);
        }

        // ── (b) TKB fallback: zeroed DisType resolved via ITkbDatabase singleton ─

        /// <summary>
        /// Entity with zeroed DisType but a <see cref="TkbIdentity"/> component and a
        /// registered <see cref="ITkbDatabase"/> singleton whose matching template carries a
        /// known DisType: Extract must emit a non-empty JSON array with the template's values.
        /// </summary>
        [Fact]
        public void Extract_ZeroDisType_WithTkbIdentity_FallsBackToTkbTemplate()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<TkbIdentity>();

            const long tkbType = 1001L;
            var expectedDis = new DISEntityType
            {
                Kind        = 1,
                Domain      = 1,
                Country     = 222,
                Category    = 2,
                Subcategory = 3,
                Specific    = 4,
                Extra       = 0,
            };

            // Build a TKB database with a template that has the expected DIS type.
            var tkb = new TkbDatabase();
            var template = new TkbTemplate("TestVehicle", tkbType) { DisType = expectedDis };
            tkb.Register(template);

            // Register ITkbDatabase as an ECS singleton.
            repo.SetSingletonManaged<ITkbDatabase>(tkb);

            // Create an entity with zero DisType but with TkbIdentity.
            var entity = repo.CreateEntity();
            repo.SetComponent(entity, new TkbIdentity { TkbType = tkbType });
            // DisType is zero by default — no call to SetDisType.

            var translator = new DisEntityTypeTranslator();
            var dom = translator.Extract(repo, entity, NullGuidResolver.Instance);

            Assert.True(dom.ContainsKey("DisEntityType"), "Translator should emit 'DisEntityType' key via TKB fallback.");
            var arr = Assert.IsType<JsonArray>(dom["DisEntityType"]);
            Assert.Equal(7, arr.Count);

            // arr layout: Kind, Domain, Country, Category, Subcategory, Specific, Extra
            Assert.Equal((int)expectedDis.Kind,        arr[0]!.GetValue<int>());
            Assert.Equal((int)expectedDis.Domain,      arr[1]!.GetValue<int>());
            Assert.Equal((int)expectedDis.Country,     arr[2]!.GetValue<int>());
            Assert.Equal((int)expectedDis.Category,    arr[3]!.GetValue<int>());
            Assert.Equal((int)expectedDis.Subcategory, arr[4]!.GetValue<int>());
            Assert.Equal((int)expectedDis.Specific,    arr[5]!.GetValue<int>());
            Assert.Equal((int)expectedDis.Extra,       arr[6]!.GetValue<int>());
        }

        // ── Minimal stub IGuidResolver ─────────────────────────────────────────

        private sealed class NullGuidResolver : IGuidResolver
        {
            public static readonly NullGuidResolver Instance = new();
            public string Resolve(Entity e)       => e.ToString();
            public Entity Resolve(string guidStr) => Entity.Null;
        }
    }
}
