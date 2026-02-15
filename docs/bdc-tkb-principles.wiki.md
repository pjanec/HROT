[[_TOC_]]

# Unified across major components
BDC.TKB aggregates the data from both CGFX TKB & B-IG into one consistent database.

The unified data consist of the following components
 * platforms
 * life forms
 * weapons
 * ammos (incl. ballistics and damage infos)
 * 3d models
and all the relations among them.

# Centralized
The data exists in one single master copy on a central server.

A single central component loads the TKB data from persistent storage and publishes it do DDS using [TKB DDS data model](https://bagirasystems.visualstudio.com/Bagira%20Systems/_git/Bagira.Infra?path=/Src/Communication/DataModel/BDC.TKB).

This compoment is started early in the system boot process so the TKB data is accessible for other compoments started later.

# Exported to native formats
The apps like CGFX and B-IG may keep using their own formats of the data during the transition period; BDC.TKB data is exported to the respective native formats of those apps.

# Accessible by anyone
Other apps can reads the TKB data from DDS into memory using TKB Dds Loader library. On startup, the apps wait for the DDS data to be available on the network.


# Keeping app-specific data
The BDC.TKB keeps not just the "common" or abstract information usable by anyone, but also the data specific to concrete apps/services that no one else is using.
 * CGFX sensors
 * B-IG ammo ballistics
 * B-IG hit damage info

Such data are still edited centrally on the server and exported from there to their respective apps.

## Importing app-specific data
Some parts of the BDC.TKB data, although stored in BDC.TKB and used/referenced/linked there, are by nature still originating in B-IG or CGFX
 * 3d models of B-IG ... exist as data files of the B-IG
 * ?? of CGFX

Such data can not exist without the apps and it make no sense (or is not worth the effort) to edit them centrally.

Such data components are imported to BDC.TKB and updated on regular basis (or on change in the source) so that BDC.TKB always reflect the current state of the system.

The rest of the data components, like
 * platforms
 * life forms
 * weapons
 * ammos
 * ...
are no more maintained in CGFX or B-IG. Instead, they are stored and edited on the central server. 

# Data Model Principles
* TKB is built from entities and descriptors using a composition principle.

* TKB defines instances of **entities** of various kinds

   * Entity kind is defined by the "kind" field of DIS Entity Type
      * Basic kinds defined in SISO-REF-010-2015 (platform, lifeform, ammo, sensor...)

      * Bagira extends it by some non-standard kinds (weapon, ig model)

* Entity instance is uniquely identified by TkbId (uint64 unique id)

   * All entity kinds share the same ID space (no independent IDs for different kinds)

* Each Tkb entity instance contains one or more instances of **descriptors**.

   * Descriptor is a named data structure describing certain part of entity features/capabilities.
   * No other data but descriptors exists for an entity, all entity specific data are kept by descriptors. 

* For each TKB entity instance there is always one **master descriptor** and an arbitrary number of other descriptors.

   * Master defines basic properties (DIS entity type, textual name)
       ``` csharp
       struct TkbMaster
       {
         uint64 Guid; // [key] unique TKB type id
         DisEntityType DisType; // 2.8.225.2.1.1
         string Name; // For example "5.56x45mm M193"
         ...
       }
       ```
   * Other descriptors define more entity-kind specific information (dynamics, ballistics, damage...)
       ``` csharp
       struct Gen.Dynamics
       {
         double MaxSpeed;
       }
       
       struct Gen.Ballistics
       {
         double MuzzleSpeed;
       }
       ```

* For different simulation engines different descriptors may carry similar (but simulation engine specific) data

    * For example visual characteristics for B-IG vs. different IG

        ``` csharp
        struct BIG.EntityVisuals
        {
          string ModelName;
          ...
        }
        
        struct SomeAnotherIG.EntityVisuals
        {
          int PrefabId;
        }
        ```

## TKB Entity Kinds

Entity kind is defined in the DIS Entity type field.

DIS entity type is derived from a type classification defined in SISO-REF-010-2015 standard.

Bagira extends it by several custom entity kinds

| Kind      | Description                                                  |
| --------- | ------------------------------------------------------------ |
| Weapon    | Weapon the simulated entity can fire.                        |
| IgModel   | 3d model of a real-world entity like platform, lifeform, weapon, accessory... |
| Material  | Type of material the target is built from; for the purpose of hit damage evaluation. |
| Accessory | Attachable/detachable optional part of a simulation entity, like a piece of equipment (helmet, backpack, weapon etc.) |

# DDS representation
 * All data types come from [TKB DDS data model](https://bagirasystems.visualstudio.com/Bagira%20Systems/_git/Bagira.Infra?path=/Src/Communication/DataModel/BDC.TKB).
 * Each TKB descriptor is published as an instance of corresponding DDS topic (one per descriptor).
 * All descriptor's QoS is transient-local, reliable, keep last 1.
 * Entities are constructed from their descriptors only (no other data for entity exists)
 * `TkbMaster` descriptor represents the entity existence
   
    * No entity can exist without an instance of the `TkbMaster` even if other descriptors for that entityId are present
 * Each descriptor is a single sample of a TKB DDS topic for that concrete descriptor type
    * Their first data field (not matter what name) is treated as a TkbId
       * Links the descriptor to the entity
       * Is same for all descriptors belonging to same TKB entity.

     ``` csharp
    struct TkbMaster
    {
      uint64 Guid; // [key] unique TKB type id
      ...
    }
    
    struct Gen.EntityDynamics
    {
      uint64 EntityGuid; // unique entity type id
      ...
    }
    
     ```
    
 * Some descriptors can have more instances of same type per entity (multi-instance descriptors)
    * Their second data field (no matter what name) is a 'part id' uniquely identifying the instance within an entity

    ``` csharp
    struct Gen.EntityBallistics
    {
      uint64 AmmoGuid; // Tkb type of the ammo entity
      uint64 WeaponGuid; // Tkb type of the weapon entity
      ...
    }
    
    
    ```
    
# In-Memory representation

 * A collection of Tkb entities (platform/lifeform/ammo/weapon...) indexed by entityId

   ```csharp
   class TkbData
   {
     Dictionary<uint64, Entity> Entities;  // enityGuid => Entity  
   }
   ```

 * Each Tkb entity provides a collection of descriptors indexed by descriptor key
   
   ```csharp
   class Entity
   {
       Dictionary<DKey, Descriptor> Descriptors;  // descrId => descr data
       ... // other stuff like comments, folder path etc.
   }
   ```
 * `DKey` = unique id of a descriptor within an entity instance

   ```csharp
   struct DKey
   {
     System.Type Type; // descriptor struct type
     uint64 PartId; // in-entity instance guid (0 by default for single-instance descriptors)
   }
   ```

 * Descriptor stores its data struct sample
   
   ```csharp
   class Descriptor
   {
     TopicBase Sample; // descriptor data structs are defined in TKB.idl as topics
     ... // other stuff like comments
   }
   ```


# Persistent storage in Json files

The TKB is stored as a set of JSON files on disk

* One json file per TKB entity (platform/lifeform/ammo/weapon...)


* Each json contains dictionary of descriptor types
    ``` csharp
    {
        "$guid": 17637, // tkb id of this entity; mandatory
        
        "TkbMaster":
        {
            // TkbId field set automatically from $id
            "DisType": "1.2.3.4.5.6.7.8", // what kind/type of entity it is; SISO-REF-010-2015
            // "Name" field is set automatically from json file name
        },
    
        "Gen.AmmoWeaponBallistics#1":
        {
            // "AmmoType" field set automatically from $id
            "WeaponGuid": 1234, // Tkb type name of the weapon entity this ballistics is for
    
            
            "MuzzleSpeed": 830,
            
            // fields can have comments (not on DDS, just for tools like the editor)
            "//MuzzleSpeed": "Value was provided by the customer."
        },
        
    // descriptors can have comments too
    "//Gen.AmmoWeaponBallistics#1": "This is a sample ballistics descriptor for sample ammo type",
  }
  ```
  
  * Descriptors and their data fields are defined in [DDS data model for TKB](https://bagirasystems.visualstudio.com/Bagira%20Systems/_git/Bagira.Infra?path=/Src/Communication/DataModel/TKB) 
  * Each descriptors stored as single json object
    * `"TkbMaster" : { "Guid" : 1234, .... },`
    * `"Armor": { ... }`
  * Multi-instance descriptors add numeric postfix to stay uniquely named within the json object properties
    * `"WeaponAmmoBallistics#1": { "WeaponGuid": 8263, "MuzzleSpeed": 830, ... }`
    * `"WeaponAmmoBallistics#2": { "WeaponGuid": 7453, "MuzzleSpeed": 870, ... }`
  
* Json can contain also extra non-descriptor records whose key does not begin with a letter
    ``` csharp
    {
        "$guid": 17637, // tkb id of the entity; mandatory
        
        "$actions": // actions to perform when loading this entity into memory (NOT USED)
        [
            { "Remove": "Gen.EntityDynamics" }
        ],
        
    	// descriptors start here...
      },
    ```
    
* Arbitrary folder structure of the json file represents "user-level" categorization
  
  * Not affecting the data in json (only json matters)
  * Tkb entity class/category (entity, ammo, weapon...) is defined by [DIS entity type](https://www.google.com/search?q=dis+entity+type+reference+for+enumerations) ONLY (folder path not significant)
  
    ```
    <rootFolder>
    	Ammo
    		SmallCal
    			5.56x45mm M193.json
    
    	Platform
    		Vehicle
    			Military
    				MBT
    					Merkava Mk4.json.json
    ```
  
* Tkb entity name field in the `TkbMaster` is set automatically to the json file name



# Attributes for TKB structs and fields

To help the editor to handle the TKB fields in an user friendly and safe way, we annotate the structs and fields in the IDL with custom attributes.

They are parsed by the `Codegen` and converted to class attributes (for C# targets).

IDL supports `@name` style annotations but cyclone idl compiler fails on non-standard ones, so no custom ones are allowed. Custom attributes then need to be hidden into comments (or pragmas???)

C# attribute syntax is used for convenience.

``` csharp
//[Description("Basic dynamics parameters for an entity.", "en")]
struct EntityDynamics
{
  //[Min(10)] ... value limits
  //[Description(Maximum horizontal speed of a vehicle")]
   long Speed;

  //[Range(min=-1,max=1)] ... value limits (here we demonstrate named argument syntax)
  double Steering;
};

struct WeaponAmmoBallistics
{
  //[WeaponRef] ... field represents a tkb id of a weapon record (editor shows weapon name)
  string WeaponGuid;

  //[AmmoRef] ...field represents a tkb id of an ammo record (editor shows ammo name)
  string AmmoGuid;
};
```

| Attribute   | Meaning                                                      |
| ----------- | ------------------------------------------------------------ |
| WeaponRef   | Reference to a Weapon TKB entity.                            |
| AmmoRef     | Reference to an Ammo entity.                                 |
| ModelRef    | Reference to a IG model entity.                              |
| SyncWith    | Synchronize this field (if changed in editor) with another descriptor automatically. For example a ballistics parameter across different simulation models. |
| Description | Description to show in the TKB Viewer/Editor. Language can be specified. |
| UsableWith  | What DisTypes (mask) the descriptor is usable with.          |



# Base TKB vs. Project-specific TKB

There is one "Base TKB" used as a base for all projects.

Project-specific versions are derived from this common base.

Projects/customers can customize this base TKB to their needs by adding/modifying/removing some TKB entities and their properties.

Later updates of the Base TKB can be propagated to project-specific TKBs if desired.

# Version Control

The nature of the TKB storage format (one small text file per TKB entity containing simple and human readable information) makes it ideal for versioning using a source code control system like Git.

* Providing all features we need (diffing, branching, merging, cherry-picking...)
* Well established, mature, with excellent tool support.
* Natively supported by Azure DevOps.

TKB is maintained in a dedicated Git repository.

Changes made to the TKB are stored as atomic commits with comment summarizing the change (what/why etc.)


## Branching in VCS (Git)

Base TKB is stored in a version control system (VCS) repository in a **master** branch.

Projects needing customization create a **project-specific branch** of the Base TKB files.

Changes are monitored using built-in **diff operation**.

Changes can be propagated across branches using built-in **merge or cherry-pick operation**.

## Working on a branch

Application consuming the TKB always work with full TKB data, no matter what branch the data comes from (maser, project-specific branch etc.)

They usually don't need to differentiate between data coming from Base TKB and the these affected by project-specific changes.

TKB editor might be an exception. For convenience, TKB editor should be able to show whether the data currently shown/edited are different from the Base TKB. This helps the users to keep a notion of project-specific changes that might need to be distributed to other projects as well.


## Propagating changes from Base TKB to projects

Base TKB can change later, after the project branch is already created.

The project TKB is not affected, living in a branch.

Changes can be easily propagated to the project branch by merging.

## Propagating changes from projects to Base TKB

In some cases we might want to propagate the change from project to the common base or also to other projects.

* For example a bug discovered during project development, fixed in the project-specific TKB.

The change can be cherry-picked into Base TKB and from there merged to other project's TKBs. 



# TKB relationships


## Weapon Suite

Weapon suite specifies what weapons are installed on an entity.

See `Gen.EntityWeaponSuite`

### Weapon order

Weapons are listed in a certain order. First weapon is the primary weapon, second is the secondary weapon. Primary weapon serves as the default wherever the weapon type is not specified explicitly.

See `Gen.EntityWeaponSuite.Weapons`

### Installed ammo

Some weapons can fire different ammo types. For each weapon installed on an entity TKB specifies what ammo is supported together with some other configurable properties (initial amount of rounds, magazine capacity...)

See `EntityInstalledAmmo` in `Gen.EntityWeaponSuite`

### Relations to IG model capabilities

Installed weapons defined for a TKB entity needs to correspond with the capabilities and the configuration of the IG model selected for the entity.

The weapon needs to be properly mapped to the IG model in order to keep acceptable 3d visual experience.

TKB editor should to check if the mapping is complete and emit errors if not, not allowing to use the TKB is such inconsistent state.

### IG-engine specific mapping

Different IG engines can provide different ways of controlling the weapon aiming and shooting, needing an engine-specific mapping.

For example B-IG requires the following mapping to model properties:

* Weapon's Bind-point (turret to control)
* Weapon's Azimuth bone (turret's horizontal rotation)
* Weapon's Elevation bone (barrel's vertical rotation)

FIXME: add BIG.XXXX descriptor for this mapping.

### Mapping un-modelled weapons

In some cases the IG 3d model does not contain the weapon the simulated real-world entity can carry.

Such weapon should be mapped to one of existing weapons on the model.

In case of B-IG, if a weapon is missing a mapping, shooting from it is not allowed.



### Multi-barrel weapons

Some weapons fire the projectiles from different launching points - multi-barrel rocket launchers, multiple weapon stores on aircraft etc.

Each shot  (rocket, bomb) can go from different barrel (hardpoint). The barrel sequence can be defined.

WARNING:

BIG model needs to maintain a bind-point name for each corresponding barrel. See `ModelInstalledGun` as part of `BIG.ModelWeaponSuite`.

## AWB (Ammon-Weapon-Ballistics) concepts

Ballistics is a function of both the Ammo as well as the Weapon firing that ammo. For example different barrel lengths can result in different muzzle speed for otherwise same ammunition.

All ammo/ballistics related descriptors are primarily bound to the Ammo (AmmoId is the first key field). But equally they are bound to the Weapon (WeaponId is the second key field.)

Parameters belonging just to the Ammo and not affected by the Weapon (like for example the ballistic coefficient) still keep the two key fields, but the WeaponId is = 0 (invalid undefined weapon).

Apps should

1. first lookup the most specific descriptors (for a combination of concrete ammoId and concrete weaponId).
2. If not found, they do look for less specific combinator (valid AmmoId & zero Weapon or vice versa)

## IG Model with built-in weapon sub-models

Some IG Models contain weapon sub-models as a fixed part of the model; such weapon sub-model is always present, can't be removed or replaced.

Such models need to describe the weapons installed on the model.

See `BIG.ModelWeaponSuite` 

### Weapon's articulated parts

Each weapon sub-model in the suite specifies the articulated part names used to operate the weapon

*  Azimuth and elevation bone for aiming
* etc.

See `BIG.InstalledGun`

### Weapon ordinal

Each weapon sub-model defines what weapon ordinal (primary, secondary etc.) in the entity's Weapon Suite it corresponds to.

Weapons in entity's Weapon Suite need to follow the order defined by this model-defined weapon ordinal.

| Entity WeaponSuite | IG Entity Model                                              |
| ------------------ | ------------------------------------------------------------ |
| primary weapon     | Weapon sub-model having Ordinal = Primary.<br />Optional list of supported weapon types (for validity checks). |
| secondary weapon   | Weapon sub-model having Ordinal = Secondary.<br />Optional list of supported weapon types (for validity checks). |

The length of `Gen.WeaponSuite.Weapons` should match the size of `BIG.ModelWeaponSuite.Weapons`.

Entity's weapon suite SHOULD NOT define any weapon without a corresponding weapon sub-model in the associated IG model.

Loader and Editor needs to check that.

It can easily happen if the IG model changes (`BIG.ModelWeaponSuite`) but the logical entity stuff (`Gen.WeaponSuite`) stays as is.

### Weapon type validity checks

Multiple weapon types (with different TkbId, different in some non-visual parameters) can share the same visual representation.

Thus weapons sub-models DO NOT specify exact TKB id of the weapon.

To auto-check the validity of the TKB setup the model can optionally specify the list of supported weapon types using

* Exact list of weapon TKB ids?
* Using list of DIS entity type masks?

See `BIG.ModelInstalledGun.SupportedWeapons`.

Editor then allows just certain weapon types to be filled to entity's Weapon Suite.

## IG Models with configurable weapons/accessories

Some IG models support defining what accessories/weapons to present. The accessory sub-model can be shown or hidden or replaced with another. Such configuration can happen either before the runtime or even during the runtime.

### Preconfigured model variant

Before-runtime configured model whose config does not change during runtime.

From TKB perspective such model looks identically to the IG model with fixed built-in weapon sub-models (see previous chapters).

## Runtime attachable weapons/accessories

What if an entity can attach different weapons/accessories at runtime? 

If no fixed weapons are present of the entity, entity does not define any weapon suite.

### Supported accessories

Carrying entity IG model can specify a list of entity-compatible accessories (their TkbIds), each with (optional) list of compatible attachment points on the entity model.

### Attach points on the IG model

IG Model defines attachment points, each with the list of supported accessories.

### Supported accessories

Entity defines a list of supported accessories (each accessory type is a TKB entity having unique TkbId)

IG model defines a list of supported accessories



### Rules for attaching accessories to IG model

The IG model can also define some default rules for attaching accessories, like the list of attachment points with the list/masks of compatible accessories.

??? Does this need to be in the TKB? Doesn't IG attach the accessory itself? Does anyone else need to know?



# BIG-bound data

Some data that used to be know just to the IG newly appears in the new TKB to become available also to other components.

But not all of these can be edited in the new TKB - some still live in the IG data and are just mirrored to the TKB.

## BIG maintained data

Some data stays maintained in the IG configs and is just mirrored to the TKB.

- 3d Models and their properties.
- Materials (for damage model's hit evaluation)

IG loads this data from their original IG configs.

Can't be edited in TKB editor. Only referenced.

### Import to TKB from BIG

The import needs to be made whenever the IG data changes.

TKB Import tool replaces the existing TKB data with those loaded from the IG.

* IG Models' jmeta files
* IG Material definition files

After the import a consistency check is run to reveal possible mismatches and conflicts.

### Mapping BIG identifiers to TKB guids

IG identifies its resources (models, materials...) via text names (string). TKB uses 64bit guids.

The import tool provides translation from IG text names to TKB 64bit guids.

#### Reusing same TKB guids for same BIG resources

Subsequent imports of the same data should keep the same TKB guids for same IG names.

* TKB data for IG need to contain the IG identifiers (they naturally do in order to address the resources in the IG)
* TKB import tool should scan the IG identifiers in existing TKB records to build a mapping table

â€‹	[IG resource type, IG text name] => TKB guid.

* Potential duplicities should be resolved (may happen as a result of merging changes across branches).

* The mapping table is then used during the import.
* For new resources not yet data not found in existing TKB a new TKB guid is allocated.

#### Parallel independent import in multiple branches

Two branches may extend the Base TKB by the same new resource not found in the basic set.

What if we do parallel import of these branches?

* The IG resource (having same name in both branches) will get different TKB guids.

What if the resource is finally added to the Base TKB?

* It will get another new guid in the Base TKB!

After merging this change to project specific branch (where the resource already exists)
*  we end up with two identical TKB entities mapped to the same IG resource.

While this duplication is not a critical failure for the system, it is not healthy as the duplicated items may contain different data and just one copy of each resource is desired.

* We need to detect and fix such duplicity as early as possible!
* It will be detected no later than during next import when scanning the IG resources in the existing TKB.

#### Global id table across all branches/projects?

Let's imagine there is only one global shared mapping table used for all TKB branches.

The table is reused/updated during each import.

Just one single import from one branch can be running in parallel.

* Parallel import  might result in multiple TKB guids generated for same IG resource!

**WARNING! would needs a central and always accessible storage for the id mapping table.**

* This is not always possible (working offline etc.)

## TKB maintained data

Some data is newly maintained in the TKB and is mirrored to the IG.

* Weapon/Ammo Ballistics.
* Entity Hit Damage Model.
* IG-controlled Movement Dynamics Model.

Data can be edited only in the TKB (with the help of the TKB Editor).

IG loads this data from TKB.

### Export from TKB to BIG

In the first phase of new TKB adoption, the IG does not use the new TKB directly.

Instead, it keeps loading its original config files.

TKB export tool rewrites IG configs so no IG source code need to be changed

* Data\Ballistics\BallisticsDB.jmeta
* Data\Ballistics\HitData.lua
* Config\CgfxDD\Common\BallisticsSettings.lua
* Config\CgfxDD\Common\MissileInfoSettings.lua
* Config\Common\ExternalWeaponBuild.lua
* Config\Common\ActorBuilder.lua

# CGFX-related data

## Final expected status [not yet there]

Data that used to be maintained in CGFX TKB should be moved to the new TKB.

This data should edited exclusively in the new TKB using the new TKB editor.

From the new TKB the data is exported back to the CGFX TKB format so that CGFX does not need to be modified much (or not at all).

The TKB export tool should rewrite the CGFX TKB file completely and everything, including the CGFX-only data, is editable only using the new TKB editor.

## Transient period

In the transient period, before all CGFX TKB data is converted to the new TKB, the new TKB will contain just a subset of the data:

* Weapons
* Ammo
* Damage model

The rest of the data (for example specific type of entities not used by other components like sensors etc.) still lives in the CGFX TKB only and is editable only there.

New TKB export tool rewrites in the CGFX TKB file just the abovementioned subset of the entities and keep the other entity kinds untouched.

# Initial import

Supported entity kinds will be transformed into new TKB format.

## From CGFX

We export a full TKB.

* usual entities like crafts, humans...
* weapons
* ammo
* ...

**BdcTkbCgfxImporter** command line app in CGFX app suite loads given TKB file and publishes its contents to DDS in Unified TKB format on **"CGFX" ** partition.

The data needs to be received by **TkbDdsPublisher** tool. Data received are saved to disk for further processing (merging).

* saved to **"Data\SampleSaved\\<tkbName>\\CGFX"**
* you copy to **"Data\Exported\\CGFX"**

Entity path starts with "CGFX" so it does not collide with DDS data exported from other sources (like BIG).

For each TKB entity there **CGFX.ABSTRACT_ENTITY** descriptor created.

## From B-IG

We need to export data for

* ballistics
* damage
* 3d models
* 3d (particle) effects

There are two currently 3 approaches implemented, each providing some necessary part; the data from them might overlap (same data generated by multiple tools).

### BDC TKB Grabber scene for Spectacle

Reads ModelDB from Bnet and writes it to a set of json files

* Saved to BIG Dist/Spectacle/Tests folder
* You copy to **Data\CgfxToIgMap**
  * models.json = all existing models as a full ModelDB records (with all properties, weapons etc.)
  * effects.json = all existing effects, each with one or more aliases
  * ammo.json = all unique combinations of ammo & effects
  * mdlwpnammo.json = for each CGFX entity a set of CGFX weapons, for each weapon a set of IG ammos with effects

### BdcTkbExportImport

* Console app in B-IG app suite that loads SimHosts config files and write to DDS in Unified TKB format on **"BIG"** partition.
* Configs include
  * models
  * munitions ballistics, damage
  * sounds
* The data needs to be received by **TkbDdsPublisher** tool. Data received are saved to disk for further processing (merging).
  * saved to **"Data\SampleSaved\\BIG"**
  * you copy to **"Data\Exported\\BIG"**
* For each model the **"BIG.Model.IgModel"** descriptor is generated
* For each ammo the **"BIG.Ammo.ABSTRACT_PROJECTILE"** is generated.
* For each weapon the **"BIG.Ammo.ABSTRACT_WEAPON"** is generated. There are for SimHost's native weapons only, not for CGFX - not used for anything in further merging.

* NOTE: This method DOES NOT include mapping from CGFX weapons to IG models - this requires a running system including CGFX DD

### BdcTkbLiveDataExporter
SimHost built-in module started when IG system is up and running
  * Reads in-memory data from SimHost and writes them to DDS in Unified TKB format, on **"BIG.LiveData"** partition
  * The data needs to be received by **TkbDdsPublisher** tool. Data received are saved to disk for further processing (merging).
    * saved to **"Data\SampleSaved\\BIG.LiveData"** 
    * you copy to **"Data\Exported\\BIG.LiveData"**
  * For each model the **"BIG.Wpn.ModelWeapons"** descriptor is generated, containing mapping to CGFX weapons (those presents in the currently used TKB as loaded from CGFF by CgfxDD)

### TkbMerger
Merges data from multiple sources (CGFX, IG) into one common Unified TKB.

Takes the data exported from CGFX and SimHost using the abovementioned tools
 * BIG static config data - 3d Models, Ammo/Ballistics, Weapons (non-CGFX)
 * CgfxToIgMap - mapping between CGFX entities and IG stuff
   * mdlwpnammo.json = for each CGFX entity a set of CGFX weapons, for each weapon a set of IG ammos with effects 
 * BIG LiveData - CGFX weapons associated with each IG model (BIG.Wpn.ModelWeapons)

The data folder is expected like the following:
![image.png](/.attachments/image-a6b53ea5-b35a-4b32-9b80-949aa165609f.png)

Exports to Data/Merged folder
 * Ammo - CGFX ammo entities, having BIG ballistics info, linked to IG visuals (projectile model, flight effect..)
 * LifeForm, Platform - CGFX entities, linked to BIG IG model, listing weapons etc.
 * Models - IG 3d model info
 * Weapon - CGFX weapons, linked to IG visuals (fire effect...

The resulting data looks like the following:

![image.png](/.attachments/image-f7b58ff1-6835-444b-bd62-24a88e1833f0.png)

## Mapping to CGFX TKB

CGFX uses integer ids to identify a TKB entity, new TKB uses long ints.

When exporting the data from the new TKB to CGFX, the existing CGFX IDs should be kept.

The new TKB needs to contain the CGFX id for each entity originating in the CGFX TKB.



# Semantic numbering of entity guids

Digits in the entity number are reserved and filled according to conventions. This convention applies to all newly created entity ids.

Number format:

​	` F PP KK NNNNNNNNNNNN`

| Digit | Meaning              | Details                                                      |
| ----- | -------------------- | ------------------------------------------------------------ |
| F     | Offline editing flag | 0 = no special care. 9 = entity number was allocated in offline mode and needs conflict checking & resolution when integrated into the common base. Used for any new entities during edits. When online, these numbers are checked for conflicts against the latest existing ids in the central storage. |
| PP    | Project number       | The id of the TKB branch this entity was created in. 0 for common TKB. When integrating the project-created entities to a common TKB, this get zeroed. |
| KK    | Entity Kind          | The kind from the DIS entity type field.                     |
| NNN   | Ordinal              | Unique number within the group defined by the abovementioned digits. Could be plain ordinal if we can guarantee that it will be unique in cases like independent merges from multiple offline edits. Or it could be a random number or a cryptographic hash of unique combination of entity properties like kind + name etc. |




# Checking for breaking changes

There are common non-IG bound settings (like entity weapon suite) that should follow the ig model weapon suite.

If the ig model changes, the non-IG settings may become out of sync with the IG model.

The importer of IG data into the TKB should check this and provide early warning. 

The Editor should show errors in the affected parts.

# Editor

Editor loads TKB data from persistent storage to an in-memory representation and edits the stuff in memory.

Once done, it saves it back to the persistent storage.

## Editor UI

### Entity Tree Panel

 * Tree view showing TKB entities according the folder path structure.

   * Clicking a leaf (entity) brings/updates the Entity Details Panel.
   ```
	Ammo
		SmallCal
			5.56x45mm M193.json

	Platform
		Vehicle
			Military
				MBT
					Merkava Mk4.json.json
   ```

 * **Quick filter** for parts of entity name
   
   * Show just those parts of the tree containing entities whose name matches the filter
   * Like in Visual Studio Solution explorer 
   
 * Each non-terminal tree level offers a context menu (right click, on-hover buttons etc.)
    * Add new sublevel
    * Rename level
    * Delete level
    * Add new TKB entity

 * For terminal items (entities) a context menu (right click, on-hover buttons etc.)
    * Rename
    * Delete
    * Duplicate
    * Move to another tree branch

 * Drag and drop of a terminal item into another tree branch?

### Entity Detail Panel

 * Multiple view modes.

   * In most generic form it shows a plain list of descriptors with items expandable to a tree of field & values

      ```
      TkbMaster
        TkbId = 1234
        DisType = "1.2.3.4.5.6.7"
        Name = "MyTkbEntityName"

      Gen.WeaponAmmoBallistics
        WeaponType = "MyWeaponTkbEntityName"
        MuzzleSpeed = 830

      CGFX.WeaponAmmoBallistics
        ...

      BIG.WeaponAmmoBallistics
        ...
      ```

   * Depending on entity kind (ammo/weapon..) we might show custom-tailored tab pages

      * Ballistics parameters editor
         * Show charts, allows interactive tweaking the ammo params

      * ...

## Editor Functions

### Highlighting diffs to Base TKB

Editor shows diffs to the Base TKB in different color.

Editor loads the Base TKB, compares its content to the currently edited state and remembers the differences.

For each entity, descriptor and field, the status remembered is one of the following

* Same
* Removed
* Added
* Modified 

Editor display mode can be set to show

* Current state only
* Changes to base
  * Removed items shown as darkened, uneditable
    * Can be returned back
  * Modified items highlighted
  * New items highlighted





### Duplicity checking

**Creating new name/renaming existing**

 * Do not allow for duplicities (names need to be unique no matter what the tree path is)

**Editing DisType**

 * Do not allow for duplicities (DisType of an entity need to be unique).
 * Offer picking from standard list of Dis Entity Type (loaded from XML file provided with SISO-REF-010-2015).
 * Offers creation of Bagira-specific DIS types (show their exising list collected from all entities of the TKB not matching any standard DIS type)

### Compare versions

* Compare differences in current data (after loading all the packages) to some other set of data (from different project, from archived state etc.)

# To solve

## Different values for same variable in different simulation models



## Different weapons on model vs. weapons installed on entity.

model does not support some weapons required in the simulation

model-unsupported weapon will be allowed to be used, but...

more complicated mapping may be needed

different mapping for BIG and Unreal models?

solved by TKB operator after importing IG models when some mapping problem occures (incomplete mapping, broken mapping...)



## Model accessories mapping to specific IG engine

Engine-specific auto-mapping of accessory to model. Not generic, will fail in some case where multiple options are available.

Discuss with Jiri Havel etc. how supported in BIG




