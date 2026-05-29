using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Geographic;

namespace Fdp.Modules.Geographic.Components
{
    /// <summary>
    /// One terrain-height request submitted by <c>TerrainQuerySubmitSystem</c> each frame.
    /// Stored in <see cref="TerrainQueryBatchData.Requests"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TerrainQueryRequest
    {
        /// <summary>Entity handle (index + generation) for result correlation.</summary>
        public Entity Entity;

        /// <summary>World-X coordinate at which to sample terrain height (IG space).</summary>
        public float QueryX;

        /// <summary>World-Y coordinate at which to sample terrain height (IG space).</summary>
        public float QueryY;

        /// <summary>
        /// Simulation Z of the entity at query time. Retained for the batch wire format;
        /// since P3D-102 the resolution system writes <c>HitZ</c> directly into the
        /// authoritative <c>SimTransform.Position.Z</c> rather than deriving a visual offset.
        /// </summary>
        public float ReferenceSimZ;
    }

    /// <summary>
    /// Terrain query result written by <c>TerrainQuerySolverSystem</c> (via <see cref="ITerrainProvider"/>).
    /// Parallel to <see cref="TerrainQueryRequest"/> by array index.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TerrainQueryResult
    {
        /// <summary>Terrain height at the queried XY position, in IG world space (metres).</summary>
        public float HitZ;

        /// <summary><c>true</c> when the terrain provider returned a valid hit; <c>false</c> on miss or OOB.</summary>
        public bool HasHit;
    }

    /// <summary>
    /// Zero-allocation singleton ECS component that batches per-frame terrain-height queries,
    /// following the <c>RaycastBatchData</c> / <c>PathfindingBatchData</c> pattern.
    ///
    /// <para>
    /// Allocated with <see cref="Allocator.Persistent"/> by <c>IgGroundClampingModule</c> at startup
    /// and disposed by the same module at shutdown. The world singleton is the authoritative owner
    /// of the backing native memory after <c>SetSingleton</c> is called.
    /// </para>
    /// </summary>
    [ComponentId(GeographicComponentIds.TerrainQueryBatchData)]
    public struct TerrainQueryBatchData
    {
        /// <summary>Default pre-allocated capacity for requests and results per frame.</summary>
        public const int DefaultCapacity = 64;

        /// <summary>Number of valid entries in <see cref="Requests"/> and <see cref="Results"/> this frame.</summary>
        public int Count;

        /// <summary>Pre-allocated request buffer. Length == <see cref="DefaultCapacity"/>.</summary>
        public NativeArray<TerrainQueryRequest> Requests;

        /// <summary>Pre-allocated result buffer. Length == <see cref="DefaultCapacity"/>.</summary>
        public NativeArray<TerrainQueryResult> Results;
    }
}
