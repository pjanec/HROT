using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.Common.Diagnostics.Gizmos
{
    [GizmoProjector(typeof(SimTransform))]
    public sealed class EntityRotationGizmo : IStatelessGizmo
    {
        private readonly GizmoSettingsRegistry _settings;

        /// <summary>⭐ CE-1480 G5 — the picture is a function of the pose and the vehicle's length (and Seeable.OnMap): held while none moved.</summary>
        public GizmoInputs? Inputs { get; } = Seeable.Inputs(typeof(SimTransform), typeof(CarKinem.Core.VehicleParams));

        public EntityRotationGizmo(GizmoSettingsRegistry settings)
        {
            _settings = settings;
            EntityRotationGizmoSettings.Register(settings);
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!Seeable.OnMap(view, entity)) return;   // ⭐ CE-4239 (was NotAUnit alone)   // ⭐ CE-1165 — a cloud, a round, the camera have no heading to show
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            var pos = tf.Position;
            var q   = tf.Rotation;
            float yawRad = SimMath.ExtractYaw(q);

            // Nose length = half the entity's length + 25%, so the indicator just
            // overreaches the front of the entity shape rather than being a long ray.
            float halfLen = 2.5f; // default for a ~5 m entity when VehicleParams is absent
            if (view.HasComponent<CarKinem.Core.VehicleParams>(entity))
            {
                float vehLen = view.GetComponentRO<CarKinem.Core.VehicleParams>(entity).Length;
                if (vehLen > 0f) halfLen = vehLen * 0.5f;
            }
            float arrowLen = halfLen * 1.25f;

            // Tip of the arrow in the facing direction.
            var tip = new Vector3(
                pos.X + MathF.Cos(yawRad) * arrowLen,
                pos.Y + MathF.Sin(yawRad) * arrowLen,
                pos.Z);

            var color = new Rgba32(255, 165, 0, 255); // orange
            draw.DrawArrow(pos, tip, color, headSize: 3f, layer: DebugTraceLayers.UnitDetails);   // ⭐ CE-1065 — off by default

            // Heading label: compass degrees where 0=north, 90=east, clockwise.
            float compassDeg = SimMath.YawRadToCompassDeg(yawRad);
            // ⭐ CE-4432 (R-2084) — formatted on the stack: a string per entity per frame was ~2.6 MB/s at 8 000 units (measured on Windows)
            System.Span<char> text = stackalloc char[12];
            ((int)MathF.Round(compassDeg, MidpointRounding.AwayFromZero)).TryFormat(text, out int n, default, System.Globalization.CultureInfo.InvariantCulture);
            text[n++] = '*';
            // ⭐ CE-4582 (§6am D-7) — a row of HIS card, never free text at the unit (the arrow stays: it is drawn on the ground)
            int net = EntityCardRows.CardIdOf(view, entity);
            if (net != 0) draw.Card(net, DebugTraceLayers.UnitDetails).Row(EntityCardRows.Heading).Text(text[..n], new Rgba32(255, 165, 0, 200), 12f);
        }
    }
}
