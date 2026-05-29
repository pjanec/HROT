using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.IG.Gizmos
{
    // GZ-PROJ: Draws the EQS search radius and lines to the current Top-K query results
    // for each entity carrying an EqsSensor component.
    // Visibility is controlled by three GizmoSettingsRegistry toggles.
    [GizmoProjector(typeof(SimTransform), typeof(EqsSensor))]
    public sealed class EqsSensorGizmo : IStatelessGizmo
    {
        private readonly GizmoSettingsRegistry _settings;
        private readonly uint _hashShowRadius;
        private readonly uint _hashShowCandidates;
        private readonly uint _hashShowScores;

        public EqsSensorGizmo(GizmoSettingsRegistry settings)
        {
            _settings = settings;
            EqsGizmoSettings.Register(settings);

            // Pre-compute FNV-1a hashes for the hot path to avoid per-frame string hashing.
            _hashShowRadius     = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowRadius);
            _hashShowCandidates = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowCandidates);
            _hashShowScores     = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowScores);
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var tf     = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var sensor = ref view.GetComponentRO<EqsSensor>(entity);

            // Use the authoritative altitude so the gizmo draws at the real height (P3D-401).
            var obsPos = new Vector3(tf.Position.X, tf.Position.Y, tf.Position.Z);

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
    }
}
