<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: section 4 - RECOMMENDED ANSWERS, awaiting the user's approval.
stale-below: nothing.
known-rot: none.
known-conflict: none. It RESOLVES what R-99 left open - R-99 settled THAT the
  orchestrator emitters are wired, not WHERE the sidecar comes from.
-->
# ⭐ Architect Question 45 — **who emits the orchestrator?** *(`BP-340`)*

> ⛔⛔ **NOT RELAYED.** The architect is generally unavailable *(`2026-08-16` user ruling)*.
> ⭐⭐ **I analyse and RECOMMEND, the user APPROVES.**
>
> 📌 **Opened `2026-08-19`, by Batch 91 STOPPING.** ⭐ **`R-99`** *(user)* settled **THAT** the
> orchestrator emitters should be wired, not delete. ⛔ **It did not settle WHERE**, and my Batch 91
> handoff's answer — *"call the emitter from the asset save/emit path"* — ran straight into a **spec
> decision that says the save path does not emit C# any more.**
>
> ⭐⭐ **They stopped instead of inventing a seam.** 📌 That was correct, and this question is the cost.

---

## 1. ⭐⭐ INVENTORY *(`R-74` — measured `2026-08-19`)*

```
grep -rn "OrchestratorEmitter" --include=*.cs .    (excl .dev)   → 2 definitions, 21 test refs, 0 prod callers
grep -n "AddSource" Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeJsonGenerator.cs → 3
grep -n "TargetFramework" …/Hrot.AiEditor.Generators/*.csproj  …/Hrot.AiEditor.Persistence/*.csproj
ls Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/                          → 6 files
```

### ⭐ What already emits C# from persisted JSON

| # | emitted by the **generator** | to |
|---|---|---|
| 1 | `{Name}.g.cs` — topology | ⭐ **`obj/GeneratedFiles`** |
| 2 | `{Name}.Blackboard.g.cs` — the managed blackboard struct | `obj/GeneratedFiles` |
| 3 | `{Name}.Registrar.g.cs` — the bridge | `obj/GeneratedFiles` |
| ⛔ **4** | **`{Name}.Orchestrators.g.cs`** | 🔴 **nothing emits it** |

⭐⭐ **The bodies live in `Hrot.AiEditor.Persistence/Emit/`** — `BTreeEmitCore` · `HsmEmitCore` ·
`BTreeBridgeEmitCore` · `HsmBridgeEmitCore` · `AiEmitCoreBase` · `BTreeBlackboardPackHelper`.

### ⭐⭐⭐ The netstandard2.0 wall — **measured, and it is NOT in the way**

| | |
|---|---|
| `Hrot.AiEditor.Generators` | **`netstandard2.0`** *(a Roslyn generator must be)* |
| ⭐⭐ **`Hrot.AiEditor.Persistence`** | ⭐⭐⭐ **ALSO `netstandard2.0`, and the generator already `ProjectReference`s it** |
| `Hrot.BTree.Editor` / `Hrot.Hsm.Editor` *(today's emitters)* | ⛔ **editor assemblies — the generator cannot reference them** |

⇒ ⭐⭐ **There is a home on the right side of the wall, already referenced, already holding the five
sibling emit cores.** ⚠ **This is the one place the `netstandard2.0` duplication debt does NOT bite** —
📌 `BATCH-03-REPORT.md:100`.

### ⚠ And the fact that misled me

📐 **`CompanionFileDiscovery:194`/`:208` DO hunt `*.Orchestrators.g.cs`** — ⛔ **but beside a
`_BT.cs` / `_HSM.cs` SOURCE FILE**, alongside `.Blackboard.cs` *(no `.g.`)*, the **hand-written** kind.
⇒ ⭐⭐⭐ **that is the CATEGORY-1 world — hand-authored C# assets and their companions** — which is
**exactly** what `EditorSubsystem:3136`'s own comment says the emit service was kept for:
> *"The emitters + emitService remain available for any future direct C# emit path (e.g. hand-authored
> assets). `AiAssetEmitService` is NOT removed per spec."*

⛔ **I cited that discovery site as proof the consumer existed for the JSON path. It does not.**

---

## 2. ⭐⭐⭐ THE CRUX — **two categories, and the question is which one owns sync**

| | **Category 1** — hand-authored `.cs` | **Category 2** — JSON-owned *(what designers author)* |
|---|---|---|
| source of truth | the `.cs` file | ⭐ the `.btree.json` / `.hsm.json` |
| who writes companions | ⭐ `AiAssetEmitService` *(constructed, `_ = emitService`, invoked by nothing)* | ⭐⭐ **the Roslyn generator**, to `obj/GeneratedFiles` |
| ⛔ **orchestrator today** | none written | none written |

⚠⚠ **`PARAMETER SYNCHRONIZATION` is authored in the editor, on a JSON-owned asset.** ⇒ ⭐⭐⭐ **if the
orchestrator is not a Category-2 artefact, Approach B is dead for every asset a designer can actually
author** — which is the whole point of `R-99`.

---

## 3. ⭐ What binds any answer

| id | binds |
|---|---|
| ⭐⭐ **`R-99`** | ✅ **WIRE, do not delete** *(user, `2026-08-19`)* — settled |
| ⭐⭐⭐ **PU-D11 / PU-402** | ⛔ **the save path writes JSON, NOT C#** — a deliberate spec decision |
| **ruling 9** | ⛔ no two implementations of one concept |
| **`R-49`** | ⛔ generate the DATA; **never** per-variable code *(a per-BINDING copy is fine)* |
| **"no rush removals"** | ⛔ nothing retires until its capability lands elsewhere |
| ⭐ **§8.3** | the emitted shape: **copy · tick · copy**, and ⛔ **no orchestrator at all** when a subtree has zero active bindings |

---

## 4. ⭐⭐⭐ THE SUB-QUESTIONS — **with recommended answers**

### ⭐⭐⭐ `Q45-A` — which path emits `{Name}.Orchestrators.g.cs`?

| ⭐⭐⭐ **RECOMMENDED: (A2) the Roslyn generator, as a FOURTH `AddSource`.** |
|---|

| option | verdict |
|---|---|
| **A1** — the editor's JSON save path | ⛔ **contradicts PU-D11 directly.** It would put C# emission back on the path a spec decision moved off it |
| ⭐⭐⭐ **A2** — the **generator** | ✅ **The orchestrator is derived from persisted JSON and authored by nobody — exactly like the other three.** ⭐ Same trigger, same output directory, same lifecycle, **nothing new to remember** |
| **A3** — revive `AiAssetEmitService` | ⛔ **nothing invokes it**, and its own comment scopes it to hand-authored assets |

**Blast radius: LOW.** ⭐ One `AddSource`, in a generator that already makes three.

### ⭐⭐ `Q45-B` — where does the emit BODY live?

| ⭐⭐⭐ **RECOMMENDED: a new `BTreeOrchestratorEmitCore` / `HsmOrchestratorEmitCore` in `Hrot.AiEditor.Persistence/Emit/`,** beside its five siblings. |
|---|

⭐ **`netstandard2.0`, already referenced by the generator** ⇒ ⛔ **no wall to cross, no algorithm
duplicated.** ⚠ **This is the rare case where the `netstandard2.0` debt does not apply — say so in the
code**, so nobody re-derives the fear.

### ⭐⭐ `Q45-C` — what happens to `BTreeOrchestratorEmitter` / `HsmOrchestratorEmitter`?

| ⭐⭐⭐ **RECOMMENDED: ROUTE — the editor emitters become thin callers of the new EmitCore, and KEEP their `WriteOrchestratorFile` for the Category-1 path.** |
|---|

⛔ **Do NOT delete them** *("no rush removals"; and the Category-1 path is explicitly *"not removed per
spec"*)*. ⛔ **Do NOT leave two copies of the copy-emitting algorithm** *(ruling 9)*.
⇒ ⭐ **one body, two callers** — the generator for Category 2, the editor emitter for Category 1.
⚠ **Their 21 existing tests keep passing** — that is the acceptance signal.

### ⭐ `Q45-D` — is `CompanionFileDiscovery` now wrong?

| ⭐⭐⭐ **RECOMMENDED: NO — leave it. It serves Category 1, and that is correct.** |
|---|

⚠ **But it is the trap that cost this programme a stopped batch** ⇒ ⭐ **add one comment naming the
category it serves**, so the next reader does not take it as proof the JSON path has a consumer.
⛔ **Comment only.**

### ⭐⭐ `Q45-E` — does anything have to change for `91c` *(`subAssetResolver`)*?

| ⭐⭐⭐ **RECOMMENDED: NO — `91c` ships WITH `Q45-A`'s batch, unchanged.** |
|---|

📌 My own Batch 91 §9: *"`91c` alone opens a panel that authors dead data."* ⭐ Once `A2` lands, the data
is live ⇒ **the pair is coherent again.** ⛔ **Still do not land `91c` alone.**

### ⚠ `Q45-F` — `SubtreeSyncBindings` persists on **BTree only**

| ⭐⭐⭐ **RECOMMENDED: MEASURE before deciding — do NOT add it to HSM on symmetry alone.** |
|---|

📐 **Batch 91's gate 9 found it:** `HsmAssetMapper` mentions `SubtreeSyncBinding` **zero** times.
⚠ **HSM may legitimately have no subtree-sync concept** — 📌 §8.2 says the panel appears for
*"a Subtree node (BTree) or an HSM state with an embedded sub-tree action"*, ⭐ **which suggests it
SHOULD** — ⛔ but that is a design sentence, not a measurement of what HSM assets can express.
⇒ ⭐ **its own small investigation, not a rider.**

---

## 5. ⭐ What this costs if approved

⭐ **One batch**: the EmitCore pair, the fourth `AddSource`, the editor emitters routed onto it, `91c`,
and a comment on `CompanionFileDiscovery`.
⭐⭐ **Then Approach B is live end to end** — authored in the Inspector, persisted in JSON, **and
executed** — and the sub-asset sharing model is genuinely complete, which `91b` made half true.
