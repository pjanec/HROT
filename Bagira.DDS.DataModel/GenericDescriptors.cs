using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Bagira.DDS.DM;

namespace Bagira.BDC.SSTD
{
    // ===================================================================================
    // GENERAL PRINCIPLES: BDC SST ENTITY DESCRIPTORS
    // ===================================================================================
    // An entity in the BDC SST architecture is defined by the aggregation of its descriptors.
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
    [DdsIdlFile("bdc-sst-generic-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityMaster
    {
        // Entity instance id.
        // 0=invalid
        // All descriptor belonging to this entity share the same id.
        [DdsKey]
        public int EntityId;

        // Link to a detailed entity type database determining whatever entity characteristics are
        // needed for the simulation.
        // Integer unique index into our TKB database. Same as TkbEntityId in CGFX.
        // Must be correlated with DisType, it's just another way to specify the same entity type.
        // 0=invalid
        public long TkbType;

        // SISO-REF-010-2015 standardized, multiple-category-level type.
        // Must be correlated with TkbType, it's just another way to specify the same entity type.
        // All zeros = invalid.
        public ulong DisType;

        // Entity type specific flags.
        // For example it might indicate whether the entity is using GeoSpatial or GeoSpatialDR
        // descriptor if the entity type allows for both.
        public ulong Flags;

        // IMPORTANT NOTE
        // The EntityMaster does NOT have OwnerId field. The owner id determined from sample metadata (sender id)
        // provided by the dds csharp bindings. Usually the last writer is the owner, according to the BDC SST rules.
    }

    public enum eForceIdentifier
    {
        FORCE_UNKNOWN,
        FORCE_FRIENDLY,
        FORCE_OPPOSING,
        FORCE_NEUTRAL
    }

    // Additional metadata for an entity that is not strictly required for existence
    // but crucial for identification and hierarchy.
    [DdsTopic("EntityInfo")]
    [DdsIdlFile("bdc-sst-generic-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct EntityInfo
    {
        // The Entity this info belongs to.
        [DdsKey]
        public int EntityId;

        // Human-readable name (e.g., "Alpha-1", "Bridge 404").
        // Used for display labels in IOS/Map.
        public string Name;

        // Affiliation/Side (Friendly, Hostile, Neutral).
        public eForceIdentifier ForceIdentifier;

        // The Hierarchical Parent (ORBAT).
        // 0 = This entity is a Root unit / Taskforce.
        // >0 = EntityId of the parent unit this entity is attached/subordinate to.
        public int CommanderId;
    }

    // Optimistic Concurrency Control Sidecar.
    // Stores the "Version Number" for a specific descriptor.
    // Principle:
    // - Updated by the Owner whenever the descriptor data changes.
    // - Checked by the Owner when receiving UpdateEntityDescriptorRequest.
    // - Ensures no "Lost Updates" in multi-user editing scenarios.
    // NOT for every descriptor, on demand only! Used just where optimistic locking is needed.
    [DdsTopic("DescriptorOptimisticLock")]
    [DdsIdlFile("bdc-sst-generic-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct DescriptorOptimisticLock
    {
        // Key 1: Entity ID.
        [DdsKey]
        public int EntityId;

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
