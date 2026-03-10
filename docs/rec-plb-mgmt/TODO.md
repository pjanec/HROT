Critical gaps / ambiguities to fix

- Master availability / crash recovery: no leader-election or durable transaction log described. Add persistent transaction state so a replacement master can resume/abort in-flight DistributedTransaction. See Bagira.Orchestrator/DrillMaster.cs.
- In-flight master failure mid-2PC: specify recovery/compensation (replay of pending NodeOpCommand or rollback). Tie to durable transaction log and TransactionEpoch semantics. See Bagira.Orchestrator/DrillMaster.cs.
- OperationStep failure semantics (e.g., ReplaySeek): behavior is ambiguous when an OperationStep fails after prior TransitionStep committed — define whether to (a) keep DSM state and mark operation failed, (b) roll back to prior state, or (c) provide compensating ops. Clarify in planner/transaction docs. See TransitionPlanner/DistributedTransaction in Bagira.Orchestrator/TransitionPlanner.cs.
- DdsIdAllocator reset robustness: if some nodes fail to return MaxNetworkId (manifest missing/corrupt), define timeout/fallback policy and safety buffer tuning. See Bagira.Orchestrator/DrillMaster.cs and ID allocator section.
- Schema/manifest compatibility and versioning policy: add explicit manifest version and compatibility rules (how to proceed if SchemaValidator fails on some nodes). See PlaybackController.cs and RecordingMetadata.
- Checkpoint in-flight DDS capture: the docs mention a ~50ms capture window; specify exact guarantee (flush ingress queues vs. pause egress), and corner cases (DDS best-effort messages). Add precise algorithm or required QoS to ensure deterministic capture. See checkpoint flow in FDP/Kernel/Fdp.Kernel/Orchestration/CheckpointIOWorker.cs.
- Future Barrier assumptions: the barrier requires consistent frame counters derived from master; document how to detect/handle frame-counter drift or missed pulses (and master->slave clock resync if slave misses barrier). See FDP/Toolkits/FDP.Toolkit.Time/DistributedTimeCoordinator.cs.
- SafeStartId race / replay collisions: clarify behavior if nodes report inconsistent MaxNetworkId values (e.g., corrupt manifests). Add validation and abort policy. See DdsIdAllocator section in Bagira.Orchestrator/DrillMaster.cs.
- Tuning constants not centralized: heartbeat timeout (5s), keyframe interval (60), PLL jitter thresholds, checkpoint capture window, upload token concurrency are embedded in text — consolidate into configurable constants and document default values and rationale. Files: time toolkit, recorder, master config (e.g., FDP.Toolkit.Time and AsyncRecorder.cs).
- Tests & verification: add explicit integration test matrix (master failover, ReplaySeek with heavy nodes, Live-from-Replay branch, concurrent checkpoints, storage gateway) and reference harness locations. Add to Batch plan in DESIGN.md.

Smaller issues / clarifications

- Explicitly state PendingNodes initialization behavior: PendingNodes should be seeded only with nodes that indicate IsParticipating=true for that NodeOpCommand (or master should remove opted-out nodes early). See transaction lifecycle in Bagira.Orchestrator/DrillMaster.cs.
- Make OperationStep vs TransitionStep failure reporting explicit in SysOpStatus payloads (include step index, error details). See SysOpStatus definition in Bagira.DDS.DataModel/Orchestration/OrchestrationMessages.cs.
- Manifest integrity checks: require cryptographic/hashing checks on .meta.json and .fdp blobs before accepting MaxNetworkId or proceeding with replay load. See RecordingMetadata in FlightRecorder.
- Clarify storage gateway error handling (partial uploads, retry/backoff) and how master surfaces partial failures to IOS. See StorageGatewayModule in Bagira.Orchestrator/StorageGatewayModule.cs.
- Add explicit logout/cleanup behavior if a node is removed mid-transaction (how master reprovisions and whether operation can continue minus that node). See NodeHealth section in Bagira.Orchestrator/DrillMaster.cs.

Recommended edits (brief)

- Add a short subsection on "Master HA & durable transaction log" under DrillMaster (persist DistributedTransaction to disk, add recovery logic).
- Add "OperationStep failure policy" text to 5.5 (planner) and 5.3 (transaction lifecycle).
- Add fallback behavior for missing MaxNetworkId and manifest validation in 5.7.
- Move all tuning constants into a central config table (and reference it from time/recorder/checkpoint sections).
- Add a "Test plan" appendix listing integration tests to validate the hairy failure cases above.

