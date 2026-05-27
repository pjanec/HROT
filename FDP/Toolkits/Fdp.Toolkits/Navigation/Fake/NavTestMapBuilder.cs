using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Fluent builder for <see cref="NavTestMap"/> instances.
    /// Used by <see cref="NavTestMaps"/> to construct canned test maps in code.
    ///
    /// Usage:
    /// <code>
    /// var map = new NavTestMapBuilder()
    ///     .Layer(NavLayerMask.Infantry, b => b
    ///         .Polygon(0, new[] { new Vector3(0,0,0), ... })
    ///         .Adjacent(0, 1)
    ///         .OffMeshLink(new OffMeshLink { ... }))
    ///     .MinAltitude(100f)
    ///     .MaxAltitude(2000f)
    ///     .NoFlyZone(new BoundingBox3D(min, max))
    ///     .Build();
    /// </code>
    /// </summary>
    public sealed class NavTestMapBuilder
    {
        private readonly List<FakeNavLayer>  _layers    = new();
        private readonly List<NoFlyVolume>   _noFlyZones = new();
        private float _minAltitude = 0f;
        private float _maxAltitude = 5000f;

        /// <summary>Add a layer built by <paramref name="configure"/>.</summary>
        public NavTestMapBuilder Layer(NavLayerMask mask, Action<NavLayerBuilder> configure)
        {
            var builder = new NavLayerBuilder((uint)mask);
            configure(builder);
            _layers.Add(builder.Build());
            return this;
        }

        /// <summary>Set minimum flyable altitude.</summary>
        public NavTestMapBuilder MinAltitude(float v) { _minAltitude = v; return this; }

        /// <summary>Set maximum flyable altitude.</summary>
        public NavTestMapBuilder MaxAltitude(float v) { _maxAltitude = v; return this; }

        /// <summary>Add a no-fly zone bounding box.</summary>
        public NavTestMapBuilder NoFlyZone(BoundingBox3D box)
        {
            _noFlyZones.Add(new NoFlyVolume { Bounds = box });
            return this;
        }

        /// <summary>Produce the finished <see cref="NavTestMap"/>.</summary>
        public NavTestMap Build() => new NavTestMap
        {
            Layers      = _layers.ToArray(),
            MinAltitude = _minAltitude,
            MaxAltitude = _maxAltitude,
            NoFlyZones  = _noFlyZones.ToArray(),
        };
    }

    /// <summary>
    /// Fluent builder for a single <see cref="FakeNavLayer"/>.
    /// Obtained from <see cref="NavTestMapBuilder.Layer"/>.
    /// </summary>
    public sealed class NavLayerBuilder
    {
        private readonly uint                    _layerMask;
        private readonly List<NavPolygon>        _polygons     = new();
        private readonly List<List<int>>         _adjacency    = new();
        private readonly List<OffMeshLink>       _links        = new();

        internal NavLayerBuilder(uint layerMask)
        {
            _layerMask = layerMask;
        }

        /// <summary>Add a polygon with given vertices.</summary>
        public NavLayerBuilder Polygon(int id, Vector3[] vertices,
                                       SurfaceType surface = SurfaceType.Generic)
        {
            _polygons.Add(new NavPolygon
            {
                Id          = id,
                Vertices    = vertices,
                SurfaceType = surface,
            });
            _adjacency.Add(new List<int>());
            return this;
        }

        /// <summary>
        /// Mark polygon at index <paramref name="fromIdx"/> adjacent to polygon at
        /// index <paramref name="toIdx"/> (and vice versa, bidirectional).
        /// </summary>
        public NavLayerBuilder Adjacent(int fromIdx, int toIdx)
        {
            EnsureAdjacency(fromIdx);
            EnsureAdjacency(toIdx);
            if (!_adjacency[fromIdx].Contains(toIdx)) _adjacency[fromIdx].Add(toIdx);
            if (!_adjacency[toIdx].Contains(fromIdx)) _adjacency[toIdx].Add(fromIdx);
            return this;
        }

        /// <summary>Add an off-mesh link.</summary>
        public NavLayerBuilder OffMeshLink(OffMeshLink link)
        {
            _links.Add(link);
            return this;
        }

        internal FakeNavLayer Build()
        {
            var adj = new int[_adjacency.Count][];
            for (int i = 0; i < _adjacency.Count; i++)
                adj[i] = _adjacency[i].ToArray();

            return new FakeNavLayer
            {
                Layer        = _layerMask,
                Polygons     = _polygons.ToArray(),
                Adjacency    = adj,
                OffMeshLinks = _links.ToArray(),
            };
        }

        private void EnsureAdjacency(int idx)
        {
            while (_adjacency.Count <= idx) _adjacency.Add(new List<int>());
        }
    }
}
