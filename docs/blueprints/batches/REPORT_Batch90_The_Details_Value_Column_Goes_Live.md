<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - the Batch 90 report.
stale-below: nothing.
known-rot: none.
known-conflict: none. It CONFIRMS the handoff's refinement of Batch 88's BP-334 lean
  (object arm, not string arm) and adds one measurement the handoff did not have — see §4.
-->
# REPORT — Batch 90: **the Details Value column goes live** *(`BP-334`)*

> 📌 **Dispatched at `67a6376e4`** · **started at `c569e5f`→`8c00a1e`** *(rule 1b marker)* ·
> **all three items LANDED.**
> ⭐ **Ids allocated** *(rule 3/5)*: **`BP-338`** *(done)*. **`BP-334` CLOSED.**
> ⭐⭐⭐ **Both hosts are live**: Blueprint through the object arm, BTree/HSM through bytes.
> 🔴🔴 **And it uncovered a latent regression that would have shipped WITH the feature — §4.**

---

## 1. ⭐ What landed

| item | verdict |
|---|---|
| ⭐⭐⭐ **`90a`** — the object arm on the row | ✅ **BUILT** — one delegate, one read site |
| ⭐⭐ **`90b`** — Blueprint supplies objects | ✅ **BUILT** — both construction sites |
| ⭐⭐ **`90c`** — BTree/HSM supply bytes | ✅ **BUILT** — no new arm needed, as the handoff predicted |
| 🔴 **`BP-338`** *(not in the handoff)* | ✅ **FOUND AND FIXED** — see §4 |

⭐ **Every premise in the handoff was re-measured and every one held.**
📐 `grep -rn "readRaw:" --include=*.cs Hrot/ | grep -v Tests` → **NOTHING**, confirmed on the
post-Batch-89 tree.

---

## 2. 🛠 `90a` — the object arm

📄 **Design basis:** the handoff §2 · `REPORT_Batch88` §2.2 *(the three options)* · **ruling 9** ·
`R-49` · `Q32` ruling 3.

| ⭐ | |
|---|---|
| **added** | `ReadObjectValue` — a `delegate object?` — and `VariableRow.ReadValueObject`, **trailing and `null` by default** ⇒ ⛔ every existing construction site unchanged |
| ⭐⭐⭐ **read in exactly ONE place** | `VariableValueFormatter.Decode`, through which **both `Cell` overloads and both `Tooltip` overloads already funnel** ⇒ ⭐ one implementation, and a live cell can never have a `(pending)` tooltip |
| ⛔ **not added** | a string arm · a second formatter · any change to `RawValueDecoder` · anything per-variable *(`R-49`)* |
| ⭐ **not changed** | `VariableValueFormatter`'s **constructor contract** — 📌 the handoff's STOP condition never triggered |

### 2.1 ⭐⭐ Why `object` survives contact with the code

📐 The pipeline is **bytes → decoder → `object` → formatter → text**, and `Decode` already returned
`object?`. ⇒ ⭐ **the arm enters one step in and everything downstream is untouched**: notation,
elision, `<unreadable>`, the struct tooltip.

⭐ **Two properties fell out for free and are railed:**
- ⛔ **No `ClrType` needed.** An object carries its own type ⇒ a blueprint row whose *declared* type
  could not be resolved now shows its live value instead of `<unreadable>`.
- ⭐ **A raw `byte[]` through the object arm is still `<unreadable>`** — the same mapping the byte path
  uses, because returning bytes is the decoder's "I could not" signal and rendering it **is** the
  `BP-01` hex bug.

### 2.2 ⚠ THE HONEST COST — **asserted, not just documented**

📐 §4a's change highlight diffs **BYTES** ⇒ a row on the object arm has none and its highlight is
**INERT**. ⭐ That is the safe direction and this codebase already chose it once — `ReadAssetTick`'s own
doc: *"a row with no tick source has an INERT highlight (never lights) rather than a WRONG one."*

⇒ ⭐⭐ **Blueprint: values live, highlight inert. BTree/HSM: values live AND highlight live.**
⛔ **No bytes were faked to light it.** ⭐ Both halves are rails, not prose
*(`AnObjectArmRowHasNoBytesAndThereforeAnInertHighlight` · `AByteArmRowStillCarriesItsBytes`)*.

---

## 3. 🛠 `90b` / `90c` — the two hosts, and the shape the measurement forced

⭐ **ONE new interface carries both arms**, each defaulting to `null` so an implementer declares only
what it can serve:

```csharp
public interface ILiveVariableProjection
{
    IReadOnlyDictionary<string, object>? GetLiveObjects(IEditableAsset asset) => null;
    IReadOnlyDictionary<string, byte[]>? GetLiveBytes  (IEditableAsset asset) => null;
}
```

| host | arm | how it is wired |
|---|---|---|
| ⭐ **Blueprint** *(`90b`)* | **objects** — `BlueprintStateSnapshot.FieldValues`, already decoded | the registrar installs the projection on `BlueprintMyBlueprintWindow` through `ILiveVariableProjectionHost`, in its **one `RegisterExtraWindow` pass** |
| ⭐ **BTree / HSM** *(`90c`)* | **bytes** — `readRaw`, the seam designed for it | the registrar builds `_sectionSource` itself ⇒ ⭐ **nothing to travel, no host interface at all** |

⭐⭐⭐ **`EditorSubsystem` gained ZERO new lines.** 📌 `R-67`, and the Blueprint registrar is the one
that has forgotten a service **four** times: the provider it already receives is type-tested once
(`LiveProjection`) and handed on from there.

### 3.1 ⭐ `90c`'s split, and the question the handoff asked

⭐ **Steps 1–5 of `GetLiveVariableValues`** *(entity → session → `BehaviorState` → name-match →
definition + `BrainBlackboard`)* became **`TryResolve`**, shared by both arms ⇒ ⛔ the byte arm and the
string arm cannot disagree about *whether this asset is live on this entity*. ⚠ **Behaviour-neutral for
the string arm**: same steps, same order, same early returns; only their home moved.

> ⭐ **The handoff asked:** *"if the string formatting can be expressed as `format(project(...))` with
> no behaviour change, say so; do not force it."*

⛔ **It cannot, and I did not force it.** 📐 `ProjectAndFormat` is
`FormatValue(Marshal.PtrToStructure(ptr, type))` — it decodes through the **marshaller**. Formatting
the raw bytes instead needs a **byte** decoder (`MarshalFromBytes`), which is a different mechanism
living above that assembly. ⇒ ⭐ **the two arms share the RESOLVE step and diverge at projection**,
which is as far as the split honestly goes. ⚠ The existing string path for `BlackboardAuthoringWindow`
is untouched — 📌 *"no rush removals."*

---

## 4. 🔴🔴 `BP-338` — **the latent regression this batch had to fix to be a fix at all**

📐 **Measured while wiring `90c`.** Both Details row sources set:

```csharp
HasEverBeenWritten: reader != null,     // "does a reader EXIST", not "was THIS NAME written"
```

⚠⚠ **Harmless for exactly as long as no production site passed a reader — and this batch passes one at
all three sites.** ⇒ ⛔⛔ **every declared variable would have claimed to be written**, and a variable
the run never touched would have rendered its decoded **ZERO** where `(pending)` belongs.

📌 **Guide row `C9` asserts the opposite, verbatim:** *"a variable declared but never written by the run
reads `(pending)`."* ⇒ ⭐⭐⭐ **the fix and the feature had to land together, or the feature was a
defect.** ⚠ This is the row the handoff flagged as *"the row that decides whether the user is
satisfied"* — it was right, and the danger was larger than it looked.

⭐ **The rule now:** presence is **MEASURED, per name, per frame** — the object arm asks
`map.ContainsKey(name)`, the byte arm asks `bytes.Length > 0`, and **both providers OMIT names they
could not project**, so absence is meaningful and free. ⛔ **No padding, no zero-filled buffers.**

⭐ **Rows are rebuilt every frame** by `VariableTableModel.Build()`, so resolving the map once per
`GetRows()` is a per-frame snapshot rather than a stale capture — railed on both hosts.

⭐⭐ **The probe makes the danger concrete:** reverting this one line reddens **5**, and ⚠ **two of them
are the NO-PROVIDER cases** — because the registrar now always passes a lambda, so `reader != null` is
`true` even when nothing is live. ⇒ ⛔ the old rule was not merely imprecise; it was **actively wrong
the moment the seam was filled**.

---

## 5. ⭐ GATES — **the rule-8 contract, plus the three this batch owns**

### ⭐ 1 + 2 — per gate, with the `--no-build` column

| # | gate | command | `--no-build`? | result | Δ vs baseline |
|---|---|---|---|---|---|
| 1 | **AiShared** | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/…csproj` | ⛔ built | **1479 / 0 / 0** | **+22** *(1457 → 1479)* |
| 2 | **Blueprints** | `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/…csproj` | ⛔ built | **3773 / 0 / 10 skip** *(3783)* | **+6** *(3767 → 3773)* |
| 3 | **BTree.Editor** | `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/…csproj` | ⛔ built | **615 / 0 / 0** | **0** |
| 4 | **Hsm.Editor** | `dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/…csproj` | ⛔ built | **551 / 0 / 0** | **0** |
| 5 | **Hrot.Editor** | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/…csproj` | ⛔ built | **201 / 0 / 0** | **0** |
| 6 | **Breakpoints** | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/…csproj` | ⛔ built | **143 / 0 / 0** | **0** |
| 7 | ⚠ **NodeEditor.Core** | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/…csproj` | ⛔⛔ **BUILT — never `--no-build`** | **211 / 0 / 0** | **0** |
| 8 | ⚠ **NodeEditor.UI** | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/…csproj` | ⛔⛔ **BUILT** | **135 / 0 / 0** | **0** |
| 9 | ⚠ **Fhsm.Tests** | `dotnet test FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/…csproj` | ⛔⛔ **BUILT** | **300 / 0 / 0** | **0** |
| 10 | ⚠⚠ **Fdp.Presentation** | `… --filter "FullyQualifiedName~Fdp.Presentation.Tests.WindowManager"` | ⛔ built | **146 / 0 / 0** | **0** |

⛔ **Row 10 is FILTERED** — 📌 `BP-337`: the full suite crashes its test host *(a Vis2D defect,
pre-existing)*, so its totals differ between runs and neither a red nor a green is evidence.
⚠ **`Fdp.Toolkits.Tests` not run** — 📌 `DEBT-AIB-030`. ⛔ Nothing in this diff touches it.

### ⭐⭐⭐ 3 — golden movement, as a DIFF SHAPE

| | |
|---|---|
| ⭐⭐⭐ **ZERO goldens moved.** | ⛔ **No asset, no emit golden, no `persistence-shape.txt`, no hash fixture is in the diff.** ⭐ Expected: this batch touches only the editor's READ path |
| **the whole diff** | **15 files**: 5 new *(2 production interfaces, 3 test files)*, 10 modified |
| **production** | **9** — `VariableRow` · `VariableValueFormatter` · `VariableRowSources` · `BlackboardSectionRowSource` · `PerspectiveWorkspaceRegistrar` · `ILiveVariableProjection` *(new)* · `ILiveVariableProjectionHost` *(new)* · `BlueprintMyBlueprintWindow` · `BlueprintMyBlueprintModel` · `BlueprintLiveValueProvider` · `LiveBlackboardValueProvider` |
| **shape** | ⭐ **additive except for two deliberate behaviour changes**: `HasEverBeenWritten` *(`BP-338`)*, and `SectionVariableRowSource.ToRow` split into an arm-selector plus one `NewRow` builder. ⛔ **One method extracted, none deleted** |
| ⚠ **existing test assertions moved** | ⭐ **NONE.** ⛔ **0 test methods deleted, 0 edited.** ⚠ Notable given `HasEverBeenWritten`'s semantics changed — no existing rail asserted the old rule, which is itself why it survived |

### ⭐⭐ 4 — every RED confirmed pre-existing vs `67a6376e4`

⭐ **All ten gates are green at `HEAD`.** ⛔ The only known failure in the repo is `BP-337`'s
`Fdp.Presentation.Tests` host crash, confirmed pre-existing **in Batch 89** in a clean worktree, and
row 10 is filtered around it.

### ⭐ 5 — the working tree is CLEAN after every suite run

✅ `git status --short` after the full set showed **only the batch's own 15 files.**

### ⭐ 6 — quarantine counts

| | before | after |
|---|---|---|
| **Blueprints skipped** | 10 | **10** |
| **every other suite skipped** | 0 | **0** |

⭐ **No new skip.**

### 🔴🔴 7b — `tracker-counts.py --check`, run **LAST** and pasted verbatim

```
$ python3 scripts/tracker-counts.py --check
tracker counts OK — open 66 / done 207 (+1 refuted)
EXIT=0
```

⛔⛔ **AND THE FIRST RUN WAS RED — the coordinator's diagnosis is exactly right.** Pasted as it came:

```
TRACKER COUNTS DISAGREE WITH THE ROWS:
  RW-M: table says open=29 done=59, rows say open=28 done=61
  Total: table says open=67 done=205, rows say open=66 done=207
```

⭐⭐⭐ **ROOT CAUSE, on my side, stated so it stops:** in Batches 88 and 89 I piped `--check` through
`tail -2`/`tail -3`. ⇒ ⛔ **that discarded the failure banner AND the exit code, leaving only the
corrected table the script prints to help you fix it** — which reads exactly like a success line.
⚠ **I was reading the script's REMEDY and reporting it as its VERDICT.**
⭐ **Every gate script this batch was run unfiltered with `EXIT=$?` shown**, and that is now my habit,
not a resolution.

```
$ python3 scripts/rulings-check.py     → 66/66 rulings verified against their sources   EXIT=0
$ python3 scripts/design-digest.py --check
    All 49 recently-changed design documents carry a STATUS header, …                   EXIT=0
```

⭐ **Ids allocated:** **`BP-338`**. **`BP-334`** closed. ⛔ No `Architect_Question_N` created *(rule 3a)*.

### ⭐⭐ 8 — THE ENUMERATION: every `IVariableRowSource` and every production construction site

```
search_graph(project="home-user-HROT", name_pattern=".*VariableRowSource.*|.*RowSource.*|IVariableRowSource")
                                                                                → total 12 (4 classes)
grep -rn "IVariableRowSource" --include=*.cs .   (production only)              → total 9
grep -rn "readRaw:" --include=*.cs Hrot/ | grep -v Tests                        → total 0  ⛔ the defect
```

| # | implementer | live seam | wired by this batch? |
|---|---|---|---|
| 1 | **`SectionVariableRowSource`** *(Details, blueprint)* | ⭐ **object arm** *(new)* | ✅ **both sites** |
| 2 | **`BlackboardSectionRowSource`** *(Details, AI)* | ⭐ `readRaw` *(existed, always `null`)* | ✅ **the registrar's site** |
| 3 | **`FixedVariableRowSource`** | ⛔ none — rows are pre-built by the caller | ⛔ n/a by construction |
| 4 | **`PinnedVariableRowSource`** *(Watch)* | ⭐ rows are **pinned copies** carrying whatever arm they were built with | ⚠ **inherits both arms for free** — see below |

⭐⭐ **Three production construction sites, all three wired:**
`BlueprintMyBlueprintWindow:~350` *(locals)* · `:~378` *(asset-scoped)* · `PerspectiveWorkspaceRegistrar:~363`
*(AI)*. ⭐ **That is the same count the handoff measured** — ⚠ **no fourth surprise this time**, unlike
Batch 87's gate 8, and stated plainly because a clean enumeration is evidence only if it is reported.

⚠ **One consequence worth naming:** a Watch row pinned from a live Details row **carries its arm with
it**, so the Watch goes live on the same seam without a line of new code. ⛔ **Not claimed as tested** —
no rail here pins a live row, and pinning is `DESIGN_Variable_Watch_Pinning.md`'s batch.

### ⭐⭐⭐ 9 — WHAT EACH RAIL ASKS *(the CELL TEXT, four cases)*

📌 The handoff: *"a rail on the provider's return value proves NOTHING."* ⇒ ⛔ **not one assertion below
reads a dictionary.** Every one runs a row through `VariableValueFormatter` — the object the control
calls.

| the four required cases | where | asserted |
|---|---|---|
| ⭐ **live value** | `TheValueColumnGoesLive` · `TheLiveProjectionReachesTheRowSources` · `TheBlueprintDetailsValueColumnIsLive` | `"7"`, `"42"`, `"99"`, `{A=1, B=2}` |
| ⭐⭐ **`(pending)` for a name ABSENT from the map** | all three files | ⭐ the `C9` rail, on both hosts **and** on the real `ResolveVariableSelection` path |
| ⭐ **`(pending)` with NO provider at all** | all three files | ⭐ plus a **string-only provider**, which must degrade rather than throw |
| ⭐⭐ **the TOOLTIP agrees with the cell** | `TheTooltipAgreesWithTheCell` | cell `{A=1, B=2}` vs tooltip `A = 1\nB = 2` |

⭐ **Beyond the four:** the arm is **preferred over bytes** · works **without a `ClrType`** · a `null`
object is `<unreadable>` **not** `(pending)` · a **throwing** arm never takes the window down · the map
is **re-read every frame** *(railed on both hosts)* · the object arm's highlight is **inert** and the
byte arm's is not.

### ⭐⭐ 10 — REVERT-GOES-RED, five probes, **never delegated**

📌 The handoff: *"at minimum drop the wiring at each of the three construction sites, separately —
three sites, three distinct reds, or one of them is unrailed."*

| probe | what was un-applied | reds |
|---|---|---|
| ⭐ **P1** | **site 1** — the registrar's `readRaw` stops consulting the projection | **2** — `AnAiSectionSourceRendersLiveValues` ×2, ⭐ and **only** those |
| ⭐ **P2** | **site 3** — the locals arm's `liveObjects` | **1** — `ALocalVariablesCellRendersItsLiveValue` |
| ⭐ **P3** | **site 2** — the asset-scoped arm's `liveObjects` | **3** — the two global rails + the per-frame rail |
| **P4** | `Decode` stops reading the object arm *(the `90a` enabler)* | **7** |
| 🔴 **P5** | `HasEverBeenWritten` back to `reader != null` *(`BP-338`)* | **5**, ⚠ **two of them the NO-PROVIDER cases** — §4 |

⭐⭐ **Three sites, three DISTINCT and non-overlapping red sets** ⇒ none is unrailed, and none is
covered only by another's rail.
⛔ **Every probe un-applied with the INVERSE EDIT** — ⛔ never `git checkout --`.

---

## 6. ⭐⭐⭐ WHAT THIS UNLOCKS — **which hosts, so the guide names the right ones**

> ⭐ The handoff: *"guide rows `C7` and `H9` INVERT… say which hosts you made live."*

| host | Details **Value** column | §4a **change highlight** |
|---|---|---|
| ⭐⭐ **Blueprint** | ✅ **LIVE** — objects, via `BlueprintLiveValueProvider.GetLiveObjects` | ⚠ **inert** *(no bytes — §2.2, by design)* |
| ⭐⭐ **BTree** | ✅ **LIVE** — bytes, via `LiveBlackboardValueProvider.GetLiveBytes` | ✅ **live** |
| ⭐⭐ **HSM** | ✅ **LIVE** — same provider, same seam | ✅ **live** |

⇒ ⭐⭐⭐ **`C7` and `H9` invert on ALL THREE hosts: a live value is now the PASS condition.**

⚠ **Three things the re-written rows must say, or a correct behaviour will read as a defect:**

1. ⭐⭐ **`(pending)` is still correct, and often.** No selected entity · no live session · the sim not
   running · **a variable the run has not written yet** ⇒ `(pending)`. 📌 **`C9` is unchanged and is
   now enforced by `BP-338`'s rule** — ⛔ a zero there would be the regression.
2. ⚠ **Blueprint's change highlight does not light.** That is §2.2's stated cost, ⛔ not a bug.
3. ⭐ **The standalone *Blackboard Variables* window is unchanged** — it still reads the string arm.
   📌 *"no rush removals."*

---

## 7. ⭐ Carried

| | |
|---|---|
| ⚠ **`BP-337`** | `Fdp.Presentation.Tests` crashes its host ⇒ **an unrunnable suite is an ungated one.** ⭐ A Vis2D defect, worth its own batch |
| ⭐ **`BP-325`** | the emitter's eight `memory + 8` sites — wants a batch that EXPECTS golden movement |
| ⭐ **row 60 / `U-16`** · **row 61** | untouched |
| ⛔ **out of scope, unchanged** | `LiveBlackboardPanel`'s retirement *(`Q38`: fixed-list formatter arm first)* · watch **pinning** · the `⋮` button · task group `D` · everything in `Q38`–`Q44` *(`R-27`)* |
| ⭐ **`DEBT-AIB` partitions touched** | ⚠ **none.** No `DEBT-AIB` row moved |
