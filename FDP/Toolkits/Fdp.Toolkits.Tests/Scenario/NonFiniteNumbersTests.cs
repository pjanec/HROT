using System;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// CE-1292 — a component may hold ±Infinity / NaN as a real value ("never": <c>SquadMemberSense.WindowStart = -Infinity</c>).
    /// The scenario serializer threw on it (".NET number values such as positive and negative infinity cannot be written as valid
    /// JSON"), so every entity dump, scenario save and checkpoint of such a unit failed — seven verify runs on 2026-10-23.
    /// </summary>
    public sealed class NonFiniteNumbersTests : IDisposable
    {
        private struct Sense
        {
            public double WindowStart;
            public float Gap;
            public double Ok;
        }

        private readonly EntityRepository _repo;

        public NonFiniteNumbersTests()
        {
            ComponentTypeRegistry.Clear();
            _repo = new EntityRepository();
            _repo.RegisterComponent<Sense>();
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void CE1292_AComponentHoldingInfinityAndNaN_IsWrittenByName()
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new Sense { WindowStart = double.NegativeInfinity, Gap = float.NaN, Ok = 1.5 });

            var dom = new ScenarioSerializerBuilder("TestSubsystem").Build().Serialize(_repo, new ScenarioHeader("TestSubsystem"));
            var json = dom.ToJsonString();

            Assert.Contains("-Infinity", json);
            Assert.Contains("NaN", json);
            Assert.Contains("1.5", json);
        }
    }
}
