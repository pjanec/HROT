{
  "EventType": "Hrot.Common.Events.MissionControlIntent",
  "Frame": 1026,
  "Payload": {
    "RequestId": "6dddce93-1948-49e6-a868-7d6d21b86129",
    "TargetEntityId": 1000,
    "BaseVersion": 0,
    "Payload": {
      "CommandType": 3,
      "FullMissionData": {
        "ActiveTaskId": "00000000-0000-0000-0000-000000000000",
        "Tasks": [
          {
            "TaskId": "12ea0008-f552-4c57-892b-84e8f19b5c25",
            "ExecutingEngine": "",
            "BehaviorId": "MoveToLocation",
            "BehaviorParams": "{\u0022targetLat\u0022:52.524205269948034,\u0022targetLon\u0022:13.415224989227431,\u0022speed\u0022:5,\u0022arrivalRadius\u0022:5}",
            "Triggers": [
              {
                "Type": "BehaviorFinished",
                "Params": ""
              }
            ],
            "State": 0
          }
        ]
      },
      "TargetTaskId": "00000000-0000-0000-0000-000000000000"
    }
  }
}



{
  "EventType": "Hrot.Common.Events.MissionControlAckEvent",
  "Frame": 1027,
  "Payload": {
    "RequestId": "6dddce93-1948-49e6-a868-7d6d21b86129",
    "ErrorCode": 0,
    "NewVersion": 1
  }
}





{
  "EventType": "Fdp.Toolkit.Behavior.Events.AssignBehaviorHashEvent",
  "Frame": 1028,
  "Payload": {
    "Entity": {
      "PackedValue": 4294967296,
      "IsNull": false,
      "Index": 0,
      "Generation": 1
    },
    "BehaviorHash": 3001
  }
}





{
  "EventType": "Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent",
  "Frame": 1028,
  "Payload": {
    "Entity": {
      "PackedValue": 4294967296,
      "IsNull": false,
      "Index": 0,
      "Generation": 1
    },
    "IntentId": "MoveToLocation",
    "JsonParams": "{\u0022targetLat\u0022:52.524205269948034,\u0022targetLon\u0022:13.415224989227431,\u0022speed\u0022:5,\u0022arrivalRadius\u0022:5}"
  }
}







{
  "EventType": "Fdp.Toolkit.Behavior.Events.AssignBehaviorHashEvent",
  "Frame": 1029,
  "Payload": {
    "Entity": {
      "PackedValue": 4294967296,
      "IsNull": false,
      "Index": 0,
      "Generation": 1
    },
    "BehaviorHash": 3001
  }
}







{
  "EventType": "Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent",
  "Frame": 1029,
  "Payload": {
    "Entity": {
      "PackedValue": 4294967296,
      "IsNull": false,
      "Index": 0,
      "Generation": 1
    },
    "BehaviorName": "MoveToLocation",
    "JsonParams": "{\u0022targetLat\u0022:52.524205269948034,\u0022targetLon\u0022:13.415224989227431,\u0022speed\u0022:5,\u0022arrivalRadius\u0022:5}"
  }
}







{
  "EventType": "Fdp.Toolkit.Behavior.Events.BehaviorFinishedEvent",
  "Frame": 1030,
  "Payload": {
    "Entity": {
      "PackedValue": 4294967296,
      "IsNull": false,
      "Index": 0,
      "Generation": 1
    },
    "Result": 0
  }
}







{
  "EventType": "Fdp.Toolkit.Behavior.Events.ClearBehaviorEvent",
  "Frame": 1031,
  "Payload": {
    "Entity": {
      "PackedValue": 4294967296,
      "IsNull": false,
      "Index": 0,
      "Generation": 1
    }
  }
}







{
  "EventType": "Fdp.Toolkit.Behavior.Events.BehaviorFinishedEvent",
  "Frame": 1031,
  "Payload": {
    "Entity": {
      "PackedValue": 4294967296,
      "IsNull": false,
      "Index": 0,
      "Generation": 1
    },
    "Result": 0
  }
}





