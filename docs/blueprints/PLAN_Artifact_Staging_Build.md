<!--STATUS
state: LIVE
build-state: PLAN — the dispatchable breakdown of an approved design. ⛔ NOT a design: every task
  REFERENCES its owning chapter and restates nothing. If this file and the design disagree, the DESIGN wins.
updated: 2026-09-18
current-answer: §2 is the stage/task table (the dispatchable unit). §3 is the under-specified register —
  ONE row, and it is an implementer measurement. §4 is what this deliberately does not contain.
stale-below: nothing — new document.
known-rot: nothing yet.
known-conflict: none. ⚠ S3 edits TkbLoadClusterStateHandler, which the terrain programme's C5 mirrored
  but did not modify; the terrain loader inherits S3's change rather than colliding with it.
related-designs:
  - docs/DESIGN_Artifact_Staging.md — THE owning design for every task here.
  - docs/designs/tkb-1/DESIGN.md — §7.3 owns the node-side read + its cache key.
  - docs/designs/cluster-master-cqrs-1/DESIGN.md — owns the prefetch saga S2 extends.
  - docs/DESIGN_Terrain_Zones_And_Assets.md — BP-550 is closed by S2+S3; the terrain loader mirrors the
    TKB one and inherits both skips unchanged.
-->

# PLAN — **Artifact staging: the build breakdown**

> ⛔ **This file carries NO design content.** Each task names its owning chapter.
> ⭐ **One batch, 8 tasks.** Stage order is **S1 → S2 → S3 → S4**; only S4 may run in parallel.

## 0. How to use this

| | |
|---|---|
| ⭐⭐ **T-1 first** (`R-142`) | the feature's own suites exist: `StorageGatewayTests`, `ClusterMasterPrefetchTests`, `TkbLoadClusterStateHandlerTests`. ⛔ Run them BEFORE writing code and add into them, not beside them |
| ⭐ **ids** | ⛔ the coordinator allocated NONE (rule 3). Number them into the tracker and state them in the report (rule 5) |
| ⚠ **§3 first** | `V1` decides a path shape S1 and S3 both depend on — settle it in S1 and say so |
| ⭐ **the whole point** | closes **`BP-550`** and makes `T3` meaningful for zones for the first time |

---

## 1. WHAT THIS BUILDS, IN ONE PARAGRAPH

The NAS gains a `tkb/` folder beside `scenarios/`; the prefetch step that already runs before every
transition — and which **already reads `Header.TkbName`** — starts returning that name instead of
discarding it, and copies the named zip plus the scenario header into each node's TKB staging directory.
Both the transfer and the in-memory ingest are skipped when the node already holds the same bytes, keyed
on **`(length, lastWriteTimeUtc)`**, which works cluster-wide because `File.Copy` preserves the source's
timestamp. ⛔ **No hash, no sidecar, no new cluster op, no new wire value.**

---

## 2. THE TASKS

### Stage S1 — The path authority *(everything else depends on this being settled)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **S1a** | Add `TkbDirectoryName` and `GetNodeTkbStagingRoot(stagingRoot, nodeId)` to `OrchestrationConstants`, beside the scenario pair | the constants exist and are the **only** place the string `"TKB"` is built; ⛔ no path is assembled by hand anywhere else | design **§3**, **§1 ⑥** |
| **S1b** | ⚠ **Settle `V1`** — make the node's TKB reader resolve the **same** root the orchestrator writes to | a rail proves the orchestrator's destination and the node's read path are **the same directory** for a given nodeId. ⭐ Precedent: `NodeBootstrapper.cs:325` already threads `nodeId` into `ReferenceArchiveHandler` | design **§8-V1** ⚠ **resolves §3-V1** |

### Stage S2 — The orchestrator half

| # | task | success condition | owning chapter |
|---|---|---|---|
| **S2a** | `CheckTkbNameConsensus` **returns** the agreed name instead of discarding it | the existing fail-loud-on-disagreement behaviour is **unchanged** (its rail still passes); the name is now available to the caller | design **§1 ④**, **§3** |
| **S2b** | Prefetch copies `{nas}/tkb/{name}.zip` → the node TKB root, in the same parallel pass | a scenario whose header names `Alpha_v1` leaves `Alpha_v1.zip` on **every** target node. ⚠ A scenario naming **no** TKB copies nothing and does **not** fail | design **§4** |
| **S2c** | Prefetch writes the agreed header to `{nodeTkbRoot}/ScenarioHeader.json` | the file the node's `ExtractTkbNameFromLocalScenario` reads now **exists in production**. ⭐⭐ **This is the half that makes the failure loud instead of silent** | design **§2** |
| **S2d** | `IsAlreadyCurrent(src, dest)` — skip the copy when **length AND mtime** both match | copying twice with no NAS change performs **zero** file writes the second time; a changed NAS artifact **does** copy. ⭐ Rail both, and rail that a **missing** destination copies | design **§6** |

### Stage S3 — The node half

| # | task | success condition | owning chapter |
|---|---|---|---|
| **S3a** | Add **length** to the node's differential cache key | the handler skips ingest on `(name, length, mtime)` match and ingests otherwise. ⛔ The existing `(name, mtime)` behaviour is a subset — no rail of it may go red | design **§6**; `tkb-1` **§7.3** |
| **S3b** | ⭐⭐ **The compose rail** — the two skips together | ① prefetch twice, load twice ⇒ **one** transfer and **one** ingest. ② change the NAS artifact ⇒ transfer **and** ingest. ③ restart the node with a current file ⇒ **no** transfer, **one** ingest. ⛔ ③ is the one that proves they are independent | design **§6** (the four-row table) |

### Stage S4 — Terrain inherits it *(may run in parallel with S3)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **S4** | Confirm the terrain loader inherits both skips with **no** new code, and close `BP-550` | a scenario naming a terrain resolves its definition from the staged header on every node; the tracker row is closed citing the rail. ⛔ **If terrain needs its own code path, that is a FINDING** — the design says it mirrors the TKB loader field for field | design **§3**, **§5**; `DESIGN_Terrain_Zones_And_Assets` **§2.1e ②a** |

---

## 3. THE UNDER-SPECIFIED REGISTER — **one row**

| # | blocks | what is missing | who settles it |
|---|---|---|---|
| **V1** | **S1b**, **S3** | the node handler takes the **bare** staging root (`NodeBootstrapper.cs:331` → `{root}/TKB`) while the orchestrator writes **per node** (`{root}/nodes/node-N/...`). Those coincide on one-node-per-machine and differ in a co-located runner | ⭐ **implementer measurement — NOT a user decision.** ⭐ **Lean: make the TKB handler node-aware**, mirroring `ReferenceArchiveHandler(localTempRoot, nodeId)` four lines above it. ⛔ **What would flip it:** if `ResolveStagingRoot()` is already per-node on every deployment shape, the bare root is right and the helper takes no nodeId. ⚠ **Measure both shapes before choosing** — the co-located ClusterRunner is the one that exposes the difference |

---

## 4. What this plan deliberately does NOT contain

⛔ A content hash or sidecar — 🔒 ruled out by the user `2026-09-18`; design §6 records the accepted risk.
⛔ Publishing TKB zips to the NAS — an authoring/build concern, upstream of everything here.
⛔ Any new `ClusterOpType` / `NodeOpType` — ⭐ the trajectory already has the slot (`TransitionPlanner.cs:150`).
⛔ Changes to the 2PC round, the cluster state machine, or the zone ops — distribution is upstream of all three.
⛔ Fixing `NedTkbCatalog.RegisterAll` as a fallback — ⚠ it stays as the no-TKB-named path, which is legal.

---

## 5. GATES — what the report must carry

⭐ Rows 1–7 of the gate-report contract (`CLAUDE.md` §"THE GATE REPORT CONTRACT").
⛔⛔ **Row 8 BINDS** — this changes what every node holds before a load. ⭐ Name the integration suite that
would break if the invariant broke and report **running** it, or state with base-sha evidence why it
cannot gate *(the `ClusterRunner.Integration.Tests` DDS-allocator crash is the known un-gateable case —
say so rather than omitting it)*.
⭐⭐ **And the one thing only this batch can report:** `T3` — 🔒 **until this lands, an end-to-end
create-from-seed THROWS by design** (`DESIGN_Terrain_Zones_And_Assets` §8.3 `N4`). ⇒ **after S4, run it**,
and say whether it is green for the first time. ⛔ That is the real acceptance test for this programme,
not any unit rail.
