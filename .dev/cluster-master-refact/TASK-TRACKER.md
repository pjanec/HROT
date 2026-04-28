# Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

## Phase 1: Storage and Episode Extractions

**Goal:** Remove manifest processing, NAS I/O, and episode state tracking from `ClusterMaster`.

- [x] **TASK-S001** StorageConsensusAggregator [details](./TASK-DETAIL.md#task-s001-storageconsensusaggregator)
- [x] **TASK-S002** StorageProcessManager [details](./TASK-DETAIL.md#task-s002-storageprocessmanager) *(DEBT-01: StorageProcessManager unit tests missing; DEBT-02: ExportArchive still in ClusterMaster)*
- [x] **TASK-S003** EpisodeConsensusAggregator and EpisodeProcessManager [details](./TASK-DETAIL.md#task-s003-episodeconsensusaggregator-and-episodeprocessmanager)

## Phase 2: Temporal Interlock Extractions

**Goal:** Remove `ReplayMasterModule` and `MasterSyncController` dependencies from `ClusterMaster`.

- [x] **TASK-T000** Slave-Side PrepareLive Re-Entrancy Audit *(prerequisite for TASK-T001)* [details](./TASK-DETAIL.md#task-t000-slave-side-preparelive-re-entrancy-audit)
- [x] **TASK-T001** LiveBranchProcessManager [details](./TASK-DETAIL.md#task-t001-livebranchprocessmanager)
- [x] **TASK-T002** Replay Seek Extraction [details](./TASK-DETAIL.md#task-t002-replay-seek-extraction)

## Phase 3: Persistence and Prefetch Extractions

**Goal:** Remove file I/O subroutines and NAS staging from `ClusterMaster`.

- [x] **TASK-P001** GlobalContextProcessManager [details](./TASK-DETAIL.md#task-p001-globalcontextprocessmanager)
- [x] **TASK-P002** AssetPrefetchProcessManager [details](./TASK-DETAIL.md#task-p002-assetprefetchprocessmanager)
