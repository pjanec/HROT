using System;
using Fdp.Core;

namespace Fdp.Toolkit.Lifecycle.Events
{
    /// <summary>
    /// Published when an entity begins construction.
    /// Modules should initialize their components and respond with ConstructionAck.
    /// </summary>
    [EventId(9001)]
    public struct ConstructionOrder
    {
        /// <summary>
        /// Entity being constructed.
        /// </summary>
        public Entity Entity;
        
        /// <summary>
        /// Entity blueprint ID (for modules to decide if they care).
        /// </summary>
        public long BlueprintId;
        
        /// <summary>
        /// Frame number when construction started.
        /// </summary>
        public uint FrameNumber;
        
        /// <summary>
        /// Optional: Initiating module ID (who spawned it).
        /// </summary>
        public int InitiatorModuleId;
    }
    
    /// <summary>
    /// Response from a module indicating it has initialized the entity.
    /// </summary>
    [EventId(9002)]
    public struct ConstructionAck
    {
        /// <summary>
        /// Entity that was initialized.
        /// </summary>
        public Entity Entity;
        
        /// <summary>
        /// Module ID that completed initialization.
        /// </summary>
        public int ModuleId;
        
        /// <summary>
        /// Optional: Success flag (allows modules to report initialization failure).
        /// </summary>
        public bool Success;
        
        /// <summary>
        /// Optional: Error message if Success == false.
        /// </summary>
        public FixedString64 ErrorMessage;
    }
    
    /// <summary>
    /// Published when an entity begins teardown.
    /// Modules should cleanup their state and respond with DestructionAck.
    /// </summary>
    [EventId(9003)]
    public struct DestructionOrder
    {
        /// <summary>
        /// Entity being destroyed.
        /// </summary>
        public Entity Entity;
        
        /// <summary>
        /// Frame number when destruction started.
        /// </summary>
        public uint FrameNumber;
        
        /// <summary>
        /// Optional: Reason for destruction (debug info).
        /// </summary>
        public FixedString64 Reason;
    }
    
    /// <summary>
    /// Response from a module indicating it has cleaned up the entity.
    /// </summary>
    [EventId(9004)]
    public struct DestructionAck
    {
        /// <summary>
        /// Entity that was cleaned up.
        /// </summary>
        public Entity Entity;
        
        /// <summary>
        /// Module ID that completed cleanup.
        /// </summary>
        public int ModuleId;
        
        /// <summary>
        /// Optional: Success flag.
        /// </summary>
        public bool Success;
    }

    /// <summary>
    /// ⭐ AQ107 (CE-3323) — an entity the lifecycle module was constructing is DONE: it went Active (<see cref="Succeeded"/>), or its
    /// construction failed or timed out and it was removed. Published by <see cref="EntityLifecycleModule"/> on EVERY node it runs on
    /// (owner and replicas), the frame the outcome is decided. ⚠ Only entities that go through the lifecycle module (spawned or promoted
    /// network entities) — a plain <c>CreateEntity</c> never raises it; that is why the name says <i>Construction</i>.
    /// 📄 docs/DESIGN_Entity_Creation_Unification.md §3.4e.
    /// </summary>
    [EventId(9005)]
    public struct EntityConstructionCompleted
    {
        /// <summary>The entity, on this node. ⚠ When <see cref="Succeeded"/> is false it is already being destroyed.</summary>
        public Entity Entity;
        /// <summary>Its cluster-wide id (<c>NetworkIdentity</c>); 0 when it has none.</summary>
        public long NetworkId;
        /// <summary>Its TKB type (the construction's blueprint id).</summary>
        public long TkbType;
        /// <summary>The creation request that made it (<c>CreationRequestId</c>); <see cref="Guid.Empty"/> when none did.</summary>
        public Guid RequestId;
        /// <summary>This node owns it (<c>NetworkAuthority.HasAuthority</c>); true when it carries no authority at all.</summary>
        public bool IsLocallyOwned;
        /// <summary>It went Active. False: a module refused it or the handshake timed out (<see cref="Failure"/>).</summary>
        public bool Succeeded;
        /// <summary>Re-opened after the world was put back (a restore re-derives in-flight constructions), not a new entity.</summary>
        public bool Resumed;
        /// <summary>Why it failed (empty on success).</summary>
        public FixedString64 Failure;
        /// <summary>The frame the outcome was decided.</summary>
        public uint FrameNumber;
    }

    /// <summary>
    /// ⭐ AQ107 (CE-3323) — an entity the lifecycle module was tearing down is GONE: every module acked (or the teardown timed out and
    /// was forced). Published by <see cref="EntityLifecycleModule"/> on every node it runs on, the frame it is destroyed. ⭐ Carries what
    /// a subscriber needs, because the handle is dead by the time the event is read. ⚠ Lifecycle-managed entities only (see
    /// <see cref="EntityConstructionCompleted"/>). 📄 docs/DESIGN_Entity_Creation_Unification.md §3.4e.
    /// </summary>
    [EventId(9006)]
    public struct EntityDestructionCompleted
    {
        /// <summary>The entity's handle on this node — dead when the event is read; for matching only.</summary>
        public Entity Entity;
        public long NetworkId;
        public long TkbType;
        public Guid RequestId;
        public bool IsLocallyOwned;
        /// <summary>A module never acked: the teardown timed out and the entity was deleted anyway.</summary>
        public bool Forced;
        /// <summary>The <see cref="DestructionOrder.Reason"/> it was torn down for.</summary>
        public FixedString64 Reason;
        public uint FrameNumber;
    }
}
