using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.DER
{
    /// <summary>
    /// The DER mirror: a non-ECS (world-less) entity repository, thread-safe and dictionary-based, filled by DDS ingress handlers and read by
    /// <c>DerEntityInspectorPanel</c>. ⭐ Kept on purpose (user, 2026-10-29: the DER inspector stays; AQ109 §5a/§5b makes it a generic wire inspector).
    /// ⛔ Its first user, the ExCon node, is retired (R-3067).
    /// </summary>
    public interface IDerRepo
    {
        /// <summary>
        /// Local DDS node ID of the application.
        /// </summary>
        int LocalNodeId { get; }

        /// <summary>
        /// Retrieve entity by ID. Returns null if not found.
        /// </summary>
        IDerEntity? GetEntity(int entityId);
        
        /// <summary>
        /// Get all entities currently in repository.
        /// </summary>
        IEnumerable<IDerEntity> GetAllEntities();
        
        /// <summary>
        /// Create new entity with specified ID and TKB type.
        /// Throws if entity ID already exists.
        /// </summary>
        IDerEntity CreateEntity(int entityId, long tkbType);
        
        /// <summary>
        /// Delete entity by ID. No-op if entity doesn't exist.
        /// </summary>
        void DeleteEntity(int entityId);
        
        /// <summary>
        /// Raised when new entity is created.
        /// </summary>
        event Action<IDerEntity> EntityCreated;

        /// <summary>
        /// Raised when entity is deleted.
        /// </summary>
        event Action<IDerEntity> EntityDeleted;
    }
}
