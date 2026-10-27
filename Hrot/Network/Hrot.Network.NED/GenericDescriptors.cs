using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Hrot.NED.Common;

namespace Hrot.NED.Descriptors
{
    /// <summary>
    /// Reserved bits on <see cref="EntityMaster"/>.<c>Flags</c> (the "entity type specific flags"
    /// field). Generic, host-ignorable: a host that does not implement a bit behaves as if it were
    /// unset. <c>WaitForAcks</c> marks an entity created in reliable-init mode — a receiving peer
    /// must report its <c>Active</c> status so the creator can release the cross-node construction
    /// barrier (docs/DESIGN_Cross_Node_Construction_Barrier.md §2c/§3a.2).
    /// </summary>
    [Flags]
    public enum EntityMasterFlags : ulong
    {
        None = 0,
        WaitForAcks = 1UL << 0,

        /// <summary>⭐ LR-2 (Hrot docs/DESIGN_Visual_Effects.md §8.5, R-271) — a LIGHTWEIGHT entity (a replicated round): a receiver creates
        /// it ACTIVE on this sample — no ghost, no promotion, no lifecycle, no acks — keeps it current from <c>WorldPos</c>, and
        /// destroys it when this master is disposed.</summary>
        Lightweight = 1UL << 1,
    }

    // ===================================================================================
    // GENERAL PRINCIPLES: NED SST ENTITY DESCRIPTORS
    // ===================================================================================
    // An entity in the NED SST architecture is defined by the aggregation of its descriptors.
    // There is no single "Entity Object" transmitted over the network; instead, separate
    // DDS topics (Descriptors) share a common EntityId.
    //
    // Life Cycle:
    // - Existence is determined solely by the 'EntityMaster' descriptor.
    // - If EntityMaster is ALIVE, the entity exists.
    // - If EntityMaster is DISPOSED, the entity is deleted.
    //
    // Ownership:
    // - Ownership is granular per descriptor.
    // - The "Owner" is simply the last node that successfully wrote to the topic.
    // - To update a descriptor owned by someone else, use 'UpdateEntityDescriptorRequest'.
    // ===================================================================================

    /// <summary>
    /// Wire representation of a DIS Entity Type decomposed into its seven standard fields.
    /// Replacing the raw <c>ulong</c> makes each field legible to DDS monitoring tools
    /// (RTI Spy, Cyclone introspection, etc.) without changing the engine-side
    /// <c>Fdp.Core.DISEntityType</c> struct or any entity-query filter logic.
    /// </summary>
    [DdsStruct]
    [DdsTypeFormat("[{Kind:0:Number}:{Domain:0:Number}:{Country:0:Number}:{Category:0:Number}:{Subcategory:0:Number}:{Specific:0:Number}:{Extra:0:Number}]")]
	public partial struct DisTypeStruct
    {
        /// <summary>DIS Kind (byte). 1 = Platform, 2 = Munition, etc.</summary>
        public byte Kind;
        /// <summary>DIS Domain (byte). 1 = Land, 2 = Air, etc.</summary>
        public byte Domain;
        /// <summary>DIS Country (16-bit ISO country code).</summary>
        public ushort Country;
        /// <summary>DIS Category — e.g. Tank vs Truck.</summary>
        public byte Category;
        /// <summary>DIS Subcategory — e.g. M1A1 vs T-72.</summary>
        public byte Subcategory;
        /// <summary>DIS Specific variation.</summary>
        public byte Specific;
        /// <summary>DIS Extra field.</summary>
        public byte Extra;
    }
    // ===================================================================================
    // GENERAL PRINCIPLES: NED SST ENTITY DESCRIPTORS
    // ===================================================================================
    // An entity in the NED SST architecture is defined by the aggregation of its descriptors.
    // There is no single "Entity Object" transmitted over the network; instead, separate
    // DDS topics (Descriptors) share a common EntityId.
    //
    // Life Cycle:
    // - Existence is determined solely by the 'EntityMaster' descriptor.
    // - If EntityMaster is ALIVE, the entity exists.
    // - If EntityMaster is DISPOSED, the entity is deleted.
    //
    // Ownership:
    // - Ownership is granular per descriptor.
    // - The "Owner" is simply the last node that successfully wrote to the topic.
    // - To update a descriptor owned by someone else, use 'UpdateEntityDescriptorRequest'.
    // ===================================================================================

    // Represents the instance of an entity.
    // When this topic is created, the entity instance starts its life, entering a temporary "incomplete" state.
    // Entity instance becomes "complete" as soon as all mandatory descriptors for that entity get created.
    // What descriptors are to be expected for what entity type is defined just by convention (even different modules
    // within the same system might have different needs).
    // Once this one is removed, the entity instances ceases to exist no matter if its descriptors still exist.
    [DdsTopic("EntityMaster")]
    [DdsIdlFile("hrot-generic-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityMaster
    {
        // Entity instance id.
        // 0=invalid
        // All descriptor belonging to this entity share the same id.
        [DdsKey]
        public long EntityId;

        // Link to a detailed entity type database determining whatever entity characteristics are
        // needed for the simulation.
        // Integer unique index into our TKB database. Same as TkbEntityId in CGFX.
        // Must be correlated with DisType, it's just another way to specify the same entity type.
        // 0=invalid
        public long TkbType;

        // SISO-REF-010-2015 standardized, multiple-category-level type decomposed into
        // named fields so DDS monitoring tools can display each sub-field individually.
        // Must be correlated with TkbType, it's just another way to specify the same entity type.
        // All zeros = invalid.
        public DisTypeStruct DisType;

        // Entity type specific flags.
        // For example it might indicate whether the entity is using WorldPos
        // descriptor if the entity type allows for both.
        public ulong Flags;

        // ⭐ AQ107 (CE-3323, R-169 — APPENDED) — the creation request that made this entity (CreateEntityRequest.RequestId);
        // all zeros when no request did (a scenario load, a direct spawn). Lets a requester on ANY node tell its entity from the
        // rest: the receiver stamps it on the replica, and the lifecycle module copies it into EntityConstructionCompleted.
        public Guid RequestId;

        // IMPORTANT NOTE
        // The EntityMaster does NOT have OwnerId field. The owner id determined from sample metadata (sender id)
        // provided by the dds csharp bindings. Usually the last writer is the owner, according to the NED SST rules.
    }

    public enum eForceIdentifier
    {
        FORCE_UNKNOWN,
        FORCE_FRIENDLY,
        FORCE_OPPOSING,
        FORCE_NEUTRAL
    }

    /// <summary>
    /// DDS-side tactical designation enum.
    /// </summary>
    /// IMPORTANT: Must be kept in sync with Hrot.Core.CommandHierarchy.TacticalDesignation
    public enum eTacticalDesignation : ushort
    {
        Undefined   = 0,
        Commander   = 1,
        SquadLeader = 2,
        Wingman     = 3,
        Support     = 4,
        // ⭐ CE-3253 (BR-8, DA-19) — the rest of TacticalDesignation, value for value (a rail pins name and value): ⛔ before, TeamLeader and
        //   Medic crossed as raw numbers outside the enum, and the squad roles did not exist.
        TeamLeader  = 5,
        Medic       = 6,
        AutomaticRifleman = 7,
        MachineGunner = 8,
        Grenadier   = 9,
        Marksman    = 10,
        AntiTank    = 11,
    }

    // Additional metadata for an entity that is not strictly required for existence
    // but crucial for identification and hierarchy.
    [DdsTopic("EntityInfo")]
    [DdsIdlFile("hrot-generic-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct EntityInfo
    {
        // The Entity this info belongs to.
        [DdsKey]
        public long EntityId;

        // Human-readable name (e.g., "Alpha-1", "Bridge 404").
        // Used for display labels in ExCon/Map.
        public string Name;

        // Affiliation/Side (Friendly, Hostile, Neutral).
        public eForceIdentifier ForceIdentifier;

        // The Hierarchical Parent (ORBAT).
        // 0 = This entity is a Root unit / Taskforce.
        // >0 = EntityId of the parent unit this entity is attached/subordinate to.
        public long CommanderId;

        // Logical role of this entity within the commander's unit.
        // Undefined = no designation or no commander.
        public eTacticalDesignation TacticalDesignation;

        // ⭐ CE-2281 / R-3019 — the fire team within the commander's unit (Fdp.Core.CommandHierarchy.FireTeam: 0 none, 1 A … 4 D).
        //   APPENDED: an older sample reads as no team.
        public byte FireTeam;
    }

    // Optimistic Concurrency Control Sidecar.
    // Stores the "Version Number" for a specific descriptor.
    // Principle:
    // - Updated by the Owner whenever the descriptor data changes.
    // - Checked by the Owner when receiving UpdateEntityDescriptorRequest.
    // - Ensures no "Lost Updates" in multi-user editing scenarios.
    // NOT for every descriptor, on demand only! Used just where optimistic locking is needed.
    [DdsTopic("DescriptorOptimisticLock")]
    [DdsIdlFile("hrot-generic-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct DescriptorOptimisticLock
    {
        // Key 1: Entity ID.
        [DdsKey]
        public long EntityId;

        // Key 2: Descriptor Type (e.g. 10=GeoSpatial).
        // Versioning is granular per descriptor type.
        [DdsKey]
        public int DescriptorType;

        // The current version number.
        // Monotonically increasing.
        public int CurrentVersion;

        // Diagnostic: Owner Id of the last writer.
        public NodeId LastWriter;
    }
}
