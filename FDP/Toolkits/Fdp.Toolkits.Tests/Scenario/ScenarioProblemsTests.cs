using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Xunit;

namespace Fdp.Toolkits.Tests.Scenario
{
    /// <summary>
    /// ⭐⭐ <c>CE-4318</c> — scenario lifecycle LC-13 (📄 docs/UX/DESIGN_Scenario_Lifecycle_Review.md §2.7): every Problems check finds its problem
    /// and names the entity a click selects; a clean scenario has none. ✅ Red-proof: the subject check dropped ⇒ the first assert red.
    /// </summary>
    public sealed class ScenarioProblemsTests
    {
        private readonly EntityRepository _w = new();

        public ScenarioProblemsTests()
        {
            _w.RegisterComponent<EntityInfo>();
            _w.RegisterComponent<NetworkIdentity>();
            _w.RegisterComponent<ObjectiveLinks>();
            _w.RegisterComponent<ObjectiveStatus>();
            _w.RegisterComponent<UnitRoster>();
        }

        private Entity Unit(string name, ForceId side, int net)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
            _w.AddComponent(e, new NetworkIdentity(net));
            return e;
        }

        private Entity Rule(string title, int subject = 0, ObjectiveRole role = ObjectiveRole.Objective, byte visible = 0xFF, int dependsOn = 0)
        {
            var e = _w.CreateEntity();
            var s = new ObjectiveStatus { Role = role, Title = new FixedString64(title), VisibleSides = visible };
            if (dependsOn != 0) { s.DependsOnCount = 1; s.DependsOn[0] = dependsOn; }
            _w.AddComponent(e, s);
            _w.AddComponent(e, new ObjectiveLinks { Subject = new Fdp.Toolkit.Replication.EntityRef(subject) });
            return e;
        }

        private List<ScenarioProblem> Problems()
        {
            var list = new List<ScenarioProblem>();
            ScenarioProblems.Collect(_w, list);
            return list;
        }

        [Fact]
        public void CE4318_EveryCheck_FindsItsProblem_AndNamesTheCulprit()
        {
            var alpha = Unit("Alpha 1", ForceId.Friend, 101);
            var bravo = Unit("Bravo 1", ForceId.Friend, 102);
            var red = Unit("Red 1", ForceId.Hostile, 201);
            var platoon = Unit("1st Platoon", ForceId.Friend, 100);
            var roster = new UnitRoster();
            UnitRoster.Add(ref roster, alpha);
            UnitRoster.Add(ref roster, red);                                   // on the other side
            UnitRoster.Add(ref roster, bravo);
            _w.AddComponent(platoon, roster);
            _w.DestroyEntity(bravo);                                          // a member gone

            var hold = Rule("Hold the bridge", subject: 999);                 // a subject that is gone
            var chain = Rule("After the bridge", subject: 101, role: ObjectiveRole.Trigger, dependsOn: 998);
            var neutralOnly = Rule("Calm the village", subject: 101, visible: (byte)(1 << (int)ForceId.Neutral));

            var p = Problems();
            Assert.Contains(new ScenarioProblem(ScenarioProblemKind.RuleSubjectGone, hold, 999), p);
            Assert.Contains(new ScenarioProblem(ScenarioProblemKind.RuleLinkGone, chain, 998), p);
            Assert.Contains(new ScenarioProblem(ScenarioProblemKind.RosterMemberGone, platoon, 0), p);
            Assert.Contains(new ScenarioProblem(ScenarioProblemKind.MixedSides, red, (int)ForceId.Friend), p);
            Assert.Contains(new ScenarioProblem(ScenarioProblemKind.ObjectiveSideEmpty, neutralOnly, (int)ForceId.Neutral), p);
            Assert.Equal(5, p.Count);
            Assert.Equal("Hold the bridge: its subject (#999) is gone", ScenarioProblems.Text(_w, p.First(x => x.Kind == ScenarioProblemKind.RuleSubjectGone)));
        }

        [Fact]
        public void CE4318_ACleanScenario_HasNoProblems_AndTheListIsReused()
        {
            var alpha = Unit("Alpha 1", ForceId.Friend, 101);
            var platoon = Unit("1st Platoon", ForceId.Friend, 100);
            var roster = new UnitRoster();
            UnitRoster.Add(ref roster, alpha);
            _w.AddComponent(platoon, roster);
            Rule("Hold", subject: 101, visible: (byte)(1 << (int)ForceId.Friend));

            var list = new List<ScenarioProblem> { new(ScenarioProblemKind.MixedSides, alpha, 0) };   // stale content
            Assert.Equal(0, ScenarioProblems.Collect(_w, list));
            Assert.Empty(list);
        }
    }
}
