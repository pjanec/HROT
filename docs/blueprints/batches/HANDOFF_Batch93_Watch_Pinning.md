<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - the Batch 93 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none. DESIGN_Variable_Watch_Pinning.md is the spec; this batch
  builds all three of its slices. Its own known-rot (a pre-Batch-90 cost model) is
  reflected in section 3.
-->
# HANDOFF — Batch 93: **watch pinning**, all three slices

> 📌 **Dispatched at `ad9f1cd93`.** ⭐ **Branch from THIS commit** *(rule 7)* — the handoff itself.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT.** ⛔ **Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids and state them.** ⭐ **Rule 1b: push
> `chore: started batch 93 at ad9f1cd93` FIRST, before any code.**
>
> 📄 **Spec: [`DESIGN_Variable_Watch_Pinning.md`](DESIGN_Variable_Watch_Pinning.md)** — ⭐⭐ **LIVE, and
> nothing in it is open any more.** Costs measured *(`M-25`)*, the one decision ruled *(`R-102`)*.
> ⛔ **`R-27` does NOT gate this** — that gates `Q38`/`Q44`. ⭐ This targets **`AiWatchWindow`**, the
> window `Q38-E` already picks as the survivor ⇒ **aligned with that merge, not in conflict.**

---

## 1. ⭐⭐⭐ WHAT IS MISSING IS THE **GESTURE**, NOT THE MACHINERY

📐 **Coordinator-measured `2026-08-19`:**

| ✅ exists | |
|---|---|
| **the store** | `PinnedVariableRowSource.Pin(...)` — ⛔ **its only caller is a TEST** *(`TrackCWiringTests:235`)* |
| **the surface** | `AiWatchWindow`, `Pinned` public, registered in all three perspectives |
| **the render** | the shared table · formatter · edit dialog · stale-greying · the heterogeneous-rows rail |
| ⭐⭐ **live values** | **Batch 90's arms** — ⭐ *"a Watch row pinned from a live Details row carries its arm with it"* |

| ⛔ missing | |
|---|---|
| ⭐⭐⭐ **the gesture** | **`CommandCatalog.ToggleWatch` DOES NOT EXIST.** `CanvasRenderer:684` has `MenuItem("Watch this Value")` **inside `BeginDisabled()`** with **no handler** — ⚠ **and it is a PIN menu, not a variable row** |

⇒ ⭐⭐ **The fourteenth instance of store-built / way-in-missing.** ⛔ **Do not rebuild the store.**

---

## 2. 🛠 **`93a` — SLICE 1: the mechanism in `AiShared`**

📄 **Spec §7** *(the gesture)* · **§1–§2** *(the model, two feeds one row type)* · `R-49`.

| ⭐ | |
|---|---|
| **the command** | **`CommandCatalog.ToggleWatch`** — ⭐⭐ **ONE command, TWO entry points** *(spec §7)*: the **My Blueprint row** context menu **and** the **Details table row**. ⛔ **A one-surface gesture re-creates the split `U-6` removed** |
| **what it does** | a **toggle** — *"Watch this variable"* / *"Stop watching"* ⇒ `Pinned.Pin(...)` / unpin |
| ⭐⭐ **when it may be used** | **Planning** ✅ · **Paused/stepping** ✅ · ⛔ **free-running FORBIDDEN** · ⛔ **replay FORBIDDEN** *(spec §7)*. ⭐ **Run state from `R-69`'s CLUSTER STATE** — ⛔ not a new notion of "running" |
| ⭐⭐⭐ **how it refuses** | **greyed + a tooltip saying WHY** — 📌 the user's own rule: *"same information value, no false expectations."* ⛔⛔ **never a click that dead-ends** |
| ⭐ **the canvas stub** | `CanvasRenderer:684`'s `"Watch this Value"` **leaves `BeginDisabled()`** and invokes the same command. ⚠ **It is a PIN menu** — ⭐ **if a pin does not map cleanly to a variable row, LEAVE IT DISABLED and say so.** ⛔ **Do not invent a pin→variable mapping** |

### ⭐⭐ The VALUE feed — **cheaper than the spec says. Read this before building a poller**

⚠⚠ **Spec §4 assumes the VALUE clock needs new per-tick polling machinery. That predates Batch 90** —
📌 the design's own `known-rot` line. ⭐⭐⭐ **Batch 90's live arms are read PER FRAME, and a pinned row
carries its arm with it** ⇒ ⭐ **a row pinned from a live Details row is live in the Watch with no new
polling code.**
⛔ **Do NOT build a per-tick poller "because §4 says so."** ⭐ **Measure what a pinned row already does,
and report it** — ⚠ **if it turns out NOT to carry the arm, STOP and report; that is a design question,
not a wiring one.**

### ⛔⛔ THE TWO CLOCKS — **unaffected, and it still binds** *(spec §4, `R-76`)*

| clock | fires |
|---|---|
| ⭐ **VALUE** — *what does this field hold?* | every frame / tick, all rows |
| ⭐⭐ **BINDING** — *which entity is this row about?* | ⛔ **NOT the tick** — ⭐ **only on selection change**, and **only** for the chameleon row |

⛔ **Re-resolving the binding per tick would churn the row's identity under the cursor** — ⭐ **and it is
also what makes slice 3's linear scan acceptable** *(§4 below)*.

### ⭐ Acceptance for slice 1 — **the spec's own words**

⛔ **NO blueprint-specific code in `AiShared`.** ⭐ *"not 'it works on Blueprint'."*

---

## 3. 🛠 **`93b` — SLICE 2: each host supplies its data**

⭐ **Tick source + blackboard base offset**, as **DATA**, not machinery *(spec §6)*.
⭐⭐ **The acceptance test is the size:** *"if this is not nearly free, slice 1 leaked host knowledge."*
⚠ **The base offset must be owned in ONE place** — 📌 the same *"whoever computes the offset must own
that `+8` in one place, not two"* the running write is held to.

---

## 4. 🛠 **`93c` — SLICE 3: restart survival.** ⭐ **Both costs MEASURED, the decision RULED**

### ⭐⭐⭐ `R-102` — the CALLBACK SINK *(user, `2026-08-19`)*

```
StagingEntityExtractor
  └─ an OPTIONAL Action<IReadOnlyDictionary<long,long>>      ← the sink
        └─ wired to the orchestration bus BY THE SUBSYSTEM
```

| ⭐ | |
|---|---|
| ✅ **the bus takes it whole** | `FdpEventBus.PublishManaged<T>` has **no `unmanaged` constraint** — its own comment: *"No class constraint — allows managed structs"* ⇒ ⛔ **no flattening to arrays** |
| ✅ **the pattern exists** | `RegisterManaged<T>()` at bootstrap *(`OrchestrationEventRegistry:17`)* → publish → `ReadManaged`. ⭐ **`EditorApplication` already reads (`:78`) and publishes (`:94`) on that bus** |
| ⛔ **NOT** a bus dependency inside `Hrot.CGF` | the extractor stops being a pure transform |
| ⛔ **NOT** a widened `Extract` return | **3 call sites + an interface + an explicit impl**, for a value one caller wants |
| ⛔⛔ **the remap CODE does not move or get copied** | ⭐ **ruling 9 on the most safety-critical mapping in the system** — ⭐ **only the MAP is published** |

### ⭐⭐ ONE `FindEntityByNetworkId` — **and NEITHER existing one is the keeper**

| | `ReplayBrowserSubsystem:933` | `EditorMissionService:54` | ⭐ take |
|---|---|---|---|
| query | ⛔ everything, then `HasComponent` | ⭐ `.With<NetworkIdentity>()` | ⭐ **the filtered one** |
| read | ⭐ `GetComponentRO` | ⛔ `GetComponent` *(copy)* | ⭐ **RO** |
| null repo | ⭐ guarded | ⛔ unguarded | ⭐ **guarded** |

⭐ **Home: `FDP/Toolkits/Fdp.Toolkits/Replication/`** — it already has `Extensions/` and `Utilities/`
and owns `NetworkIdentity` itself. ⭐ **Both call sites move onto it; this batch is the third caller.**

⚠⚠ **Both are LINEAR SCANS, and that is CORRECT — ⛔ do NOT index or cache.** ⭐⭐⭐ **§4's two-clocks
rule is what makes it correct**: a binding resolves on selection change / load, ⛔ **never per tick.**
📌 **Say this in the code**, or the next reader "optimises" it.

### ⭐ Persistence

⭐ **Key on the STAGING id** *(the stable authoring artefact)*, resolve at **bind** time through the
published map. ⭐ **Extend `SaveWatches`/`LoadWatches`** — ⛔ **do not invent a second file.**
⚠ **They persist breakpoints marked `IsWatch` today, keyed by `PropertyMatchDto`, not entity-keyed** —
⭐ **that is the shape to extend, and `Q44-B` will later retire the `IsWatch` half.** ⛔ **Do not do
`Q44-B` here** *(`R-27`)*.

---

## 5. ⛔ WHAT MUST NOT BE BUILT *(spec §9, verbatim in force)*

| ⛔ | why |
|---|---|
| a second watch window, or work on `WatchPanelWindow` | ⭐ target `AiWatchWindow`; its retirement is row **60** |
| a per-variable emitted push | 📌 **`R-49`** |
| an editor-side copy of the id remap | 📌 **`R-79`** · ruling 9 |
| a fourth `FindEntityByNetworkId` | 📌 **`R-77`** |
| one row per live entity | ⛔ **user: *"unbearable — thousands of entities"*** |
| a panel-wide tick | ⭐ *"rows tick at different rates"* |
| touching `EntityWatchPanel` / `FdpEntityWatchWindow` | ⭐ **a different concept** — entity components |
| anything in `Q38`–`Q44` | ⛔⛔ **`R-27` — the visual check has not run** |

⚠ **Spec §10 leaves ONE thing open: watching variables from DIFFERENT ASSETS in one panel.** The key
supports it and the store is shared, ⛔ **but the poll would span debug sessions** ⇒ **out of scope.**
⭐ **If a rail forces the question, STOP and report.**

---

## 6. ⭐ GATES — the contract, plus the four this batch owns

| # | report |
|---|---|
| **1–7** | the standard contract · **`--no-build` column** *(⛔ `NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` report a STALE BIN)* · every red confirmed **pre-existing vs `ad9f1cd93`** · clean tree after every suite · both quarantine counts · every id you allocated |
| ⭐ **7b** | ⭐⭐ **every gate script UNFILTERED with `EXIT=$?`** — ⭐ **Batch 92 got this exactly right; keep it.** ⚠ `tracker-counts --check` is RED on the first run of any batch that adds a row — **that is the script working** |
| ⭐⭐ **8 — GOLDEN** | ⛔ **nothing here touches emission** ⇒ **it must not move.** Movement is a **STOP-AND-REPORT** |
| ⭐⭐⭐ **9 — THE ENUMERATION** | ⭐ **every production caller of `PinnedVariableRowSource.Pin`, before and after**, and ⭐ **every `FindEntityByNetworkId` in the repo** — 📌 `R-77` says a fourth is forbidden; **prove there are now ONE** |
| ⭐⭐⭐ **10 — WHAT EACH RAIL ASKS** | ⛔⛔ **a rail that `Pin` was called proves nothing.** ⭐⭐ **Ask the ARTEFACT** — the **row the Watch table would draw**, its **cell text**, and ⭐ **the refusal: greyed + a reason, in each forbidden run state** |
| ⭐⭐ **11 — REVERT-GOES-RED** | one probe per slice, **INVERSE EDIT** — ⛔ never `git checkout --`. ⚠ **Separately for the two entry points** *(My Blueprint row · Details row)* — ⭐ **two reds, or one of them is unrailed** |

⭐ **Baseline** *(post-Batch-92)*: AiShared **1485** · BTree.Editor **622** · Hsm.Editor **554** ·
AiEditor.Generators **277** · AiEditor.Persistence **143** · Blueprints **3773/3783/10** ·
Hrot.Editor **201** · Breakpoints **143** · NodeEditor.Core **211** · NodeEditor.UI **135** ·
Fhsm **300** · `Fdp.Presentation` **146 FILTERED** *(`BP-337`)* · tracker **open 69 / done 209** ·
rulings **67/67**.
⛔ **`Fdp.Toolkits.Tests`: do not run it** — 📌 `DEBT-AIB-030`.

## 7. ⭐⭐ If you must stop

| ⭐ complete on its own | |
|---|---|
| **`93a` + `93b`** | ⭐⭐ **in-session pinning — the whole visible feature.** ⛔ `93a` alone leaks host knowledge by definition; land both |
| **`93c`** | ⭐ **restart survival — independently droppable.** ⚠ Say so plainly if you drop it: *"a pin does not survive a scenario reload"* is a **user-visible** limitation |

⚠ **If the value feed does NOT come free from Batch 90's arms — STOP AND REPORT.** ⛔ **Do not build a
poller on your own judgement**; ⭐ that is a design question and it is mine to take to the user.

## 8. ⭐⭐⭐ WHAT THIS UNLOCKS

⭐ **`E2`–`E7` of 📄 [`GUIDE_Blueprint_Visual_Check.md`](GUIDE_Blueprint_Visual_Check.md) — the LAST
rows still marked SKIP.** ⭐ **Say which of them are now runnable**; ⛔ **I will rewrite them — do not
edit the guide.**
