using System;
using System.Linq;
using Fdp.Core;
using Fdp.Testing;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Orchestration;
using Hrot.Common.Macros;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-6009</c> (city battle M-1; 📄 docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §4, §11 M-1) — the SHIPPED company / platoon
    /// macros (<c>macros/city-battle/</c>) through the REAL expander and <see cref="FileMacroLibrary"/>: what each expands into, by type, every part on the
    /// side the instance was given — through the nested platoon instances too. Its own file: <c>MacroExpanderTests.cs</c> is on the parallel programme's list.
    /// </summary>
    public sealed class CityBattleMacrosTests
    {
        private static readonly NodeId Owner = (NodeId)3;
        private readonly ITestOutputHelper _out;

        static CityBattleMacrosTests()
        {
            using var repo = new EntityRepository();   // the staging repo registers every globally known type (as MacroExpanderTests does)
            HrotSharedComponentRegistry.RegisterAll(repo);
            Hrot.CGF.CgfComponentRegistry.RegisterAll(repo);
        }

        public CityBattleMacrosTests(ITestOutputHelper output) => _out = output;

        private static MacroExpander.Result Expand(string macro, string args)
        {
            var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(registry, null, UnknownComponentPolicy.WarnAndSkip);
            var expander = new MacroExpander(new FileMacroLibrary(TestRepo.Root), new StagingEntityExtractor(), serializer,
                                             Hrot.CGF.Configuration.CgfBehaviorSetup.CreateBehaviorRemapper(registry));
            return expander.Expand(new MacroInstance { Macro = macro, ArgsJson = args }, 500, Guid.NewGuid(), Owner,
                                   new SimTransform { Position = new WorldPoint(1000, 1000, 0), Rotation = WorldRotation.FromHeadingDeg(0) },
                                   outside: null, new SequentialIdAllocator());
        }

        private static long TypeOf(EntityCreationRequest r) => (long)r.TkbType;
        private static EntityInfo? Info(EntityCreationRequest r) => r.InitialComponents?.OfType<EntityInfo>().Cast<EntityInfo?>().FirstOrDefault();

        /// <summary>
        /// ⭐⭐ <c>CE6009_M1</c> — the rifle platoon: ONE body-less platoon (305) named by the <c>name</c> arg, SIX squads (302; CE-6010: sized to the battle, the platoon's command picture holds 6), 36 men (2002 / one
        /// 2004 grenadier per squad), every part on the <c>side</c> arg. 🔴 Red-proof: drop the platoon's <c>EntityInfo.Name ← name</c> binding ⇒ red.
        /// </summary>
        [Fact]
        public void CE6009_M1_TheRiflePlatoon_IsOnePlatoon_SixSquads_ThirtySixMen_OnItsSide()
        {
            var r = Expand("city-battle/rifle-platoon", """{"side": "Hostile", "name": "Blue 2nd Platoon", "key": "B2"}""");
            Assert.True(r.Ok, r.Failure);
            Assert.Single(r.Children, c => TypeOf(c) == 305);
            Assert.Equal(6, r.Children.Count(c => TypeOf(c) == 302));
            Assert.Equal(36, r.Children.Count(c => TypeOf(c) is 2002 or 2004));
            Assert.Equal(6, r.Children.Count(c => TypeOf(c) == 2004));
            Assert.All(r.Children, c => Assert.Equal(ForceId.Hostile, Info(c)!.Value.ForceId));
            Assert.Equal("Blue 2nd Platoon", Info(r.Children.Single(c => TypeOf(c) == 305))!.Value.Name.ToString());
            // ⭐ CE-6010 — the platoon carries the instance's key, so a scenario trigger can Order THIS platoon ("@B2")
            Assert.Equal("B2", r.Children.Single(c => TypeOf(c) == 305).InitialComponents!.OfType<RefKey>().Single().Value.ToString());
        }

        /// <summary>
        /// ⭐⭐ <c>CE6009_M2</c> — the mechanised company: its three platoon macros flattened (3 nested instances + 3 × 43 parts), the support element (a tank
        /// section 301 with 2 M1, 4 Bradleys) — and the company's <c>side</c> reaches EVERY man through the nested instances' args
        /// (<c>MacroInstance.Args.side ← side</c>). ⚠ No company commander (CE-7002). 🔴 Red-proof: drop that binding from one nested instance ⇒ its men
        /// keep the platoon's default (Friend) ⇒ red.
        /// </summary>
        [Fact]
        public void CE6009_M2_TheMechCompany_ThreePlatoons_AndItsSupportElement_AllOnTheCompanysSide()
        {
            var r = Expand("city-battle/mech-company", """{"side": "Hostile"}""");   // p1..p3 left at their defaults
            Assert.True(r.Ok, r.Failure);
            var byType = r.Children.GroupBy(TypeOf).ToDictionary(g => g.Key, g => g.Count());
            _out.WriteLine(string.Join(", ", byType.OrderBy(k => k.Key).Select(k => $"{k.Key}×{k.Value}")));
            Assert.Equal(3, byType.GetValueOrDefault(8813));
            Assert.Equal(3, byType.GetValueOrDefault(305));
            Assert.Equal(18, byType.GetValueOrDefault(302));
            Assert.Equal(108, byType.GetValueOrDefault(2002) + byType.GetValueOrDefault(2004));
            Assert.Equal(1, byType.GetValueOrDefault(301));
            Assert.Equal(2, byType.GetValueOrDefault(100));
            Assert.Equal(4, byType.GetValueOrDefault(101));
            var wrongSide = r.Children.Where(c => Info(c) is { } i && TypeOf(c) != 8813 && i.ForceId != ForceId.Hostile).Select(c => Info(c)!.Value.Name.ToString()).ToList();
            Assert.True(wrongSide.Count == 0, "not on the company's side: " + string.Join(", ", wrongSide.Take(6)));
            var names = r.Children.Where(c => TypeOf(c) == 305).Select(c => Info(c)!.Value.Name.ToString()).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "1st Platoon", "2nd Platoon", "3rd Platoon" }, names);
            // ⭐ CE-6010 — the company's p1 / p2 / p3 reach its three platoons' keys (MacroInstance.Args.key ← pN)
            var keys = r.Children.Where(c => TypeOf(c) == 305).Select(c => c.InitialComponents!.OfType<RefKey>().Single().Value.ToString()).OrderBy(k => k).ToArray();
            Assert.Equal(new[] { "Platoon1", "Platoon2", "Platoon3" }, keys);
        }

        /// <summary>⭐ <c>CE6009_M3</c> — the defender company: three militia platoons (2003 fighters) + 2 technicals, Hostile by default.</summary>
        [Fact]
        public void CE6009_M3_TheDefenderCompany_ThreeMilitiaPlatoons_TwoTechnicals_HostileByDefault()
        {
            var r = Expand("city-battle/defender-company", "{}");
            Assert.True(r.Ok, r.Failure);
            Assert.Equal(108, r.Children.Count(c => TypeOf(c) == 2003));
            Assert.Equal(2, r.Children.Count(c => TypeOf(c) == 104));
            Assert.All(r.Children.Where(c => TypeOf(c) != 8813), c => Assert.Equal(ForceId.Hostile, Info(c)!.Value.ForceId));
        }
    }
}
