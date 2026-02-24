using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Modules.Geographic.Components;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;

namespace Fdp.Modules.Geographic.Systems
{
    /// <summary>
    /// Converts <see cref="SimTransform"/> + <see cref="SimVelocity"/> into
    /// <see cref="GeoTransform"/> + <see cref="GeoVelocity"/> for all locally-owned
    /// entities each tick.
    ///
    /// Runs PostSimulation — after physics has updated SimTransform, before egress.
    /// Generic: works for any entity type with SimTransform (not vehicle-specific).
    ///
    /// Analogue of <see cref="CoordinateTransformSystem"/> but for SimTransform
    /// instead of Position.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class SimTransformBridgeSystem : IModuleSystem
    {
        private readonly IGeographicTransform _geo;

        public SimTransformBridgeSystem(IGeographicTransform geo)
            => _geo = geo ?? throw new ArgumentNullException(nameof(geo));

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();

            var query = view.Query()
                .With<SimTransform>()
                .With<NetworkOwnership>()
                .Build();

            foreach (var entity in query)
            {
                ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(entity);
                if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                    continue; // Remote entities driven by inbound path (GeodeticSmoothingSystem)

                UpdateEntity(view, cmd, entity);
            }
        }

        private void UpdateEntity(ISimulationView view, IEntityCommandBuffer cmd, Entity entity)
        {
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);

            // ?? GeoTransform ??????????????????????????????????????????????????
            var (lat, lon, alt) = _geo.ToGeodetic(tf.Position);
            float headingDeg = RotationToHeadingDeg(tf.Rotation);

            var geoTf = new GeoTransform
            {
                Latitude   = lat,
                Longitude  = lon,
                Altitude   = (float)alt,
                HeadingDeg = headingDeg,
                PitchDeg   = 0f, // flat terrain — no pitch tracking
                RollDeg    = 0f, // flat terrain — no roll tracking
            };

            if (view.HasComponent<GeoTransform>(entity))
                cmd.SetComponent(entity, geoTf);
            else
                cmd.AddComponent(entity, geoTf);

            // ?? GeoVelocity ???????????????????????????????????????????????????
            // GeoVelocity is only meaningful when SimVelocity is present.
            if (!view.HasComponent<SimVelocity>(entity))
                return;

            ref readonly var vel = ref view.GetComponentRO<SimVelocity>(entity);

            // Acceleration: left as zero to avoid a CarKinem project dependency.
            // If per-vehicle acceleration is needed, the application layer (e.g. SimHost)
            // should set GeoVelocity.Accel in a separate system that has access to VehicleState.
            var geoVel = new GeoVelocity
            {
                Linear  = vel.Linear,  // Already in ENU frame — direct copy
                Angular = vel.Angular, // Already in ENU frame — direct copy
                Accel   = Vector3.Zero,
            };

            if (view.HasComponent<GeoVelocity>(entity))
                cmd.SetComponent(entity, geoVel);
            else
                cmd.AddComponent(entity, geoVel);
        }

        // ?? Static helpers (public for unit-testability and external usage) ????????????????????

        /// <summary>
        /// Converts <see cref="SimTransform.Rotation"/> to compass heading degrees [0, 360).
        /// UnitX-forward convention: matches CarKinematicsSystem (lines ~122–125).
        /// X=East, Y=North. 0°=North, 90°=East, clockwise.
        /// </summary>
        public static float RotationToHeadingDeg(Quaternion rotation)
        {
            Vector3 fwd3D = Vector3.Transform(Vector3.UnitX, rotation);
            Vector2 fwd2D = new Vector2(fwd3D.X, fwd3D.Y);
            if (fwd2D.LengthSquared() < 1e-6f) return 0f;
            float mathYaw = MathF.Atan2(fwd2D.Y, fwd2D.X);
            return (90f - mathYaw * (180f / MathF.PI) + 360f) % 360f;
        }

        /// <summary>
        /// Converts a world-space ENU velocity vector to compass azimuth degrees [0, 360).
        /// Falls back to <paramref name="fallback"/> when the speed is negligible.
        /// </summary>
        public static float VelocityToAzimuthDeg(Vector3 linearENU, float fallback)
        {
            Vector2 xy = new Vector2(linearENU.X, linearENU.Y);
            if (xy.LengthSquared() < 1e-4f) return fallback;
            float mathYaw = MathF.Atan2(xy.Y, xy.X);
            return (90f - mathYaw * (180f / MathF.PI) + 360f) % 360f;
        }
    }
}
