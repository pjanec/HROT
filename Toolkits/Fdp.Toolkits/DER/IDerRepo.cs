using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.DER
{
    /// <summary>
    /// Non-ECS entity repository for ExCon Mock.
    /// Thread-safe dictionary-based storage.
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
