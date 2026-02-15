# Entities made of descriptors

An entity is composed of individual descriptors and nothing else but the descriptors.

Descriptor is a DDS network trasferred data structure. Each descriptor has its own dedicated DDS topic.

Different entity types may require different set of descritors.

What concrete descriptors are needed for what type of entity is defined by convention - an agreement among the applications using such entity type.

Some descriptor types can come in multiple instances per entity. Each per-entity instance has its descriptor instance id which is unique per entity id.

EntityMaster descriptor is mandatory for all entity types. EntityMaster descriptor defines crucial info like entity id and type.
 
Once descriptor is created for an entity it is expected to live as long as the entity lives (e.g. descriptors cannot be deleted from a live entity). See also entity life cycle.

# Descriptors as DDS topic instances

Descriptors are communicated as instances of usual DDS Topics. Each descriptor type uses different dds topic.

Each descriptor data structure begins with entity id field, making it bound to a concrete entity instace.

	struct SomeDescriptor
	{
		long EntityId;

		unsigned long long SomeNumber;
		Vector3  SomeVector;
	};


For a multi-instance-per-entity descriptors the descriptor instance id is stored as the second field right after the entity id. The name of this field is not important.
    
    struct SomeDescriptor
    {
		long EntityId;
		long InstanceId;

		Vector3f SomeData;
    };

Descriptor topics should be set to KeepLast 1 so that dds_read_instance always return just a single sample.


# Entity life cycle

EntityMaster descriptor controls the life of the entity instance. If entity master instance is not present on the network, the entity instance does not exist. If entity master instance is present, the entity instance exists.

Entity might exist even while some of its descriptors are not available.

Entity never exists without an EntityMaster descriptor.

## Disposal
When EntityMaster is disposed, the entity is considered deleted regardless of its other descriptors.

When a non-EntityMaster descriptor is disposed, the ownership of it is simply returned to the current owner of EntityMaster.

In case the owner of EntityMaster is disposing other descriptors then it is assumed that the entity is being deleted.

In summary:

 - Master disposed. Same as entity deleted. Results in entity deleted callbacks being invoked on all alive nodes. Entity must be created anew if dispose was result of a crash.
 - Non-master disposed by "partial" owner. Returns ownership to default (master) owner. Master's owner should detect this dispose and should take ownership in the same way as if `UpdateOwnership` message was received. Other nodes should ignore this dispose message.
   * This mechanism is there to solve a node quitting/crashing.
   * If you want to change ownership you should use `UpdateOwnership` message.
 - Non-master disposed by master's owner. Assume entity is being deleted. Nodes should ignore this dispose message and wait for master disposed message which is expected to come.

Note:
 * The rules above virtually prevent the deletion of a descriptor from a living (non disposed) entity.


# Mandatory and optional descriptors

Descriptors are optional in general. Some descriptors for certain types of entities might be considered mandatory because they carry information which is crucial for the app.

Which descriptors are mandatory is a matter of convention agreed among a set of applications.

Your app may decide to wait for necessary descriptors before considering the entity "completely created".

# Entity ID
Each entity ID must be unique (obviously).

Entity IDs are allocated centrally using a DDS-based id allocator server. The server is multi-drill capable, allocating the IDs independently for each execise session (drill) that is running in parallel. I.e. just a single server instance is needed for a site.

# Entity type

Entity type helps organizing the entity into categories. It's meaning is defined by an agreement among the apps using the entities.

The type can serve for
 - Quick filtering of entities to handle.
 - Determining the set of descriptors to expect for such entity type.

There are multiple flavors of entity type present in the EntityMaster descriptor; neither of them is mandatory in general (again, that's just an agreement between apps).
  - StaticType .... something like the CGFX entity type [TODO: remove]
  - TkbType ....... unique id in a TKB database; currently CGFX TKB id; will be changed to Unified TKB id
  - DisType ....... compatible with DIS entity type id (SISO-REF-010-2015 extended by Bagira entity types)

 In current implementation the **TkbType is mandatory** while the DisType is just informative. StaticType is not used at all.

```
    struct EntityMaster
    {
		long EntityId;
        
        uint64 DisType; 
        
        // ...
    };
```
  

# Entity ownership

Onwer is the one updating the entity.

The ownership is determined for each descriptor indiviudually, allowing for partial owners.

Ownership is determined by the most recent writer - the owner is whoever published the current value. This is not true during the short time of ownership update. However, it is again true after the ownership update is finished.

**Only the descriptor owner is allowed to publish its updates.** Updates from non-owners result in an undefined behaviour and should be avoided at all cost. 

## Ownership updates

1. An arbitraty node sends `OwnershipUpdate` message. This message is for the current and the new owner only - i.e. it doesn't serve as an ownership indicator.

2. Current owner receives message and updates its state to a non-owner (e.g. it stops sending further updates). It does NOT dispose sample/writer/instance which would result in descriptor deletion followed by descriptor creation on new owner.

3. A new owner receives `OwnershipUpdate` message and writes descriptor value (usually same value) to "confirm" the ownership.

One time updates (e.g. teleporting an entity) should usually be done by sending a specific message to the current owner and letting him perform the update rather that changing the ownership temporarily.


	// Unique identifier of a participating node
	struct NodeId
	{
		int Domain; // AppDomainId

		// Individual node; unique within a domain
		int Node;  // AppInstanceId
	};

	struct OwnershipUpdate
	{
        // What entity instance is affected.
        long EntityId; 

        // Unique id of the descriptor type.
        long DescrTypeId;

        // Id of the descriptor instance.
        // Non-zero if there are multiple descriptors of the same type per entity instance, having own unique id within the entity.
        // Zero if there is just one descriptor of that type per entity instance.
        long DescrInstanceId;

        NodeId NewOwner;
	};


# Working with entities and descriptors using plain dds topic
No special infrastructure is necessary. All can be achieved using plain dds-topic readers and writers.


## Entities

To create/add an entity
  - Allocate unique non-conflicting entity id (see Entity ID above)
  - Publish descriptors (preferrably EntityMaster last)

To delete an entity
  - Unpublish the descriptors (preferrably EntityMaster first)


To detect entity creation/deletion
   - Read (do not take) samples of EntityMaster and check the instance state.
   - ALIVE = entity exists, otherwise it does not.

To check if entity with particular id exists
  - Read the particular instance of EntityMaster descriptor
    - Lookup the instance handle by the entity id
    - Read the instance sample
	- check if ALIVE

To enumerate all entities, filter by type
  - Read all ALIVE instances of EntityMaster descritpor.
  - Filter by the entity type information stored in the EntityMaster descriptor.

To enumerate all entities having certain descriptor
  - Read all ALIVE instances of desired descriptor.
  - Get entityId from the descriptor.

To change ownerhip
  - Follow the mechanism described in Entity ownership chapter.

## Descriptors

To check if the descriptors you need are present
  - Try to read the instance of each of the descriptor topics.
  
To read a value of particular descriptor
  - Try to read the particular instance of the descriptor topic.
  - Use the entityId and possibly the descriptor instance id (for multi-instance descriptors) as the instance key

To write a value of particular descriptor
  - Write the particualr instance of the descriptor topic.
  - Do it only if you are the owner of the descriptor!

To change ownerhip
  - Follow the mechanism described in Entity ownership chapter.


# descriptor change requests

Changing an unowned DDS entity component (descriptor) should be done via sending a change request over DDS,
carrying the entity id and the new value of the descriptor.

There should be one generic message for requesting a descriptor change UpdateEntityDescriptorRequest,
internally implemented as IDL Union to support all different descriptor types. Each descriptor has a unique small
integer id.

The requests need to be acknowledged by a generic ack message carrying request correlation id, descriptor type and
operation result (Error code, 0=success)
