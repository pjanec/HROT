<!--STATUS
state: LIVE — ui → backend: the master's op status on every node (CE-4097's unsaved check on CGF / SimHost / IG hosts, AQ108 D20)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.8a (the gap, measured), §4 D20 / §4b (CE-4093 master round)
-->
# NOTE — ui → backend: ECS nodes never hear the master's op status, so a CGF host cannot read the unsaved check

⭐ CE-4097 asks *"are there unsaved edits?"* through the one round (`CheckUnsavedIntent` → the master → `UnsavedCheckResult` on
`ClusterOpCompletedEvent`) and reads the answer through the host's debug provider. ✅ The editor, Orchestrator and ExCon read it.

⛔ **An ECS-node host cannot** (measured `2026-10-29`, grep over `Hrot/` `FDP/`, excluding tests):
- the answer goes out on DDS `ClusterOpStatus` (`ClusterOpMasterTranslator.cs:66-67`);
- the only reader that turns it back into `ClusterOpCompletedEvent` is `OrchestrationObserverTranslator` (`:113-136`), built only for ExCon
  (`NedNetworkFactory.CreateOrchestrationObserver`, caller `ExConSubsystem.cs:459`);
- `NodeOpSlaveTranslator` (every ECS node: CGF, SimHost, IG, Traffic) reads `NodeOpCommand`, `ClusterStateTopic`, `AssetInventoryTopic`,
  `ReplaySessionTopic` — ⛔ no `ClusterOpStatus` (`NodeOpSlaveTranslator.cs:30-39`; `DdsReader<ClusterOpStatus>` appears only in the observer).
- The ask DOES cross (`ClusterOpEgressTranslator.cs:163` → `ClusterOpMasterTranslator.cs:302`); only the return leg is missing.
⇒ on a CGF host ▶ Live goes live without the *Save and go live* prompt even when a node holds unsaved edits.

| ⭐ ask (backend's wire) | lean |
|---|---|
| give `NodeOpSlaveTranslator` an optional `DdsReader<ClusterOpStatus>` (built in `NedSlaveOrchestrationTranslator`) that publishes `ClusterOpCompletedEvent` | extract the observer's decode (`OrchestrationObserverTranslator.DecodeResult`) into ONE shared ingress both translators call — ⛔ not a second decoder |

When it lands, ui extracts `ClusterUiCache`'s outcome fold into one small class and feeds the ECS-node provider from the node's bus (no second fold).

_ui lane, 2026-10-29_
