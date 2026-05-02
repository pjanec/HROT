General architecture

* No network layer by default. Network as a plugin.  
* Network translator packs are just a set of pluggable modules.  
* Actuator Intents and env/navig/sensor queries are communicated through internal shared ECS and event bus by the brain and muscles.  
* SimHost core pack does not integrate network translators directly.  
* Translators convert network messages to internal ECS / events.  
* FDP based simhost core pack works with ECS and events only.  
* Subsystems combine feature packs with network translator packs.

Logic packs

* FDP Simhost core pack is just a set of installable modules for  
  * Locomotion  
  * Perception  
  * Navigation  
  * Environment queries  
* Translator packs  
  * Entity states and simulation events  
  * Actuator Intents (locomotion, weapons, comms)  
  * Service queries/responses (navigation, perception�)  
  * Network id allocation  
* FDP CGF logic pack included modules for  
  * Behavior machinery, BTree, HSM, Missions  
  * Actions and conditions for use with BTree/HSM  
  * Json Scenario management  
* FDP Orchestration logic pack  
  * Cluster state sync (ClusterMaster, ClusterSlave)  
  * Time sync master (Wall clock, SimClock)  
* FDP Network id allocation  
  * FDP network id allocations server  
  * FDP network id allocation client  
* FDP Scenario Editor logic pack  
  * ImGui UI and Vis2d map  
  * UI for manipulating entities  
  * UI for editing missions  
  * UI for editing routes  
  * UI for editing areas


  
Bagira Map/ORBAT demo should

* Work with BDC SST DDS data model (the subset for map/ORBAT control)  
* Use mocks of IOS, IG, SimHost based on FDP  
* Show Remote map control from IOS (remote map control) to IG (map renderer)  
* IG not just a passive renderer, but active map editing node able to create new entities  
* Show IOS working with BDC DER library  
* Show Separation of IG from SimHost (IG request entity creation, SimHost owns entities)  
* Work Without any orchestration, simply connecting to current session via BDC SST  
* Not need for any cluster state management, exercise loading, replay, pause etc.  
* Uses Bagira IdAllocator.

Bagira CGF demo

* Working with full BDC SST DDS data model  
* Reuses all of Map/ORBAT deme features (IOS, IG, SimHost)  
* All subsystems are networked and separable to standalone runner based apps.  
* Adds CGF subsystem installing Bagira translators and FDP CGG logic pack.  
* Demonstrates separation of CGF from SimHost (CGF \= brain, SimHost \= muscle)  
* SimHost will be implemented via Bagira Sim/IG (C++, BDC connected, full perception and navigation and movement) or mocked via FDP based SimHost (simplified but working perception, navigation etc.)  
* Should use bagira id allocator  
* Full orchestration  
  * Orchestrator uses fdp orchestration toolkit events  
  * Bbroker network adapter translates between fdp orchestrator toolkit events to bbroker network messages.

HROT demo should

* Demonstrate FDP engine capabilities  
* Work with independent simplified NED DDS Data model  
* Completely separate from Bagira stuff.  
* Show decoupled network distributed architecture  
* Subsystems are simplified to bare minimum  
  * ExCon \- remote cluster control. Non ECS network entity monitoring. Entity manipulation requests.  
  * SimHost \- the muscle.  Simple kinematics, navigation, perception. Owns entity �muscle� components (navig state, position�)  
  * Cgf \- the �brain�. Owns entity �brain� components (navig intent, weapon intent�)  
  * Ig \- the �presenter�. Owns nothing.  
* Full Orchestration  
  * orchestration logic packcluster state sync, network id allocations, exercise time master, wall clock sync master.  
  * Uses NED messages  
* Should serve as development platform for new features  
* Subsystems follow cluster state management (exercise lifecycle \- load/operate/unload, recording/replay, preview, editing, file archiving�)

HROT editor

* All in one engine based on FDP modular architecture  
* Simhost feature switchable between  
  * Internal FDP SimHost logic pack  
  * External full Bagira IG/SimHost, accessed via network  
    * Represented by a network translator pack for intents/queries.  
* Requires ScenarioEditor logic pack  
* 

  