# BUG1 — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1 — Infrastructure & Configuration

**Goal:** Fix the DDS domain bug that causes SimHost to silently join the wrong domain, add the
`--node-id` CLI flag for multi-instance support, and fix the batch scripts' working directory.

- [x] **BUG1-F001** Fix SimHost DDS Domain Zero Guard [details](./TASK-DETAIL.md#bug1-f001-fix-simhost-dds-domain-zero-guard)
- [x] **BUG1-F002** Add `--node-id` CLI Option to Runner [details](./TASK-DETAIL.md#bug1-f002-add---node-id-cli-option-to-runner)
- [x] **BUG1-F003** Fix Batch Script Working Directory [details](./TASK-DETAIL.md#bug1-f003-fix-batch-script-working-directory)

> **BUG1-C001 IOS Context Menu Delete Action** — Already implemented (Delete in
> `MenuStrategy.Standard`). No work required.

---

## Phase 2 — Network Correctness

**Goal:** Stop non-authoritative nodes from emitting spurious ACKs, and ensure all descriptor topic
instances are disposed when an entity is deleted.

- [x] **BUG1-N001** Enforce Silent Bystander Rule in `UpdateEntityDescriptorRequestSystem` [details](./TASK-DETAIL.md#bug1-n001-enforce-silent-bystander-rule-in-updateentitydescriptorrequestsystem)
- [x] **BUG1-N002** Fan-Out Entity Descriptor Disposal [details](./TASK-DETAIL.md#bug1-n002-fan-out-entity-descriptor-disposal)

---

## Phase 3 — IG Continuous Drag Mode

**Goal:** Add a debug-panel toggle that sends throttled WorldPos updates during entity drag so
developers can observe network latency in real time.

- [x] **BUG1-I001** Add Continuous Drag Update Toggle to IG [details](./TASK-DETAIL.md#bug1-i001-add-continuous-drag-update-toggle-to-ig)

---

## Phase 4 — Mission System Fixes

**Goal:** Fix entity stopping at the first waypoint due to an absent trigger, and fix the OCC
version conflict that occurs after clicking ABORT then COMMIT.

- [x] **BUG1-M001** Default `DoctrineFinished` Trigger on Task Creation [details](./TASK-DETAIL.md#bug1-m001-default-doctrinefinished-trigger-on-task-creation)
- [x] **BUG1-M002** Track Control Commands for OCC Version Sync [details](./TASK-DETAIL.md#bug1-m002-track-control-commands-for-occ-version-sync)
