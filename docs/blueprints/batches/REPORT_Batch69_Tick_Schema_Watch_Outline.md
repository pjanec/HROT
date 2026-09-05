# REPORT — Batch 69: make the table LIVE — `C-tick` · `DEBT-AIB-009` · `C-watch` · `C-outline` · `E4`

> **Branch** `claude/hrot-implementation-j1jvin` · **base** `139b534` *(coordinator dispatch)*
> **Rule 7** re-synced at start · **rule 4** re-fetched before the final commit — ⭐ **nothing new on
> the coordinator branch.** ⭐ **All five items done.**

---

## 0. 🔴 `StructureHash` — **unchanged for all 43.** `persistence-shape.txt` — **unchanged.**

⭐ **Stated first.** Tree clean after every suite run; no `.bp.json` and no golden file regenerated.
⭐⭐ **For item 1 this is STRUCTURAL, not luck** — §1 explains why the counter cannot move either file.

---

## 1. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** *(baseline exactly)* |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3657 / 3647 / 0 / 10** *(**+8**)* |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1280 / 1280 / 0 / 0** *(**+19**)* |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **615 / 615 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** |
| Generators | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **203 / 203 / 0 / 0** |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **531 / 531 / 0 / 0** *(**+3**)* |
| **AiEditor.Persistence** *(added — the diff reaches it)* | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build -v q --nologo` | ✅ **136 / 136 / 0 / 0** |
| Toolkits | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | ✅ **1964 / 1964 / 0 / 0** · sample 2 ✅ **1964 / 1964** *(**+6**)* |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208 / 0 / 0** ⭐ **no `--no-build`** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131 / 0 / 0** ⭐ **no `--no-build`** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 61 / done 148 (+1 refuted)** |

⚠ **Toolkits: two samples, both green.** ⛔ **Per the standing rule that is NOT evidence** — it is simply
the outcome this time. Batches 67 and 68 each caught a red on the second sample; this batch did not.

---

## 2. 🔴 Item 1 — **where the tick counter went, and why not `InstanceVersion`**

> ⭐ **Asked explicitly.**

| placement | verdict |
|---|---|
| ⛔ **`BlueprintSlotEntry.InstanceVersion`** | it is the **latent-cursor staleness token** — bumped on hard reload, compared against `BlueprintLatentCursor.InstanceVersion`. ⛔⛔ **A second meaning on one field is the trap this programme keeps finding**, and the handoff named it first |
| ⛔ **a NEW field on `BlueprintSlotEntry`** | the entry is **exactly 16 bytes with a documented budget** — `StructureHash` is already *"truncated from ulong to fit"*. Growing it shrinks usable payload in **every** tier and moves the tier-fit arithmetic, ⚠ **for a counter no simulation code reads** — and it would enter the recorded snapshot (`[DataPolicy(NoSave)]` means snapshotted **and recorded**) |
| ⛔ **`BlueprintBlackboardHeader.Reserved`** | **wrong granularity** — the header is per **entity-tier**, and one entity hosts many slots. The ruling wants per `(asset, entity)` |
| ⭐ **a SIDE TABLE, owned by `Fdp.Toolkits`** | the counter is **editor telemetry, not simulation state**: nothing in the sim reads it, so it should cost the sim nothing and must not appear in a recorded frame |

⭐⭐ **That choice is also what makes §0 structural:** the counter adds **no byte to any persisted or
snapshotted layout**, so this item *cannot* move `StructureHash` or `persistence-shape`.

📐 **Confirmed where the handoff asked me to:** the stamp is inside the tick path.
`BlueprintTickSystem.Execute` opens with `if (deltaTime <= 0f) return;`, so **all four** stamps —
three entity tiers plus world singletons — are unreachable while paused. ⇒ ⭐ **frozen comes free, and
there is no fifth definition of *"am I frozen"***.

⚠ **Opt-in, default OFF**, held on by a **refcount** so closing one panel does not switch it off under
another; **allocation-free on the steady-state path** when enabled, asserted — the loop calls it once
per instance per frame and the allocation-trait tests would see a per-call closure.

### 🔴 Did the frozen rail fail before item 1? — ⭐ **yes, and not marginally**

⛔ **It could not even be written.** With no per-asset counter, `AssetTick` was `null` on every row and
`VariableChangeMonitor` reported `None` **by design** — Batch 68's own inertness assertion. ⇒ every
assertion in `AssetTickCounterTests` and `LiveVariableTableTests` was unreachable.
🔴 **Revert probes: dropping the four stamps reddens 5; keying by asset without the entity reddens both
independence rails.**

---

## 3. ⭐⭐ Item 2's question — **three instances of a fixable pattern, or three coincidences?**

> ⭐ **A pattern, and a narrow one. But the obvious fix is wrong.**

**The shape they share:** an **OPTIONAL constructor parameter whose silent default is
indistinguishable, at the call site, from a deliberate choice.**

| instance | how it presented |
|---|---|
| `HsmValidator._isStatefulSubtree` / `_sharedScopeKeys` | `_ => false` / `_ => empty` ⇒ rules 8/8b **inert** |
| `BlackboardAuthoringWindow._actionSchemaExporter` | null ⇒ the DTO reflection **contributes nothing** |

⛔⛔ **The fix cannot be "ban optional dependencies".** They exist so tests and lightweight hosts need
not supply everything, and that is legitimate — every one of these was **deliberately** optional.
⛔ **Nor can it be an automatic sweep**: a test over every optional parameter in the assembly would
flag dozens of correctly-defaulted ones and be switched off within a batch.

⭐⭐ **What actually distinguishes the three from the harmless majority is not the default — it is that
the caller HELD the value and did not pass it.** `PerspectiveWorkspaceRegistrar` handed the exporter to
the validator **two lines above** the window it did not hand it to; `EditorSubsystem` had the catalog
that both `E4` resolvers needed. ⇒ ⭐ **the checkable rule is "a production caller that HAS a
dependency must pass it"**, and the practical control is the one this batch used twice: **a forwarding
rail per dependency, asserted on the CONSTRUCTED OBJECT**, next to the precedent
(`…ForwardsFacetEditService_ToInspector`) that already existed for the same registrar.

📌 **Verdict: one pattern, three instances, and the register is now three rails long.** ⚠ I did **not**
build a generic detector — see §5 for the one I tried and threw away.

---

## 4. Items 3 & 4 — ⚠ **two design claims corrected, and what the visual check hides**

### 🔴 `C-watch` — §7 is stale twice, and the real hole was underneath

| §7 says | measured |
|---|---|
| *"`QuickReloadService:64` hardcodes `CompilerMode.Debug`"* | ⛔ **false** — it reads `asset.EditorMetadata.CompilerMode` |
| *"Debug emits no `PinValueChanged`"* | ✅ true (`DebugProbeInsertion:149` gates on `Trace`) — and `AddWatch` **already** requested `Trace` |
| 🔴 **the actual defect** | the request was guarded on `!_debugMaps.ContainsKey(assetId)`. ⇒ **set a breakpoint first (a Debug compile) and the asset HAS a map**, so adding a watch requested **nothing** — the watch showed `(pending)` forever, ⛔ **indistinguishable from *"it has not changed"***. The guard now asks whether the map knows **this PIN**, which is what Trace adds |

### ⚠⚠ What `C-watch` / `C-outline` could NOT be verified — **the visual check is suspended**

| | |
|---|---|
| **`C-watch`** | the **greying** of a stale row · the pin/unpin gestures · the `Type` column actually being **hidden on screen** |
| **`C-outline`** | the outline **drawing** · the section headers' **order on screen** · the per-section **"+"** affordance |

⭐ **What IS asserted is the meaning**: which rows and items exist, which section each variable lands
in, how they nest, which are highlighted, and what refuses a dialog.

---

## 5. 🔴🔴 **A rail I wrote was VACUOUS, and the probe is what said so**

⚠ **Second batch running.** The first `DEBT-AIB-009` rail scanned the caller's IL for a mention of
`IActionSchemaExporter` — which `PerspectiveWorkspaceRegistrar` satisfies **whether or not it passes
the argument on**, because the type is in its own signature. ⇒ ⛔ **removing the fix did not redden it.**

⭐ **Replaced with an assertion on the constructed object**, mirroring
`PerspectiveRegistrar_ForwardsFacetEditService_ToInspector` — the precedent for **exactly this question
about exactly this registrar**, which was **sitting in the same file the whole time**.
📌 **The lesson generalises:** when a rail is about *"did production wire X"*, ask the **object**, not
the call site. Batch 68's `C-dialog` probe taught the same thing one level down *(count call sites, not
methods)*.

---

## 6. ⭐ IDs allocated *(rule 5)*

| kind | allocated |
|---|---|
| tracker rows | ⭐ **`BP-270` · `BP-271` · `BP-272` · `BP-273` · `BP-274`** |
| blueprint diagnostics | ⛔ none *(`BP1675` remains FREE)* |
| analyzer diagnostics · architect questions | ⛔ none |

---

## 7. ⭐⭐ Debt rows touched *(the new standing ask)*

| row | what happened |
|---|---|
| ⭐ **`DEBT-AIB-009`** *(Track C)* | ✅ **CLOSED** — both production constructors now supply the exporter |
| ⭐ **`DEBT-AIB-028`** *(Track E)* | ⭐ **(b)+(c) discharged Batch 68; the `sharedScopeKeys` half closed here.** ⛔ **(a)** — persisting `StateNode.SubtreeAssetId` — **remains**, and is `E5`'s prerequisite. ⇒ rules 8/8b still will not fire on assets loaded from disk, **as expected** |

⛔ **No other row on the partition list was touched.**

---

## 8. What this batch did **not** do

**`E0`** *(the HSM golden harness)* · `E3` · `E5` · `E6` · `E7a`/`E7b` · `DEBT-AIB-021`'s overlay fix ·
the Instance params seam · multi-occurrence · `G7`+`W10` · the `InspectorWindow` "STATIC PARAMETERS"
retirement · **a tick counter for BTree/HSM hosts** *(the handoff allowed partial: those rows stay
`null` and therefore inert, which is the designed fallback — Blueprint Instances have
`BlueprintTickSystem` and a slot, the AI hosts would need their own stamp point)*.
