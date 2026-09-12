# HANDOFF — Batch 47: ⭐⭐ **`U-7` + `U-8` — the type-existence rail, then the picker that depends on it**

> 📌 **Dispatched at `bf5b642e`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1671+` is the next free diagnostic.
>
> ⚠⚠ **ORDER SWAP — deliberate, and recorded in the plan.** These are the plan's *"batch 48"* tasks,
> pulled ahead of `U-6`/`U-13`/`U-16`. ⛔ **That batch is the one thing in the programme that hard-
> requires the visual check** — a Details table, a read-only view and deleting a whole window — and the
> check has not run for **eleven** batches. ⭐ **`U-7`/`U-8` are headless-provable and depend on nothing
> in it.** 📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md) §2 · `U-7`, `U-8`.

---

## 1. 🔴 `U-7` — the type-existence rail (`BP-228`, `Q-j`)

### 1.1 The defect, as probed

`TypeId = "Totally.Made.Up.Type"` ⇒ ⛔ **`SUCCEEDED=True`, `DIAGS=[]`**, emitting
`public global::Totally.Made.Up.Type Threat;` and a `BlueprintFieldDescriptor` over a type that does
not exist.

⭐ **The rule is purely SYNTACTIC:** contains a dot ⇒ **trusted verbatim**; no dot ⇒ `BP1500`.
⇒ ⛔ **The dot is doing the work of a type check.**

### 1.2 ⭐ The seam already exists — `Q-j`'s ruling

⛔ **Do not build a resolver.** ⭐ **`IClrSignatureResolver` is already threaded through
`CompileOptions`**, and the generator already hands it Roslyn's `Compilation`:

```csharp
// BlueprintIncrementalGenerator
ClrSignatureResolver: new RoslynClrSignatureResolver(compilation));
// CompileOptions.cs
IClrSignatureResolver? ClrSignatureResolver = null);
```

⭐⭐ **And Batch 44 measured that the in-process reflection path and the semantic-model path are
42/42 byte-identical** ⇒ **the oracle is the same oracle at both ends.** ⚠ **That measurement is one
of the two things this batch rests on.**

### 1.3 Gates

| | |
|---|---|
| ⭐ **Pass 1** | with an oracle knowing only `…StructDemoData`: a variable typed `Totally.Made.Up.Type` ⇒ **`Succeeded == false`** and a diagnostic **naming the variable AND the type**. ⛔ **Compiles clean today** |
| ⭐⭐ **Pass 2 — the fallback contract** | with **NO** oracle (`null`) the same asset compiles **exactly as today.** ⛔ **This is not a nicety:** unit tests, `.Succeeded` checks and every in-memory caller pass `null`, and a rail that fires without an oracle would redden the suite for the wrong reason |
| ✅ **Pass 3** | ⭐ **golden 42/42, both tiers, unchanged** — every shipped asset still compiles |
| 🔴 **Revert-goes-red** | remove the check ⇒ **Pass 1 fails** |

⚠ **Assert Pass 1 is RED before your change**, per Batch 45's lesson — an `Event`-graph fixture there
emitted an empty body and the first draft of the tests passed for nothing.

---

## 2. `U-8` — the type-choice union *(stage B′)*

| | |
|---|---|
| ⭐⭐ **Pass 1** | **every offered type COMPILES** — for each entry, build a variable of that type and compile it against a real oracle. ⭐ **This is `BP-87`'s lock, restored** |
| ✅ **Pass 2** | the list contains every `[BlackboardDtoStruct]` FQN **and** every primitive |
| ✅ **Pass 3** | ⛔ **no short names are offered** — a short name is `BP1500` |
| 🔴 **Revert** | drop the struct contributor ⇒ **Pass 2's count fails** |

### 📐⚠ The one thing genuinely open, and the plan says so

⛔ **Does the EDITOR get a type oracle at all?** ⭐ **`Q-j`'s lean was *not at first*.**
⚠ **The Batch 38 review pushed back:** `IClrSignatureResolver` is **semantic-model-backed in the
generator and reflection-backed in-process** ⇒ mirror it, and *"no oracle"* becomes a **unit-test
corner** instead of the editor's reality.

⇒ 📐 **Decide, and say which.** ⭐ **The lean is the review's** — an editor that offers a type it cannot
prove compiles is `BP-12c`'s inert button in a different costume. ⛔ **But if wiring an oracle into the
editor turns out to reach further than `CompileOptions`, STOP and report the shape** rather than
half-wiring it. ⭐ **`U-7` alone is a complete, shippable batch that closes `BP-228`.**

---

## 3. Gates

**Baseline — coordinator-run on the merged Batch-46 tree (`ea53e7e0`):**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** · BP diagnostics **10 distinct**, all `BP3010` |
| Blueprints | **3465 total / 3455 passed / 0 failed / 10 skipped** |
| ⭐ **AiShared 1216** *(moved +3 last batch, as declared)* | ⛔ **should NOT move here** |
| BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |
| ⭐⭐ **Golden 42/42, both tiers** | ⛔ **unchanged** |
| `tracker-counts.py --check` | clean **fifteen** batches running |

⭐ **Run the five `--no-build` suites in parallel; keep `\[FAIL\]` in the grep.**
⚠⚠ **The two NodeEdit gates take NO `--no-build`.**

⛔ **Say plainly what is NOT covered headlessly.**

---

## 4. ⚡ How to work

**You are on Opus.** ⭐ **`U-7`'s rail is compiler work — keep it.** 🟢 **Sonnet fits `U-8`'s
contributor sweep** once the oracle decision is made. ⛔ **Never the decision, never the gates.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the **tracker is yours** — ⭐ **`BP-228` closes with `U-7`** |
| ⚠ **Stop point** | ⭐ **after `U-7`**, if §2's oracle question turns out wider than `CompileOptions` |

---

## 5. Reporting

Per-suite numbers · ⭐⭐ **golden 42/42 both tiers, stated explicitly** · `tracker-counts.py --check` ·
⭐ **every id and diagnostic code you allocated** · ⭐⭐ **that Pass 1 was RED before the change** ·
⭐⭐ **your oracle decision and its reach** · **confirmation the no-oracle fallback is unchanged** ·
⭐ **where you stopped** · anything here **wrong against the code**.

⭐⭐ **Batch 46's best two moves were both about noticing a SILENCE:** it answered `BP-230`'s
eight-batch-old *"drawn-but-dead or hidden?"* from the panel code rather than waiting for a screenshot,
and it caught a **gate that did not move when the handoff said it would** — a green suite reading as
proof rather than as the absence of one.

⚠ **`U-7`'s Pass 2 is the same shape.** ⛔ **A rail that never fires because no caller supplies an
oracle is a rail nobody can tell is missing.** ⭐ **So report not just that Pass 2 passes, but HOW MANY
call sites actually supply one** — if the answer is "the generator, and nothing else," say so plainly.
