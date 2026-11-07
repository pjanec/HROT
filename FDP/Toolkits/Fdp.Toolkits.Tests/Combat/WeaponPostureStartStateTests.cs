using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>
    /// ⭐ Initial state S2 (docs/UX/DESIGN_Initial_State.md §2.1, §3, R-4137) — the weapon's START state: <see cref="WeaponPosture"/> is SAVED (a unit
    /// starts aimed at a point or a unit, a phone in hand; the aimed-at unit through the save's entity links), and the four <c>Weapon.*</c>
    /// attributes set it on the brain's node at once (the component is the state, so a paused world shows it).
    /// </summary>
    public sealed class WeaponPostureStartStateTests
    {
        private static Entity Find(EntityRepository repo, System.Func<Entity, bool> where)
        {
            for (int i = 0; i <= repo.MaxEntityIndex; i++)
            {
                var c = new Entity(i, repo.GetEntityIndex().GetMetadata(i).Generation);
                if (repo.IsAlive(c) && where(c)) return c;
            }
            return Entity.Null;
        }

        /// <summary>✅ Red-proof: WeaponPosture back to NoScenario ⇒ not serialised ⇒ red.</summary>
        [Fact]
        public void S2_APosture_AimedAtAUnit_OrAtAPointWithAPhone_SurvivesASaveAndLoad()
        {
            using var src = new EntityRepository();
            src.RegisterComponent<WeaponPosture>();
            src.RegisterComponent<NetworkIdentity>();
            var enemy = src.CreateEntity();
            src.AddComponent(enemy, new NetworkIdentity { Value = 7 });
            var rifleman = src.CreateEntity();
            src.AddComponent(rifleman, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = enemy });
            var watcher = src.CreateEntity();
            src.AddComponent(watcher, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Point = new Vector3(10f, 20f, 1f), HeldItem = HeldItemKind.Phone });

            var serializer = new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("Fdp.Toolkits.Tests").Build();
            var dom = serializer.Serialize(src, new Fdp.Toolkit.Scenario.ScenarioHeader("Fdp.Toolkits.Tests"));

            using var fresh = new EntityRepository();
            fresh.RegisterComponent<WeaponPosture>();
            fresh.RegisterComponent<NetworkIdentity>();
            serializer.Deserialize(fresh, dom);

            var enemyBack = Find(fresh, e => !fresh.HasComponent<WeaponPosture>(e));   // the one entity with no posture (a network id is not saved with a scenario)
            var rifleBack = Find(fresh, e => fresh.HasComponent<WeaponPosture>(e) && !fresh.GetComponentRO<WeaponPosture>(e).Target.IsNull);
            var watchBack = Find(fresh, e => fresh.HasComponent<WeaponPosture>(e) && fresh.GetComponentRO<WeaponPosture>(e).HeldItem == HeldItemKind.Phone);
            Assert.False(rifleBack.IsNull);
            Assert.False(enemyBack.IsNull);
            Assert.Equal(WeaponPostureKind.Aimed, fresh.GetComponentRO<WeaponPosture>(rifleBack).Kind);
            Assert.Equal(enemyBack, fresh.GetComponentRO<WeaponPosture>(rifleBack).Target);   // the link, remapped to the loaded unit
            Assert.False(watchBack.IsNull);
            Assert.Equal(new Vector3(10f, 20f, 1f), fresh.GetComponentRO<WeaponPosture>(watchBack).Point.AsVector3);
        }

        /// <summary>⭐ The four attributes on the brain's node: posture, a point (one axis per record), a unit by network id (an unknown one refused),
        /// a held item he has (one he does not, refused). ✅ Red-proof: the AimTarget handler without the lookup ⇒ Target stays null ⇒ red.</summary>
        [Fact]
        public void S2_TheWeaponAttributes_SetTheStartStateAtOnce()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<WeaponPosture>();
            repo.RegisterComponent<WeaponState>();
            repo.RegisterComponent<NetworkIdentity>();
            var target = repo.CreateEntity();
            repo.AddComponent(target, new NetworkIdentity { Value = 42 });
            var man = repo.CreateEntity();
            repo.AddComponent(man, default(WeaponPosture));
            repo.AddComponent(man, default(WeaponState));
            repo.SetAuthority<WeaponPosture>(man, true);

            void Apply(ushort id, AttributeValue v, int sub = 0)
            {
                var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter();
                var ctx = interpreter.CreateContext(EcsPatchContext.Create(repo, man));
                ctx.Repo = repo; ctx.Entity = man;
                interpreter.Apply(ctx, new[] { new EntityAttributeChange { AttributeId = id, SubIndex1 = (byte)sub, Value = v } });
            }

            Apply(AttributeIds.WeaponPosture, AttributeValue.FromString("Aimed"));
            Assert.Equal(WeaponPostureKind.Aimed, repo.GetComponentRO<WeaponPosture>(man).Kind);

            Apply(AttributeIds.WeaponAimPoint, AttributeValue.FromDouble(12.5), sub: 0);
            Apply(AttributeIds.WeaponAimPoint, AttributeValue.FromDouble(-3.0), sub: 1);
            Assert.Equal(new Vector3(12.5f, -3f, 0f), repo.GetComponentRO<WeaponPosture>(man).Point.AsVector3);

            Apply(AttributeIds.WeaponAimTarget, AttributeValue.FromDouble(42));
            Assert.Equal(target, repo.GetComponentRO<WeaponPosture>(man).Target);
            Assert.Equal(Vector3.Zero, repo.GetComponentRO<WeaponPosture>(man).Point.AsVector3);   // a unit, not a point
            Apply(AttributeIds.WeaponAimTarget, AttributeValue.FromDouble(999));   // ⛔ no such unit
            Assert.Equal(target, repo.GetComponentRO<WeaponPosture>(man).Target);

            Apply(AttributeIds.WeaponHeldItem, AttributeValue.FromDouble((int)HeldItemKind.Weapon));
            Assert.Equal(HeldItemKind.Weapon, repo.GetComponentRO<WeaponPosture>(man).HeldItem);
            Apply(AttributeIds.WeaponHeldItem, AttributeValue.FromString("Phone"));   // ⛔ his type carries no phone (no TKB here)
            Assert.Equal(HeldItemKind.Weapon, repo.GetComponentRO<WeaponPosture>(man).HeldItem);
        }
    }
}
