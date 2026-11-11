using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ O-7 B (CE-4418, docs/DESIGN_Terrain_Authoring.md §18e-2) — the EDITOR's live block expansion: a <see cref="TerrainBlock"/> entity in the world is expanded in memory (the SAME <see cref="OverlayExpansion"/> the scenario
    /// save uses), merged into the base and baked with the obstacles by the node's one <see cref="StaticObstacleBakeSystem"/>; a moved block dirties only its old and its new place; an entity that did not change is not
    /// expanded again; a steady frame allocates nothing. The wiring (the editor's system HAS a <see cref="LiveOverlay"/>, no other host's does) is railed in <c>EntityCreationPackRails</c> and the editor's
    /// <c>TheEditorShowsItsTerrainBlocksLiveTests</c>.
    /// </summary>
    public sealed class LiveOverlayBakeTests : IDisposable
    {
        private const string Hut = """{"footprint":[[0,0],[10,0],[10,8],[0,8]],"storeys":[{"height":3,"walls":[{"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}""";

        private readonly EntityRepository _world = new();
        private readonly string _staging = Path.Combine(Path.GetTempPath(), "o7b-" + Guid.NewGuid().ToString("N")[..10]);
        private double _wall;
        private TerrainResidency _residency = null!;

        public LiveOverlayBakeTests()
        {
            SimHostComponentRegistry.RegisterAll(_world);
            string terrain = Path.Combine(_staging, "Terrain", "base-t");
            Directory.CreateDirectory(Path.Combine(terrain, "buildings")); Directory.CreateDirectory(Path.Combine(_staging, "terrain-recipes"));
            File.WriteAllText(Path.Combine(terrain, "terrain.json"), """{"schemaVersion":2,"name":"base-t","world":"w.geojson"}""");
            File.WriteAllText(Path.Combine(terrain, "w.geojson"), """
                {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[
                  {"type":"Feature","properties":{"kind":"building","label":"B","building":HUT},"geometry":{"type":"Point","coordinates":[100,100]}}]}
                """.Replace("HUT", Hut));
            File.WriteAllText(Path.Combine(terrain, "buildings", "hut.building.json"), Hut);
            File.WriteAllText(Path.Combine(_staging, "terrain-recipes", "litter.recipe.json"), """{"id":"litter","scatter":{"density":2,"items":[{"kind":"clutter","type":"car","weight":1},{"kind":"tree","weight":5},{"kind":"bush","weight":4}]}}""");
            File.WriteAllText(Path.Combine(_staging, "terrain-recipes", "village.recipe.json"), """{"id":"village","houses":{"templates":["hut"],"count":[6,6],"radius":50,"minSpacing":4},"woods":{"count":[2,2],"size":[8,12]}}""");
            _residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            _residency.Commit(_world, _residency.Prepare("base-t"));
            Assert.True(_world.HasSingletonManaged<StaticObstacleBakery>());
        }

        public void Dispose()
        {
            _world.Dispose();
            try { Directory.Delete(_staging, recursive: true); } catch { }
        }

        private StaticObstacleBakeSystem Editor() => new(() => _wall) { LiveOverlay = new LiveOverlay() };

        private Entity Block(float x, float y, Guid? guid = null, string recipe = "village")
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0), Rotation = Quaternion.Identity });
            _world.AddComponent(e, new TerrainBlock { Recipe = new FixedString32(recipe), Seed = 7, HeadingDeg = 0, Fit = TerrainBlock.FitAll });
            _world.AddComponent(e, new EntityGuid(guid ?? Guid.Parse("11111111-2222-4333-8444-555555555555")));
            return e;
        }

        private void Move(Entity e, float x, float y)
        {
            var t = _world.GetComponentRO<SimTransform>(e);
            _world.SetComponent(e, new SimTransform { Position = new Vector3(x, y, 0), Rotation = t.Rotation });
        }

        private Entity Car(float x)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new Vector3(x, 380f, 0), Rotation = Quaternion.Identity });
            _world.AddComponent(e, new StaticObstacle { Material = new FixedString32("car-body") });
            _world.AddComponent(e, new ObstacleShape { Length = 4.5f, Width = 1.8f, Height = 1.5f });
            return e;
        }

        private void RunUntilBaked(StaticObstacleBakeSystem sys, int bakes)
        {
            _wall += 1.0; sys.Execute(_world, 0.016f);   // the signature moves
            _wall += 1.0;
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (sys.Bakes < bakes && DateTime.UtcNow < deadline)
            {
                sys.Execute(_world, 0.016f);
                if (sys.Bakes < bakes) Thread.Sleep(5);
            }
            Assert.Equal(bakes, sys.Bakes);
        }

        /// <summary>
        /// ⭐⭐ (a) a block entity in an editor-like world appears in the baked <see cref="TerrainWorld"/>: its houses and its woods, on top of the base house. 🔴 Red-proof: give the system no <c>LiveOverlay</c>
        /// (or pass <c>null</c> to <c>BakeObstacles</c>) ⇒ the world holds only the base house.
        /// </summary>
        [Fact]
        public void O7B_a_ABlockEntity_AppearsInTheBakedTerrain_HousesAndWoods()
        {
            var sys = Editor();
            Block(250f, 250f);
            RunUntilBaked(sys, 1);

            var baked = _world.GetSingletonManaged<TerrainWorld>()!;
            Assert.True(baked.Buildings.Count > 1, $"the block's houses are in the terrain (buildings: {baked.Buildings.Count})");
            Assert.Equal("B", baked.Buildings[0].Label);   // the base keeps its index
            Assert.Contains(baked.Buildings, b => b.Label.StartsWith("11111111-2222-4333-8444-555555555555/", StringComparison.Ordinal));
            Assert.Contains(baked.Surfaces, s => s.Type == TerrainSurfaceType.Forest);   // a wood
            Assert.Contains(sys.BakedParts, p => p.Key.Contains("/w"));
            Assert.NotEmpty(sys.LastDirty!);   // the first bake after the load names the places of its parts
        }

        /// <summary>
        /// ⭐⭐ (b) moving the block gives a dirty region of its OLD and its NEW parts only (houses and woods), and a later bake that changes only an obstacle re-expands NOTHING (the block comes from the cache).
        /// 🔴 Red-proof: key the cache on the guid alone (ignore the position) ⇒ the moved block keeps its old parts and no region appears; drop the cache ⇒ the hit counter stays 0.
        /// </summary>
        [Fact]
        public void O7B_b_AMovedBlock_DirtiesItsOldAndNewPlaceOnly_AndAnUnchangedBlockIsNotExpandedAgain()
        {
            var sys = Editor();
            var live = sys.LiveOverlay!;
            var block = Block(120f, 250f);
            RunUntilBaked(sys, 1);
            var oldParts = sys.BakedParts.ToList();
            Assert.NotEmpty(oldParts);
            Assert.Equal(1, live.Expansions);

            Move(block, 280f, 250f);
            RunUntilBaked(sys, 2);
            var newParts = sys.BakedParts.ToList();
            Assert.Equal(2, live.Expansions);
            Assert.Equal(oldParts.Select(p => p.Key).OrderBy(k => k), newParts.Select(p => p.Key).OrderBy(k => k));   // the stable keys: the same parts, elsewhere

            var dirty = sys.LastDirty!;
            Assert.NotNull(dirty);
            var expected = oldParts.Concat(newParts).Select(p => (p.Min, p.Max)).Distinct().OrderBy(r => r.Min.X).ThenBy(r => r.Min.Y).ToList();
            var got = dirty.Distinct().OrderBy(r => r.Min.X).ThenBy(r => r.Min.Y).ToList();
            Assert.Equal(expected, got);   // exactly the old and the new place — nothing else, no whole-world region
            Assert.All(got, r => Assert.True(r.Max.X - r.Min.X < 100f && r.Max.Y - r.Min.Y < 100f, "each region is a part's box, not the terrain"));

            // an unchanged second bake: only an obstacle changed ⇒ the block's parts come from the cache, and only the car is dirty
            int expansions = live.Expansions, hits = live.CacheHits;
            Car(60f);
            RunUntilBaked(sys, 3);
            Assert.Equal(expansions, live.Expansions);
            Assert.True(live.CacheHits > hits, "the cached expansion was used");
            Assert.Single(sys.LastDirty!);   // the car's box only: the parts are unchanged
        }

        /// <summary>
        /// ⭐⭐ CE-4427 R-4 — damage on an overlay house STAYS on that house when a block elsewhere re-sorts the overlay's parts: block X's guid sorts before block A's, so A's houses move to higher indices in the new
        /// merged world; the committed bake re-keys the <see cref="BuildingDamage"/> entity (by key and within-building panel). 🔴 Red-proof: drop <c>RemapDamageEntities</c> from <c>CommitObstacles</c> ⇒ the entity
        /// still points at the old index — another house.
        /// </summary>
        [Fact]
        public void CE4427_R4_DamageOnAnOverlayHouse_StaysOnItWhenABlockElsewhereReSortsTheParts()
        {
            var sys = Editor();
            Block(250f, 250f, Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"));
            RunUntilBaked(sys, 1);
            var w1 = _world.GetSingletonManaged<TerrainWorld>()!;
            int house = Array.FindIndex(w1.Buildings.ToArray(), b => b.Label.StartsWith("aaaaaaaa-", StringComparison.Ordinal));
            Assert.True(house > 0);
            string key = BuildingDamages.KeyOf(w1, house);
            int local = 1, panel = BuildingPanelIndex.ToGlobal(w1, house, local);
            var e = _world.CreateEntity();
            var d = new BuildingDamage { Building = house };
            BuildingDamages.Add(ref d, new WallBreach(panel, 3f, 5f, 0f, 2.2f, BreachKind.Passage), w1);
            _world.AddComponent(e, d);
            _world.SetManagedComponent(e, new TerrainObjectKey { Key = key });

            Block(100f, 100f, Guid.Parse("00000000-0000-4000-8000-000000000002"));   // sorts BEFORE aaaa…: every house of A moves up
            RunUntilBaked(sys, 2);
            var w2 = _world.GetSingletonManaged<TerrainWorld>()!;
            int now = BuildingDamages.IndexOfKey(w2, key);
            Assert.True(now > house, "the house moved to a higher index in the new world");

            var got = _world.GetComponentRO<BuildingDamage>(e);
            Assert.Equal(now, got.Building);                                          // the entity followed its house
            Assert.Equal(1, got.Count);
            Assert.Equal(now, w2.Panels[got.Get(0).Panel].Building);                  // its breach is on THAT house's panel…
            Assert.Equal(local, BuildingPanelIndex.ToLocal(w2, got.Get(0).Panel));    // …the same wall
        }

        /// <summary>
        /// ⭐⭐ CE-3439 — UNPACKING a block whose house is damaged keeps the damage on that house: Unpack re-keys the house (<c>&lt;guid&gt;/&lt;i&gt;</c> → <c>&lt;feature guid&gt;/0</c>) and the <c>BuildingDamage</c> entity with it; the committed bake re-points the entity by its OWN
        /// key (<c>BuildingDamages.Remap</c>), the old numbering's label no longer naming the house. 🔴 Red-proof: drop the damage-key rewrite in <c>BlockOperations.Unpack</c> ⇒ the key finds no house ⇒ the damage is gone ⇒ red; ignore the key in <c>RemapDamageEntities</c> ⇒ red.
        /// </summary>
        [Fact]
        public void CE3439_AnUnpackedBlocksDamagedHouse_KeepsItsDamage()
        {
            var sys = Editor();
            var block = Block(250f, 250f, Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"));
            RunUntilBaked(sys, 1);
            var w1 = _world.GetSingletonManaged<TerrainWorld>()!;
            int house = Array.FindIndex(w1.Buildings.ToArray(), b => b.Label.StartsWith("aaaaaaaa-", StringComparison.Ordinal));
            string key = BuildingDamages.KeyOf(w1, house);
            int local = 1, panel = BuildingPanelIndex.ToGlobal(w1, house, local);
            var e = _world.CreateEntity();
            var d = new BuildingDamage { Building = house };
            BuildingDamages.Add(ref d, new WallBreach(panel, 3f, 5f, 0f, 2.2f, BreachKind.Passage), w1);
            _world.AddComponent(e, d);
            _world.SetManagedComponent(e, new TerrainObjectKey { Key = key });

            var ctx = BlockExpansionContext.Of(_residency)!;
            Assert.True(BlockOperations.Unpack(_world, block, ctx).Done);
            string newKey = _world.GetComponent<TerrainObjectKey>(e).Key;
            Assert.NotEqual(key, newKey);
            RunUntilBaked(sys, 2);

            var w2 = _world.GetSingletonManaged<TerrainWorld>()!;
            int now = BuildingDamages.IndexOfKey(w2, newKey);
            Assert.True(now >= 0, "the unpacked house is in the new world under its new key");
            var got = _world.GetComponentRO<BuildingDamage>(e);
            Assert.Equal(now, got.Building);
            Assert.Equal(1, got.Count);
            Assert.Equal(now, w2.Panels[got.Get(0).Panel].Building);
            Assert.Equal(local, BuildingPanelIndex.ToLocal(w2, got.Get(0).Panel));
        }

        /// <summary>
        /// ⭐ CE-4427 R-7 — editing the RECIPE (or a template) file re-expands the block that read it: the cache key holds the files' (length, mtime) as well as the entity's parameters, and a once-a-second poll (no per-frame IO) moves
        /// the signature so the bake runs. 🔴 Red-proof: ignore the stamp in the cache lookup (<c>StampOf(entry.Files) != entry.Stamp</c> removed) ⇒ the block keeps its old houses and <c>Expansions</c> stays 1.
        /// </summary>
        [Fact]
        public void CE4427_R7_EditingTheRecipeFile_ReExpandsTheBlock()
        {
            var sys = Editor();
            var live = sys.LiveOverlay!;
            Block(250f, 250f);
            RunUntilBaked(sys, 1);
            int before = _world.GetSingletonManaged<TerrainWorld>()!.Buildings.Count;
            Assert.Equal(1, live.Expansions);

            string recipe = Path.Combine(_staging, "terrain-recipes", "village.recipe.json");
            File.WriteAllText(recipe, """{"id":"village","houses":{"templates":["hut"],"count":[2,2],"radius":50,"minSpacing":4}}""");
            File.SetLastWriteTimeUtc(recipe, DateTime.UtcNow.AddMinutes(5));
            RunUntilBaked(sys, 2);

            Assert.Equal(2, live.Expansions);                                                        // the block read the edited file again
            Assert.True(_world.GetSingletonManaged<TerrainWorld>()!.Buildings.Count < before, "fewer houses: the new recipe is in the baked world");
        }

        /// <summary>
        /// ⭐⭐ T-2 (CE-4429) — the compiler (the scenario JSON) and the live overlay (the ECS components) give the SAME parts for EVERY kind: the same labels and the same bounds, the shape read as a position plus relative vertices
        /// by both. 🔴 Red-proof: have <c>LiveOverlay.Snapshot</c> use the polyline's vertices as WORLD points (drop the <c>at +</c>) ⇒ the line and area kinds sit elsewhere.
        /// </summary>
        [Fact]
        public void T2_TheCompilerAndTheLiveOverlay_GiveTheSamePartsForEveryKind()
        {
            var specs = new (byte Kind, float X, float Y, string Template, float Heading, float Pad, uint Seed, (float, float)[] Points)[]
            {
                (TerrainFeature.House, 300, 300, "hut", 90f, 0f, 0, Array.Empty<(float, float)>()),
                (TerrainFeature.Wall, 100, 300, "brick", 0f, 2.5f, 0, new[] { (0f, 0f), (20f, 0f) }),
                (TerrainFeature.Fence, 100, 320, "", 0f, 0f, 0, new[] { (0f, 0f), (20f, 5f) }),
                (TerrainFeature.Hedge, 100, 340, "", 0f, 0f, 0, new[] { (0f, 0f), (20f, 0f), (20f, 10f) }),
                (TerrainFeature.Wood, 250, 100, "", 0f, 80f, 0, new[] { (-30f, -20f), (30f, -20f), (30f, 20f), (-30f, 20f) }),
                (TerrainFeature.Tree, 360, 100, "", 0f, 0f, 0, Array.Empty<(float, float)>()),
                (TerrainFeature.Scrub, 350, 350, "", 0f, 0f, 0, new[] { (0f, 0f), (20f, 0f), (20f, 20f), (0f, 20f) }),
                (TerrainFeature.Clutter, 200, 200, "sandbag-wall", 30f, 0f, 0, Array.Empty<(float, float)>()),
                (TerrainFeature.Water, 200, 60, "", 0f, 0f, 0, new[] { (-15f, -20f), (15f, -20f), (15f, 20f), (-15f, 20f) }),
                (TerrainFeature.Bridge, 20, 200, "", 0f, 4f, 0, new[] { (0f, 0f), (34f, 0f) }),
                (TerrainFeature.Scatter, 300, 60, "litter", 0f, 3.9f, 7, new[] { (-30f, -30f), (30f, -30f), (30f, 30f), (-30f, 30f) }),
            };
            var dom = new System.Text.Json.Nodes.JsonObject();
            var entities = new System.Text.Json.Nodes.JsonObject();
            for (int i = 0; i < specs.Length; i++)
            {
                var sp = specs[i];
                var guid = Guid.Parse($"d{i:x7}-0000-4000-8000-000000000001");
                var e = _world.CreateEntity();
                _world.AddComponent(e, new SimTransform { Position = new Vector3(sp.X, sp.Y, 0), Rotation = Quaternion.Identity });
                _world.AddComponent(e, new TerrainFeature { Kind = sp.Kind, Template = new FixedString32(sp.Template), HeadingDeg = sp.Heading, Pad = sp.Pad, Seed = sp.Seed });
                _world.AddComponent(e, new EntityGuid(guid));
                var node = new System.Text.Json.Nodes.JsonObject
                {
                    ["TerrainFeature"] = new System.Text.Json.Nodes.JsonObject { ["Kind"] = sp.Kind, ["Template"] = sp.Template, ["HeadingDeg"] = sp.Heading, ["Pad"] = sp.Pad, ["Seed"] = sp.Seed },
                    ["SimTransform"] = new System.Text.Json.Nodes.JsonObject { ["Position"] = new System.Text.Json.Nodes.JsonArray(sp.X, sp.Y, 0), ["Rotation"] = new System.Text.Json.Nodes.JsonArray(0, 0, 0, 1) },
                };
                if (sp.Points.Length > 0)
                {
                    _world.SetManagedComponent(e, new Hrot.Map.Common.Components.EditablePolyline { Points = sp.Points.Select(p => new Vector2(p.Item1, p.Item2)).ToList() });
                    var pts = new System.Text.Json.Nodes.JsonArray();
                    foreach (var (px, py) in sp.Points) pts.Add(new System.Text.Json.Nodes.JsonObject { ["X"] = px, ["Y"] = py });
                    node["EditablePolyline"] = new System.Text.Json.Nodes.JsonObject { ["Points"] = pts, ["Version"] = 0 };
                }
                entities[guid.ToString("D")] = node;
            }
            dom["header"] = new System.Text.Json.Nodes.JsonObject { ["terrainName"] = "base-t" };
            dom["entities"] = entities;

            // the compiler's parts: label → bounds
            string scenarioDir = Path.Combine(_staging, "scenario-x");
            Directory.CreateDirectory(scenarioDir);
            string definition = Path.Combine(_staging, "Terrain", "base-t", "terrain.json");
            var compiled = Fdp.Toolkit.Terrain.OverlayCompiler.Compile(dom, scenarioDir, name => name == "base-t" ? definition : null, bakeNavtiles: false, recipeRoots: new[] { Path.Combine(_staging, "terrain-recipes") });
            Assert.True(compiled.Wrote);
            var file = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(compiled.Folder!, "overlay.world.geojson")))!["features"]!.AsArray();
            var fromCompiler = file.Select(f => (Label: Fdp.Toolkit.Terrain.OverlayExpansion.Label((System.Text.Json.Nodes.JsonObject)f!), Box: Fdp.Toolkit.Terrain.OverlayExpansion.BoundsOf((System.Text.Json.Nodes.JsonObject)f!))).OrderBy(t => t.Label, StringComparer.Ordinal).ToList();

            // the live overlay's parts, from the components
            var live = new LiveOverlay();
            var (@base, version, folder) = _residency.ExpansionBase();
            var result = live.Expand(live.Snapshot(_world), @base, version, folder)!;
            var fromLive = result.Parts.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();

            Assert.True(fromCompiler.Count > 50, $"{fromCompiler.Count} parts: every kind produced some (a scatter alone makes ≈ 140)");
            Assert.Equal(fromCompiler.Select(t => t.Label), fromLive.Select(p => p.Key));
            for (int i = 0; i < fromLive.Count; i++)
            {
                Assert.Equal(fromCompiler[i].Box.Min.X, fromLive[i].Min.X, 2); Assert.Equal(fromCompiler[i].Box.Min.Y, fromLive[i].Min.Y, 2);
                Assert.Equal(fromCompiler[i].Box.Max.X, fromLive[i].Max.X, 2); Assert.Equal(fromCompiler[i].Box.Max.Y, fromLive[i].Max.Y, 2);
            }
            // …and the live overlay's world parses with every kind in it
            Assert.Contains(result.World.Surfaces, s => s.Type == TerrainSurfaceType.Water);
            Assert.Contains(result.World.Surfaces, s => s.Type == TerrainSurfaceType.Forest);
            Assert.Contains(result.World.Surfaces, s => s.Type == TerrainSurfaceType.Scrub);
            Assert.NotEmpty(result.World.Walkables);   // the bridge
            Assert.Contains(result.World.Panels, p => p.Material?.Name == "hedge");
        }

        private long SteadyBytes(StaticObstacleBakeSystem sys, int frames = 50)
        {
            for (int i = 0; i < 30; i++) sys.Execute(_world, 0.016f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames; i++) sys.Execute(_world, 0.016f);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        /// <summary>
        /// ⭐⭐ CE-3436 (HOT PATH) — a steady <c>Execute</c> (obstacles present, nothing changing, past the debounce) allocates 0 B over 50 frames, on a PLAIN node and with a LiveOverlay and a block. The per-frame
        /// <c>CollectPrisms</c> (a query, a prism and a label per obstacle) is gone: the prisms are built only when a bake starts. 🔴 Red-proof: call <c>CollectPrisms(world, _prisms)</c> every frame again.
        /// </summary>
        [Fact]
        public void CE3436_ASteadyExecute_AllocatesNothing_OnAPlainNode_AndWithABlockAndALiveOverlay()
        {
            var plain = new StaticObstacleBakeSystem(() => _wall);
            var car = Car(60f); _world.SetLifecycleState(car, EntityLifecycle.Active);
            RunUntilBaked(plain, 1);
            Assert.Equal(0L, SteadyBytes(plain));
            Assert.Equal(1, plain.Bakes);

            var editor = Editor();
            Block(250f, 250f);
            RunUntilBaked(editor, 1);
            Assert.Equal(0L, SteadyBytes(editor));
            Assert.Equal(1, editor.Bakes);
        }

        /// <summary>
        /// ⭐⭐ (d) steady frames with no change allocate NOTHING more than a node without an overlay (CE-2394's pattern): the live overlay's signature is read from the components through a cached query. HOT PATH.
        /// 🔴 Red-proof: allocate in <c>SignatureOfWorld</c> (a <c>new List</c> / LINQ per call) ⇒ the delta is not 0.
        /// </summary>
        [Fact]
        public void O7B_d_SteadyFrames_WithABlock_AllocateNothingBeyondTheBase()
        {
            var sys = Editor();
            var plain = new StaticObstacleBakeSystem(() => _wall);
            Block(250f, 250f);
            RunUntilBaked(sys, 1);

            // the overlay's own steady cost: the signature of the world
            var live = sys.LiveOverlay!;
            live.SignatureOfWorld(_world);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) live.SignatureOfWorld(_world);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - a0);

            // and the whole Execute: a frame with a block costs the same as one without
            for (int i = 0; i < 20; i++) { sys.Execute(_world, 0.016f); plain.Execute(_world, 0.016f); }
            long s0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) sys.Execute(_world, 0.016f);
            long withOverlay = GC.GetAllocatedBytesForCurrentThread() - s0;
            long p0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) plain.Execute(_world, 0.016f);
            long without = GC.GetAllocatedBytesForCurrentThread() - p0;
            Assert.True(withOverlay <= without, $"a steady frame with a live overlay allocated {withOverlay} B per 100 frames, the plain one {without} B");
        }
    }
}
