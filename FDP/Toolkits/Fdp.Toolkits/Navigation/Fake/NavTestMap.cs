using System;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// A no-fly volume: an axis-aligned box in which aerial agents are not permitted.
    /// </summary>
    public struct NoFlyVolume
    {
        public BoundingBox3D Bounds;
    }

    /// <summary>
    /// Declarative description of a navigation test map.
    /// Consumed by <see cref="FakeNavmeshProvider"/>, <see cref="FakeVolumetricPathProvider"/>,
    /// and loaded from JSON by <see cref="NavTestMapLoader"/>.
    /// </summary>
    public sealed class NavTestMap
    {
        /// <summary>Nav layers in the map.</summary>
        public FakeNavLayer[] Layers = Array.Empty<FakeNavLayer>();

        /// <summary>Lower altitude bound for aerial navigation (metres above sea level).</summary>
        public float MinAltitude = 0f;

        /// <summary>Upper altitude bound for aerial navigation (metres above sea level).</summary>
        public float MaxAltitude = 5000f;

        /// <summary>No-fly zones. Aerial paths must route around these volumes.</summary>
        public NoFlyVolume[] NoFlyZones = Array.Empty<NoFlyVolume>();
    }
}
