using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using CarKinem.Spatial;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs.Golden;

/// <summary>
/// Flat-terrain golden baseline (P3D-001) and parity gate (P3D-403, Axis-1).
///
/// <para><b>Capture (P3D-001):</b> run with <c>EQS_GOLDEN_CAPTURE=1</c> on the pre-change tree to
/// (re)write the committed golden artifacts under <c>Eqs/Golden/*.flat.golden.json</c>. These
/// record the legacy 2D Top-K (EntityId / X / Y / Score / Flags) for every registered
/// <c>[EqsTemplate]</c> starter template on a constant-Z (flat) map.</para>
///
/// <para><b>Parity (P3D-403, the merge gate):</b> the default mode re-runs the identical
/// deterministic scenario on the post-change tree and asserts X/Y/Score/Flags are
/// bit-or-tolerance-identical to the golden. On flat ground the promotion only adds a constant Z,
/// so any X/Y/score drift means a change did more than carry altitude — fix before merge. The new
/// <c>PositionZ</c> is additionally asserted ≈0 for all flat-map candidates.</para>
///
/// <para>Determinism: the scenario uses a fixed set of cover points, a fixed threat position, a
/// deterministic <see cref="MockLosService"/>, and pumps the offline <see cref="EditorHarness"/>
/// to a fixed terminal state (buffer ready). EQS scoring is pure math over that state, so the
/// captured Top-K is reproducible run-to-run.</para>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsFlatTerrainGoldenTests
{
    // Deterministic LOS: candidate positions east of x=2 are exposed (rejected); west are covered.
    private sealed class MockLosService : ILosService
    {
        public bool HasLineOfSight(Vector3 eye, Vector3 aim) => aim.X > 2f;   // the candidate is the aim (CE-210 3-D seam)
    }

    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    /// <summary>
    /// Maps each known starter-template BlueprintId to (template name, deterministic flat-terrain
    /// capture). Discovery (below) asserts this map covers exactly the registered
    /// <c>[EqsTemplate]</c> set, so a newly-added template fails loudly until a scenario is added.
    /// </summary>
    private static readonly IReadOnlyDictionary<uint, (string Name, Func<EqsGoldenTemplate> Capture)> Scenarios =
        new Dictionary<uint, (string, Func<EqsGoldenTemplate>)>
        {
            [FindCoverFromTarget.BlueprintId] = (nameof(FindCoverFromTarget), CaptureFindCoverFromTarget),
            [Hrot.SimHost.Systems.EntitiesOfForceInArea.BlueprintId] =
                (nameof(Hrot.SimHost.Systems.EntitiesOfForceInArea), CaptureEntitiesOfForceInArea),
            // ⭐ EQS §19.6 — the terrain starter templates, on flat ground with one wall (CaptureOnFlatGround).
            [FindOpenFiringPosition.BlueprintId] = (nameof(FindOpenFiringPosition),
                () => CaptureOnFlatGround(nameof(FindOpenFiringPosition), FindOpenFiringPosition.BlueprintId, 30f)),
            [FindFlankingPosition.BlueprintId] = (nameof(FindFlankingPosition),
                () => CaptureOnFlatGround(nameof(FindFlankingPosition), FindFlankingPosition.BlueprintId, 20f)),
            [FindSafeRetreatPoint.BlueprintId] = (nameof(FindSafeRetreatPoint),
                () => CaptureOnFlatGround(nameof(FindSafeRetreatPoint), FindSafeRetreatPoint.BlueprintId, 25f)),
            // ⭐ Stage 7a (CE-3134) — window positions from a manual database: two see the target past the wall's ends, one
            //   is behind the wall (rejected), and a Cover point at the same range must never appear (one kind per query).
            [FindWindowFiringPosition.BlueprintId] = (nameof(FindWindowFiringPosition),
                () => CaptureOnFlatGround(nameof(FindWindowFiringPosition), FindWindowFiringPosition.BlueprintId, 30f, cover: new ManualCoverProvider(new[]
                {
                    new CoverPoint { PositionX = 0f,   PositionY = -5f, DirectionY = -1f, Quality = 1f, StanceHeight = 1, Kind = CoverKind.WindowFiring },
                    new CoverPoint { PositionX = 28f,  PositionY = -5f, DirectionY = -1f, Quality = 1f, StanceHeight = 1, Kind = CoverKind.WindowFiring },
                    new CoverPoint { PositionX = -27f, PositionY = -8f, DirectionY = -1f, Quality = 1f, StanceHeight = 1, Kind = CoverKind.WindowFiring },
                    new CoverPoint { PositionX = 26f,  PositionY = -6f, DirectionY = -1f, Quality = 1f, StanceHeight = 2, Kind = CoverKind.Cover },
                }))),
            [FindThreatsInView.BlueprintId] = (nameof(FindThreatsInView),
                () => CaptureOnFlatGround(nameof(FindThreatsInView), FindThreatsInView.BlueprintId, 60f, ForceId.Hostile)),
        };

    [Fact]
    public void RegisteredTemplates_AllHaveAGoldenScenario()
    {
        var discovered = EqsGolden.DiscoverEqsTemplateTypes();
        Assert.NotEmpty(discovered); // success condition 1: at least the starter set exists.

        // Every discovered template must have a deterministic capture scenario, keyed by its
        // BlueprintId. We resolve BlueprintId from a public const on the template type.
        var missing = new List<string>();
        foreach (var t in discovered)
        {
            var idField = t.GetField("BlueprintId",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.True(idField != null,
                $"Template {t.FullName} must expose a public const uint BlueprintId.");
            uint id = (uint)idField!.GetValue(null)!;
            if (!Scenarios.ContainsKey(id))
                missing.Add($"{t.FullName} (BlueprintId=0x{id:X8})");
        }

        Assert.True(missing.Count == 0,
            "New [EqsTemplate] starter template(s) without a flat-terrain golden scenario: " +
            string.Join(", ", missing) + ". Add a deterministic scenario to EqsFlatTerrainGoldenTests.");
    }

    [Fact]
    public void FindCoverFromTarget_FlatTerrain_MatchesGolden()
    {
        RunGoldenForTemplate(nameof(FindCoverFromTarget));
    }

    [Theory]
    [InlineData(nameof(FindOpenFiringPosition))]
    [InlineData(nameof(FindFlankingPosition))]
    [InlineData(nameof(FindSafeRetreatPoint))]
    [InlineData(nameof(FindThreatsInView))]
    [InlineData(nameof(FindWindowFiringPosition))]
    public void TerrainStarterTemplate_FlatTerrain_MatchesGolden(string template) => RunGoldenForTemplate(template);

    [Fact]
    public void EntitiesOfForceInArea_FlatTerrain_MatchesGolden()
    {
        RunGoldenForTemplate(nameof(Hrot.SimHost.Systems.EntitiesOfForceInArea));
    }

    // ── core capture/compare driver ────────────────────────────────────────────

    private static void RunGoldenForTemplate(string templateName)
    {
        var scenario = Scenarios.Single(kv => kv.Value.Name == templateName).Value;
        EqsGoldenTemplate actual = scenario.Capture();

        if (EqsGolden.CaptureMode)
        {
            EqsGolden.Write(actual);
            return;
        }

        EqsGoldenTemplate golden = EqsGolden.Read(templateName);
        var diffs = EqsGolden.Compare(golden, actual);
        Assert.True(diffs.Count == 0,
            $"Flat-terrain parity gate (P3D-403) FAILED for {templateName}:\n  " +
            string.Join("\n  ", diffs));

#if EQS_HAS_POSITIONZ
        // P3D-403: the only new information on flat ground is a constant Z ≈ 0.
        Assert.True(actual.MaxAbsPositionZ <= 1e-3f,
            $"Flat-terrain PositionZ expected ≈0 but max |Z| = {actual.MaxAbsPositionZ:R}");
#endif
    }

    // ── per-template deterministic flat scenarios ──────────────────────────────

    private static EqsGoldenTemplate CaptureFindCoverFromTarget()
    {
        using var harness = new EditorHarness();

        // Flat map: every cover point at Z = 0. Three points west of x=2 are covered (survive),
        // one east is exposed (rejected). Distinct Y => distinct distance => distinct, ordered score.
        var provider = new ManualCoverProvider(new[]
        {
            new CoverPoint { PositionX = 0f, PositionY = 5f,  Quality = 1f }, // covered
            new CoverPoint { PositionX = 0f, PositionY = 10f, Quality = 1f }, // covered
            new CoverPoint { PositionX = 0f, PositionY = 15f, Quality = 1f }, // covered
            new CoverPoint { PositionX = 9f, PositionY = 0f,  Quality = 1f }, // exposed (rejected)
        });
        harness.Repo.SetSingletonManaged<ICoverProvider>(provider);

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(FindCoverFromTarget.Build(new MockLosService()));
        harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        var observer = harness.Repo.CreateEntity();
        harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });

        var mem = new TargetMemory();
        TargetMemory.AddOrUpdateTarget(ref mem, entityId: (long)harness.Repo.CreateEntity().PackedValue, posX: 20f, posY: 0f, scoreBoost: 100f, tick: 1);
        harness.Repo.AddComponent(observer, mem);

        var targetEntity = harness.Repo.CreateEntity();
        harness.Repo.AddComponent(targetEntity, new SimTransform
        {
            Position = new Vector3(20f, 0f, 0f),
            Rotation = Quaternion.Identity,
        });

        harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = FindCoverFromTarget.BlueprintId,
            Epoch           = 1,
            SearchRadius    = 25f,
            ThreatThreshold = 50f,
            ContextSlot1    = targetEntity,
        });
        harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8101L });

        bool ready = harness.PumpUntil(
            () => harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 5000);
        Assert.True(ready, "FindCoverFromTarget golden scenario produced no results within 5 s");

        ref readonly var buffer = ref harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        var golden = new EqsGoldenTemplate
        {
            Name        = nameof(FindCoverFromTarget),
            BlueprintId = FindCoverFromTarget.BlueprintId,
            Count       = buffer.Count,
        };

        var span = buffer.GetSpanRO();
        for (int i = 0; i < buffer.Count; i++)
        {
            ref readonly var r = ref span[i];
            golden.Rows.Add(new EqsGoldenRow
            {
                EntityId        = r.EntityId,
                PositionX       = r.PositionX,
                PositionY       = r.PositionY,
                Score           = r.Score,
                Flags           = r.Flags,
                FlagsMeaningful = r.FlagsMeaningful,
            });
#if EQS_HAS_POSITIONZ
            golden.MaxAbsPositionZ = MathF.Max(golden.MaxAbsPositionZ, MathF.Abs(r.PositionZ));
#endif
        }

        // Cleanup the EqsResultPool native array allocated by the solver.
        if (harness.Repo.HasSingleton<EqsResultPool>())
        {
            var pool = harness.Repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated)
                pool.Results.Dispose();
        }

        return golden;
    }

    /// <summary>
    /// The area query's EQS form on flat ground: a ±15 m square around (50, 50); two live hostiles
    /// inside, one hostile outside, one hostile wreck inside, one friendly inside. ⭐ Runs the real
    /// solver on a plain SimHost world with the PRODUCTION registry — not <see cref="EditorHarness"/>,
    /// whose EQS pumping is red on the base commit for reasons unrelated to templates.
    /// </summary>
    private static EqsGoldenTemplate CaptureEntitiesOfForceInArea()
    {
        using var repo = new EntityRepository();
        Hrot.SimHost.SimHostComponentRegistry.RegisterAll(repo);
        EqsTemplateRegistry.InstallDefault(repo);

        var area = repo.CreateEntity();
        repo.AddComponent(area, new SimTransform { Position = new Vector3(50f, 50f, 0f), Rotation = Quaternion.Identity });
        var ecb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        ecb.AddManagedComponent(area, new Hrot.IG.Components.EditablePolyline
        {
            Points = new List<Vector2> { new(-15f, -15f), new(15f, -15f), new(15f, 15f), new(-15f, 15f) },
        });
        ecb.Playback(repo);

        Entity Unit(float x, float y, ForceId force, float? health = null)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
            repo.AddComponent(e, new EntityInfo { ForceId = force });
            if (health is float h) repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = h, Max = 100f });
            return e;
        }
        Unit(50f, 50f, ForceId.Hostile);
        Unit(60f, 40f, ForceId.Hostile);
        Unit(80f, 80f, ForceId.Hostile);          // outside
        Unit(45f, 55f, ForceId.Hostile, 0f);      // wreck
        Unit(55f, 55f, ForceId.Friend);           // friendly

        var sensor = repo.CreateEntity();
        repo.AddComponent(sensor, Hrot.SimHost.Systems.EntitiesOfForceInArea.SensorFor(area, ForceId.Hostile));
        repo.AddComponent(sensor, new EqsCognitiveBuffer());

        var view = (ISimulationView)repo;
        new Hrot.SimHost.Systems.EqsSolverSystem().Execute(view, 0.1f);
        ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(repo);
        repo.Bus.SwapBuffers();
        new Hrot.SimHost.Systems.EqsResultUpdateSystem().Execute(view, 0.1f);

        ref readonly var buffer = ref repo.GetComponentRO<EqsCognitiveBuffer>(sensor);
        Assert.True(buffer.IsReady, "EntitiesOfForceInArea golden scenario produced no result");
        var golden = new EqsGoldenTemplate
        {
            Name        = nameof(Hrot.SimHost.Systems.EntitiesOfForceInArea),
            BlueprintId = Hrot.SimHost.Systems.EntitiesOfForceInArea.BlueprintId,
            Count       = buffer.Count,
        };
        // Order is not part of the contract (all scores are 0 — design §17.5) ⇒ record rows sorted.
        var span = buffer.GetSpanRO();
        var rows = new List<EqsResult>();
        for (int i = 0; i < buffer.Count; i++) rows.Add(span[i]);
        foreach (var r in rows.OrderBy(r => r.EntityId))
        {
            golden.Rows.Add(new EqsGoldenRow
            {
                EntityId        = r.EntityId,
                PositionX       = r.PositionX,
                PositionY       = r.PositionY,
                Score           = r.Score,
                Flags           = r.Flags,
                FlagsMeaningful = r.FlagsMeaningful,
            });
#if EQS_HAS_POSITIONZ
            golden.MaxAbsPositionZ = MathF.Max(golden.MaxAbsPositionZ, MathF.Abs(r.PositionZ));
#endif
        }

        if (repo.HasSingleton<EqsResultPool>())
        {
            var pool = repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated) pool.Results.Dispose();
        }
        return golden;
    }

    /// <summary>
    /// ⭐ EQS §19.6 — one flat world for the terrain starter templates: ground Z = 0, one 3 m wall along x ∈ [−20, 20] at
    /// y = −10. Self at the origin, the target at (0, −30) beyond the wall, a hostile in the open at (10, 0) and one behind the
    /// wall at (0, −40), a friendly at (5, 5). Runs the real solver with the PRODUCTION registry (as
    /// <see cref="CaptureEntitiesOfForceInArea"/>), so the templates are the ones a blueprint gets.
    /// </summary>
    private static EqsGoldenTemplate CaptureOnFlatGround(string name, uint blueprintId, float radius, ForceId? threats = null,
        ICoverProvider? cover = null)
    {
        using var repo = new EntityRepository();
        Hrot.SimHost.SimHostComponentRegistry.RegisterAll(repo);
        EqsTemplateRegistry.InstallDefault(repo);
        repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
        var wall = new[] { new Vector2(-20, -10), new Vector2(20, -10), new Vector2(20, -9.5f), new Vector2(-20, -9.5f) };
        repo.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainWorld
        {
            BoundsMin = new Vector2(-100, -100), BoundsMax = new Vector2(100, 100),
            Prisms = new[]
            {
                new Fdp.Toolkit.Terrain.TerrainPrism
                {
                    Kind = Fdp.Toolkit.Terrain.TerrainPrismKind.Wall, Footprint = wall, BaseZ = 0, TopZ = 3,
                    Min = new Vector2(-20, -10), Max = new Vector2(20, -9.5f),
                },
            },
        });
        if (cover != null) repo.SetSingletonManaged(cover);
        using var grid = new GridOwner(SpatialHashGrid.Create(100, 100, 5f, 64, Fdp.Core.Collections.Allocator.Persistent));
        grid.Grid.Clear();
        repo.SetSingleton(new SpatialGridData { Grid = grid.Grid });

        Entity Unit(float x, float y, ForceId force)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
            repo.AddComponent(e, new EntityInfo { ForceId = force });
            grid.Grid.Add(e, new Vector2(x, y));
            return e;
        }
        var self   = Unit(0, 0, ForceId.Friend);
        var target = Unit(0, -30, ForceId.Hostile);
        Unit(10, 0, ForceId.Hostile);
        Unit(0, -40, ForceId.Hostile);
        Unit(5, 5, ForceId.Friend);

        var sensor = repo.CreateEntity();
        repo.AddComponent(sensor, new EqsSensor
        {
            BlueprintId = blueprintId, Epoch = 1, SearchRadius = radius,
            FactionFilter = threats is ForceId f ? 1u << (int)f : 0u,
            ContextSlot0 = self, ContextSlot1 = target,
        });
        repo.AddComponent(sensor, new EqsCognitiveBuffer());

        var view = (ISimulationView)repo;
        new Hrot.SimHost.Systems.EqsSolverSystem().Execute(view, 0.1f);
        ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(repo);
        repo.Bus.SwapBuffers();
        new Hrot.SimHost.Systems.EqsResultUpdateSystem().Execute(view, 0.1f);

        ref readonly var buffer = ref repo.GetComponentRO<EqsCognitiveBuffer>(sensor);
        Assert.True(buffer.IsReady, $"{name} golden scenario produced no result");
        var golden = new EqsGoldenTemplate { Name = name, BlueprintId = blueprintId, Count = buffer.Count };
        var span = buffer.GetSpanRO();
        for (int i = 0; i < buffer.Count; i++)
        {
            ref readonly var r = ref span[i];
            golden.Rows.Add(new EqsGoldenRow
            {
                EntityId = r.EntityId, PositionX = r.PositionX, PositionY = r.PositionY,
                Score = r.Score, Flags = r.Flags, FlagsMeaningful = r.FlagsMeaningful,
            });
#if EQS_HAS_POSITIONZ
            golden.MaxAbsPositionZ = MathF.Max(golden.MaxAbsPositionZ, MathF.Abs(r.PositionZ));
#endif
        }
        if (repo.HasSingleton<EqsResultPool>())
        {
            var pool = repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated) pool.Results.Dispose();
        }
        return golden;
    }

    private sealed class GridOwner : IDisposable
    {
        public SpatialHashGrid Grid;
        public GridOwner(SpatialHashGrid grid) => Grid = grid;
        public void Dispose() => Grid.Dispose();
    }
}
