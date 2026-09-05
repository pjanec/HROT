# REPORT — Batch 74: **HSM's runtime caught up, and the compound key was spelled two ways**

> **Branch** `claude/hrot-implementation-j1jvin` · **base** `179a627` *(dispatch `6c49dc9db`)*
> **Rule 7** re-synced at start · **rule 4** re-fetched before the final commit.
> ⭐⭐ **All four dispatched items landed, plus BOTH amendments** — §0a.
> ⛔⛔ **The handoff was RE-DISPATCHED MID-RUN under rule 1a, and the ancestry check gave a FALSE
> NEGATIVE.** ⭐ **Read §0a before anything else.**

---

## 0a. ⛔⛔ **Rule 1a's ancestry check cannot see a run in progress — it reads the REMOTE branch**

📌 **What happened.** I ff-merged `179a627` *(which contains the dispatch sha `6c49dc9db`)* and started
building. ⭐ **While I was building**, the coordinator amended the handoff **twice** under rule 1a, each
time checking `git merge-base --is-ancestor <dispatch-sha> origin/<impl-branch>` and concluding *"no run
was ever in progress."*

⛔ **That check was correct about the remote and wrong about reality.** ⚠ **My branch had no PUSHED
commits yet**, so `origin/claude/hrot-implementation-j1jvin` still pointed at `0808253` and the dispatch
sha was genuinely not its ancestor — **while three items were already built locally.**

⇒ ⭐⭐ **Rule 1a's guard has a blind window: from the moment the implementation session merges the
dispatch to the moment it first pushes.** ⭐ **Two cheap fixes, either sufficient:** the implementation
session **pushes an empty "started at `<sha>`" commit immediately after the rule-7 merge**, or the
coordinator **asks** before re-dispatching rather than inferring from the remote. ⛔ **Reporting it
rather than working around it, per the standing ask.**

### ⭐ What the two amendments did, and how the collision resolved

| amendment | my state when it landed | outcome |
|---|---|---|
| ⭐⭐ **1 — `BP-281` PULLED** *("its destination is undecided")* | ✅ **already built, gated, green** | ⭐⭐⭐ **KEPT — and the SAME commit's new design doc un-pulls it.** 📄 **`DESIGN_Hsm_Storage_Model.md` §2**, verbatim: *"`BP-281` is **NOT blocked** … my pull was right for the wrong reason … it can be dispatched immediately."* ⭐ **What I built matches that §2 exactly** — packed offsets in `BehaviorParameters`, post-`-021` shape, both guards. ⇒ **no substantive conflict; only a timing one** |
| ⭐ **2 — the `InspectorWindow` retirement WITHDRAWN** *("no rush removals")* | ✅ **I had already declined to remove it**, on my own measurement | ⭐⭐ **Aligned independently.** ⚠ **The amendment asks for two things I had NOT done — an XML-doc line and a label rename — and both are now done** *(§5)* |
| ⭐⭐ **NEW item 4 — park the producer picker** | ⛔ **not started; added after my run began** | ✅ **Done anyway** *(§5b)* — it is small and self-contained |

⭐ **Nothing in the amended handoff is left undone.**

---

## 0. 🔴 Goldens — stated first

| baseline | moved? |
|---|---|
| 🔴🔴 **blueprint** *(`persistence-shape`, 43 `Emit/*.cs.txt`, `StructureHash`)* | ⛔ **NO, in any commit** |
| ⭐ **HSM emit** | ✅ **moved twice, both expected** — `8ae9507` *(item 1: `HsmVariableShowcase.Registrar` +51 lines, params only)* · `9f5045e` *(item 2: `HsmVariableShowcase.g.cs`, the compound key)* |
| ⭐ **`hsm-persistence-shape`** | ✅ moved in `9f5045e` — the seeded corpus binding |
| ⭐ **generated-code (HSM)** | ✅ moved in `9f5045e` — **one line**, the convention comment *(`{MethodName}` → `{MethodFqn}`)*. ⚠ **§4a: this cost me a probe reading** |
| ⭐ **generated-code (BTree)** | ✅ **CREATED** in `a73a9b0` — 21 files under `Golden/Generated/BTree/` |

---

## 1. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3691 / 3681 / 0 / 10** *(+1 — the inert rail)* |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1281 / 1281 / 0 / 0** *(+1)* |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **615 / 615 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** |
| **Generators** | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **266 / 266 / 0 / 0** *(**+17**)* |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj -v q --nologo` | ✅ **543 / 543 / 0 / 0** ⚠ **red first — §4b** |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build -v q --nologo` | ✅ **136 / 136 / 0 / 0** |
| Examples.Scenarios | `dotnet test FDP/Examples/Fdp.Examples.Scenarios.Tests/*.csproj --no-build -v q --nologo` | ✅ **68 / 56 / 0 / 12 skipped** ⭐ **quarantine count UNCHANGED at 12** |
| Examples.UrbanCombat | `dotnet test FDP/Examples/Fdp.Examples.UrbanCombat.Tests/*.csproj --no-build -v q --nologo` | ✅ **29 / 29 / 0 / 0** |
| Toolkits *(sample 1)* | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | 🔴 **1 failed** — `StatelessGizmoRegistryTests.SC_GZ022_2` |
| Toolkits *(sample 2)* | *same* | 🔴 **1 failed** — **the same test** |
| ⭐ **Toolkits *(isolated)*** | `… --filter "…StatelessGizmoRegistryTests.SC_GZ022_2…"` | ✅ **1 / 1** |
| ⭐ **Toolkits *(whole class)*** | `… --filter "…StatelessGizmoRegistryTests"` | ✅ **2 / 2** |
| ⭐⭐ **Toolkits *(whole `Gizmos` namespace)*** | `… --filter "…Gizmos"` | ✅ **187 / 187** ⇒ **`DEBT-AIB-030`** |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208** ⭐ **no `--no-build`** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131** ⭐ **no `--no-build`** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 61 / done 170 (+1 refuted)** |

⚠⚠ **The Toolkits red is red in BOTH full samples**, which is new — Batch 73's was 1-of-2. ⭐ **But it is
green in isolation, green for its whole class, and green for all 187 `Gizmos` tests**, and this diff
touches no gizmo code. ⇒ **`DEBT-AIB-030`, sixth distinct test.** ⛔ **Not signal.**

---

## 2. ⭐⭐⭐ Item 1 — `BP-281`, and the answer to *"did you reproduce (b) and (c)?"*

> 🔴 **Asked explicitly.** ⭐ **Answer: NO — and avoiding them needed a different fix than the obvious one.**

📐 **Defects (b) and (c) were not "the wrong condition".** They were **TWO conditions that disagreed**:
the options field and the `ParseParams` body each decided for themselves whether params existed, and one
of them said *"≥1 default"*. ⛔ **Copying the BTree bridge's guards — even its FIXED ones — would have
reproduced the SPLIT on a second host**, because the split is structural, not textual.

⇒ ⭐⭐ **The decision here is the PACKED FIELD LIST itself**, computed once in `EmitBridge` and consumed by
all three emissions: the `#nullable enable` pragma, the options field, the delegate. 📌 **This is not
cosmetic** — it is what makes `HsmOrthogonalRegions` *(managed, one `Role=State` variable)* emit **none of
the three**, which a copied pair of guards would have got wrong in exactly the (c) direction.

### 📐 Where the destination pointer comes from *(the STOP)*

| | |
|---|---|
| ⭐ **the pointer** | `BehaviorIngressSystem:100` passes a **shadow of the whole `BrainBlackboard`**, whose `BehaviorParameters` sit at **`[FieldOffset(0)]`** ⇒ `memory + packedOffset` **is** `bb.BehaviorParameters[0] + offset` — the region the analyzer's HSM thunks already read |
| ⭐⭐ **and the root case was enough** | a root HSM behaviour has **one** params area ⇒ ⛔ **nothing here waits on `E3`.** The occurrence question is about a SECOND home, and there is no second home to disambiguate |
| ⭐ **the offsets** | `BTreeBlackboardPackHelper.Pack`, **called not copied** — the same choice `E1` made for the slot key |

### 🔴 The revert probes, and what the first one caught

| probe | result |
|---|---|
| **A** — guard back to *"≥1 default"* *(defect b/c restored)* | 🔴 reddens **exactly two** rails: the no-defaults rail and the options-field rail |
| **B** — the whole `ParseParams` emission removed | 🔴 reddens **7 of 9** new rails **+ the HSM emit golden**. ⭐ The 2 that stay green assert an ABSENCE, correctly |

⛔⛔ **Probe A first found a VACUOUS rail OF MINE.** `TheJsonOptionsField_…` asserted
`Contains("__paramJsonOpts")` — and the emitted **body** calls `Deserialize(…, __paramJsonOpts)`, so the
bare name is present **even when the field declaration is gone**. ⚠ The rail was satisfied by the very
code that needs the missing field. 🛠 Fixed twice over: it now asserts the **declaration**, and the
compile harness takes the options field **from the emitter** instead of supplying its own, so a missing
declaration fails the compile. ⭐ **Fifth instance of *ask the artefact, not the thing that produced it*.**

### ⭐⭐ The rails assert BYTES

`CompileEmittedParseParams` **slices the emitted registrar**, compiles it, and runs the delegate over
real memory. ⛔ Nothing about the emitted shape is restated in the test — a re-statement is how a rail
comes to agree with itself.

---

## 3. ⭐⭐⭐ Item 2 — `E7b`, and **the finding of this batch**

> ⭐ **The STOP asked: does emitting it need per-transition storage that does not exist?**
> 📐 **NO. It needs a per-transition ACTION ID, and the storage is the shared params region.**
> ⛔ **Not `E3`-shaped.** Multiple transitions binding one variable **share its bytes, correctly** — it is
> one variable.

### ⛔⛔ What was actually wrong

📐 **The mechanism existed and was unreachable.** `HsmActionGenerator` emits a per-binding thunk for
every `[SharedAiAction]` — projecting the bound field at its byte offset out of
`bb.BehaviorParameters[0]` — and registers it under a **compound key**. **Nothing on the asset side ever
produced a compound key**, so those registrations were addressable by nobody.

⭐⭐⭐ **And the two generators spelled that key differently:**

| generator | compound key, at all three of its sites |
|---|---|
| ⭐ **`BTreeActionGenerator`** *(`:334`, `:385`, `:444`)* | `ContainingType + "." + Name + "@" + offset` — **the FQN** |
| ⛔ **`HsmActionGenerator`** *(`:261`, `:308`, `:365`)* | **`sym.Name`** — the SIMPLE name |

⇒ ⭐⭐ **`E6`(A)'s ruling had not reached the compound key.** An HSM asset stores its action as an FQN,
so a simple-name compound key could never be addressed from an asset — **the same silent `TryGetValue`
miss as `E6`, one layer down.** ⛔ Batch 72 fixed the plain-action key and I did not sweep the compound
one; this is that sweep.

🛠 The spelling now has **one home** — `HsmActionKey.CompoundKeyName`. ⚠ The netstandard2.0 emit core
**mirrors** it *(that assembly deliberately references nothing — the same forced mirror as
`HsmActionKey.Compute` ↔ `HsmFlattener.ComputeHash`)*, so ⭐ **an agreement test compares the id the ASSET
emits against the id read out of the GENERATED REGISTRAR** rather than recomputing either.

### 📐 The bytes, and the boundary that stops short of them

⚠ **I did not get a byte assertion, and the reason is worth recording rather than working around.**
📐 **No shipped assembly generates a compound-key thunk at all**: the four production `[SharedAiAction]`
methods live in `Fdp.Toolkits`, which does **not** run `HsmActionGenerator`; the one assembly that does
(`Hrot.AI.Behaviors`) declares none. ⇒ the binding is now **addressable**, and the thunk must be
generated **where the method lives** before there are bytes to assert. ⭐ The rail therefore stops at the
id agreement — which is the link that was broken — and the gap is named here rather than papered over.

### 🔴 The revert probes

| probe | result |
|---|---|
| **C** — analyzer compound key back to the simple name | 🔴 reddens the id-agreement rail |
| **D** — emitter stops baking the compound key | 🔴 reddens the emitted-key rail, the id-agreement rail **and** the HSM emit golden |

---

## 4. ⚠ Two things that cost me a reading, recorded

### a. **The generated-code golden moved for a COMMENT**

I renamed the convention comment `{MethodName}` → `{MethodFqn}`, which moved the Batch-73 baseline. ⛔ On
the first probe-C run that baseline was **already stale**, so its redness was **not evidence of anything**.
⭐ Re-read after regenerating: probe C reddens **only** the id-agreement rail. ⚠ **A stale baseline reads
exactly like a caught defect** — the reading is only valid against an up-to-date one.

### b. **A gap test I forgot to invert**

`ExpressionTargetFieldCountTests.TheRuntimeHalfDoesNotExistYet` — my own Batch 71 gap test — went red in
the gate run, correctly. ⭐ Inverted, not deleted. 📌 **Two gap tests covered `E7b` and I inverted one**;
the suite caught the other, which is what the suite is for.

---

## 5. ⛔⛔ Item 4 — **the premise is inverted; the panel is NOT retired**

> ⭐ **`.dev/` answered it first, exactly as the `2026-08-15` rule requires.**

| where | what it says |
|---|---|
| **`.dev/_DONE/blueprint-finalize/reports/BATCH-BB1B-REPORT.md:103`** | ⭐ designs "STATIC PARAMETERS" as the authoring surface for a bound variable's `DefaultValueJson` |
| **`.dev/_DONE/blueprint-finalize/reviews/BATCH-BB1B-REVIEW.md:21`** | ⚠ files *the composition root not wiring its accessor* as a **defect** (BB1C CT0) |

📐 **That wiring has since landed** — `EditorSubsystem:2135/2153` pass `ResolveExpressionTargetField` ⇒
**the panel runs.** 📐 **Track C's `VariableEditLauncher`** — its intended replacement
*(📄 `DESIGN_Variable_Details_And_Editing.md` §3, the `⋮` menu and a value-cell double-click)* — is
**constructed by nothing**: the table's context menu is not wired yet.

⇒ ⛔ **Retiring the panel would have deleted the ONLY LIVE surface and left the replacement unreachable.**

⭐⭐ **And ruling 9 is already satisfied, which is what the ask was really about**: Batch 68 routed the
panel through `DefaultValueAuthoring.OpenSession`, so the two are **one implementation with two entry
points**, pinned by the existing `ExactlyOneCallSite_OpensAVariableEditSession` rail.

🛠 **A gap rail asserts both halves** *(the panel routes; the launcher is constructed by nobody)*, named
for what it is — ⭐ **invert it when the Track C menu lands.** 🔴 **Probes E** *(the panel stops routing)*
**and F** *(the launcher gains a caller)* each redden it.

### ⭐ Amendment 2's two extra asks — both done

| | |
|---|---|
| ⭐ **the XML-doc line, where the code is** | ✅ On the section: **what it is** *(the default-value editor for the `ExpressionTargetField` variable — the OUTPUT binding)*, **that its duplicate-CODE half was resolved by `BP-267`**, and **why it is kept** *(node-scoped where Track C's table is asset-scoped)* |
| ⭐ **rename the label "if it is free"** | ✅ **It was free — measured: no test asserted the string.** Now **`DEFAULT VALUE — {var}`**, with the subtitle naming `ExpressionTargetField` |

⭐⭐ **And the irony the amendment names is real:** this section authors a default for a binding that
**`E7b`, in this same batch, is what makes the runtime able to read at all.**

---

## 5b. ⭐⭐⭐ Amended item 4 — **the producer picker is PARKED**

🔴 **The finding is OURS.** 📐 `ProducerPicker` + `ProducerCatalog` *(built Batch 70 for `G7`+`W10`)* are
complete and tested, and **nothing on either side calls them**: no panel constructs the picker, no
registrar supplies the catalog, **no asset field stores what `Persist()` returns**, ⛔ **and the runtime
they would feed does not exist** *(`R1`/`R2`/`R4`, resolver design §8.1)*.

| | |
|---|---|
| ⛔ **not deleted** | `2026-08-15`: **unreferenced ≠ unintentional** — built to a ruled design *(plan §4c / `AQ2`)*; deleting removes a **capability**, not a mistake |
| ⛔ **not wired** | the `2026-08-17` user ruling forbids exactly that — **an authoring surface whose consumer does not exist**; wiring repeats the mistake larger |
| ⭐⭐ **PINNED, not described** | `ThePickerIsInert_UntilTheResolverRuntimeExists` **fails the moment anyone constructs a picker** ⇒ wiring becomes the reminder to build the consumer. 🔴 **Probe L** *(one `new ProducerPicker(...)` inside the editor assembly)* **reddens it** |
| ⭐ **and findable** | XML-doc on **both** types carries the same sentence with the pointer, so the next session that greps for callers finds the answer rather than a deletion candidate |

---

## 6. ⭐⭐ Item 3 — the BTree emit tier, and a rail the handoff did not ask for

⭐ **What made it buildable: neither compilation-derived input needs SYNTAX.** `StructSizeResolver`
resolves through `Compilation.GetTypeByMetadataName`; `BTreeDeactivatorScanner` walks *"all named types
in the compilation (source **and referenced assemblies**)"*. ⇒ **a compilation with no syntax trees at
all**, carrying every loaded assembly as a metadata reference.

| rail | |
|---|---|
| 🔴🔴 **the acceptance test** | the same generator over a **BARE** compilation produces **different** output, and only the real one emits `RegisterDeactivator` ⇒ **the baseline is provably not the fallback** |
| ⭐⭐⭐ **the VALIDITY rail** *(not asked for)* | the harness output is **byte-for-byte** `obj/GeneratedFiles/…` — what `csc` really wrote — modulo the BOM the build adds on disk |

⚠ **Why the second one exists.** ⛔ **A harness can be "over the real compilation" and still take a
different arm than the build** — a missing input, a different assembly name, an unloaded reference — and
then the baseline pins a **harness artefact**: green, and reddening on nothing that matters. ⭐ That is
the same trap as baselining fallback output, only harder to notice.

### 📐 Measured, not assumed

⚠ I first wrote *"omitting the `*.bp.json` texts would silently take the fallback arm"*. **Probe J
disproved it**: withholding them changes nothing, because those `Params` sizes resolve from the compiled
`*_Bp.Params` types and the JSON fallback never fires against real assemblies. ⭐ **The comment now says
the measured fact**; the files are still fed because **the build feeds them**.

### 🔴 Probes

| probe | result |
|---|---|
| **G** — mutate a deactivator target the corpus does **not** reach | ✅ green — **correct**, not vacuous |
| **H** — mutate one it **does** reach | ⛔ **cannot compile**: analyzer `BHU_017` rejects it ⇒ **a stronger gate than mine already guards that string** |
| **I** — grow the FIRST struct in a one-variable blackboard | ✅ green — **correct**: nothing follows it, so no offset moves |
| ⭐ **K** — swap two variables so packed offsets shift | 🔴 reddens **the baseline AND the validity rail** |

---

## 7. ⭐ IDs allocated *(rule 5)*

| kind | allocated |
|---|---|
| tracker rows | ⭐ **`BP-292` · `BP-293` · `BP-294` · `BP-295` · `BP-296`** |
| diagnostics · architect questions | ⛔ none |

---

## 8. ⭐⭐ Debt rows touched

| row | what happened |
|---|---|
| ⭐ **`DEBT-AIB-021`** | its two defects are now **structurally impossible on the HSM host** — one value, not two guards (§2) |
| ⚠ **`DEBT-AIB-030`** | **red in BOTH full Toolkits samples** *(new — B73's was 1-of-2)*, green isolated / whole-class / whole-`Gizmos`-namespace. **Sixth distinct test. Not signal** |

⛔ **No other partition row touched.**

---

## 9. Not done

⛔ **`E3`** *(blocked on `Q35` — untouched, per the handoff)* · blueprint **multi-occurrence**
*(user-DEFERRED)* · **`E5`** · **`E7a`** · the **compound-key THUNK** *(§3 — no shipped assembly generates
one; the binding is addressable, the bytes await a `[SharedAiAction]` in a generator-bearing assembly)* ·
the 12 quarantined scenario tests *(out of programme)* · the Track C **visual check** and its **menu**
*(§5 — the gap rail inverts when it lands)* · **wiring the producer picker** *(§5b — parked by ruling;
its runtime is a decision the user has not taken)*.
