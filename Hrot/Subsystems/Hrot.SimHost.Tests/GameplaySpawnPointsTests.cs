using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Weather;
using Hrot.Common;
using Hrot.Gameplay;
using Hrot.IG.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ Player Join S2-c (D-11) — <see cref="SpawnPoints.Pick"/> alone (no DDS): the point lies INSIDE the area's outline (a concave one, so its bounding box has misses), the area's own anchor is no obstacle, anyone within 3 m
    /// blocks a point, the ground height comes from the world's terrain (the area's Z without one), the yaw is the area's, and the two refusals carry their text. Plus the shared containment <c>ItemSources</c> also reads.
    /// </summary>
    public sealed class GameplaySpawnPointsTests : IDisposable
    {
        private readonly EntityRepository _w = new();

        public GameplaySpawnPointsTests()
        {
            _w.RegisterComponent<SimTransform>(); _w.RegisterComponent<SpawnArea>(); _w.RegisterManagedComponent<EditablePolyline>();
        }

        public void Dispose() => _w.Dispose();

        private static readonly Vector2[] LShape = { new(0, 0), new(30, 0), new(30, 10), new(10, 10), new(10, 30), new(0, 30) };   // concave: the bounding box 30×30 is mostly outside

        private Entity Area(ForceId side, Vector3 origin, Vector2[] points, float facing = 0f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = origin, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SpawnArea { Side = side, FacingDeg = facing });
            _w.SetManagedComponent(e, new EditablePolyline { Points = points });
            return e;
        }

        private Entity Body(Vector3 at) { var e = _w.CreateEntity(); _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity }); return e; }

        private static bool InL(Vector3 p, Vector3 origin) { float x = p.X - origin.X, y = p.Y - origin.Y; return (x >= 0 && x <= 30 && y >= 0 && y <= 10) || (x >= 0 && x <= 10 && y >= 0 && y <= 30); }

        /// <summary>Every pick of 100 seeds is inside the concave outline and carries the area's Z and facing. ✅ Red-proof: the inside test dropped (the bounding box alone) ⇒ some pick is outside ⇒ red.</summary>
        [Fact]
        public void APick_IsInsideTheOutline_OnTheGround_FacingTheArea()
        {
            var origin = new Vector3(1000f, 2000f, 7f);
            Area(ForceId.Friend, origin, LShape, facing: 135f);
            for (int seed = 0; seed < 100; seed++)
            {
                Assert.True(SpawnPoints.Pick(_w, ForceId.Friend, new Random(seed), out var pos, out float yaw, out string why), why);
                Assert.True(InL(pos, origin), $"seed {seed}: {pos} is outside the outline");
                Assert.Equal(7f, pos.Z); Assert.Equal(135f, yaw);
            }
        }

        /// <summary>With a terrain the Z is its ground height at the point, not the area's. ✅ Red-proof: the area's Z used ⇒ red.</summary>
        [Fact]
        public void WithATerrain_TheZIsTheGroundHeightThere()
        {
            _w.SetSingletonManaged(new TerrainWorld { GroundZ = 42f });
            Area(ForceId.Friend, new Vector3(0f, 0f, 7f), LShape);
            Assert.True(SpawnPoints.Pick(_w, ForceId.Friend, new Random(1), out var pos, out _, out _));
            Assert.Equal(42f, pos.Z);
        }

        /// <summary>Anyone within 3 m blocks a point: with the area's open ground 5×5 and a man in its middle every other point is ≥ 3 m from him; an area so small that the clearance covers all of it refuses "no free point" after 20
        /// tries — while an EMPTY small area still serves (its own anchor in the middle is no one standing there). ✅ Red-proof: the clearance dropped ⇒ a pick lands on the man ⇒ red; the anchor counted ⇒ the empty case refuses ⇒ red.</summary>
        [Fact]
        public void TheClearance_BlocksCrowdedPoints_ButNotTheAreasOwnAnchor()
        {
            var small = new[] { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(2, 2), new Vector2(-2, 2) };
            var origin = new Vector3(50f, 50f, 0f);
            Area(ForceId.Friend, origin, small);
            for (int seed = 0; seed < 20; seed++) Assert.True(SpawnPoints.Pick(_w, ForceId.Friend, new Random(seed), out _, out _, out _), "an empty area serves");
            Body(origin);   // a man in the middle: the whole 4×4 is within 3 m of him
            Assert.False(SpawnPoints.Pick(_w, ForceId.Friend, new Random(1), out _, out _, out string why));
            Assert.Equal(SpawnPoints.NoFreePoint, why);

        }

        /// <summary>A pick never lands within 3 m of a body in a roomy area. ✅ Red-proof: the clearance dropped ⇒ red.</summary>
        [Fact]
        public void APick_IsAtLeast3mFromAnyBody()
        {
            var origin = new Vector3(0f, 0f, 0f);
            Area(ForceId.Friend, origin, new[] { new Vector2(-6, -6), new Vector2(6, -6), new Vector2(6, 6), new Vector2(-6, 6) });
            var men = new[] { new Vector3(0, 0, 0), new Vector3(3, 3, 0), new Vector3(-3, 2, 0) }.Select(Body).ToArray();
            for (int seed = 0; seed < 100; seed++)
                if (SpawnPoints.Pick(_w, ForceId.Friend, new Random(seed), out var pos, out _, out _))
                    foreach (var m in men)
                    {
                        var mp = _w.GetComponentRO<SimTransform>(m).Position;
                        Assert.True(Vector2.Distance(new Vector2(pos.X, pos.Y), new Vector2(mp.X, mp.Y)) >= SpawnPoints.ClearanceM, $"seed {seed}: {pos} is within 3 m of {mp}");
                    }
        }

        /// <summary>No area of the side (or none at all) ⇒ false with "no spawn area for &lt;side&gt;". ✅ Red-proof: the side filter dropped ⇒ the Hostile pick succeeds ⇒ red.</summary>
        [Fact]
        public void NoAreaForTheSide_IsRefusedWithItsText()
        {
            Assert.False(SpawnPoints.Pick(_w, ForceId.Friend, new Random(1), out _, out _, out string none)); Assert.Equal("no spawn area for Friend", none);
            Area(ForceId.Friend, Vector3.Zero, LShape);
            Assert.False(SpawnPoints.Pick(_w, ForceId.Hostile, new Random(1), out _, out _, out string why)); Assert.Equal("no spawn area for Hostile", why);
        }

        /// <summary>The ONE area-entity containment is what both <c>ItemSources</c> and the spawn point read (<c>WeatherField.AreaEntityContains</c>; the private copy in <c>ItemSources</c> is gone): inside / outside / on a concave notch. ✅ Red-proof: the even-odd test broken ⇒ red.</summary>
        [Fact]
        public void TheSharedContainment_AnswersInsideOutsideAndTheNotch()
        {
            var a = Area(ForceId.Friend, new Vector3(100f, 100f, 0f), LShape);
            Assert.True(WeatherField.AreaEntityContains(_w, a, new Vector2(105f, 105f)));
            Assert.True(WeatherField.AreaEntityContains(_w, a, new Vector2(125f, 105f)));
            Assert.False(WeatherField.AreaEntityContains(_w, a, new Vector2(120f, 120f)));   // the notch
            Assert.False(WeatherField.AreaEntityContains(_w, a, new Vector2(50f, 50f)));
        }
    }

    /// <summary>
    /// ⭐ Player Join S2-d (D-14, §7b) — the respawn delay is a SCENARIO PARAMETER: a scenario header that declares <c>respawnDelay</c> and binds it to <c>GameplaySettings.RespawnDelaySeconds</c> of the World section
    /// (<c>ParamBindings</c>, SR-41) loads with the bound value — the scenario's default, or the run's arg; and the component SAVES in the header's World section (it is the World Environment's, like the clock).
    /// </summary>
    public sealed class GameplaySettingsScenarioTests
    {
        private const string Scenario = """
            { "header": { "Params": [ {"Name": "respawnDelay", "TypeId": "float", "Default": 4} ],
                          "World": { "GameplaySettings": {"RespawnDelaySeconds": 10},
                                     "ParamBindings": {"Bindings": {"GameplaySettings.RespawnDelaySeconds": "respawnDelay"}} } },
              "entities": {} }
            """;

        private static float Bound(string? args)
        {
            string injected = ScenarioWorldSection.InjectEntity(Scenario, 8810);   // exactly the load's first step: Header.World becomes the World Environment entity
            var dom = (JsonObject)JsonNode.Parse(injected)!;
            ScenarioParams.Prepare(dom, ScenarioHeaderJson.Read(dom), args, null, "scenario 'gameplay'");
            return dom["entities"]![ScenarioWorldSection.EntityKey]!["GameplaySettings"]!["RespawnDelaySeconds"]!.GetValue<float>();
        }

        /// <summary>The declared default (4) lands in the World Environment's component, not the authored 10; the run's arg overrides it (0 ⇒ observer for good). ✅ Red-proof: the binding line removed from the scenario's World ⇒ 10 ⇒ red.</summary>
        [Fact]
        public void ARespawnDelayParam_BindsTheWorldEnvironmentsGameplaySettings()
        {
            Assert.Equal(4f, Bound(null));
            Assert.Equal(0f, Bound("""{"respawnDelay": 0}"""));
            Assert.Equal(25f, Bound("""{"respawnDelay": 25}"""));
        }

        /// <summary>Saved: the World Environment's <c>GameplaySettings</c> goes into <c>Header.World</c> (and no environment entity into Entities), and loads back through the same injection. ✅ Red-proof: a component the section does not carry ⇒ red.</summary>
        [Fact]
        public void TheSettings_AreSavedInTheWorldSection_AndComeBack()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<ScenarioClock>(); repo.RegisterComponent<WeatherConditions>(); repo.RegisterComponent<GameplaySettings>();
            var env = repo.CreateEntity();
            repo.AddComponent(env, new ScenarioClock { StartUtcTicks = 638860968000000000L, DisplayUtcOffsetMinutes = 0 });
            repo.AddComponent(env, WeatherConditions.Standard);
            repo.AddComponent(env, new GameplaySettings { RespawnDelaySeconds = 6f });

            var dom = new ScenarioSerializerBuilder("Hrot.SimHost").Build().Serialize(repo, new ScenarioHeader("Hrot.SimHost"));
            var world = ScenarioWorldSection.Of(dom)!;
            Assert.Equal(6f, world["GameplaySettings"]!["RespawnDelaySeconds"]!.GetValue<float>());
            Assert.Empty(dom["Entities"]!.AsObject());

            var back = (JsonObject)JsonNode.Parse(ScenarioWorldSection.InjectEntity(dom.ToJsonString(), 8810))!;
            var entity = (back["Entities"] ?? back["entities"])![ScenarioWorldSection.EntityKey]!;
            Assert.Equal(6f, entity["GameplaySettings"]!["RespawnDelaySeconds"]!.GetValue<float>());
        }
    }
}
