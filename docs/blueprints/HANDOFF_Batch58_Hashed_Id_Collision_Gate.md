# HANDOFF — Batch 58 (`W1`): ⭐⭐⭐ **the hashed-id collision gate — two silent no-op mechanisms, one rail**

> 📌 **Dispatched at `<stamped below>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⛔⛔ **RUNS AFTER BATCH 56 LANDS AND IS VERIFIED, AND BEFORE BATCH 57.** ⭐ **User ruling `2026-08-15`
> (Option A): correctness before the panel.** 📐 **If 56 is not merged when you start, do 56 first.**
> ⭐ **Rule 7:** branch from this branch. ⭐ **Rule 4:** pull it again before your final commit.
> ⛔ **Rule 3: the coordinator allocates no ids.** **You** allocate the diagnostic ids and tracker rows.
> ⚠ **ONE ITEM. Ship it ALONE** — the design session's own condition, and it is right: a gate that
> changes what compiles must not land beside a change to what is compiled.

---

## 0. Where this comes from

📄 **[`HANDOFF_Cross_Host_Parameter_Model.md`](HANDOFF_Cross_Host_Parameter_Model.md)** `W1`
*(design session, `claude/cross-host-variable-model-3k8cfh` @ `a01c583dd`)* ·
📄 **sequencing + corrections: [`PLAN_Cross_Host_Sequencing.md`](PLAN_Cross_Host_Sequencing.md).**

⚠⚠ **Provenance — put this in the tracker row.** ⛔ **These rulings are Claude-authored**: the NotebookLM
architect was unavailable and the design session was designated **architect of record**. ⭐ **Weaker than
the relayed rounds that redirected four of the last nine batches. Overturn on evidence, not authority.**

---

## 1. ⭐⭐ What is actually broken — coordinator-verified by reading, `2026-08-15`

⭐ **Two independent mechanisms, both silent, both ending in a no-op. The rail is the same rail.**

| | |
|---|---|
| **the hash** | `HsmActionGenerator:802` — `ComputeHash` is **FNV-1a truncated to 16 bits**. ⭐⭐ **The same hash family `UT0103` already guards** ⇒ *"mirror, do not invent"* is exactly right |
| **the ids** | `HsmActionGenerator:517/528/630/642/655/660` — **`ushort id = ComputeHash(name)`**, over the full `0…65535` |
| 🔴 **mechanism 1 — reserved values** | ✅ **VERIFIED, and stronger than `W1` states.** `HsmKernelCore` guards **five** call sites with `if (…ActionId != 0 && …ActionId != 0xFFFF)` (`:304`, `:448`, `:669`, `:682`, `:714`), and `GlobalTransitionDef.cs:19` documents **`// Effect action (0 = none)`** ⇒ **a real action whose name hashes to `0` or `0xFFFF` is registered and then NEVER INVOKED** |
| 🔴🔴 **mechanism 2 — the counter-allocated stubs** | `HsmBridgeEmitCore:119/138` — `ushort actionId = 100` / `guardId = 200`, each `++` per entry, registering **no-op bodies** (`__hsActionStub`/`__hsGuardStub { }`). ⛔ **The emitter is LIVE** (`HsmJsonGenerator:88`, `EditorSubsystem:3298`) — not dead like the orchestrators |
| ⭐⭐ **why either one is silent** | `HsmActionDispatcher:30` — **`RegisterAction(ushort id, IntPtr a) => ActionTable[id] = a;`** ⇒ **last writer wins. No guard, no diagnostic, no throw** |

⇒ 🔴🔴 **In both cases the HSM does not crash. It behaves correctly everywhere except one state, forever.**

### ⛔⛔ THE CORRECTION TO `W1` — as specified, it is blind to mechanism 2

`W1` says *"refuse duplicate hashed ids."* ⛔ **The stub ids are literal counters — they are never
hashed, so they never enter the hash set, and a gate built exactly to spec cannot see the collision
`W3` describes.** ⇒ ⭐⭐ **the gate must range over the FINAL id set — hashed ∪ counter-allocated —
or `W1` and `W3` both ship and the defect remains undetectable.**
⭐ **`W1` is `W3`'s detector. The handoff calls them independent; they are not.**

---

## 2. Scope — ⭐ **`W1` only**

| | |
|---|---|
| ⭐ **the collision rail** | refuse **two keys producing the same id**. ⭐ **Mirror `UtilityInputGenerator:173` — keep-first, report-rest**, with `UT0102` (duplicate *name*) as the sibling precedent |
| ⭐ **the reserved-value rail** | refuse any key hashing to **`0`** or **`0xFFFF`** — ✅ **premise verified above, cite those line numbers in the test** |
| **the standalone-key rail** | refuse any standalone key but `@0` *(design session's item — ⚠ **coordinator has NOT verified this one**; measure it before building the rail, and say what you found)* |
| ⭐⭐ **range over the FINAL id set** | §1's correction — ⛔ **not the hashed set** |
| 📐 **where** | `Fdp.Toolkits.Analyzers` — ⭐ **the same project as `HsmActionGenerator`**, so the gate sits beside the allocator. ⭐ **Put descriptors in a `Shared*Diagnostics` class**: `SharedUtilityDiagnostics.cs:7` says centralizing *"avoids RS1019 duplicate-descriptor warnings when both components"* reference one — ⚠ **a real build warning, and this project sets it** |
| ⛔ **NOT in this batch** | `W2` · `W3`'s fix *(next batch — this gate is what verifies it)* · `W4` · anything else |

---

## 3. Gates

**Baseline:** ⚠ **re-baseline against Batch 56's merged numbers, not `ee4d134ab`'s.**
⭐ **`Hrot.AiEditor.Generators` (193) and the analyzer suites are the ones that should move.**

| | |
|---|---|
| 🔴🔴 **the corpus must still BUILD** | ⛔ **A new refusal that reddens a shipped asset is a FINDING, not a failure to suppress.** 📐 **If any real HSM action collides or hashes to a reserved value TODAY, stop and report it — that is a live defect this gate just found, and it outranks the gate** |
| ⭐⭐ **fixtures, and they must be RED first** | **(a)** two keys colliding ⇒ build fails · **(b)** a key hashing to `0` ⇒ build fails · **(c)** a key hashing to `0xFFFF` ⇒ build fails · ⭐⭐ **(d) THE ONE THAT MATTERS: a real action colliding with the `100+`/`200+` stub window ⇒ build fails.** ⛔ **Without (d) the §1 correction is not actually implemented** |
| ⭐ **`StructureHash` unchanged for all 42** · golden Tier 1 **and** Tier 2 unchanged · `persistence-shape.txt` unchanged | ⛔ **this batch adds a DIAGNOSTIC. It emits nothing** |
| `tracker-counts.py --check` | clean |

⭐ **Precedent to copy for the fixtures:** `UtilityInputGeneratorTests:206` — `HashCollision_EmitsUT0103`,
which asserts `d.Id == "UT0103"` on the **second** method. ⭐ **Same shape, same assertion style.**

---

## 4. ⚡ How to work

**Opus.** ⭐ **A gate that is subtly too narrow is worse than no gate: it certifies the thing it cannot see.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

---

## 5. Reporting

⭐⭐ **Fixture (d) — the stub-window collision — and that it was RED before** · ⭐ **whether the real
corpus trips any of the four rails today** *(and if so, STOP and report)* · ⭐ **what you found on the
standalone-key rail, which the coordinator did not verify** · `StructureHash`/golden/persistence all
unchanged · per-suite numbers **full and filtered** · `tracker-counts.py --check` · ⭐ **every id you
allocated** (rule 5).

⭐⭐⭐ **The question to carry:** ⛔ **`ActionTable[id] = action` silently accepts a second writer, and
`ComputeHash` is used in at least six places.** 📐 **How many OTHER content-addressed id spaces in this
repo register last-writer-wins with no collision check?** ⚠ **`UT0103`/`UT0102`/`UT0150` prove the
pattern was recognised once, in one family, and not generalised.**
