<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - the Batch 88 report.
stale-below: nothing.
known-rot: none.
known-conflict: none. Architect_Question_38 landed AFTER this batch's dispatch sha
  and is FYI only; §6 records how 88b relates to it.
-->
# REPORT — Batch 88: **Blueprint's live values, and a Details host for BTree / HSM**

> 📌 **Dispatched at `f7f57e79b`** · **started at `0109c6e`** *(rule 1b marker)* · **both items LANDED.**
> ⭐ **Ids allocated** *(rule 3/5)*: **`BP-333`** *(done)* · **`BP-334`** *(OPEN — the finding below)* ·
> **`BP-335`** *(done)*. **`BP-317` CLOSED.**
> ⛔⛔ **ONE FINDING THAT CHANGES WHAT `88a` DELIVERS — read §2.2 before believing `C7` is fixed.**

---

## 1. ⭐ What landed

| item | verdict |
|---|---|
| ⭐⭐ **`88a`** — Blueprint's `ILiveBlackboardValueProvider` | ✅ **BUILT** — ⚠ **but it does NOT close `C7`**; see §2.2 |
| ⭐⭐⭐ **`88b`** — a Details host for BTree and HSM | ✅ **BUILT** — ⭐ **this lifts `R-21`/`R-62`'s blocker on those hosts** *(§4)* |

⭐ **The handoff's framing was right on both:** *"BOTH ITEMS ARE ROUTING, NOT CONSTRUCTION."* ⛔ No byte
reader was built, no table was built, `BlueprintDetailsWindow` was not unsealed, and neither item added
machinery to a shared type.

---

## 2. 🛠 `88a` — Blueprint's live-value provider *(`BP-333`)*

📄 **Design basis:** `Q32` §4 **row 58** — *"the Value column… **+ blueprint's `ILiveValueProvider`**"* ·
`FINDINGS_VisualCheck_PostBatch86.md` §2 *(`C7`)*.

### 2.1 ⭐ What was missing, and why nothing looked broken

| 📐 measured | |
|---|---|
| `EditorSubsystem` | a provider for BTree *(`:2178`)* and HSM *(`:2190`)*, **`null` for Blueprint** |
| `Hrot.Blueprints.*` | ⛔ **zero** `ILiveBlackboardValueProvider` implementations |
| the rendered column | **`(pending)`** — ⚠ **the DESIGNED output for a source with no reader** |

⇒ ⭐⭐ **A gap whose symptom is indistinguishable from correct behaviour.** That is why row 58 merged
with half of it unbuilt, and it is the reason the row is worth the words in the tracker.

### 2.2 ⛔⛔ THE FINDING — **`C7` is NOT closed, and the handoff's rail instruction rests on a conflation**

> ⭐ The handoff says: ⚠ *"**Rail:** ask the ARTEFACT — assert the **cell text the control would draw**
> with a fake live session."*

📐 **Measured while writing that rail — there are TWO live-value seams, not one:**

| seam | shape | who consumes it |
|---|---|---|
| ⭐ **`ILiveBlackboardValueProvider`** | `GetLiveVariableValues(asset)` → name → **STRING** | ⛔ **exactly ONE surface: `BlackboardAuthoringWindow:514`**, which hands the map to `VariablesPanelControl.DrawSingle` |
| ⭐⭐ **`SectionVariableRowSource` / `BlackboardSectionRowSource`** `readRaw` | name → **BYTES**, decoded by `VariableValueFormatter` | ⭐ **the Track C Details table** — and every production construction site passes **`readRaw: null`, `entity: default`** |

⇒ ⭐⭐⭐ **`88a` makes the *Blackboard Variables* window's Value column live on Blueprint** — a real gap,
and the one row 58 literally names. ⛔⛔ **The Details panel still renders `(pending)` on all three
hosts**, because it never reads this interface.

⚠⚠ **Reported, NOT worked around.** ⛔ The handoff forbids the only fix that adds no new machinery —
*"⛔ **DO NOT build a byte reader**"* — and ⛔ the alternative changes a **shared** contract, which §6's
own rule marks a STOP: *"if either item starts looking like new machinery, you have taken a wrong turn."*

⭐ **Filed as `BP-334` with a lean**, because this is ruling 9's question and not a wiring question:

| option | ⚠ cost |
|---|---|
| **(a)** give the row sources a `readRaw` from the debug session | ⛔ Blueprint's snapshot hands back **DECODED** field values ⇒ this **re-encodes what was just decoded** |
| ⭐⭐ **(b)** give `IVariableRowSource` a formatted-value arm | one new **optional** member; the formatter stays the one formatter — ⭐ **the lean** |
| **(c)** keep both live paths | ⛔ **exactly what ruling 9 forbids** |

### 2.3 ⭐ What was built

- **`BlueprintLiveValueProvider`** *(`Hrot.Editor`)* — reads `IBlueprintDebugSession.CaptureLiveState`
  through **`BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot`**, which already owns the
  paused-pointer-vs-live decision. ⛔ **Re-deciding it here would be a second answer to one question.**
- ⭐⭐⭐ **Ruling 9 satisfied, not broken.** `LiveBlackboardValueProvider` reads
  `BehaviorRegistry` → `BehaviorState` → `BrainBlackboard`, gated on a behavior-name match —
  **BTree/HSM-shaped end to end**. Blueprint state lives in the `BlueprintBlackboard{16384,4096,1024}`
  partitions. ⇒ **ONE interface, ONE formatter, TWO adapters** — one concept with two sources.
- ⭐⭐ **A narrow `Func<ReadBlueprintState?>` seam, not the debug registry.** 📐 `IBlueprintDebugSession`
  has **36 members**; a provider that demands all of them is one **nobody rails**, which is how a
  provider ends up untested. ⭐ Mirrors `LiveBlackboardValueProvider`'s own `Func<…>` factories.
- ⚠ **Honest emptiness** — no entity / no reader / no snapshot ⇒ an **EMPTY map** ⇒ `(pending)`,
  ⛔ never a zero that looks like a value. 📌 `R-66`: a session existing means *"a document is OPEN"*,
  ⛔ not *"the sim is up"* — **liveness is decided by the SNAPSHOT.**

---

## 3. 🛠 `88b` — a Details host for BTree and HSM *(`BP-317` closed · `BP-335`)*

📄 **Design basis:** `Q32` **ruling 6** — *"The same Details panel is REUSED for every asset type — HSM,
BTree, Blueprint ⇒ **this is a cross-host deliverable, not a blueprint one**"* · `BP-317` · `R-60`/`R-62`.

### 3.1 ⭐⭐ `AiDetailsWindow` — the host, and what it is NOT

| ⭐ | |
|---|---|
| **is** | a `ManagedWindow` in **`AiShared`** hosting the **shared `VariableDetailsSection`**, titled `"Details"`, ids `ai_details_btree` / `ai_details_hsm` |
| ⛔ **is not** | a generalised `BlueprintDetailsWindow`. 📐 That window's OTHER arm is a blueprint node inspector *(`BlueprintAsset`, `BlueprintNodeDrawerRegistry`, `BlueprintNodeSelection`, a cached `INodeEditSession`)* and it is `sealed` — ⚠ **unsealing it drags `Hrot.Blueprints.Editor` into the AI perspectives to reuse the one part that is ALREADY shared** |
| ⛔ **does not** | build a table, a formatter or a row source — ⭐ **all three already shipped** |
| ⚠ **one arm, deliberately** | no node arm ⇒ **no `SelectionOrigin` arbitration.** The AI node surface is `InspectorWindow`, which **STAYS** *(`BP-295`; `R-86` out of scope)* |
| ⛔ **not a claimant** | 📌 the registrar's own rule: *"the Watch, the Inspector and Details itself must not claim, or a window that does not drive the panel would steal it."* ⭐ Railed over the TYPE, so adding the interface later FAILS rather than silently passing |

### 3.2 ⭐⭐⭐ Constructed by the REGISTRAR — **`EditorSubsystem` gained nothing to forget**

📌 **`R-67`**, and the reason `MyBlueprint` and `Variables` are built the same way. The window, its
routing, its run-state source and its gesture binding all land in the constructor and
`RegisterWindows`; ⛔ **there is no new composition-root line.**

⭐⭐ **The routing goes through the SAME pair `RegisterExtraWindow` uses for Blueprint** —
`IVariableOutlineSelectionSource` → `IVariableDetailsHost`, connected by the one
`ConnectOutlineToDetails`. ⛔ **Not re-implemented for the AI hosts.**

### 3.3 ⭐⭐ `BP-335` — the outline was throwing the clicked row away

📐 `MyBlueprintPanel` has always called **`navigateToItem(sectionId, itemId)`** *(`:300`)*, and
`AiMyBlueprintWindow` wired it as **`(sectionId, _) => SelectSection(sectionId)`**. ⇒ the item id
reached the window and was dropped one line later, so ⛔ **no AI row could be highlighted however the
table drew** — the same end state `BP-328` fixed on Blueprint, by a different mechanism *(there the
TYPE could not carry it; here the CALL SITE discarded it)*.

⭐ **`SelectItem(sectionId, itemId)`** raises **both** `SectionSelected` *(re-filters the standalone
table)* and the new `VariableSelectionChanged` *(drives Details)* — ⭐ **two LISTENERS of one gesture,
⛔ not two mechanisms**: one resolve, one raise, so a panel rebuild cannot double-fire.
`SelectSection` survives as `SelectItem(sectionId, null)`.

⭐ **`BlackboardMyBlueprintModel.DisplayNameOf` is static on purpose** — the heading must resolve
**before** an asset is selected *(the model is null until then)*, and a model-dependent lookup would
have silently shown the raw id `"bb.inputs"` as the heading.

### 3.4 ⭐ The Value column on the AI hosts

⭐ BTree and HSM already have live-value providers ⇒ the run state and the writability rules arrive
free. ⚠⚠ **But §2.2 applies here too:** the Details table's live arm is `readRaw`, which is `null` on
these hosts as well ⇒ ⛔ **the Value column is `(pending)` on BTree/HSM Details until `BP-334` is
settled.** ⭐ The handoff's *"the Value column comes free"* is true of the **standalone** table, ⛔ not
of Details.

---

## 4. ⭐⭐⭐ WHAT THIS UNLOCKS — **stated explicitly, as the handoff asked**

📌 **`R-21`** suspends visual checks *"until the Details panel is implemented and the emitters and all
access infrastructure are unified"*, and **`R-62`** records that as met **for Blueprint only, because
`R-60`: BTree/HSM have no Details window at all.**

⇒ ⭐⭐⭐ **`R-60`'s statement is now false: BTree and HSM have a Details window.** ⭐ **`88b` is the thing
that lifts the `R-21`/`R-62` blocker for those two hosts.**

⚠ **Two honest qualifications the guide should carry:**

1. ⛔ **The Value column on the new host reads `(pending)`** — `BP-334`, §2.2. ⭐ A check that reports
   *"no live values in BTree Details"* is confirming a KNOWN gap, not finding a new one.
2. ⛔ **These rails cannot see that ImGui draws anything** *(`R-21`/`R-62`: no visual checks)*. They
   prove the panel exists, is registered, is routed, is gesture-bound and holds the right rows.

---

## 5. ⭐ GATES — **the rule-8 report contract, seven rows**

### ⭐ 1 + 2 — per gate, with the `--no-build` column

| # | gate | command | `--no-build`? | result | Δ vs baseline |
|---|---|---|---|---|---|
| 1 | **AiShared** | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/…csproj` | ⛔ built | **1446 / 0 fail / 0 skip** | **+22** *(1424 → 1446)* |
| 2 | **Blueprints** | `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/…csproj` | ⛔ built | **3767 / 0 / 10 skip** *(3777)* | **0** |
| 3 | **BTree.Editor** | `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/…csproj` | ⛔ built | **615 / 0 / 0** | **0** |
| 4 | **Hsm.Editor** | `dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/…csproj` | ⛔ built | **551 / 0 / 0** | **0** |
| 5 | **Hrot.Editor** | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/…csproj` | ⛔ built | **201 / 0 / 0** | **+7** *(194 → 201)* |
| 6 | **Breakpoints** | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/…csproj` | ⛔ built | **143 / 0 / 0** | **0** |
| 7 | ⚠ **NodeEditor.Core** | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/…csproj` | ⛔⛔ **BUILT — never `--no-build`** | **211 / 0 / 0** | **0** |
| 8 | ⚠ **NodeEditor.UI** | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/…csproj` | ⛔⛔ **BUILT** | **135 / 0 / 0** | **0** |
| 9 | ⚠ **Fhsm.Tests** | `dotnet test FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/…csproj` | ⛔⛔ **BUILT** | **300 / 0 / 0** | **0** |

⭐ **Rows 7–9 are OUT OF SOLUTION** — 📌 they report a **STALE BIN** under `--no-build`, which is how
`Fhsm.Tests` once produced a false regression. ⛔ **All three were built, not skipped.**

⚠ **`Fdp.Toolkits.Tests` not run** — 📌 `DEBT-AIB-030`: seven distinct tests, **identity ROTATES between
runs**, so neither a red nor a green is evidence. ⛔ **This batch touches nothing in it** *(no
`Fdp.Toolkit.*` file changed — see the diff shape below)*.

### ⭐⭐⭐ 3 — golden movement, as a DIFF SHAPE

| | |
|---|---|
| ⭐⭐⭐ **ZERO goldens moved.** | ⛔ **No `.bp.json`, no emit golden, no `persistence-shape.txt`, no `StructureHash` fixture is in the diff.** |
| **the whole diff** | **9 files**: 4 new *(2 production, 2 test)*, 5 modified. **Production: 4** — `BlueprintLiveValueProvider.cs` *(new)* · `EditorSubsystem.cs` · `AiDetailsWindow.cs` *(new)* · `AiMyBlueprintWindow.cs` · `PerspectiveWorkspaceRegistrar.cs` · `BlackboardAuthoringWindow.cs` · `BlackboardMyBlueprintModel.cs` |
| **removed lines in production** | ⭐ **one comment sentence** *("Blueprint has no … live-value provider yet")*, replaced by the wiring it described. ⛔ **No production behaviour was deleted.** |
| ⚠ **test-count assertions moved** | **4**, all window counts, **all +1 or +2 per AI perspective, none per Blueprint** — `8→9` · `10→11` · `23→25` · `29→31`. ⭐ **Each carries an inline comment naming Batch 88b and why it is +2 and not +3** *(Blueprint keeps `BlueprintDetailsWindow`)*. ⛔ **0 test methods deleted.** |

### ⭐⭐ 4 — every RED confirmed pre-existing vs the base

⭐ **There are NO reds.** All nine suites are green at `HEAD`, against base **`f7f57e79b`**.
⚠ The 3 + 1 window-count failures seen mid-batch were **caused by this batch** and are the deliberate
movement in row 3 — ⛔ **not pre-existing, and not left red.**

### ⭐ 5 — the working tree is CLEAN after every suite run

✅ `git status --short` after the full set showed **only the batch's own 9 files**. ⛔ **No golden was
regenerated by a test.**

### ⭐ 6 — quarantine counts

| | before | after |
|---|---|---|
| **Blueprints skipped** | 10 | **10** |
| **every other suite skipped** | 0 | **0** |

⭐ **No new skip.** 📌 *"a new skip is a finding, not a fix."*

### ⭐ 7 — tracker, rulings, ids

```
python3 scripts/tracker-counts.py --check   → tracker counts OK — open 66 / done 204 (+1 refuted)
python3 scripts/rulings-check.py            → 60/60 rulings verified against their sources
python3 scripts/design-digest.py --check    → all 48 recently-changed design documents carry a STATUS header
```

⭐ **Baseline was open 66 / done 201.** ⇒ **+3 done** *(`BP-317`, `BP-333`, `BP-335`)*, **+1 open**
*(`BP-334`)*, **−1 open** *(`BP-317` closed)* ⇒ **66 / 204.** ✅ arithmetic holds.

⭐ **Ids allocated:** **`BP-333`** · **`BP-334`** · **`BP-335`**. ⛔ **No `Architect_Question_N` created**
*(rule 3a)*.

### ⭐⭐ 8 — THE `88b` ENUMERATION *(the handoff's extra gate)*

```
search_graph(project="home-user-HROT", label="Class",
             name_pattern=".*DetailsWindow.*|.*DetailsPanel.*|.*InspectorWindow.*")
                                                                          → total 13
search_graph(project="home-user-HROT", name_pattern="VariableDetailsSection|VariableTableControl|…")
                                                                          → total 8 (ONE Class)
```

| # | what the graph found, **before this batch** | disposition |
|---|---|---|
| 1 | **`BlueprintDetailsWindow`** *(Blueprints.Editor, 304 lines)* | ⭐ **the only window titled `"Details"`** |
| 2 | **`InspectorWindow`** *(`AiShared`, 678)* | ⛔ **STAYS** *(`BP-295`)* |
| 3 | ⚠ **`InspectorWindow`** *(`Blueprints.Editor`, 70)* | ⛔ a SECOND one — `BP-317`'s note, out of scope |
| 4 | **`RuntimeInspectorWindow`** *(`AiShared`, 57)* | ⛔ runtime family, out of scope |
| 5 | **`DetailsPanel`** *(`NodeEditor.UI`, 151)* | ⛔ external primitive; ⚠ *"unreferenced is not unintentional"* |
| 6–9 | `FakeAnimBackendInspectorWindow` · `FakeNavigationInspectorWindow` · `FdpEntityInspectorWindow` ×2 | ⛔ engine/sim, different lifecycle |
| 10–13 | the four `*Tests` classes | — |

⇒ ⭐⭐ **`VariableDetailsSection`: `total 8` nodes, exactly ONE `Class`, `in_degree 4`, hosted in
production by exactly ONE type — `BlueprintDetailsWindow`.** ⭐ **The handoff's premise held on
re-measurement.** ⚠ **Unlike Batch 87's gate 8, this one found no surprise** — ⭐ stated plainly rather
than implied, because a clean enumeration is evidence only if it is reported.

### ⭐⭐ 9 — WHAT EACH RAIL ASKS *(the handoff's other extra gate)*

📌 **Batch 87's lesson: a rail on a return value proves nothing** — `IsSelected` returned `true`
throughout the defect it existed to catch.

| rail family | ⭐ what it ASKS | ⛔ what it deliberately does NOT ask |
|---|---|---|
| **`BlueprintLiveValueProviderTests`** *(7)* | the **map the provider returns**, through a three-line fake reader — 4 of them assert **EMPTINESS**, which is the honest-`(pending)` contract | ⛔ **that the Details table shows it** — §2.2: it does not, and the class doc says so |
| **`PerspectiveWorkspaceRegistrar…LiveValueProvider`** *(2)* | ⭐⭐ **the CONSTRUCTED `BlackboardAuthoringWindow`** *(`HasLiveValueProvider`)* | ⛔ **the call site's signature** — 🔴 that mistake is recorded two rails above, in `DEBT-AIB-009`'s pair: it passes whether or not the argument is forwarded |
| **`TheAiHostsHaveADetailsPanelTests`** *(20)* | ⭐⭐⭐ **the CONSTRUCTED window**: the object the registrar built · the id the **`WindowManager` actually holds** · the **rows `Model.Build()` produces** · membership in `BoundTables` | ⛔ **that an event fired**; ⛔ that ImGui drew — 📌 `R-21`/`R-62` |
| ⭐ **the negative controls** *(5)* | Blueprint gets **no** `AiDetails`; the panel shows **nothing** before a click; an unroutable selection **clears**; the window is **not** a claimant; the standalone table **still** follows the same click | ⭐ **without these the positives could pass vacuously** |

⚠ **None of the 20 passes a `hostKind`, a section-source resolver or a details host** — 📌 `R-67`:
Batch 79's routing was inert in the editor **because it needed a call no production caller made**, while
every rail supplied it. ⇒ **if any of those were required here, these would be red.**

### ⭐⭐⭐ 10 — REVERT-GOES-RED, six probes, **never delegated**

| probe | what was un-applied | reds |
|---|---|---|
| **P1** | `BlueprintLiveValueProvider` returns `Empty` after resolving a snapshot | **2 / 7** ⭐ *(the two that assert CONTENT; the 5 emptiness rails stay green — correctly)* |
| **P2** | the registrar stops forwarding `liveValueProvider` to `BlackboardAuthoring` | **1** ⭐ the `R-67` forwarding rail |
| **P3** | `AiDetails` is never constructed | **17 / 20** |
| **P4** | the outline↔Details connection is removed *(window still built)* | **9 / 20** ⭐⭐ **and the exists / registered / gesture-bound / not-a-claimant rails STAY GREEN** — ⛔ the two halves are genuinely independent, not one rail wearing two names |
| **P5** | `SelectItem` stops grafting `SelectedVariablePath` | **1** ⭐ `BP-335`'s rail alone |
| **P6** | `RegisterCore(wm, AiDetails)` removed | **3** ⭐ the two WM rails + the cross-perspective id-count rail |

⛔ **Every probe was un-applied with the INVERSE EDIT** — ⛔ never `git checkout --`.
⚠ **P1 needed two attempts:** the first form tripped `CS0162 Unreachable code` under warnings-as-errors,
which is a **compile** failure and not a red rail. ⭐ Stated because a probe that fails to build is a
probe that proved nothing.

---

## 6. ⚠ Rule 4 — the coordinator branch, re-pulled before the final commit

📐 `origin/claude/blueprint-authoring-status-gm0akp` moved `f74d150 → a918597`: **one file**,
`Architect_Question_38_One_Details_Panel.md` *(+208 lines: the `2026-08-18` revision and recommended
answers `A`–`F`)*.

⛔ **It landed AFTER the dispatch sha `f7f57e79b`** ⇒ 📌 **FYI ONLY**, and ⭐ **it does not invalidate
either item** — its `R5` explicitly anticipates this batch: *"`88b` will add the BTree/HSM host that the
check must cover."* ⇒ **nothing adapted, nothing reverted, nothing to STOP over.**

⭐⭐ **Two things the coordinator should fold into `Q38`, since they postdate its inventory:**

1. ⭐ **`AiDetailsWindow` is a new member of `Q38`'s family.** ⚠ `R4` recommends **FOLDING**
   `BlueprintDetailsWindow` into the chameleon — ⇒ ⭐ **`AiDetailsWindow` folds the same way, as the AI
   feed.** ⛔ **It is not a new duplicate to retire:** it is the second instance of ruling 6's *one
   panel*, built because the AI perspectives had **none**, and it hosts the **same** shared section.
2. ⭐⭐ **`Q38-A`'s recommendation is already true here by construction.** `AiDetailsWindow` has **one
   arm and no mode switch**, and it deliberately **does not claim** the surface ⇒ ⛔ there is no second
   authority to unwind when the chameleon lands.

---

## 7. ⭐ Carried, and what the next batch inherits

| | |
|---|---|
| ⛔⛔ **`BP-334`** | **the two live-value seams.** ⭐ Needs a ruling-9 decision *(lean: **(b)**)*, ⛔ **not a wiring item.** ⚠ Until it is settled, **Details renders `(pending)` on all three hosts** |
| ⭐ **`BP-325`** | the emitter's eight `memory + 8` sites — still wants a batch that EXPECTS golden movement |
| ⭐ **row 60 / `U-16`** · **row 61** | untouched, and explicitly out of `88b`'s scope fence |
| ⛔ **PARKED** | `E3` · `E5` · `E7a` · `Q36` · `Q37` |
| ⭐ **`DEBT-AIB` partitions touched** | ⚠ **none.** ⛔ No `DEBT-AIB` row moved; `DEBT-AIB-030` was avoided, not resolved |

⭐ **The re-check the coordinator will write:** ⛔ **do not schedule `Q38`** *(`R-27`, and `Q38`'s own
`R5` agrees)*; ⭐ **do schedule the post-88 visual check**, now that the BTree/HSM host exists — ⚠ **with
`BP-334` named in it**, so `(pending)` in the Value column is read as a known gap and not a new defect.
