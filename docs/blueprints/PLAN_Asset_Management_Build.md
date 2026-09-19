<!--STATUS
state: LIVE
build-state: PLAN — the dispatchable breakdown of an approved design. ⛔ NOT a design: every task
  REFERENCES its owning chapter and restates nothing. If this file and the design disagree, the DESIGN wins.
updated: 2026-09-19 (REVISED after the backend session's review of the design — see its §HISTORY)
current-answer: §2 is the stage/task table (15 tasks — B4a and C5 added). §3 is the under-specified
  register (W1-W4 live, all implementer calls; ✅ W5 and W6 both CLOSED by the user 2026-09-19).
  §4 is what this deliberately does not contain. §5 is the dispatch grouping — ✅ ALL THREE batches are
  dispatchable.
stale-below: nothing — new document.
known-rot: nothing; ⚠ but A3's archive-arm rail is RED until B4 lands the mtime restore, by design.
known-conflict: ✅ RESOLVED 2026-09-19 — the `FileManifestEntry` change moved from A1 to C1 (it is the
  node->NAS DTO and publish is its first consumer), so batch ① now carries NO wire-contract change.
  ⛔ The caution still applies to C1: additive and low-risk, but it is a contract.
related-designs:
  - docs/blueprints/RESUME_Assets_And_Occurrences.md — ⭐ the coordinator resumption. §2.4 carries the two
    things a dispatching handoff MUST say (B2 goes FIRST; deferring batch ③ now has a cost).
  - docs/DESIGN_Asset_Management.md — THE owning design for every task here.
  - docs/blueprints/Architect_Question_72_Asset_Lifecycle_And_Distribution.md — the decision record; read
    it when a task's rationale is unclear.
  - docs/DESIGN_Artifact_Staging.md — BUILT. Its (length, mtime) skip is REUSED VERBATIM, not re-derived.
  - docs/DESIGN_Cluster_Load_Phase.md — owns RoleLoadRequirements, which B1 derives the needs tokens from.
-->

# PLAN — **Asset management: the build breakdown**

> ⛔ **No design content here.** Each task names its owning chapter.
> ⭐ **Three increments, 15 tasks.** `A` is the enabler — ⛔ nothing else may start before it.

## 0. How to use this

| | |
|---|---|
| ⭐⭐ **T-1 first** (`R-142`) | the feature suites exist: `StorageGatewayTests`, `ClusterMasterPrefetchTests`, `TheHostsAgreeOnTheScenarioRootTests`. ⛔ Run them BEFORE writing code; add into them. ⭐⭐ **For `B1` also `EveryEcsHostComposesTheLoadPhaseChainRails` + `LoadPhaseChainTests`** — they own `RoleLoadRequirements`. ⛔⛔ **And `B6` must not regress `ClusterMasterPrefetchTests`' six new `L8` rails** — park / resume / reject-second / fail / expire / cancel |
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
| **A1** | `AssetManifestEntry` + `AssetManifest.Diff` returning **THREE sets — added / changed / removed**. ⛔⛔ **NOT the `FileManifestEntry` fields — they moved to `C1`** | `Diff` returns exactly the entries that differ **and** the ones present only on the node. ⭐ Rail the **removed** set explicitly: a one-sided diff passes an added/changed fixture and fails this | design **§3**, **§8**; ⚠ **see §3-W4** |
| **A2** | ⭐⭐ **Recursive enumeration, BOTH sides** — a shared walker used for NAS and node alike | a tree **3+ levels deep** enumerates completely, and every entry's `RelativePath` **preserves the subfolder structure**. ⛔ Rail the depth explicitly: a 1-level walker passes a flat fixture and fails this | design **§1 ①**, **§7.4**; `Q72-K` |
| **A3** | Prove the manifest's freshness semantics against the BUILT skip | a file copied with `File.Copy` compares **equal**; a modified file compares **unequal**. ⭐ This is the rail that shows §2's "one predicate" is true, ⛔ not a new predicate. ⚠ **The ARCHIVE arm is NOT railed here** — it is red until `B4` restores the mtime, and 🔒 **gate row 6 says a new skip is a finding, not a fix** ⇒ ⛔ batch ① must not ship a skip it then files against itself. ⭐ **Say so in the report and point at `B4`**; the case is not forgotten, it is homed | design **§2**, **§7.1**; `DESIGN_Artifact_Staging` §6 |

### Increment B — needs-filtered sync with the transport partition

| # | task | success condition | owning chapter |
|---|---|---|---|
| **B1** | ⭐⭐ **Derive** `hrot.asset.needs.*` from `RoleLoadRequirements` through the **3-row `LoadPart → AssetKind` adapter**, ⭐ **plus one rule for `Brain`: every kind whose contributor exposes a non-null `BaseFolder`** *(⛔ never the phrase "all AI asset kinds" — `G4` retired it)* — ⛔ do NOT author a parallel table, ⛔⛔ do NOT extend `RoleLoadRequirements` *(it would claim a load step nobody implements)*. ✅ **RULED — `Q72-M`, design §7.3a carries the whole map.** ⭐⭐ **The AI-kind set is a PREDICATE — *the contributor has a non-null `BaseFolder`*** — ⛔ **not a list**: `AssetRoots.AssetsRelative:197-204` **throws** for `Blackboard`/`Utility` | the token set is **computed** from the requirement table; changing the table changes the tokens with **no second edit**. ⭐⭐ **Rail `Map2D` explicitly: it gets the KNOWLEDGE BASE and NOT terrain** — ⛔ *"Map2D gets nothing"* would throw `FileNotFoundException` in `KnowledgeBaseLoadStep` on IG. ⭐ Rail the subtraction: a role with no declared consumer loses **that part**, never the node. ⭐ Rail that a rootless kind yields **no** token rather than throwing | design **§7.3**, **§7.3a**, **§6 caption**; `Q72-L` |
| **B2** | `hrot.asset.authors.*` + 🔴🔴 **THE SUBTRACTION, in its THREE clauses** — ① subtract the authored kind · ② the token is **CONFIGURED**, ⛔ never derived from host type or from *"this host has an `AssetRoots`"* · ③ for an authored kind the sync is **ADD-ONLY** — never overwrite, never delete. ⛔⛔ **And BOUNDED: never a `LoadPart`-derived kind** | ⛔⛔ **The task that stops `B` destroying authored work — and clauses ②③ stop it delivering to nobody.** 📐 `CgfSubsystem.DefaultRole = NodeRole.Brain` and `SimHostApp.DefaultRole` has **no Brain** (`:182-183`) ⇒ **every Brain host today is an authoring host**. ⭐⭐ **Four rails, and each pins a different failure:** ① **LOSS** — author locally, sync against a NAS that never saw it, assert the file **survives unchanged** · ② **NON-VACUITY** — a Brain host advertising **no** authorship receives the full mirror · ③ **ADD-ONLY** — an author gets a **new** NAS file, and a **changed** one does **not** overwrite its local copy · ④ **BOUNDED** — a node advertising `authors.scenario` **still receives the scenario** *(else `ScenarioLoadStep.cs:122` throws)*. ⭐ Also: *"nobody authors K"* stays **legal** (the external-tool case) | design **§7.3b**, **§6**; `Q72-J`, `Q72-M` |
| **B3** | `TransportPartitioner` — size and extension/path rules select **standalone** vs **archived** | *one 100 GB file + 3 000 small files* ⇒ **2 transfers**: one standalone copy, one archive. ⛔ The big file is **never** added to an archive | design **§7.2**; `Q72-H1` ⚠ **see §3-W1** |
| **B4** | `AssetSyncService.SyncToNodesAsync` — enumerate, diff, partition, copy, **unpack in place**, ⭐⭐⭐ **then RESTORE each member's mtime from the manifest** (`File.SetLastWriteTimeUtc`) | after a sync the node tree is **byte-and-structure identical** to NAS, subfolders included; a second sync with no NAS change performs **zero** writes. 🔴 **Without the mtime restore this second condition CANNOT PASS for the archived set** — ⭐⭐ **`A3`'s archive arm lives HERE, beside its fix**: *zip a file, unpack it, assert it compares EQUAL to the NAS source* | design **§4**, **§2**, **§7.1** |
| **B4a** | ⭐⭐ **Place the sync INSIDE the prefetch saga** — `AssetPrefetchProcessManager`, never a parallel path | the transition unparks **only** via `PrefetchDistributionCompletedEvent`. ⛔ A parallel path leaves it parked to the 300 s liveness bound — **a hang, not an error**. ⭐ Rail: sync failure ⇒ the parked entry is dropped and **nothing** fans out | design **§4 caption**; `DESIGN_Cluster_Load_Phase` §7 |
| **B5** | Extract `IAssetStorageStrategy`; `ITkbStorageStrategy` **narrows** it | a tree asset and an archive asset are read through one seam. ⛔ `ZipTkbProvider`/`RawDirectoryTkbProvider` keep working **unchanged** — if they need edits, that is a finding | design **§3**; `Q72-D` |
| **B6** | ⭐⭐ **PRESERVE AND RAIL** — do **not** add: failing the load on any sync failure for a **named** artifact is **ALREADY BUILT** | 📐 `L8`: `IsSuccess:false` ⇒ `ClusterMaster` drops the parked entry, publishes `Failure`, fans out **nothing** (rail `A_failed_distribution_fails_the_request_and_fans_out_nothing`). ⇒ the deliverable is a rail proving the **new** sync's failures travel the **same** path — ⛔ a second failure route is a finding | design **§4**, **§7.5**; `Q72-F`; `DESIGN_Cluster_Load_Phase` §7.4 |

### Increment C — publish and the staleness probe *(independent of B; safe to defer)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **C1** | `PublishToNasAsync` — the **explicit**, user-triggered publish, reusing `PullToNasAsync` — ⭐ **and the two new fields on `FileManifestEntry`** (moved here from `A1`: this is that DTO's direction and its first consumer) | publishing copies exactly the differing set to NAS and **nothing else transfers at any other time**. ⛔ No auto-publish on save or on load. ⚠ The DTO change is additive — existing callers compile untouched | design **§5**, **§8**, **§1 ②**; `Q72-I` |
| **C2** | `ProbeAuthoringNodesAsync` — a **summary** per kind `(count, newest mtime)` over `BaseFolder`, ⭐ **comparing in BOTH directions** | the probe reads **no file contents** and parses **no asset**; it runs on a scenario load without measurably extending it. ⭐⭐ **BEHIND must be detectable, not just AHEAD** — §7.3b subtracts authored kinds from the sync, so this is the **only** thing that can tell an author their assets are stale | design **§5**, **§7.3b**, **§1 ⑤** ⚠ **see §3-W2, W3** |
| **C3** | The warn/fail split | an authoring node ahead of NAS ⇒ **WARN**, load continues. A missing **named** artifact ⇒ **FAIL**. ⭐ Rail **both** arms — ⛔ one arm passing proves nothing. ⭐⭐ **A THIRD arm: authoring node BEHIND NAS ⇒ WARN, load continues, ⛔ TRANSFER NOTHING** — §7.3b; the fix is an explicit user act (`W6`), never an automatic overwrite. ⭐⭐ **And a FOURTH rail: author OFFLINE ⇒ no warning, load continues** — 📌 the probe cannot see an offline author, and without this rail a later reader takes the silence for proof | design **§5 caption**, **§7.5**; `Q72-I` |
| **C4** | Surface the publish as a user operation, available **any time** | 🔒 *"expose this as a user-triggerable operation any time"* — reachable without a scenario load in progress | design **§5**; `Q72-I` |
| **C5** | ⭐⭐ **`RefreshFromNasAsync` — the MIRROR of `C1`/`C4`: *"refresh my authored kinds from NAS"***, explicit and user-triggered *(ruled `2026-09-19`: **"w6 — yes, seems useful"**)* | ⛔⛔ **The ONLY route by which an UPDATE reaches a file an authoring station already holds** — §7.3b clause ③ is add-only. ⭐ Reuses `C1`'s diff pointed the other way: exactly the differing set, nothing else. ⭐⭐ **Rail the pair:** a NAS-newer file **does not** move on load *(clause ③)* and **does** move on an explicit refresh. ⛔⛔ **NEVER automatic, never on load, never on save** (`Q72-I`) — ⭐ and it is the author's own folder, so an **overwrite warning naming the files** is part of the operation, not a nicety | design **§5**, **§7.3b**, **§9-W6**; `Q72-I`, `Q72-M` |

---

## 3. THE UNDER-SPECIFIED REGISTER — **three rows, all implementer calls**

| # | blocks | what is missing | who settles it |
|---|---|---|---|
| **W1** | **B3** | the **standalone size threshold** and the default extension/path rule set | ⭐ **implementer**, configurable with a stated default. ⚠ `Q72-H1` ruled the SHAPE (partition, not compression level); the number is tuning. ⛔ Do not re-open the shape. 🔴 **NOT pure tuning until `B4`'s mtime restore lands** — until then the threshold decides how much traffic goes down the arm that re-transfers every sync; **settle it after, or state the coupling** |
| **W2** | **C2** | whether the summary is computed per call or cached on `ContributorChanged` | ⭐ **implementer.** 📐 The walk is stat-only (`design §1 ⑤`) ⇒ **start simple**; cache only if measured slow, and say which you did |
| **W3** | **C2** | how scenarios participate, given `BaseFolder == null` for them | ⭐ **implementer.** ⚠ Scenarios already travel by the prefetch path, so the likely answer is **they do not participate** — ⛔ but that must be **stated in the report**, not assumed silently |
| **W4** | **A1**, **B4** | whether a **removed** NAS entry **deletes** the node's copy or is only reported | ⭐ **implementer, but STATED AND RAILED either way.** ⛔ *"the node MIRRORS NAS"* is not a testable condition until this is answered; a rename leaves an orphan on every node otherwise |
| ~~**W6**~~ | ✅ **CLOSED** | the **mirror of `C4`**: *"refresh my authored kinds from NAS"* | ✅ **RULED by the user `2026-09-19`** — *"w6 — yes, seems useful"*. ⇒ **it is now task `C5`**, not an open row |
| ~~**W5**~~ | ✅ **CLOSED** | ⛔ `LoadPart` ∩ `AssetKind` = **∅**, and behaviour assets have **no** `LoadPart` | ✅ **RULED by the user `2026-09-19`** — a **3-row adapter** owned by the design + **`Brain ⇒ all AI kinds`**; ⛔ `RoleLoadRequirements` is **not** extended. 📄 **the map is design §7.3a; the ruling is `Q72-M`.** ⇒ **batch ② is unblocked** |

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
| **① A** | `A1`–`A3` | ⭐⭐⭐ **the enabler, alone.** Everything else needs the manifest, and `§1 ①` says recursion exists nowhere. ⛔ Landing B or C first builds on sand. ⭐ Small and fully railable without a cluster. ✅ **Dispatchable now** — the wire-DTO change moved out to `C1`, so ① carries **no contract change at all** |
| **② B** | `B1`–`B6` *(7 tasks — `B4a` added)* | ⭐ the increment that makes *"nodes keep just copies they really need"* true. ⚠ Largest; `B5`'s seam extraction rides here because `B4` needs it. ✅ **Unblocked — `W5` ruled `2026-09-19`.** 🔴🔴 **`B2`'s SUBTRACTION (design §7.3b) is not optional and not last** — ⛔ without clause ① every other task in ② mirrors NAS onto the author's own folder; ⛔ without clauses ②③ the AI sync **reaches no host at all**; ⛔ unbounded, it breaks the editor's own scenario load |
| **③ C** | `C1`–`C5` | ⭐ independent of B and **safe to defer**; ⛔ the only part that touches authoring hosts. ⚠ **But deferring it has a COST now:** §7.3b clause ③ is add-only, so until `C5` lands an authoring station has **no route at all** to an update — ⭐ `C3` can only warn |

⭐⭐ **Reporting:** the gate-report contract rows 1–7 per batch. ⛔⛔ **Row 8 BINDS ② and ③** — both change
what nodes hold before a load, and ③ reaches across to authoring hosts. Name the integration suite and
report **running** it, or state with base-sha evidence why it cannot gate.

⚠ **And one acceptance only ② can give:** a **tree** asset with subfolders syncing completely. 📐 `§1 ①`
means that has never worked — `PrefetchScenarioAsync` is flat — so ② is the first time a nested asset
reaches a node intact. ⛔ A green unit rail is not that proof; **sync a 3-level tree and diff it.**
