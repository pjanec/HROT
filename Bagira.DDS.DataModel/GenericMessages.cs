using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Bagira.DDS.DM;
using Bagira.BDC.SSTD;

namespace Bagira.BDC.SSTM
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

    /*
    **Error Codes** :

    | Code | Name                              | Description                    |
    | ---- | --------------------------------- | ------------------------------ |
    | 0    | SUCCESS                           | Operation completed            |
    | 1    | ERR_UNKNOWN_DESCRIPTOR_TYPE       | Descriptor type not supported  |
    | 2    | ERR_ENTITY_NOT_FOUND              | EntityMaster not ALIVE         |
    | 3    | ERR_DESCRIPTOR_INSTANCE_NOT_FOUND | Instance ID invalid            |
    | 4    | ERR_NOT_OWNER                     | Request reached non-owner      |
    | 5    | ERR_VALIDATION_FAILED             | Invalid value/state transition |
    | 6    | ERR_NOT_SUPPORTED                 | Descriptor updates forbidden   |
    | 7    | ERR_VERSION_CONFLICT              | currentVersion mismatch        |
    */

    // Message to change the ownership of a descriptor.
    // Used when a node wants to gracefully hand over control of a specific component
    // (e.g., simulation physics) to another node without deleting/recreating data.
    [DdsTopic("OwnershipUpdate")]
    [DdsIdlFile("bdc-sst-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct OwnershipUpdate
    {
        // The unique ID of the entity instance affected.
        [DdsKey]
        public int EntityId;

        // Unique ID of the descriptor type (e.g., 0=Master, 10=GeoSpatial).
        [DdsKey]
        public int DescrTypeId;

        // ID of the descriptor instance.
        // Non-zero if there are multiple descriptors of the same type per entity instance
        // (e.g., multiple radios, multiple engines).
        // Zero if there is just one descriptor of that type per entity instance.
        [DdsKey]
        public int DescrInstanceId;

        // The NodeId of the participant taking over ownership.
        // The current owner monitors this; if it matches 'NewOwner', it stops publishing.
        // The new owner monitors this; if it matches 'NewOwner', it starts publishing.
        public NodeId NewOwner;
    }

    // ===================================================================================
    // GENERIC REQUESTS (SST UTILITIES)
    // ===================================================================================

    // CreateEntityRequest is for asking SOMEONE ELSE to create the entity, to simplify the sender
    // if the sender is NOT the intended owner of the new entity.
    [DdsTopic("CreateEntityRequest")]
    [DdsIdlFile("bdc-sst-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct CreateEntityRequest
    {
        public Guid RequestId;

        // who should own the entity created; {0}=default owner
        public NodeId Owner;

        // Extra info on how to create the entity; might be owner specific; 0=default
        // For example, if the entity is intended to be saved to scenario or something.
        public long Flags;

        // Sequence of initial descriptors to populate the new entity.
        // Note: The EntityId inside these descriptors will be ignored/overwritten 
        // by the CGF with the newly allocated ID.
        [DdsManaged]
        public List<EntityDescriptorUnion> InitialDescriptors;
    }

    // Acknowledgment for entity creation.
    [DdsTopic("CreateEntityAck")]
    [DdsIdlFile("bdc-sst-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct CreateEntityAck
    {
        public Guid RequestId;

        // The newly allocated Entity ID (if successful).
        public int NewEntityId;

        // 0 = success
        public int ErrorCode;
    }

    // Request to update a descriptor owned by another node.
    // SST Standard: "If you don't own it, request it."
    [DdsTopic("UpdateEntityDescriptorRequest")]
    [DdsIdlFile("bdc-sst-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct UpdateEntityDescriptorRequest
    {
        public Guid RequestId;

        // The Entity ID to update.
        public int EntityId;

        // The specific Descriptor Type (e.g. 10=GeoSpatial).
        public EDescriptorType DescriptorType;

        // Secondary ID (for multi-instance descriptors like Articulated Parts).
        public int PartId;

        // Optimistic Locking Version.
        // "I believe the current version is X. If it is > X, fail this request."
        public int CurrentVersion;

        // The new data payload.
        public EntityDescriptorUnion Payload;
    }

    // Acknowledgment for update requests.
    [DdsTopic("UpdateEntityDescriptorAck")]
    [DdsIdlFile("bdc-sst-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct UpdateEntityDescriptorAck
    {
        public Guid RequestId;

        public int EntityId;

        // 0 = success
        public int ErrorCode;
    }

    // All possible entity attributes we can change (if the entity supports it).
    public enum EntityAttribute
    {
        eaName,
        eaGeoPosition
    }

    // Unified attribute payload - what data we provide for the attrinute
    [DdsUnion]
    [DdsIdlFile("bdc-sst-generic-msgs")]
    [DdsManaged]
    public partial struct EntityAttributePayload
    {
        [DdsDiscriminator]
        public EntityAttribute _d;

        [DdsCase(EntityAttribute.eaName)]
        public string Name;

        [DdsCase(EntityAttribute.eaGeoPosition)]
        public GeoPosition GeoPosition;
    }

    // SST-compliant entity single field update request
    // GENERIC - reusable across any SST application (map, radio, weather, etc.)
    // Used ONLY for updating attributes indescriptors owned by other participants
    // For own descriptors: write directly to DDS topics
    // Acknowledged by CreateUpdateDeleteEntityAck 
    [DdsTopic("UpdateEntityAttributeRequest")]
    [DdsIdlFile("bdc-sst-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct UpdateEntityAttributeRequest
    {
        public Guid RequestId;

        public int EntityId;

        public EntityAttribute AttributeId; // what field to change

        public EntityAttributePayload Payload;
    }

    // Acknowledgment for entity creation, descriptor update, deletion, attribute update.
    [DdsTopic("CreateUpdateDeleteEntityAck")]
    [DdsIdlFile("bdc-sst-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct CreateUpdateDeleteEntityAck
    {
        public Guid RequestId;

        // 0 = success
        public int ErrorCode;
    }
}
