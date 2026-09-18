<!--STATUS
state: LIVE
build-state: DISPATCH — a pointer, not a design. ⛔ Carries NO UML and NO design content by rule.
updated: 2026-09-18
current-answer: §2 is the item list. §1 is the one thing that makes this batch small.
stale-below: nothing — new document.
known-conflict: none. ⚠ S3 edits `TkbLoadClusterStateHandler`, which the terrain programme MIRRORED
  but never modified — the terrain loader inherits S3 rather than colliding with it.
related-designs:
  - ../PLAN_Artifact_Staging_Build.md — the plan this dispatches (8 tasks, 4 stages).
  - ../../DESIGN_Artifact_Staging.md — THE owning design. §6 is the skip rules and why they compose.
  - ../../designs/tkb-1/DESIGN.md — §7.3 owns the node-side read and its cache key.
  - ../../designs/cluster-master-cqrs-1/DESIGN.md — owns the prefetch saga being extended.
-->

# HANDOFF — **Artifact staging: getting the named TKB to the nodes**

**Dispatched at `a5e9a5278`.** ⛔⛔ **Scope FROZEN at that sha.** A later document that invalidates an item
⇒ **stop that item and report it**; ⛔ never stop the batch (`R-106`).

⭐ **This closes `BP-550`** — the last thing standing between the terrain/zones programme and a working
end-to-end zone load.

## 1. ⭐⭐⭐ WHY THIS IS ONE SMALL BATCH — read this before scoping it

📐 **The prefetch step already does almost all of it:**

| already true | where |
|---|---|
| it runs **before any state transition** | `TransitionPlanner.cs:150` |
| it copies a NAS directory to every node's staging dir, in parallel, with per-file success/failure | `StorageGatewayModule.PrefetchScenarioAsync:229` |
| ⭐⭐ **it already reads `Header.TkbName` and already fails loud when slices disagree** | `:255 CheckTkbNameConsensus` |

⇒ ⭐ **`S2a` is "return the value it already computes."** ⛔ Do not build a new distribution mechanism, a
new cluster op, or a new wire value — the trajectory slot exists and `R-42` permanence is not in play.

## 2. 🔴 THE GAP IS WIDER THAN `BP-550` SAID — and this is what makes the failure silent

📐 **Measured at the coordinator:** the node reads `{stagingRoot}/TKB/ScenarioHeader.json`
(`TkbLoadClusterStateHandler.cs:139`, `ScenarioTerrainName.cs:31`, `TerrainAssetHandler.cs:70`) — and
**`ScenarioHeader.json` has ZERO production writers.** Only tests write it.

⇒ ⛔⛔ **`ExtractTkbNameFromLocalScenario` returns `null` on every real load**, and the handler falls
through to `NedTkbCatalog.RegisterAll()`. **The TKB loader has never resolved a name in production.**
⭐⭐ That is why the missing zip was never noticed: **the name was never read, so the zip was never wanted.**

⇒ ⭐⭐⭐ **`S2c` (write the header) is the half that matters most.** It is not a convenience — it is what
turns a silent fallback into a loud failure when an artifact is genuinely absent.

## 3. The items — 8, in `PLAN_Artifact_Staging_Build.md` §2

`S1a` constants + root helper · `S1b` settle `V1` · `S2a` return the agreed name · `S2b` copy the zip ·
`S2c` **write the header** · `S2d` the `IsAlreadyCurrent` skip · `S3a` add length to the node cache key ·
`S3b` **the compose rail** · `S4` terrain inherits it, close `BP-550`.

⭐ Success conditions are one row per task in the plan. ⛔ Not restated here.

## 4. The key is RULED — `(length, lastWriteTimeUtc)`

🔒 **User, `2026-09-18`: no hash, no sidecar.**

📐 **Measured, and the scheme rests on it:** `File.Copy` **preserves the source's last-write time**
*(source `02:22:38.1012573Z` → destination identical, while "now" was `07:22`)*. ⇒ ⭐ **a timestamp is a
cluster-wide identity**, so both sides compare the same number with no exchange.

⚠ **The accepted residual risk is recorded in design §6** — `(length, mtime)` is not a content identity.
⛔ Do not "improve" it with a hash; that decision is made and its cost is written down.

## 5. Three traps

| ⚠ | |
|---|---|
| **`S3b` case ③ is the rail that proves independence** | *restart the node with a current file ⇒ **no transfer, one ingest**.* ⛔ If that fails, the two skips are secretly one cache. Cases ① and ② can both pass while the design is wrong |
| **a scenario naming NO TKB must still load** | ⭐ `NedTkbCatalog.RegisterAll()` stays as the legal no-TKB path. ⛔ `S2b` copying nothing is **not** a failure |
| **`S4` is a CONFIRMATION, not a port** | ⭐ the terrain loader mirrors the TKB one field for field, so it should inherit both skips with **no new code**. ⛔⛔ **If it needs its own path, that is a FINDING** — report it rather than writing the parallel implementation |

## 6. Gates

Rows 1–7 against base `a5e9a5278`. ⛔⛔ **Row 8 BINDS** — this changes what every node holds before a
load. Name the integration suite and report **running** it, or state with base-sha evidence why it cannot
gate *(the `ClusterRunner.Integration.Tests` DDS-allocator crash is the known un-gateable case)*.

⭐⭐ **And the acceptance test only this batch can run:** `T3`. 🔒 Until this lands, an end-to-end
create-from-seed **throws by design** (`DESIGN_Terrain_Zones_And_Assets` §8.3 `N4`). ⇒ **after `S4`, run
it** and say whether it is green **for the first time**. ⛔ That is the real acceptance for this
programme, not any unit rail.

## 7. What to send back

`docs/blueprints/batches/REPORT_Artifact_Staging.md`, and **inline** via your report trigger: the gate
table · every id · row 8's named suite and result · **`T3`'s verdict** · `V1`'s measurement and which way
you went · the UML check (obligation ③) with any deviation **folded back into the design** and the edit
cited (obligation ⑤) · what the design got wrong · the sha you pushed.
