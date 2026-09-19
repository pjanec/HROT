<!--STATUS
state: LIVE
build-state: PLAN — the dispatchable breakdown of an approved design. ⛔ NOT a design: every task
  REFERENCES its owning chapter and restates nothing. If this file and the design disagree, the DESIGN wins.
updated: 2026-09-19
current-answer: §2 is the stage/task table. §3 is the under-specified register (3 rows, all implementer
  calls). §4 is what this deliberately does not contain. §5 is the dispatch grouping.
stale-below: nothing — new document.
known-rot: nothing yet.
known-conflict: ⚠ A1 changes `FileManifestEntry`, a WIRE DTO in Hrot.Network.Orchestration. Additive and
  low-risk, but it is a contract change — plan it, do not discover it.
related-designs:
  - docs/DESIGN_Asset_Management.md — THE owning design for every task here.
  - docs/blueprints/Architect_Question_72_Asset_Lifecycle_And_Distribution.md — the decision record; read
    it when a task's rationale is unclear.
  - docs/DESIGN_Artifact_Staging.md — BUILT. Its (length, mtime) skip is REUSED VERBATIM, not re-derived.
  - docs/DESIGN_Cluster_Load_Phase.md — owns RoleLoadRequirements, which B1 derives the needs tokens from.
-->

# PLAN — **Asset management: the build breakdown**

> ⛔ **No design content here.** Each task names its owning chapter.
> ⭐ **Three increments, 13 tasks.** `A` is the enabler — ⛔ nothing else may start before it.

## 0. How to use this

| | |
|---|---|
| ⭐⭐ **T-1 first** (`R-142`) | the feature suites exist: `StorageGatewayTests`, `ClusterMasterPrefetchTests`, `TheHostsAgreeOnTheScenarioRootTests`. ⛔ Run them BEFORE writing code; add into them |
| ⭐⭐ **reuse, do not re-derive** | `IsAlreadyCurrent`'s `(length, mtime)` predicate is **BUILT and railed**. ⛔ A second freshness rule is a review finding |
| ⭐ **ids** | ⛔ the coordinator allocated NONE (rule 3). Number them into the tracker; state them in the report |
| ⚠ **the wire DTO** | `A1` touches `FileManifestEntry`. Additive, but it is a contract — see `known-conflict` |

---

## 1. WHAT THIS BUILDS, IN ONE PARAGRAPH

A recursive per-file manifest — `(relative path, length, mtime)` — on **both** the NAS and the node, so a
sync can diff before it copies; a needs filter **derived** from the load phase's existing
`RoleLoadRequirements` so a node receives only what it will load; a transport that **partitions** the
differing set (big or already-compressed files travel standalone, the rest as one archive, unpacked on
arrival so the node mirrors NAS exactly, subfolders and all); and an **explicit publish** from authoring
hosts with a cheap staleness probe on load. ⛔ **No new capability facility, no new freshness rule, no new
cluster op.**

---

## 2. THE TASKS

### Increment A — the manifest and the recursive walk *(the enabler — ⛔ nothing else starts first)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **A1** | `AssetManifestEntry` + `AssetManifest.Diff`, and the two new fields on `FileManifestEntry` | a manifest of a nested tree round-trips through the wire DTO with **length and mtime intact**; `Diff` returns exactly the entries that differ. ⚠ Additive to the DTO — existing callers compile untouched | design **§3**, **§1 ②** |
| **A2** | ⭐⭐ **Recursive enumeration, BOTH sides** — a shared walker used for NAS and node alike | a tree **3+ levels deep** enumerates completely, and every entry's `RelativePath` **preserves the subfolder structure**. ⛔ Rail the depth explicitly: a 1-level walker passes a flat fixture and fails this | design **§1 ①**, **§7.4**; `Q72-K` |
| **A3** | Prove the manifest's freshness semantics against the BUILT skip | a file copied with `File.Copy` compares **equal** on both sides; a modified file compares **unequal**. ⭐ This is the rail that shows §2's "one predicate" is true, ⛔ not a new predicate | design **§2**, **§7.1**; `DESIGN_Artifact_Staging` §6 |

### Increment B — needs-filtered sync with the transport partition

| # | task | success condition | owning chapter |
|---|---|---|---|
| **B1** | ⭐⭐ **Derive** `hrot.asset.needs.*` from `RoleLoadRequirements` — ⛔ do NOT author a parallel table | the token set a node advertises is **computed** from the requirement table; changing the table changes the tokens with **no second edit**. ⭐ Rail: a role with no declared consumer yields **no** need token | design **§7.3**; `Q72-L`; `DESIGN_Cluster_Load_Phase` §4 |
| **B2** | `hrot.asset.authors.*`, and *"nobody authors K"* as a **legal** answer | a kind with no advertised author is **not** an error — it is the external-tool case. ⭐ Rail that the probe does not look for a publisher for such a kind | design **§6**; `Q72-J` |
| **B3** | `TransportPartitioner` — size and extension/path rules select **standalone** vs **archived** | *one 100 GB file + 3 000 small files* ⇒ **2 transfers**: one standalone copy, one archive. ⛔ The big file is **never** added to an archive | design **§7.2**; `Q72-H1` ⚠ **see §3-W1** |
| **B4** | `AssetSyncService.SyncToNodesAsync` — enumerate, diff, partition, copy, **unpack in place** | after a sync the node tree is **byte-and-structure identical** to NAS, subfolders included; a second sync with no NAS change performs **zero** writes | design **§4**, **§2** |
| **B5** | Extract `IAssetStorageStrategy`; `ITkbStorageStrategy` **narrows** it | a tree asset and an archive asset are read through one seam. ⛔ `ZipTkbProvider`/`RawDirectoryTkbProvider` keep working **unchanged** — if they need edits, that is a finding | design **§3**; `Q72-D` |
| **B6** | Fail the load on any sync failure for a **named** artifact | ⛔ **ANY** failure count > 0 fails; ⭐ the existing prefetch slot already throws, so this is preserving behaviour, not adding it | design **§4**, **§7.5**; `Q72-F` |

### Increment C — publish and the staleness probe *(independent of B; safe to defer)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **C1** | `PublishToNasAsync` — the **explicit**, user-triggered publish, reusing `PullToNasAsync` | publishing copies exactly the differing set to NAS and **nothing else transfers at any other time**. ⛔ No auto-publish on save or on load | design **§5**, **§7.5**; `Q72-I` |
| **C2** | `ProbeAuthoringNodesAsync` — a **summary** per kind `(count, newest mtime)` over `BaseFolder` | the probe reads **no file contents** and parses **no asset**; it runs on a scenario load without measurably extending it | design **§5**, **§1 ⑤** ⚠ **see §3-W2, W3** |
| **C3** | The warn/fail split | an authoring node ahead of NAS ⇒ **WARN**, load continues. A missing **named** artifact ⇒ **FAIL**. ⭐ Rail both arms — ⛔ one arm passing proves nothing | design **§5**, **§7.5**; `Q72-I` |
| **C4** | Surface the publish as a user operation, available **any time** | 🔒 *"expose this as a user-triggerable operation any time"* — reachable without a scenario load in progress | design **§5**; `Q72-I` |

---

## 3. THE UNDER-SPECIFIED REGISTER — **three rows, all implementer calls**

| # | blocks | what is missing | who settles it |
|---|---|---|---|
| **W1** | **B3** | the **standalone size threshold** and the default extension/path rule set | ⭐ **implementer**, configurable with a stated default. ⚠ `Q72-H1` ruled the SHAPE (partition, not compression level); the number is tuning. ⛔ Do not re-open the shape |
| **W2** | **C2** | whether the summary is computed per call or cached on `ContributorChanged` | ⭐ **implementer.** 📐 The walk is stat-only (`design §1 ⑤`) ⇒ **start simple**; cache only if measured slow, and say which you did |
| **W3** | **C2** | how scenarios participate, given `BaseFolder == null` for them | ⭐ **implementer.** ⚠ Scenarios already travel by the prefetch path, so the likely answer is **they do not participate** — ⛔ but that must be **stated in the report**, not assumed silently |

---

## 4. What this plan deliberately does NOT contain

⛔ A content hash or sidecar — 🔒 ruled out; `DESIGN_Artifact_Staging` §6 records the accepted risk.
⛔ A second freshness rule — the BUILT `(length, mtime)` predicate is reused verbatim.
⛔ A new capability facility — `AQ-70`'s exists; both vocabularies are namespaces on it.
⛔ A new `ClusterOpType` / `NodeOpType` — the prefetch trajectory slot already exists.
⛔ Node-to-node asset transfer — 🔒 the NAS is the only path, which is what makes *"master source"* true.
⛔ Publishing terrain/road-net assets — external tools write those to NAS directly (`Q72-J`).
⛔ A standby/cache-ahead node — deferred by `DESIGN_Cluster_Load_Phase` §5.1; it is what would widen `B1`.

---

## 5. DISPATCH GROUPING

| batch | tasks | ⭐ why this boundary |
|---|---|---|
| **① A** | `A1`–`A3` | ⭐⭐⭐ **the enabler, alone.** Everything else needs the manifest, and `§1 ①` says recursion exists nowhere. ⛔ Landing B or C first builds on sand. ⭐ Small and fully railable without a cluster |
| **② B** | `B1`–`B6` | ⭐ the increment that makes *"nodes keep just copies they really need"* true. ⚠ Largest; `B5`'s seam extraction rides here because `B4` needs it |
| **③ C** | `C1`–`C4` | ⭐ independent of B and **safe to defer**; ⛔ the only part that touches authoring hosts |

⭐⭐ **Reporting:** the gate-report contract rows 1–7 per batch. ⛔⛔ **Row 8 BINDS ② and ③** — both change
what nodes hold before a load, and ③ reaches across to authoring hosts. Name the integration suite and
report **running** it, or state with base-sha evidence why it cannot gate.

⚠ **And one acceptance only ② can give:** a **tree** asset with subfolders syncing completely. 📐 `§1 ①`
means that has never worked — `PrefetchScenarioAsync` is flat — so ② is the first time a nested asset
reaches a node intact. ⛔ A green unit rail is not that proof; **sync a 3-level tree and diff it.**
