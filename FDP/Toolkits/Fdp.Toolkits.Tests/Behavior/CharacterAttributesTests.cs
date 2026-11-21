using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;
using Xunit;

namespace Fdp.Toolkit.Tests.Behavior
{
    /// <summary>
    /// ⭐ B-5 S6 (R-4137, docs/UX/DESIGN_Initial_State.md §3) — the AI character settings as attributes (80 <c>Proficiency</c>, 81-84 <c>Temperament.*</c>), applied on the
    /// brain's node; a NEGATIVE value is the reset (clears the authored bit, so the unit reads its type's value — with the spread stamped at creation — again).
    /// </summary>
    public sealed class CharacterAttributesTests
    {
        private static (EntityRepository Repo, Entity Man) Man()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<CombatProficiency>();
            repo.RegisterComponent<CombatTemperament>();
            var man = repo.CreateEntity();
            // the type's values with the man's own seeded spread (TypeSet), as the spawn stamps them
            repo.AddComponent(man, new CombatProficiency { TypeDefault = 0.62f, Set = CombatProficiency.TypeDefaultSet });
            repo.AddComponent(man, new CombatTemperament
            {
                TypeRiskAcceptance = 0.41f, TypeObedience = 0.77f, TypeNerve = 0.55f, TypeResolve = 0.33f,
                TypeSet = (byte)(CombatTemperament.RiskAcceptanceSet | CombatTemperament.ObedienceSet | CombatTemperament.NerveSet | CombatTemperament.ResolveSet),
            });
            repo.SetAuthority<CombatProficiency>(man, true);
            repo.SetAuthority<CombatTemperament>(man, true);
            return (repo, man);
        }

        private static void Apply(EntityRepository repo, Entity man, ushort id, double v)
        {
            var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter();
            var ctx = interpreter.CreateContext(EcsPatchContext.Create(repo, man));
            ctx.Repo = repo; ctx.Entity = man;
            interpreter.Apply(ctx, new[] { new EntityAttributeChange { AttributeId = id, Value = AttributeValue.FromDouble(v) } });
        }

        /// <summary>Each attribute sets its field AND its authored bit on the owner; the value is clamped to 0..1. ✅ Red-proof: a handler not setting the bit ⇒ the reader ignores it ⇒ red.</summary>
        [Fact]
        public void S6_EachCharacterAttribute_AppliesOnTheOwner_WithItsAuthoredBit()
        {
            var (repo, man) = Man();
            using var _ = repo;

            Apply(repo, man, AttributeIds.Proficiency, 0.9);
            var p = repo.GetComponentRO<CombatProficiency>(man);
            Assert.Equal(0.9f, p.Authored);
            Assert.True((p.Set & CombatProficiency.AuthoredSet) != 0);
            Assert.Equal(0.9f, ProficiencyOf.Value(repo, man));

            Apply(repo, man, AttributeIds.TemperamentRiskAcceptance, 0.2);
            Apply(repo, man, AttributeIds.TemperamentObedience, 0.3);
            Apply(repo, man, AttributeIds.TemperamentNerve, 0.4);
            Apply(repo, man, AttributeIds.TemperamentResolve, 5.0);   // clamped
            var t = repo.GetComponentRO<CombatTemperament>(man);
            Assert.Equal((0.2f, 0.3f, 0.4f, 1.0f), (t.RiskAcceptance, t.Obedience, t.Nerve, t.Resolve));
            Assert.Equal(CombatTemperament.RiskAcceptanceSet | CombatTemperament.ObedienceSet | CombatTemperament.NerveSet | CombatTemperament.ResolveSet, t.Set);
            Assert.Equal(0.2f, TemperamentOf.RiskAcceptance(repo, man));
        }

        /// <summary>"Reset to type" restores the TYPE's value with its spread (the stamped Type* field), for each field, and touches no other. ✅ Red-proof: the reset leaving the bit set ⇒ red.</summary>
        [Fact]
        public void S6_AResetValue_RestoresTheTypesValueWithItsSpread_PerField()
        {
            var (repo, man) = Man();
            using var _ = repo;
            Apply(repo, man, AttributeIds.Proficiency, 0.9);
            Apply(repo, man, AttributeIds.TemperamentRiskAcceptance, 0.1);
            Apply(repo, man, AttributeIds.TemperamentNerve, 0.8);

            Apply(repo, man, AttributeIds.Proficiency, -1);
            Assert.Equal(0.62f, ProficiencyOf.Value(repo, man));                       // the type's, not the authored 0.9
            Apply(repo, man, AttributeIds.TemperamentRiskAcceptance, -1);
            Assert.Equal(0.41f, TemperamentOf.RiskAcceptance(repo, man));              // the type's spread value
            Assert.Equal(0.8f, TemperamentOf.Nerve(repo, man));                         // another field, still authored
            Apply(repo, man, AttributeIds.TemperamentNerve, -0.001);
            Assert.Equal(0.55f, TemperamentOf.Nerve(repo, man));
            Assert.Equal(0, repo.GetComponentRO<CombatTemperament>(man).Set);
        }

        /// <summary>The JSON route (a creation request) writes the same bits.</summary>
        [Fact]
        public void S6_TheJsonSetters_WriteTheSameBits()
        {
            var vocab = System.Linq.Enumerable.ToList(AttributeVocabulary.All);
            foreach (var path in new[] { "Proficiency", "Temperament.RiskAcceptance", "Temperament.Obedience", "Temperament.Nerve", "Temperament.Resolve" })
                Assert.Contains(vocab, d => d.JsonPath == path);
            var c = default(CombatTemperament);
            FormAttributes.SetTemperament(ref c, FormAttributes.TemperamentField.Resolve, 0.25);
            Assert.Equal((0.25f, CombatTemperament.ResolveSet), (c.Resolve, c.Set));
            FormAttributes.SetTemperament(ref c, FormAttributes.TemperamentField.Resolve, -1);
            Assert.Equal(0, c.Set);
        }
    }
}
