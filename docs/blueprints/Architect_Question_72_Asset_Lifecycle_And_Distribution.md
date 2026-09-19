<!--STATUS
state: LIVE
build-state: ✅ RESOLVED 2026-09-19 — all sub-questions ruled by the user. ⛔ Still NOT a design: no UML,
  no task breakdown. The next step is a DESIGN + PLAN, not a dispatch. ⚠ Number 72 taken as the next free
  across ALL active branches (rule 3a; 67-71 in use).
updated: 2026-09-19
current-answer: ✅✅ EVERY SUB-QUESTION IS NOW RULED — Q72-A..G (§4a), Q72-H / H1 (§3a), Q72-I, Q72-J,
  Q72-K (subfolders of any depth) and ⭐ Q72-M (2026-09-19: the LoadPart->AssetKind adapter; the load
  table is NOT extended). ⚠ Q72-M NARROWS Q72-L — read them together, Q72-L alone is insufficient. ⭐⭐ The load-bearing content is §3a: the THREE FORMS (NAS form ==
  node form always — now meaning shape AND structure; freshness keyed on the at-rest per-file manifest,
  NEVER on the transport) and the transport PARTITION (big/pre-compressed parts travel standalone, the
  rest as one archive, paths preserved). ⚠⚠ READ §5c + Q72-K BEFORE PLANNING: recursive enumeration
  exists NOWHERE today, the NAS half is entirely NEW CODE, and the shipped artifact-staging slice covers
  the single-file zip case only — a TREE asset in subfolders would be silently truncated.
  ⇒ this document is READY TO BECOME A DESIGN + PLAN.
stale-below: nothing — new document.
known-rot: nothing yet.
known-conflict: none. ⚠ DESIGN_Artifact_Staging.md is IN FLIGHT as a dispatched batch and is deliberately
  a subset of this picture; §4 records what it settles so this document does not reopen it. ⚠⚠ §5c records
  a measured LIMIT of that slice (PrefetchScenarioAsync is flat, so the TKB tree form is not covered) —
  in scope HERE, not a defect there.
related-designs:
  - docs/DESIGN_Asset_Management.md — ⭐⭐ THE DESIGN THIS QUESTION BECAME (2026-09-19). It owns the UML,
    the seams and the three increments; this document owns the RULINGS and the measurement register that
    justify them. ⛔ Do not re-open a ruling in the design — argue it here.
  - docs/blueprints/PLAN_Asset_Management_Build.md — the 13-task dispatchable breakdown of that design.
  - docs/DESIGN_Artifact_Staging.md — the TKB slice, DISPATCHED 2026-09-18. It owns the NAS->node push for
    ONE artifact kind with a (length, mtime) skip. This document owns the general case it is a subset of.
  - docs/blueprints/Architect_Question_70_Host_Capabilities_And_Reliable_Init_Degradation.md — OWNS the
    capability-token facility (namespaced token set on NodeCapabilities; roles DERIVED from fdp.role.*).
    Q72-A/B build on it and add no new facility.
  - docs/designs/cluster-master-cqrs-1/DESIGN.md — owns the prefetch saga and the storage gateway, in BOTH
    directions (PushToNodes and PullToNas).
  - docs/designs/tkb-1/DESIGN.md — owns the TKB storage-strategy VFS seam (ITkbStorageStrategy) that
    Q72-D proposes to generalise.
  - docs/DESIGN_Terrain_Zones_And_Assets.md — owns the terrain definition asset whose schema Q72-A would
    extend with a component vocabulary.
  - docs/DESIGN_Cluster_Load_Phase.md — ⭐⭐⭐ owns WHICH ROLE LOADS WHICH PART (RoleLoadRequirements, the
    per-role ordered chain) and WHEN, inside the Loading* phase. Q72-L measures that this is the natural
    SOURCE of the hrot.asset.needs.* token set rather than a competing vocabulary: it decides what a node
    loads into memory, this document decides what bytes reach its disk beforehand.
-->

# Architect Question #72 — **Asset lifecycle and distribution**

## 0. THE REQUIREMENT — in the user's words

> 🔒 **User, `2026-09-18`:** *"Terrain data size is usually not neglectable and terrain presence is not
> necessary on all hosts/nodes."* · *"Maybe we could let nodes to publish NeedsTerrain/NeedsTKB/
> NeedsScenario and similar flags in their capability list?"* · *"add some automatic multi-file/folder
> data packaging mechanism to copy a small amount of zips (one per resource type) instead of many
> files"* · *"make terrain loader agnostic to the file system, same as the TKB loader is"* ·
> *"not all hosts might need same terrain data files, stride host will need stride assets while some
> other might need just road network"* · *"Maybe 'NeedsStrideTerrain' flag? Or maybe something more
> sophisticated like publishing the asset requirements separately?"*

> 🔒 **The invariant, verbatim:** *"i want the loadable assets to live on NAS as the master source and
> nodes to keep just copies they really need and automatic file sync from NAS to nodes if nodes are not
> up to date, ensuring that scenario always loads with consistent data everywhere needed. **Error during
> sync should fail the scenario loading.**"*

> 🔒 **And the scope warning, which is why this is a question and not a plan:** *"it might span across the
> behavior assets as well and should count with the authoring of the assets — meaning they might need to
> be also synced from the authoring nodes (editor capability — different nodes per asset type). Pretty
> wide topic."*

### 0a. ROUND 2 — the requirements that came WITH the approval *(user, `2026-09-19`)*

> 🔒 *"packaging rather at authoring time; **not always a single zip** though, i can imagine cases where
> each terrain component (roadmap, navmesh, heightmap, buildings etc.) need their own package and **some
> may need to be kept as file trees** (where the automatic packaging might still be useful to reduce the
> number of files)."*

> 🔒 *"the terrain is largely **unimplemented** at this time so this is mostly a theoretical question, but
> anyway **i need to setup the rules and have the infrastructure support it**; this includes the file tree."*

> 🔒 ⭐⭐ **The worked example:** *"TKB … supports both file tree AND zip reading so if it happens to be on
> NAS in file tree form, we need it **synced to nodes in same form, but not file by file** (TKB could be
> hundreds of small files)."*

> 🔒 *"**No node advertises authoring role today; every CGF host should be capable of editing in same way
> as the editor host.**"* · *"**not all assets are authorable by our hosts** — things like road nets and
> terrain components will come from some **external tools directly to NAS** storage."*

> 🔒 ⭐⭐ **The behaviour-asset case:** *"behavior assets need to be synced to **every brain role host** and
> they can be numerous and will certainly **benefit from zipping during transport but to stay editable
> they need to be present as individual files**."*

> 🔒 **The unresolved half:** *"When they are saved it is now happening to the **local disk storage** and
> how to handle their syncing to NAS and from there to other nodes is **still unresolved**. We certainly
> **do not want the sync to happen every time** (because there are things like auto-save and syncing is a
> long operation), also **at authoring time the other nodes might not be running at all** (only the
> authoring node is). So the sync might need to first **ask the authoring nodes that are online if they
> have something more up to date than what is on NAS**; i guess this is something that can happen on every
> scenario load **only if it is cheap**, and in any case we should **expose this as a user-triggerable
> operation any time (publish authored changes to NAS)**."*

## 1. INVENTORY — measured `2026-09-18`, extended `2026-09-19`

⚠ **Coverage caveat, stated rather than implied:** these are **grep-corroborated, not graph-enumerated**,
and `check_index_coverage` was not reachable this session. ⭐ Treat the counts as a floor, not a proven
complete set.

⚠⚠ **CORRECTION `2026-09-19` — I mis-attributed the cause, and the build found the real one.** An earlier
version of this caveat said *"the graph index returned 0 results for several of these patterns"*, implying
an index-coverage problem. 📐 **`BP-558`:** `scripts/find.sh` given a regex **alternation** returns
`graph 0 · grep 0` **and prints "The two agree on the file set"** — for classes that demonstrably exist.
⇒ 🔴 **the zero was a TOOLING DEFECT, not a coverage fact**, and it is exactly the silent degradation
`CLAUDE.md` promises the script will never do — ⛔ made worse because **both halves appear to corroborate.**
⚠ **I hit it twice while building this INVENTORY** *(the `Sha256\|Checksum\|ContentHash` and
`localStagingRoot\|…` queries)* and read the grep half without questioning the graph zero.

| # | what exists | where | ⭐ bearing on this question |
|---|---|---|---|
| ① | **`AssetKind`** = `Blueprint`, `BTree`, `Hsm`, `Blackboard`, `Utility`, `Scenario` — **6** | `AssetKind.cs:5-10` | 🔴 **Terrain and TKB are NOT asset kinds.** The authoring vocabulary and the runtime-artifact vocabulary are **disjoint today** |
| ② | **`CapabilityTokens`** — the AQ-70 facility, general and namespaced | `CapabilityTokens.cs` | 🔴 **exactly ONE feature token exists**: `fdp.reliable-init`. The extension point is built and **essentially unused** |
| ③ | roles are **DERIVED** from the `fdp.role.*` token subset | AQ-70 §Q70-C, `NodeRoleTokens` | ⭐⭐ **precedent that a POLICY can be computed from tokens** — which is exactly what "who needs which artifact" is |
| ④ | **`ITkbStorageStrategy`** + `ZipTkbProvider` (read-only) + `RawDirectoryTkbProvider` + `TkbUnifiedLoader` | `Fdp.Toolkits/Tkb/Vfs/` | ⭐⭐⭐ **the filesystem-agnostic seam the user wants terrain to match ALREADY EXISTS** — for TKB only |
| ⑤ | **`PullToNasAsync(IReadOnlyList<FileManifestEntry>, nasBasePath)`** — the **node→NAS** direction | `StorageGatewayModule.cs:101` | ⭐⭐ **the authoring-sync direction is already built**, driven by consensus aggregators + manifests (`StorageProcessManager`, `DiagnosticsDumpProcessManager`) |
| ⑥ | `PushToNodesAsync` / `PrefetchScenarioAsync` — the **NAS→node** direction | `:167` / `:229` | the artifact-staging batch extends ⑥ for TKB |
| ⑦ | **`TerrainDefinition`** — versioned root, `Name`, `RoadNetworks[]` | `Terrain/TerrainDefinition.cs` | ⭐ `SchemaVersion` exists **precisely so a field can be added**; ⛔ there is **no component-kind vocabulary** yet |
| ⑧ | NAS layout constants: `scenarios` · `exercises` · `episodes` · `shared` | `OrchestrationConstants.cs:21-43` | ⛔ no per-asset-kind NAS roots |
| ⑨ | `AssetRoots` — `Assets/` (Blueprint, Hsm, BTree) + `Recipes/` (+ Scenarios) | `AssetRoots.cs` | ⭐ authoring roots exist and are **config-driven** (ruling 67); ⛔ they are **local**, with no NAS relationship |
| ⛔ | any packaging/zip **writer** | — | **none.** `ZipTkbProvider` is `ZipArchiveMode.Read`, strictly read-only at runtime |
| ⑩ | **`PrefetchArchiveAsync`** — per-node `.fdp` archives NAS→node | `StorageGatewayModule.cs:365` | ⭐⭐ **archive-as-transport prior art already exists** and is a sibling of the file-copy path |
| ⑪ | **`ExportArchiveAsync` / `ImportArchiveAsync`** + `ClusterOpType.ExportArchive(6)`/`ImportArchive(7)` | `EventDrivenStorageGateway.cs:16,19`; `StorageProcessManager` | ⭐ the **node↔NAS archive round trip** exists as cluster ops — ⛔ for exercise archives, not assets |
| ⑫ | **CGF already composes the authoring surface** — `WireAssetCreation` + the picker shell | `CgfSubsystem.cs:2205`; `DESIGN_Cgf_Asset_Picker_Shell_Slice` | ⭐⭐⭐ *"every CGF host should be capable of editing like the editor"* is **largely already TRUE** (shipped as `CE-049`) |
| ⑬ | behaviour assets are saved to **`AssetRoots.AssetsFor(Kind)`** — a **local**, config-driven root | `BlueprintAssetContributor.cs:32`, `HsmNewAssetService.cs:42`, `BTreeNewAssetService.cs:42` | 🔴 **local only.** ⛔ There is **no NAS relationship for authored assets at all** — this is the *"still unresolved"* half, and it is unresolved in the code too |

## 2. WHAT THE INVENTORY CHANGES ABOUT THE QUESTION

⭐⭐⭐ **Three of the four things the user proposed already have a home.** The question is therefore
**much less about mechanism than it first appears**, and much more about **vocabulary and ownership**:

| the user's idea | ⭐ what it actually needs |
|---|---|
| *"NeedsTerrain/NeedsTKB flags in the capability list"* | ⛔ **no new facility** — AQ-70's token set is built, namespaced, and has one token in it. This would be its **first real feature vocabulary** |
| *"make terrain loader agnostic to the file system, same as the TKB loader"* | ⛔ **no new seam** — `ITkbStorageStrategy` is that seam. The work is **generalising it beyond TKB**, not inventing it |
| *"synced from the authoring nodes"* | ⛔ **no new direction** — `PullToNasAsync` + `FileManifestEntry` already move node→NAS, driven by a consensus aggregator |
| *"one zip per resource type instead of many files"* | ⭐ **this one IS new** — nothing writes an archive today, and it **interacts with a ruling** (see `Q72-C`) |

🔴 **And one structural fact worth surfacing before any decision:** ① says the **authoring** vocabulary
(`AssetKind`) and the **runtime-artifact** vocabulary (TKB, terrain) are disjoint. ⚠ Every option below
either unifies them or deliberately keeps them apart — ⛔ **that choice is upstream of all the others**,
and it is `Q72-A`.

## 3. THE SUB-QUESTIONS

### `Q72-A` — **WHO declares the requirement: the node, or the asset?** ⭐ *the upstream one*

| option | shape | ⚠ cost |
|---|---|---|
| **A1** node declares artifact KINDS | `hrot.asset.needs.terrain.stride`, `…needs.tkb` | ⛔ **the token vocabulary grows with every asset kind forever**, and each new terrain component is a new token every host must learn |
| ⭐ **A2 INTERSECTION** *(lean)* | the **asset** declares its components (`TerrainDefinition.Components[]`: `roadnet`, `stride`, `navmesh`); the **node** declares which component kinds it consumes; the orchestrator **intersects** and stages only that | ⭐ token set stays **small and stable** while asset vocabulary grows freely; ⚠ needs a schema field on ⑦ and a naming convention |
| **A3** role-derived, no new tokens | infer need from `NodeRole` | ⛔⛔ **measured dead on arrival**: `Map2D` taught us a role is an ownership policy, and the terrain programme proved the editor sits outside the role that "obviously" implies its needs |

⭐⭐ **My lean: A2.** 📐 The deciding measurement is ⑦ — `TerrainDefinition` **already has a versioned
root added for exactly this kind of extension**, and ③ proves a policy can be derived from tokens.
⚠ **What would flip it:** if component kinds turn out to be a closed set of 3-4 that never grows, A1 is
simpler and A2 is over-engineering. **I have not measured how many Stride terrain component kinds exist**
— that is the one thing I would measure before you rule.

### `Q72-B` — **the token namespace**

⭐ **Lean: `hrot.asset.needs.<kind>`**, mirroring `fdp.role.*` / `fdp.reliable-init`. ⭐ AQ-70's rule —
*"unknown tokens are ignored, absence = unsupported"* — gives **graceful degradation for free**: a node
that never advertises a need is simply never staged to, and an older node is not a failure.
⚠ **Blast radius: none.** ② is a static class with one const; this adds constants beside it.

### `Q72-C` — **packaging: publish-time or on-the-fly?**

🔴 **This one collides with a ruling you already made.** 🔒 The artifact-staging slice keys its skip on
**`(length, lastWriteTimeUtc)`**. ⇒ ⛔⛔ **a zip built on the fly gets a NEW mtime on every run and
defeats both skips** — every node re-downloads and re-ingests every load.

| option | verdict |
|---|---|
| ⭐ **C1 publish-time packaging** *(lean)* | the archive is an **authored artifact** with a stable mtime ⇒ the skip works. ⇒ **packaging is an authoring-pipeline concern, not a distribution one** |
| **C2 on-the-fly in the orchestrator** | ⛔ defeats the skip, **or** forces the content hash you explicitly ruled out |
| **C3 no packaging; sync file trees** | ⭐ simplest, ⚠ and the user's *"not neglectable"* size concern is about **transfer count**, which `PrefetchScenarioAsync` already parallelises. ⛔ Unmeasured: whether file COUNT is actually the bottleneck |

⚠ **I cannot lean confidently between C1 and C3 without one measurement: how many files a real Stride
terrain has.** ⭐ If it is dozens, C3 is fine; if thousands, C1 pays for itself. **State the number and
this decides itself.**

### `Q72-D` — **filesystem-agnostic loaders**

⭐ **Lean: GENERALISE ④, do not duplicate it.** `ITkbStorageStrategy` + its zip/raw pair is the shape the
user asked for, and the seam law says the answer here is adoption, not invention. ⚠ **The honest caveat:**
its members (`EnumerateEntityFiles`, `WriteEntityFile`, `DeleteEntityFile`) are **TKB-entity-shaped**, so
generalising means extracting a narrower `IAssetStorageStrategy` underneath it — ⛔ not free, but far
cheaper than a second parallel VFS.

### `Q72-E` — **authored assets: node → NAS**

⭐ **Lean: reuse ⑤ unchanged.** `PullToNasAsync` + `FileManifestEntry` + a consensus aggregator is exactly
the vehicle, and the gateway's own doc states the design rationale: *"reads, rather than having all nodes
push."* ⚠ **The genuinely new part is ROUTING** — 🔒 the user's *"different nodes per asset type"* means
the orchestrator must know **which node is authoritative for which `AssetKind`**. ⭐ That is a second
token vocabulary (`hrot.asset.authors.<kind>`), symmetric with `Q72-B`'s. ⛔ **I have not measured whether
any node advertises an authoring role today** — `EditorCapabilities` exists, its relationship to
`AssetKind` does not.

### `Q72-F` — **failure semantics**

🔒 **The user has already ruled the WHAT:** *"Error during sync should fail the scenario loading."*
⭐ **Lean on the WHERE: the existing prefetch step, unchanged.** 📐 It is already prepended **before any
state transition** (`TransitionPlanner.cs:150`) and `PrefetchScenarioAsync` already **throws** on a
missing or empty NAS source. ⇒ ⭐⭐ **failing the load is the default behaviour of the slot we are already
using** — ⛔ nothing new to design, only to preserve.
⚠ **One real sub-question:** *partial* failure. `GatewayResult` reports per-file success/failure counts and
`PullToNasAsync` **deliberately swallows per-file errors**. ⇒ ⛔ **"fails the load" must be defined as
ANY failure count > 0**, or the invariant is not enforced.

### `Q72-G` — **scope and sequencing**

⭐ **Lean: three increments, and the first is already dispatched.**
① **TKB staging** *(in flight — `DESIGN_Artifact_Staging.md`)* · ② **the needs vocabulary + terrain
components** *(`Q72-A`/`B`/`F`)* · ③ **authoring sync + routing** *(`Q72-E`)*.
⛔ **Packaging (`Q72-C`) is deliberately NOT in the sequence** until the file-count measurement says it
earns its place.

## 3a. ROUND 2 — **the three questions the approval opened**

⭐⭐⭐ **First, the thing that falls out of your own requirements and resolves an apparent contradiction.**

🔒 You asked for *"zipping during transport"* **and** *"to stay editable they need to be present as
individual files"* **and** (`Q72-C`, approved) a `(length, mtime)` freshness key. ⛔⛔ Those three cannot
all hold of the **same object** — a transport archive built from a tree gets a **new mtime every run**, so
a skip keyed on the archive never fires.

⇒ ⭐⭐⭐ **THE RULE THAT DISSOLVES IT: THREE FORMS, AND FRESHNESS IS KEYED ON THE AT-REST PAIR, NEVER ON
THE TRANSPORT.**

| form | who decides | ⭐ property |
|---|---|---|
| **at rest on NAS** | the publisher — our authoring host, **or an external tool** | tree **or** archive |
| **in transport** | the **sync mechanism**, automatically | an archive **whenever the at-rest form is a tree with many files** — 🔒 *"not file by file"* |
| **at rest on the node** | the **LOADER's contract** | must be the form the loader reads. ⭐ TKB reads both; ⛔ behaviour assets **must** be individual files |

⭐⭐ **Consequence:** freshness compares the NAS at-rest form against the node at-rest form as a **per-file
`(relative path, length, mtime)` manifest** — ⛔ never the archive. ⇒ **transport form becomes a pure wire
optimisation, invisible to both the loader and the skip.**

⚠⚠ **CORRECTION to my own first draft of this section, measured `2026-09-19`:** I wrote that
`FileManifestEntry` *"already exists and is exactly this shape."* 🔴 **It is the right VEHICLE and the
WRONG SHAPE.** 📐 `OrchestrationPayloadDtos.cs:153` carries **`SourceUnc`, `RelativeDest`, `DocType`** —
⛔ **no length, no mtime.** ⇒ the rule needs **two added fields** (or a sibling DTO), and ⚠ it is a **wire
DTO** returned as `ResultPayload` in `NodeOpCompletedEvent`, so the addition is a contract change —
additive and low-risk, ⛔ but not free, and **not something to discover during a batch.** ⚠ And because `File.Copy` preserves
mtime (measured, `DESIGN_Artifact_Staging` §6), a tree copied file-by-file **or** unpacked from an archive
lands with the same per-file key — ⭐ **the two transport shapes are indistinguishable to the skip**, which
is what makes the optimisation safe to apply automatically.

### `Q72-H` — ✅ **RULED `2026-09-19` — the transport form is an optimisation and nothing else**

> 🔒 **User:** *"what is on NAS should appear on nodes **for simplicity** (zip or file tree). Same for
> everything else, so **zipping big file trees stays pure transport optimization** in my opinion."*

⭐⭐⭐ **THE RULE: NAS FORM == NODE FORM. ALWAYS.** ⇒ a tree on NAS is a tree on the node; a zip on NAS is a
zip on the node. **Packing is pack → send → unpack**, and the node never sees the transport container.

⭐⭐ **This RESOLVES the caveat I raised against H1**, rather than leaving it open. I had asked *"what if a
loader wants the archive as its at-rest form?"* ⇒ 🔒 **the question dissolves: at-rest node form is never
the sync's choice, it is a MIRROR of NAS.** ⛔ A loader that wants an archive gets one by the archive being
on NAS — which is exactly the TKB case, and 🔒 the user's ruling that *"the TKB … I would not change the
format between NAS and nodes."*

⭐ **And it simplifies the freshness rule rather than complicating it:** because both sides hold the same
form, the per-file `(relative path, length, mtime)` manifest of §3a compares like with like. ⛔ There is no
"unpacked here, packed there" asymmetry to reason about.

| ⭐ what survives from H1 | |
|---|---|
| **pack a many-file tree for the wire, automatically, by file count** | 🔒 *"not file by file"* — ⭐ still the whole point |
| **unpack on arrival so the node mirrors NAS** | ⭐⭐ **new, and it is what makes the ruling true** |
| ⛔ **never change the at-rest form** | the archive exists only between the two disks |

#### `Q72-H1` — ✅ **RULED `2026-09-19` — PARTITION the tree; do not force everything into one archive**

> 🔒 **User:** *"avoiding re-archiving is **not purely CPU concern**; terrain files might be **huge hundred
> GB files** and forcing them to be archived with thousands accompanying smaller files just to get a single
> zip for transport is **waste and may hit disk size limits as well (unzipping)**, rather **transfer
> prepackaged big parts as standalone files and package the rest to another archive**."*

⛔⛔ **MY LEAN `H1a` WAS WRONG, and the reason I got it wrong is worth recording.** 📐 I framed the cost of
re-compression as **CPU only**, and on that framing "keep the big file inside the container, just stored"
is strictly better because it preserves the file-count win. 🔴 **I never considered the DISK FOOTPRINT.**
⇒ a 100 GB member makes the archive 100 GB+, and **unpacking needs the size twice over** — which can
simply fail. ⚠ **A cost model that omits a whole resource is not a lean, it is a guess with a table around
it.**

⭐⭐⭐ **THE RULING — the transport payload is a PARTITION, not one container:**

| partition | what goes in it | ⭐ why |
|---|---|---|
| ⭐ **standalone parts** | entries matching the **size** and/or **already-compressed** rules — big `.zip`/`.pak`/heightmaps/terrain blobs | ⭐⭐ copied **as files**, exactly as they are. ⛔ Never packed, never unpacked, **no 2× disk**, no wasted CPU |
| ⭐ **one archive for the rest** | the thousands of small files | ⭐⭐ this is where the **file-count** win actually lives, and it is untouched |

⇒ ⭐⭐ **Both goals are met, and they were never in tension** — ⚠ **I had assumed they were**, which is what
produced the false choice between H1a and H1b. 📌 A tree of *"one 100 GB heightmap + 3 000 small files"*
becomes **1 standalone file + 1 archive = 2 transfers**, not 3 001 and not one 100 GB zip.

| ⭐ the rule shape | |
|---|---|
| **selection is by SIZE and by extension/path regex** | 🔒 *"path/name/extension regex based rules"* + a size threshold. ⭐ Either match ⇒ standalone |
| ⭐ **the partition is invisible on both disks** | 🔒 `Q72-H`: NAS form == node form. The node ends up with the same tree either way — ⛔ the partition exists only on the wire |
| ⭐ **freshness is unaffected** | §3a keys on the **per-file at-rest manifest**, so a file being standalone or archived changes nothing about the skip |

⚠ **What remains genuinely open:** the default size threshold, and whether a *"stored in archive"* level is
still worth having for **medium** already-compressed files that are below the size threshold. ⭐ My lean:
**no** — one rule (standalone) is simpler than two, and a medium compressed file costs little either way.

### `Q72-I` — **the publish negotiation: when does node→NAS sync happen?**

🔒 Your constraints: ⛔ **not on every save** *(auto-save exists, sync is long)* · ⛔ **other nodes may be
offline at authoring time** · ⭐ *"ask the authoring nodes that are online if they have something more up to
date than NAS"* · ⭐ *"on every scenario load **only if it is cheap**"* · ⭐⭐ **always available as an
explicit user operation.**

| option | ⭐ |
|---|---|
| ⭐ **I1 explicit publish + a CHEAP staleness PROBE on load** *(lean)* | the user-triggered *"publish authored changes to NAS"* is the **only thing that transfers**. On scenario load the orchestrator asks online authoring nodes for a **summary** — per kind: newest mtime + file count — and **fails or warns loudly** if a node is ahead of NAS. ⛔ **It never silently transfers during a load** |
| **I2 auto-publish on load** | ⛔ violates *"we do not want the sync to happen every time"* and makes load time unbounded |
| **I3 explicit publish only, no probe** | ⭐ simplest; ⛔ loses the *"consistent data everywhere needed"* guarantee — a stale NAS loads silently, which is the exact failure class this whole programme exists to remove |

⭐⭐⭐ **Lean I1 — and the probe's feasibility is now MEASURED (`2026-09-19`), not assumed:**

| 📐 measured | where | ⇒ |
|---|---|---|
| ✅ **`IAssetCatalogContributor.BaseFolder`** — *"the absolute base folder for this contributor's assets"*, per `Kind` | `IAssetCatalogContributor.cs:20` | ⭐⭐ **the probe already knows exactly which directory to stat, per kind.** No new path authority |
| ✅ `Kind` + `Enumerate()` + **`ContributorChanged`** event | `:5,6,23` | ⭐ a cached summary can be **invalidated on change** instead of walked every time |
| ⛔ **`IEditableAsset` carries NO timestamp and NO length** — `AssetId`, `Name`, `Kind`, `SourceFilePath`, `IsDirty`, `IsEditorOwned` | `IEditableAsset.cs:3-12` | ⇒ the probe **cannot** read mtimes off the catalogue; it must **stat the files** |
| ⚠ **`BaseFolder` is `null` for non-file contributors** — *"assembly, test fakes, **scenarios**"* | `:11-12` | ⭐ the probe covers **Blueprint / BTree / Hsm** — the behaviour assets this is actually about — ⛔ **not scenarios**, which have no file-backed contributor |

⇒ ⭐⭐ **VERDICT: feasible and cheap, but NOT free.** The probe is a **stat-only directory walk** per kind
over `BaseFolder` returning `(kind, file count, newest mtime)`. ⛔ No file is read, no asset is parsed.
⭐ For the *"hundreds of small files"* case that is milliseconds; ⚠ it is not zero, so 🔒 the *"only if it
is cheap"* condition is **met by construction rather than by hope** — and `ContributorChanged` is there if
it ever needs to become a cached value instead of a walk.

✅ **RULED `2026-09-19`: WARN for authored assets, FAIL for named artifacts.** 🔒 *"warn for authored,
fail for named artifacts."* ⇒ the split is now canon: `Q72-F`'s *"error during sync fails the load"* binds
**artifacts a scenario NAMES**; an un-published blueprint edit is **not** one, so it warns and the load
proceeds. ⭐⭐ **This also matches what the build independently found** — `BP-555`: a scenario naming an
unpublished TKB is **not** counted a prefetch failure, because the loud failure has a designed home in the
node's handler. ⇒ **two routes to the same rule, and they agree.**

### `Q72-J` — **the external producer, and what "authoring capability" means now**

📐 **Measured, and it makes two of your statements meet:** ⑫ **CGF already composes the authoring surface**
(`WireAssetCreation`, shipped as `CE-049`) ⇒ 🔒 *"every CGF host should be capable of editing in same way
as the editor host"* is **largely already true in code**. ⛔ But ⑬ shows authored assets are saved to a
**local** root with **no NAS relationship whatsoever** — so the capability exists and the lifecycle does not.

⇒ ⭐⭐ **Lean: there is no "authoring role" token at all.** Instead:
- **`hrot.asset.authors.<kind>`** advertises *"this host can PUBLISH this kind"* — ⭐ every CGF host and the
  editor advertise the same set, so it is **not a per-node speciality**, it is a capability.
- 🔒 **External-tool assets** *(road nets, terrain components)* have **no publisher inside the cluster**.
  ⇒ ⭐⭐⭐ **they are NAS-authoritative by definition, and the probe must not look for one** — ⛔ a kind with
  no advertised author is not a failure, it is the normal case for externally-produced content.

⚠ **This is the cleanest fallout of the whole round:** ⭐ *"who may publish kind K"* and *"who needs kind K"*
are **two symmetric token vocabularies over the same facility** (`hrot.asset.authors.*` /
`hrot.asset.needs.*`), and *"nobody authors K"* is a **meaningful, legal answer** that means *"external"*.

✅ **RULED `2026-09-19` — lean accepted in full.** ⛔ No authoring ROLE; two symmetric vocabularies; a kind
with no advertised author is the **normal** case for externally-produced content and the probe must not
look for one.

### `Q72-K` — ✅ **RULED `2026-09-19` — SUBFOLDERS OF ANY DEPTH ARE A USER-FACING REQUIREMENT**

> 🔒 **User:** *"i need to support **subfolders of any depth** — this is a way how **user organizes the
> assets**."*

⭐⭐⭐ **This is not a technical nicety, it is an organisational feature**, which changes its status: ⛔ it
cannot be deferred as an optimisation, because the folder structure is **content the user authored** and
losing it is losing their work.

📐 **And it collides head-on with §5c's measurement.** Every enumeration in the orchestrator today is
**one level deep** — `ScanLocalScenarios:440`, `ScanLocalExercises:459`, `ScanNasExercises:484`, and
`PrefetchScenarioAsync:244`'s flat `Directory.GetFiles(sourceDir)`. ⛔ **There is no recursive walk
anywhere.**

| ⭐ what this ruling makes MANDATORY | |
|---|---|
| ⭐⭐⭐ **recursive enumeration on BOTH sides** | the manifest is `(relative path, length, mtime)` and the **relative path carries the subfolder structure** — §3a already assumed this, ⚠ but §5c shows neither side can produce it today |
| ⭐⭐ **the relative path is preserved END TO END** | NAS tree → transport → node tree → loader. 🔒 `Q72-H`'s *NAS form == node form* now means **shape AND structure**, not just tree-vs-zip |
| ⭐ **the archive preserves paths** | ⭐ zip entries are relative paths natively, so the transport partition (`Q72-H1`) is unaffected — ⛔ but it must **not** flatten on pack or unpack |
| ⚠ **the standalone partition keeps its path too** | a big file at `terrain/tiles/x/y/huge.bin` lands at the same relative path, ⛔ not at the root |

⚠⚠ **AND A SHARPENED CONSEQUENCE FOR THE SHIPPED SLICE.** §5c already noted `PrefetchScenarioAsync` is
flat. 🔒 With this ruling the gap is **no longer hypothetical**: ⛔ **the moment a user organises a TKB —
or any tree asset — into subfolders on the NAS, a flat copy silently drops everything below the top
level.** ⭐ `DESIGN_Artifact_Staging` shipped the **single-file zip** case, which is unaffected; ⛔ **the
tree case must not be used until the recursive walk exists.**

### `Q72-L` — ⚠ **RAISED `2026-09-19` by the load-phase programme: the "needs" vocabulary ALREADY HAS A SOURCE**

> ⚠⚠ **NARROWED by `Q72-M` (below), `2026-09-19`.** The derivation is right about **needs** and wrong that
> **asset kinds** fall out of it — `LoadPart` and `AssetKind` do not intersect. ⛔ Do not quote this
> section's *"`Q72-B` may need no hand-maintained vocabulary at all"* without `Q72-M`.

📐 **Measured after the cluster load-phase batch landed.** `DESIGN_Cluster_Load_Phase` §4 composes a
per-role load chain from a **`RoleLoadRequirements`** declaration — *"which role needs the knowledge base /
terrain / scenario entities, by measured consumer"* — owned by `DESIGN_Node_Roles_And_Policies` §3.2.
⭐ And §4.1a is already RULED and BUILT: **"a role with no consumer does NOT load terrain."**

⛔⛔ **That is the same question `Q72-A`/`Q72-B` answer, one layer down** — and two vocabularies for one
question is exactly the duplication this codebase produces most.

| layer | what it decides | scope |
|---|---|---|
| ⭐ **`RoleLoadRequirements`** *(BUILT)* | which role **LOADS which part into memory** on this node | in-process, at load time |
| ⭐ **`hrot.asset.needs.*`** *(`Q72-B`, not built)* | which node needs the **BYTES staged to its disk** | cross-node, orchestrator-side, **before** load |

⇒ ⭐⭐⭐ **They compose rather than compete — you cannot load what was never staged, and you must not stage
what nothing loads.** ⇒ ⭐⭐ **LEAN: the token set is DERIVED from `RoleLoadRequirements`, not authored
beside it.** 📐 The precedent is exact and one layer up: `AQ-70` §Q70-C derives **roles from tokens**;
this derives **staging tokens from role requirements**. ⇒ **`Q72-B` may need no hand-maintained vocabulary
at all.**

⚠ **What would flip it:** a node that needs bytes it does **not** load — a **standby** or **cache-ahead**
node. 📌 `DESIGN_Cluster_Load_Phase` §5.1 already names a *"future standby mode"* as out of scope, so
🔒 **this is real but not yet.** ⭐ If it arrives, the token set becomes the superset and the requirement
table one contributor to it — ⛔ which is a widening, not a redesign.

⭐ **Corroboration for `Q72-A` worth recording:** §4.1a's *"a role with no consumer does NOT load terrain"*
is the **intersection principle already ruled and BUILT** at the load layer. ⇒ `Q72-A`'s A2 is not a new
idea to introduce; it is **the same idea, extended from loading to staging.**

### `Q72-M` — ✅ **RULED `2026-09-19` — `Q72-L`'s derivation is TRUE but INSUFFICIENT: an ADAPTER here, and the load table is NOT extended**

⛔⛔ **Raised by the backend session's review of `DESIGN_Asset_Management`, and it is a defect in `Q72-L`
as written.** 📐 Measured:

| vocabulary | values | where |
|---|---|---|
| `LoadPart` | `KnowledgeBase`, `Terrain`, `ScenarioEntities` | `LoadPhaseContracts.cs:32-42` |
| `AssetKind` | `Blueprint`, `BTree`, `Hsm`, `Blackboard`, `Utility`, `Scenario` | `AssetKind.cs:3-11` |

⇒ **the intersection is EMPTY**, neither vocabulary contains TKB / terrain / road-net, and ⛔ **behaviour
assets have NO `LoadPart` at all** — `LoadPhaseChain` never loads them, yet 🔒 *"behavior assets need to be
synced to every brain role host… as individual files"* (§0a). ⇒ ⚠ **`Q72-L`'s *"derive the token set from
`RoleLoadRequirements`"* cannot be executed as written**, and taken literally this design would ship
**three** vocabularies where §7.3 warns against two.

### ✅ THE RULING *(user, `2026-09-19`: "ok accepting your lean")*

| ⭐ | |
|---|---|
| ⭐⭐⭐ **a 3-row `LoadPart → AssetKind` ADAPTER, owned by `DESIGN_Asset_Management`** | 🔒 **an adapter between two EXISTING vocabularies is not a third vocabulary** — it is total, one-directional and has no independent content to drift |
| ⭐⭐⭐ **plus ONE rule for behaviour assets: `Brain ⇒ all AI asset kinds`** | ⭐ **one line, not a table.** That is what keeps it out of the duplication `Q72-L` exists to prevent: there is nothing to maintain per kind |
| ⛔⛔ **`RoleLoadRequirements` is NOT extended** | ⚠ it is an **ordered execution list** with one `ILoadPartProvider` per part (`LoadPhaseChain.cs:97`); a `LoadPart.BehaviorAssets` would make the table **claim a load step nobody implements** *(`R-133` — a fake must announce itself)*, and it edits a table owned by `DESIGN_Node_Roles_And_Policies` §3.2 |
| ⭐ **`Q72-L` stands, narrowed** | it is right that the **needs** come from the load table; ⛔ it was wrong that the **kinds** come out of it for free |

⚠ **What would flip it:** a behaviour asset that only SOME brain hosts need. ⭐ Then the one line becomes a
table and the adapter is where it goes — **a widening, not a redesign**, exactly as `Q72-L` says of standby
nodes.

## 4. NOT IN QUESTION — settled, and this document does not reopen it

| | |
|---|---|
| **the NAS is the master source** | 🔒 user, `2026-09-18` |
| **`(length, mtime)` is the freshness key; no hash, no sidecar** | 🔒 user, `2026-09-18`; risk recorded in `DESIGN_Artifact_Staging.md` §6 |
| **the capability facility itself** | ✅ AQ-70, BUILT — ⛔ do not design a second one |
| **prefetch runs before any transition, and throws on a missing source** | ✅ built; `Q72-F` preserves it |
| **roles are derived from tokens, not carried separately** | ✅ AQ-70 §Q70-C — ⛔ do not add a parallel "needs" mask to the heartbeat |

### 4a. ✅ ROUND 1 RULED — `2026-09-19`, *"Agreed with your leans"*

| # | the ruling |
|---|---|
| **`Q72-A`** | ✅ **A2 INTERSECTION** — the ASSET declares its components, the NODE declares which kinds it consumes, the orchestrator intersects. ⭐ `TerrainDefinition.SchemaVersion` is the extension point |
| **`Q72-B`** | ✅ **`hrot.asset.needs.<kind>`** on the AQ-70 facility. Unknown-token-ignored ⇒ graceful degradation free |
| **`Q72-C`** | ✅ **packaging at AUTHORING time**, ⛔ never on the fly. ⚠ **NARROWED in round 2:** not always ONE zip — per COMPONENT, and some components stay file trees (§3a) |
| **`Q72-D`** | ✅ **generalise `ITkbStorageStrategy`**, do not duplicate it |
| **`Q72-E`** | ✅ **reuse `PullToNasAsync` + `FileManifestEntry`**; the new part is routing. ⚠ **REFRAMED in round 2 by `Q72-J`:** not a per-node authoring speciality — a capability every CGF host has |
| **`Q72-F`** | ✅ fail the load on sync error, in the existing prefetch slot; **ANY failure count > 0 counts** |
| **`Q72-G`** | ✅ three increments; packaging not sequenced until it earns its place |

## 5. WHAT I WOULD MEASURE BEFORE YOU RULE

⭐ **Round 1's three are now ANSWERED — two by you, one superseded:**

| # | status |
|---|---|
| component kinds a Stride terrain has | 🔒 **answered: theoretical.** *"terrain is largely unimplemented … mostly a theoretical question, but I need to set up the RULES and have the INFRASTRUCTURE support it"* ⇒ ⭐ **A2 wins on extensibility precisely BECAUSE the set is unknown** — that is the case A2 is for |
| files in a real terrain asset | ⚠ **superseded as the deciding number.** 🔒 The TKB worked example — *"hundreds of small files"* — and *"behavior assets … can be numerous"* already establish that **tree-with-many-files is a real case**, so the infrastructure must handle it regardless. ⭐ The number now only tunes `Q72-H`'s threshold |
| any node advertising authoring capability | 🔒 **answered: NONE today**, and *"every CGF host should be capable of editing in same way as the editor host."* 📐 Corroborated: ⑫ CGF already composes `WireAssetCreation` ⇒ the capability largely exists; ⑬ the NAS lifecycle does not |

### 5a. ⚠ WHAT IS STILL UNMEASURED — round 2

| # | what | decides |
|---|---|---|
| **1** | ✅ **DISSOLVED, not measured.** 🔒 The `Q72-H` ruling — *NAS form == node form, always* — means the at-rest node form is **never the sync's choice**, so "would a loader break if handed an archive" cannot arise: it is handed whatever NAS holds | ⭐ the question was real; the ruling removed it rather than answering it |
| **2** | ✅ **MEASURED `2026-09-19`** — `IAssetCatalogContributor.BaseFolder` gives the per-kind directory; `IEditableAsset` carries **no** timestamp ⇒ the probe is a **stat-only walk**, not a cached read. Full result in `Q72-I` | ⚠ and it surfaced a **scope limit**: `BaseFolder` is `null` for scenarios, so the probe covers behaviour assets only |
| **3** | ✅ **MEASURED `2026-09-19` — and it corrected me.** `FileManifestEntry` (`OrchestrationPayloadDtos.cs:153`) = `SourceUnc` + `RelativeDest` + `DocType`. ⛔ **No length, no mtime** | ⇒ §3a's manifest-freshness rule costs **two fields on a wire DTO**, not zero. ⭐ Additive and low-risk, ⚠ but a contract change to plan for rather than to discover mid-batch. **The correction is recorded in §3a itself** |

### 5b. ⚠ WHAT IS STILL UNMEASURED — after round 3

| # | what | decides |
|---|---|---|
| **A** | ✅ **MOOT.** `Q72-H1`'s ruling means a big entry is **never put in an archive at all**, so the stored-entry cost never arises | — |
| **B** | ✅ **MEASURED `2026-09-19` — and it is bigger than I implied.** See §5c | it materially sizes the first increment |

### 5c. 📐 THE NAS SIDE, MEASURED — **it is entirely new code**

| # | measured | where |
|---|---|---|
| ① | **every existing scan is ONE LEVEL DEEP** — `Directory.GetDirectories(root)` then `GetFiles(d, …)`, ⛔ **no `AllDirectories` anywhere** | `ScanLocalScenarios:440`, `ScanLocalExercises:459`, `ScanNasExercises:484` |
| ② | they return **identities only** — a scenario NAME, an `ExerciseInventoryItem` — ⛔ **never per-file length or mtime** | same |
| ③ | `PrefetchScenarioAsync` copies with **`Directory.GetFiles(sourceDir)` — FLAT** | `:244` |
| ④ | ⛔ **nothing enumerates a NAS asset tree for Blueprint/BTree/Hsm at all.** The only asset-ish reader is a **ledger directory** of `*.json` | `AssetInventoryProcessManager.cs:152` |

⇒ ⭐⭐⭐ **VERDICT: the NAS half of §3a's manifest is NEW CODE — there is no recursive enumeration in the
orchestrator at all, and no NAS notion of an asset tree.** ⚠ This is the opposite of the node side, where
`BaseFolder` handed us the directory for free. ⇒ **the first increment is larger than the node-side
measurement alone suggested**, and this is the number I would put in front of any plan.

⚠⚠ **AND A CONSEQUENCE FOR THE BATCH ALREADY IN FLIGHT, worth recording so nobody assumes otherwise:**
📐 ③ means `PrefetchScenarioAsync` is **flat**. ⇒ `DESIGN_Artifact_Staging` handles the TKB **zip** form
correctly *(one file)*, ⛔ **but it does NOT handle a TKB that lives on NAS as a file TREE** — a nested
tree would be silently **partially** copied. ⭐ That is **in scope for this document, not a defect of that
slice**, which never claimed the tree form. 🔒 It becomes real the day someone puts a raw TKB directory on
the NAS — and `Q72-H`'s *"NAS form == node form"* ruling is exactly what makes that legal, so **the gap
must close before the tree form is used.**
