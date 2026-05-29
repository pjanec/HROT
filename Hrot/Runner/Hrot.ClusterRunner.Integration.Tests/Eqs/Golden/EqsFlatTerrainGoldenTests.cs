using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
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
        public bool HasCheapLineOfSight(Vector2 from, Vector2 to) => from.X > 2f;
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
        TargetMemory.AddOrUpdateTarget(ref mem, entityId: 999L, posX: 20f, posY: 0f, scoreBoost: 100f, tick: 1);
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
}
