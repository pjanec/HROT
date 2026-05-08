using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Hrot.NED.Common;
using Hrot.NED.Descriptors;

namespace Hrot.NED.Messages
{
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

    // NedStatusCode has been moved to Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs
    // (namespace Hrot.NED.Messages). All existing usages remain valid without change.

    // Message to change the ownership of a descriptor.
    // Used when a node wants to gracefully hand over control of a specific component
    // (e.g., simulation physics) to another node without deleting/recreating data.
    [DdsTopic("OwnershipUpdate")]
    [DdsIdlFile("hrot-generic-msgs")]
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
    // ATTR2 BINARY CONTRACT: ATTRIBUTE WIRE TYPES
    // ===================================================================================
    // These types implement the binary DDS wire contract described in ATTR2-DESIGN.md §3.1.
    // They carry strongly-typed attribute values without JSON parsing on the receiving host.
    // ===================================================================================

    /// <summary>
    /// Discriminator tag for the active branch of <see cref="AttributeValueUnion"/>.
    /// Nine scalar/vector types are supported to cover all well-known attribute schemas.
    /// </summary>
    public enum AttributeValueType : int
    {
        /// <summary>32-bit signed integer.</summary>
        KindInt32 = 0,
        /// <summary>64-bit signed integer.</summary>
        KindInt64 = 1,
        /// <summary>32-bit IEEE 754 floating-point.</summary>
        KindFloat32 = 2,
        /// <summary>64-bit IEEE 754 floating-point.</summary>
        KindFloat64 = 3,
        /// <summary>Boolean value.</summary>
        KindBool = 4,
        /// <summary>UTF-8 string.</summary>
        KindString = 5,
        /// <summary>3-component single-precision vector (x, y, z).</summary>
        KindVec3f = 6,
        /// <summary>3-component double-precision vector (x, y, z).</summary>
        KindVec3d = 7,
        /// <summary>4-component single-precision vector (x, y, z, w).</summary>
        KindVec4f = 8,
    }

    /// <summary>
    /// Tagged-union value container for a single attribute value transmitted over DDS.
    /// The <see cref="ValueType"/> discriminator (annotated with <c>[DdsDiscriminator]</c>)
    /// identifies which branch is active; each branch is annotated with the corresponding
    /// <c>[DdsCase]</c> value so CycloneDDS generates a valid IDL union definition.
    /// Marked <c>[DdsManaged]</c> because the <see cref="StringValue"/> branch holds a
    /// managed <c>string</c> reference — an explicit layout is therefore not possible.
    /// See ATTR2-DESIGN.md §3.1.
    /// </summary>
    [DdsUnion]
    [DdsIdlFile("hrot-generic-msgs")]
    [DdsManaged]
    public partial struct AttributeValueUnion
    {
        /// <summary>Discriminator: identifies which value field is populated.</summary>
        [DdsDiscriminator]
        public AttributeValueType ValueType;

        /// <summary>Active when <see cref="ValueType"/> == <see cref="AttributeValueType.KindInt32"/>.</summary>
        [DdsCase(AttributeValueType.KindInt32)]
        public int IntValue;

        /// <summary>Active when <see cref="ValueType"/> == <see cref="AttributeValueType.KindInt64"/>.</summary>
        [DdsCase(AttributeValueType.KindInt64)]
        public long LongValue;

        /// <summary>Active when <see cref="ValueType"/> == <see cref="AttributeValueType.KindFloat32"/>.</summary>
        [DdsCase(AttributeValueType.KindFloat32)]
        public float FloatValue;

        /// <summary>Active when <see cref="ValueType"/> == <see cref="AttributeValueType.KindFloat64"/>.</summary>
        [DdsCase(AttributeValueType.KindFloat64)]
        public double DoubleValue;

        /// <summary>Active when <see cref="ValueType"/> == <see cref="AttributeValueType.KindBool"/>.</summary>
        [DdsCase(AttributeValueType.KindBool)]
        public bool BoolValue;

        /// <summary>Active when <see cref="ValueType"/> == <see cref="AttributeValueType.KindString"/>.</summary>
        [DdsCase(AttributeValueType.KindString)]
        [DdsManaged]
        public string? StringValue;

        /// <summary>
        /// Active when <see cref="ValueType"/> == <see cref="AttributeValueType.KindVec3f"/>.
        /// 3-component single-precision vector. Value type — no heap allocation.
        /// </summary>
        [DdsCase(AttributeValueType.KindVec3f)]
        public Vec3f Vec3fValue;

        /// <summary>
        /// Active when <see cref="ValueType"/> == <see cref="AttributeValueType.KindVec3d"/>.
        /// 3-component double-precision vector. Value type — no heap allocation.
        /// </summary>
        [DdsCase(AttributeValueType.KindVec3d)]
        public Vec3d Vec3dValue;

        /// <summary>
        /// Active when <see cref="ValueType"/> == <see cref="AttributeValueType.KindVec4f"/>.
        /// 4-component single-precision vector. Value type — no heap allocation.
        /// </summary>
        [DdsCase(AttributeValueType.KindVec4f)]
        public Vec4f Vec4fValue;
    }

    /// <summary>
    /// The wire atom for a single binary attribute update.
    /// Encodes one typed value identified by a 16-bit attribute ID and two optional
    /// sub-index fields (e.g. list index, part slot).
    /// See ATTR2-DESIGN.md §3.1.
    /// </summary>
    [DdsStruct]
    [DdsIdlFile("hrot-generic-msgs")]
    public partial struct AttributeRecord
    {
        /// <summary>Well-known attribute schema ID — see <c>FDP.Toolkit.Replication.Patching.AttributeIds</c>.</summary>
        public ushort AttributeId;

        /// <summary>First optional sub-index (e.g. list position). Zero when not used.</summary>
        public short SubIndex1;

        /// <summary>Second optional sub-index (e.g. nested list position). Zero when not used.</summary>
        public short SubIndex2;

        /// <summary>The typed value payload. The active branch is identified by <see cref="AttributeValueUnion.ValueType"/>.</summary>
        [DdsManaged]
        public AttributeValueUnion Value;
    }

    // ===================================================================================
    // GENERIC REQUESTS (SST UTILITIES)
    // ===================================================================================

    // CreateEntityRequest is for asking SOMEONE ELSE to create the entity, to simplify the sender
    // if the sender is NOT the intended owner of the new entity.
    [DdsTopic("CreateEntityRequest")]
    [DdsIdlFile("hrot-generic-msgs")]
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

        // Optional fine-grained attribute overrides applied AFTER TKB defaults and
        // InitialDescriptors have been processed.  Expressed as a JSON object whose
        // keys are property paths (e.g. { "Name": "Alpha", "GeoPosition": { ... } }).
        // Processed by JsonAttributeCompiler — see ATTR-DESIGN.md §3.
        [DdsManaged]
        public string? InitialAttributesJson;

        /// <summary>
        /// Optional binary attribute overrides applied alongside <see cref="InitialAttributesJson"/>.
        /// Each <see cref="AttributeRecord"/> carries a strongly-typed value keyed by a 16-bit
        /// attribute ID, eliminating JSON parsing on the receiving host.
        /// See ATTR2-DESIGN.md §3.1.
        /// </summary>
        [DdsManaged]
        public List<AttributeRecord>? InitialAttributeRecords;
    }

    // Request to delete an entity; issued by non-owning nodes.
    [DdsTopic("DeleteEntityRequest")]
    [DdsIdlFile("hrot-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct DeleteEntityRequest
    {
        public Guid RequestId;
        public int EntityId;
    }

    // Request to update a descriptor owned by another node.
    // SST Standard: "If you don't own it, request it."
    [DdsTopic("UpdateEntityDescriptorRequest")]
    [DdsIdlFile("hrot-generic-msgs")]
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
    [DdsIdlFile("hrot-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct UpdateEntityDescriptorAck
    {
        public Guid RequestId;

        public int EntityId;

        // 0 = success
        public int ErrorCode;
    }

    // SST-compliant entity single field update request
    // GENERIC - reusable across any SST application (map, radio, weather, etc.)
    // Used ONLY for updating attributes indescriptors owned by other participants
    // For own descriptors: write directly to DDS topics
    // Acknowledged by CreateUpdateDeleteEntityAck 
    [DdsTopic("UpdateEntityAttributeRequest")]
    [DdsIdlFile("hrot-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct UpdateEntityAttributeRequest
    {
        public Guid RequestId;

        public int EntityId;

        // Hierarchical JSON attribute patch, e.g. {"Name":"Bravo-2"} or
        // {"Weapons":{"0":{"Ammo":{"Count":5}}}}.  Processed by JsonAttributeCompiler
        // using the same routing table as CreateEntityRequest.InitialAttributesJson.
        [DdsManaged]
        public string AttributePatchJson;

        // When true, the processing node must send a CreateUpdateDeleteEntityAck after applying
        // the patch.  When false (default), the message is fire-and-forget.
        public bool RequireAck;

        /// <summary>
        /// Optional binary attribute patch applied alongside <see cref="AttributePatchJson"/>.
        /// Each <see cref="AttributeRecord"/> carries a strongly-typed value keyed by a 16-bit
        /// attribute ID, eliminating JSON parsing on the receiving host.
        /// See ATTR2-DESIGN.md §3.1.
        /// </summary>
        [DdsManaged]
        public List<AttributeRecord>? AttributeRecords;
    }

    // Unified acknowledgment for entity creation, descriptor update, and deletion.
    [DdsTopic("CreateUpdateDeleteEntityAck")]
    [DdsIdlFile("hrot-generic-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct CreateUpdateDeleteEntityAck
    {
        public Guid RequestId;

        /// <summary>The affected entity. For creation requests this holds the newly allocated ID.</summary>
        public int EntityId;

        /// <summary>Maps to NedStatusCode: 0=Success, 1=InProgress, >=2=Error.</summary>
        public int StatusCode;

        /// <summary>Identifies which node is sending this acknowledgment.</summary>
        public NodeId RespondingNode;

        /// <summary>
        /// Optional 32-byte engine-specific execution receipt (256 bits).
        /// FDP nodes pack a component-mutation bitmask here: bit N is set when ECS component
        /// type ID N was authorised and written during this request.
        /// Non-FDP nodes may use this field for custom flags or leave it null/zeroed.
        /// Serialized as an unbounded DDS octet sequence.
        /// </summary>
        public byte[] OpaqueData;
    }

    // ===================================================================================
    // ENTITY ATTRIBUTE SCHEMA (GZ052)
    // ===================================================================================

    // One record per SimHost instance. Keyed by NodeId.
    // Carries the full JSON schema of entity attributes supported by this node's JsonAttributeCompiler.
    // TransientLocal: late-joining ExCon subscribers immediately receive the latest published schema.
    [DdsTopic("EntityAttributeSchema")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct EntityAttributeSchema
    {
        [DdsKey]
        public int NodeId;

        // JSON Schema document describing all attribute paths, types, and validation rules
        // known to this node's JsonAttributeCompiler instance.
        // Compatible with JSON Schema Draft-07 subset (same as StructEdit's EditDocument format).
        [DdsManaged] public string SchemaJson;
    }
}
