using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.ScenarioEditor.Gizmos
{
    // GZ-PROJ: Draws the EQS search radius and lines to the current Top-K query results
    // for each entity carrying an EqsSensor component.
    // Visibility is controlled by three GizmoSettingsRegistry toggles.
    // ⭐ CE-3123 (R-228) — lives in Hrot.Presentation (was the Hrot.IG assembly, which only IG and the Editor reference): every
    //   host discovers it without relying on the runner pre-loading Hrot.IG. Its namespace moved with it (the generator emits one registrar per namespace per assembly).
    // ⭐ CE-3143 — keyed on EqsSensor alone: a unit's sensors are CHILD entities with no SimTransform (EqsChildSensor.Ensure), so the
    //   old (SimTransform, EqsSensor) key matched none of them and this gizmo drew nothing for every current tactic. It now draws from
    //   the sensor's SELF (EqsContext.SelfPosition — the parent unit, as the solver resolves it), and adds the VERDICT: every candidate
    //   the template generates, green when the template's own filters keep it (EqsFilters.Run — the solver's pipeline), red when they
    //   reject it. ⛔ SUPERSEDED: [GizmoProjector(typeof(SimTransform), typeof(EqsSensor), …)].
    [GizmoProjector(typeof(EqsSensor), Family = Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Eqs)]
    public sealed class EqsSensorGizmo : IStatelessGizmo
    {
        private readonly GizmoSettingsRegistry _settings;
        private readonly uint _hashShowRadius;
        private readonly uint _hashShowCandidates;
        private readonly uint _hashShowScores;
        private readonly uint _hashShowVerdict;
        private EqsResult[] _verdict = new EqsResult[64];

        /// <summary>A kept candidate (the filters let it through — e.g. hidden from the threat).</summary>
        public static readonly Rgba32 KeptColor     = new(0, 230, 90, 230);
        /// <summary>A rejected candidate (a filter removed it — e.g. seen by the threat).</summary>
        public static readonly Rgba32 RejectedColor = new(230, 40, 40, 230);

        /// <summary>The template registry when the world holds none (a host that does not solve EQS): discovered once.</summary>
        private static readonly System.Lazy<IEqsTemplateRegistry> FallbackRegistry =
            new(() => EqsTemplateRegistry.Discover(EqsTemplateRegistry.CandidateAssemblies()));

        public EqsSensorGizmo(GizmoSettingsRegistry settings)
        {
            _settings = settings;
            EqsGizmoSettings.Register(settings);

            // Pre-compute FNV-1a hashes for the hot path to avoid per-frame string hashing.
            _hashShowRadius     = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowRadius);
            _hashShowCandidates = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowCandidates);
            _hashShowScores     = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowScores);
            _hashShowVerdict    = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowVerdict);
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var sensor = ref view.GetComponentRO<EqsSensor>(entity);

            // ⭐ CE-3143 — the sensor's self (a child sensor's parent unit), at its authoritative altitude (P3D-401).
            if (!EqsContext.SelfPosition(view, entity, sensor, out var obsPos)) return;

            // ⭐ CE-3143 — the verdict: what the AI's filters keep (green) and reject (red), recomputed on this view.
            if (_settings.Read(_hashShowVerdict).BoolValue) DrawVerdict(view, entity, in sensor, draw);

            // 1. Draw dashed search radius sphere in cyan.
            if (_settings.Read(_hashShowRadius).BoolValue)
            {
                draw.DrawSphere(
                    obsPos, sensor.SearchRadius,
                    new Rgba32(0, 255, 255, 100),
                    thickness: 1f,
                    style: LineStyle.Dashed);
            }

            // 2. Draw lines to Top-K candidate positions (requires EqsCognitiveBuffer).
            if (!view.HasComponent<EqsCognitiveBuffer>(entity))
                return;
            if (!_settings.Read(_hashShowCandidates).BoolValue)
                return;

            ref readonly var buffer = ref view.GetComponentRO<EqsCognitiveBuffer>(entity);
            if (!buffer.IsReady || buffer.Count == 0)
                return;

            bool showScores = _settings.Read(_hashShowScores).BoolValue;

            for (int i = 0; i < buffer.Count; i++)
            {
                var candidate = buffer.GetSpanRO()[i];
                // Draw each Top-K candidate at its real altitude (extruded for multi-level debug, P3D-401).
                var targetPos = new Vector3(candidate.PositionX, candidate.PositionY, candidate.PositionZ);

                // Green = positional candidate (EntityId == 0), yellow = entity-shaped candidate.
                var lineColor = candidate.EntityId == 0
                    ? new Rgba32(0, 255, 0, 150)
                    : new Rgba32(255, 255, 0, 150);

                draw.DrawLine(obsPos, targetPos, lineColor, thickness: 1.5f);
                draw.DrawSphere(targetPos, 1.5f, lineColor);

                if (showScores)
                {
                    // Score label above the candidate position.
                    draw.DrawText(
                        targetPos.X, targetPos.Y + 2f,
                        new Fdp.Core.FixedString32(string.Format("#{0} ({1:F2})", i + 1, candidate.Score)),
                        Rgba32.White);
                }
            }
        }

        /// <summary>
        /// Generates the sensor's candidates with its template and runs the template's FILTER tests (<see cref="EqsFilters.Run"/>, the
        /// solver's pipeline) on this view; a dot per candidate: kept green, rejected red. Scoring is not run (one score test submits
        /// raycasts). Nothing when the template is unknown or the generator cannot evaluate yet.
        /// </summary>
        private void DrawVerdict(ISimulationView view, Entity entity, in EqsSensor sensor, IDebugDrawBuilder draw)
        {
            var registry = view is EntityRepository repo && repo.HasSingletonManaged<IEqsTemplateRegistry>()
                ? repo.GetSingletonManaged<IEqsTemplateRegistry>()
                : null;
            registry ??= FallbackRegistry.Value;
            if (!registry.TryGetTemplate(sensor.BlueprintId, out var template) || template.Generator == null || template.MaxCandidates <= 0) return;

            if (_verdict.Length < template.MaxCandidates) _verdict = new EqsResult[template.MaxCandidates];
            var candidates = _verdict.AsSpan(0, template.MaxCandidates);
            var probe = sensor;   // the tests take the sensor by ref; never write the live component from a gizmo
            int count = template.Generator.Generate(entity, ref probe, view, candidates);
            if (count <= 0) return;
            var generated = candidates.Slice(0, count);
            EqsFilters.Run(in template, entity, ref probe, view, generated);
            foreach (ref readonly var c in generated)
            {
                var color = c.EntityId == -1L ? RejectedColor : KeptColor;
                draw.DrawSphere(new Vector3(c.PositionX, c.PositionY, c.PositionZ + 0.1f), 0.3f, color, fillColor: color);
            }
        }
    }
}
