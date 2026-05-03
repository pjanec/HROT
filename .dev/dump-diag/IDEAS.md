-----------------------------------------
[IDEA] Event Browser Multi-select & Copy to JSON
I need to be able to select multiple events in the Event Browser.
In the context menu of the event list I would like to see "Copy" item
which should make a JSON copy of each selected event (from older to newer) and concatenate these and copy the resulting
text to the clipboard.
[
    {
      "EventType": "Fdp.Toolkit.Replication.Messages.OwnershipUpdate",
      "Frame": 773,
      "Payload": { ... }
    },
    {
      "EventType": "Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand",
      "Frame": 769,
      "Payload": { ... }
    }
]

The json needs to be post processed via Newtonsoft.json as done when saving scenario file
 - put array containing numbers in a single line etc to save number of lines
 - Share this code with scenario saving!
 - This post-process should be done in same manner what clickin "Copy to JSON" for individual records

---------------------------------------
[IDEA] Entity inspector Multi-select & Copy to JSON
I need to be able to select multiple entities in the Entity inspector.
In the context menu of the entity list I would like to see "Copy" item
which should make a JSON copy of each selected entity

[
    {
      "EntityId": [0, 1],
      "Components": { ... }
    },
    {
      "EntityId": [1, 5],
      "Components": { ... }
    },
]

 and copy the resulting json text to the clipboard.

The json needs to be post processed via Newtonsoft.json as done when saving scenario file
 - put array containing numbers in a single line etc to save number of lines
 - Share this code with scenario saving!
 - This post-process should be done in same manner what clickin "Copy to JSON" for individual records
---------------------------------------
[IDEA] Grabbing complete entity state & events across the cluster
In cluster runner distributed config, i need to take a snapshot of selected entity (or all entities)
across all nodes, as a json file looking like { "CFG": { entity json }, "SimHost": { entity json } }
and copy it to the clipboard.

Smilarly i would like to grab the event browser event snapshot of all registered event providers,
in chronological order of frame indexes (from older to newer)
{ "CFG": { "World": [ list of json formatted events ], "Perception": [ ... ] }, "SimHost": { ... }  }

The orchestrator should support this new state dump operation - asking all nodes to make the snapshots
and send them (copy them as files) to dedicated central NAS location.

The orchestrator should have UI dialog where we can select what kind of snapshot dump to create
 - a matrix table with subsystems as columns and dump kinds as rows
     - if we want the entity dump
       - if just selected entity (by network id) or all entities
     - if we want the event dump
       - from what subsystems
       - what event provider (hardcoded list of names like "World", "Perception", "Orchestration"
          matching the names given to the event providers by the nodes)
     - if we want architectural diagnostic window dump
     - if we want the message log dump
       - how much detailed - threshold debug level
       - age threshold (hours)
       - note: logs are never saved as json or markdown, alway just like .log file

     - if we want the message log dump

 - checkbox if we want to add .md extension to the files istead of plain json - in such a case the files
   should contain markdown like the following, with pretty formatted json
     ``` json
     { "CFG": { ... }, "SimHost": { ... } }
     ```
 - The UI should monitor the progress of the operation and show the result table (tree) per node
       CGF
         Entities
         Events
         Arch
         Logs
     
   For each element there should be a context menu allowing to
      - copy to the content to clipboard
      - copy the NAS file name to clipboard
      - open the file (directly from NAS) in default application
      - save to a local file (should open some kind of save as dialog)

The dump files should be saved to the NAS folder (same as defined for recordings, scenarios..)
 - "[NAS]/dumps/dump_DATETIME_entities.json[.md]"
 - "[NAS]/dumps/dump_DATETIME_events.json[.md]"
 - "[NAS]/dumps/dump_DATETIME_arch.json[.md]"
 - "[NAS]/dumps/dump_DATETIME_logs_CGF.log"
The DATETIME part is the timestamp or the user request and must be identical for all files frot the same request.
format "YYYYMMDD_HHMMSS" in local time of the orchestrator.


The jsons needs to be post processed via Newtonsoft.json as done when saving scenario file
 - put array containing numbers in a single line etc to save number of lines
 - Share this code with scenario saving!

---------------------------------------
[BUG] Event browser's "Copy to json" wrongly serializaes the "Reason" field below
(probably fixedString)

{
  "EventType": "Fdp.Toolkit.Lifecycle.Events.DestructionOrder",
  "Frame": 3095,
  "Payload": {
    "Entity": {
      "PackedValue": 4294967297,
      "IsNull": false,
      "Index": 1,
      "Generation": 1
    },
    "FrameNumber": 3096,
    "Reason": { // <== FIXED string???
      "Length": 11,
      "IsEmpty": false
    }
  }
}


The serialization code including the custom formatters and post-processing should be shared as much as possible.
---------------------------------------
[IDEA] JSON Dump of the architectural diagnostic window
The content of the window should be saveable to json string
  - module list
  - system list
  - translator list
Each including also the available stats - avg time, max time, total time, number of runs...
---------------------------------------
[IDEA] Dump of the node log file (the nlog file)
The node must be setup to save its nlog logs into one single file, with automatic rotation.
The content of the files (current one including the rotation archives),
after filtering by the criteria like record age and log level threshold,
should be copied to the dump file on NAS.
Because of the size the log copying should not go via orchestrator dds network.

---------------------------------------
----