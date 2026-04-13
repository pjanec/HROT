using System.Numerics;
using Fdp.Kernel;
using Microsoft.Extensions.Logging;

namespace Hrot.ClusterRunner.Testing
{
    /// <summary>
    /// Creates a new entity in the ECS world at the given position.
    /// Args: <c>x</c>, <c>y</c>, <c>z</c> (double) — world-space position in metres.
    /// Returns: <c>{"entity_id": N}</c>.
    /// </summary>
    public sealed class SpawnActionHandler : ITestActionHandler
    {
        private readonly EntityRepository? _world;
        private readonly ILogger _log;

        public string ActionName => "spawn";

        public SpawnActionHandler(EntityRepository? world, ILogger log)
        {
            _world = world;
            _log   = log;
        }

        public Task<object?> ExecuteAsync(Dictionary<string, object> args)
        {
            if (_world == null)
            {
                _log.LogWarning("spawn: no EntityRepository available — skipping.");
                return Task.FromResult<object?>(new Dictionary<string, object> { ["entity_id"] = -1 });
            }

            float x = (float)(args.TryGetValue("x", out var vx) ? Convert.ToDouble(vx) : 0.0);
            float y = (float)(args.TryGetValue("y", out var vy) ? Convert.ToDouble(vy) : 0.0);
            float z = (float)(args.TryGetValue("z", out var vz) ? Convert.ToDouble(vz) : 0.0);

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(x, y, z),
                Rotation = Quaternion.Identity
            });

            _log.LogDebug("spawn: created entity {Id} at ({X},{Y},{Z})", entity.Index, x, y, z);
            return Task.FromResult<object?>(new Dictionary<string, object> { ["entity_id"] = entity.Index });
        }
    }

    /// <summary>
    /// Teleports an existing entity to a new position.
    /// Args: <c>entity_id</c> (int), <c>x</c>, <c>y</c>, <c>z</c> (double).
    /// Returns: <c>{"moved": 1}</c> on success, <c>{"moved": 0}</c> if component absent.
    /// </summary>
    public sealed class MoveActionHandler : ITestActionHandler
    {
        private readonly EntityRepository? _world;
        private readonly ILogger _log;

        public string ActionName => "move";

        public MoveActionHandler(EntityRepository? world, ILogger log)
        {
            _world = world;
            _log   = log;
        }

        public Task<object?> ExecuteAsync(Dictionary<string, object> args)
        {
            if (_world == null)
            {
                _log.LogWarning("move: no EntityRepository available — skipping.");
                return Task.FromResult<object?>(new Dictionary<string, object> { ["moved"] = 0 });
            }

            int entityIdx = args.TryGetValue("entity_id", out var ve) ? Convert.ToInt32(ve) : -1;
            float x = (float)(args.TryGetValue("x", out var vx) ? Convert.ToDouble(vx) : 0.0);
            float y = (float)(args.TryGetValue("y", out var vy) ? Convert.ToDouble(vy) : 0.0);
            float z = (float)(args.TryGetValue("z", out var vz) ? Convert.ToDouble(vz) : 0.0);

            var entity = _world.GetEntityByIndex(entityIdx);
            if (!_world.HasComponent<SimTransform>(entity))
            {
                _log.LogWarning("move: entity {Id} has no SimTransform — cannot move.", entityIdx);
                return Task.FromResult<object?>(new Dictionary<string, object> { ["moved"] = 0 });
            }

            ref var transform = ref _world.GetComponentRW<SimTransform>(entity);
            transform.Position = new Vector3(x, y, z);

            _log.LogDebug("move: entity {Id} → ({X},{Y},{Z})", entityIdx, x, y, z);
            return Task.FromResult<object?>(new Dictionary<string, object> { ["moved"] = 1 });
        }
    }

    /// <summary>
    /// Reads the <see cref="SimTransform.Position"/> of an entity for assertion.
    /// Args: <c>entity_id</c> (int).
    /// Returns: <c>{"x": F, "y": F, "z": F}</c>.
    /// </summary>
    public sealed class AssertPositionActionHandler : ITestActionHandler
    {
        private readonly EntityRepository? _world;
        private readonly ILogger _log;

        public string ActionName => "assert_position";

        public AssertPositionActionHandler(EntityRepository? world, ILogger log)
        {
            _world = world;
            _log   = log;
        }

        public Task<object?> ExecuteAsync(Dictionary<string, object> args)
        {
            if (_world == null)
            {
                _log.LogWarning("assert_position: no EntityRepository available.");
                return Task.FromResult<object?>(null);
            }

            int entityIdx = args.TryGetValue("entity_id", out var ve) ? Convert.ToInt32(ve) : -1;
            var entity = _world.GetEntityByIndex(entityIdx);

            if (!_world.HasComponent<SimTransform>(entity))
            {
                _log.LogWarning("assert_position: entity {Id} has no SimTransform.", entityIdx);
                return Task.FromResult<object?>(null);
            }

            var transform = _world.GetComponent<SimTransform>(entity);
            _log.LogDebug("assert_position: entity {Id} at ({X},{Y},{Z})",
                entityIdx, transform.Position.X, transform.Position.Y, transform.Position.Z);

            return Task.FromResult<object?>(new Dictionary<string, object>
            {
                ["x"] = (double)transform.Position.X,
                ["y"] = (double)transform.Position.Y,
                ["z"] = (double)transform.Position.Z
            });
        }
    }
}
