<!--STATUS
state: LIVE
build-state: OPEN — a decision document, NOT buildable. ⛔ Nothing here is dispatched. Every sub-question
  carries a recommended lean for the user to approve or redirect (the "I analyse and SUGGEST, the user
  APPROVES" rule). ⚠ Number 72 taken as the next free across ALL active branches (rule 3a; 67-71 in use).
updated: 2026-09-18
current-answer: §3 holds the sub-questions and my leans — that is what needs your ruling. §1 is the
  measured INVENTORY, §2 is the prior art that changes the shape of the problem, §4 is what is already
  settled and NOT in question, §5 is the sequencing lean.
stale-below: nothing — new document.
known-rot: nothing yet.
known-conflict: none. ⚠ DESIGN_Artifact_Staging.md is IN FLIGHT as a dispatched batch and is deliberately
  a subset of this picture; §4 records what it settles so this document does not reopen it.
related-designs:
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

## 1. INVENTORY — measured `2026-09-18`

⚠ **Coverage caveat, stated rather than implied:** the graph index returned **0 results** for several of
these patterns and only grep answered — `scripts/find.sh` reports the disagreement loudly. ⛔ These are
therefore **grep-corroborated, not graph-enumerated**, and `check_index_coverage` was not reachable this
session. ⭐ Treat the counts as a floor, not a proven complete set.

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

## 4. NOT IN QUESTION — settled, and this document does not reopen it

| | |
|---|---|
| **the NAS is the master source** | 🔒 user, `2026-09-18` |
| **`(length, mtime)` is the freshness key; no hash, no sidecar** | 🔒 user, `2026-09-18`; risk recorded in `DESIGN_Artifact_Staging.md` §6 |
| **the capability facility itself** | ✅ AQ-70, BUILT — ⛔ do not design a second one |
| **prefetch runs before any transition, and throws on a missing source** | ✅ built; `Q72-F` preserves it |
| **roles are derived from tokens, not carried separately** | ✅ AQ-70 §Q70-C — ⛔ do not add a parallel "needs" mask to the heartbeat |

## 5. WHAT I WOULD MEASURE BEFORE YOU RULE

⭐ Three numbers, each of which decides a sub-question that is currently a guess:

1. **how many component kinds a Stride terrain actually has** → decides `Q72-A` (A1 vs A2)
2. **how many FILES a real terrain asset contains** → decides `Q72-C` (C1 vs C3)
3. **whether any node advertises an authoring capability per `AssetKind` today** → sizes `Q72-E`

⛔ **I have not measured any of the three**, and each is marked as a guess above rather than presented as
a finding. ⭐ Say the word and I will measure them before we discuss — 🔒 per the standing rule that a
lean resting on an unmeasured load-bearing row is not finished.
