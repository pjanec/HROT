# REPORT — Batch 72: **occurrence identity** — `E6`(A) ✅ · `E3` ⛔ escalated · multi-occurrence ⛔ not started · BTree corpus ✅ half

> **Branch** `claude/hrot-implementation-j1jvin` · **base** `5d01a5c` *(dispatch `844f81e93`)*
> **Rule 7** re-synced at start · **rule 4** re-fetched — nothing new on the coordinator branch.
> ⚠⚠ **THREE of four items landed. Item 3 was NOT STARTED — §5, and it is the one you most wanted.**

---

## 0. 🔴 Goldens — **blueprint set unchanged. HSM emit baseline unchanged too, and that is a finding.**

| baseline | moved? |
|---|---|
| ⭐ **blueprint** *(`persistence-shape`, 43 `Emit/*.cs.txt`, `StructureHash`)* | ⛔ **no, in any commit** |
| ⚠ **HSM emit** | ⛔ **no** — the handoff expected it to move. **§2 explains why it cannot** |
| ⭐ **BTree shape** *(new, 26 assets)* | ✅ **created** in `cb9286f` |

---

## 1. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** ⭐ **`FDP/Examples` builds — item 1's real gate** |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3690 / 3680 / 0 / 10** |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1280 / 1280 / 0 / 0** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **615 / 615 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** |
| **Generators** | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **245 / 245 / 0 / 0** *(**+17**)* |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **543 / 543 / 0 / 0** |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build -v q --nologo` | ✅ **136 / 136 / 0 / 0** |
| ⭐ **Examples** *(item 1's blast radius)* | `dotnet test FDP/Examples/Fdp.Examples.UrbanCombat.Tests/*.csproj --no-build -v q --nologo` | ✅ **29 / 29 / 0 / 0** |
| Toolkits *(sample 1)* | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | 🔴 **2 failed** — `GizmoRegistryTests.SC_GZ004_2` · `StatelessGizmoRegistryTests.SC_GZ022_2` |
| Toolkits *(sample 2)* | *same* | 🔴 **1 failed** — `GizmoRegistryTests.SC_GZ004_2` |
| ⭐ **Toolkits *(both reds, isolated)*** | `… --filter "…SC_GZ004_2…|…SC_GZ022_2…"` | ✅ **2 / 2** ⇒ **`DEBT-AIB-030`** |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208** ⭐ **no `--no-build`** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131** ⭐ **no `--no-build`** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 61 / done 161 (+1 refuted)** |

⚠ **An EARLIER Toolkits run was 6 red and NOT the flake** — a real regression from item 1. §3.

---

## 2. ⭐⭐⭐ Item 1 — `E6`(A) shipped. **Two premises did not hold.**

⭐ **Registrar ids and blob ids agree for every corpus asset**, asserted against the **real compiled
blob** on one side and the **running generated registrar** on the other. ⭐ Four example call sites
moved to the FQN. ⭐ **STOP swept clean:** nothing outside `FDP/Examples` and the HSM path addresses an
action by name — the only other hits are direct C# calls in tests.

### ⛔ Premise 1 — *"the HSM emit baseline MOVES (ids change)"*. **It does not, and cannot.**

📐 The HSM emitted `.g.cs` carries action **STRINGS** (`.OnEntry("<FQN>")`). The **ids** are computed
at runtime by `HsmFlattener` inside `Compile()`, and by the **analyzer's** `HsmActionRegistrar` —
⭐⭐ **neither is in `E0`'s baseline**, which covers `HsmEmitCore` / `HsmBridgeEmitCore` output only.
⇒ **an id change is invisible to the emit tier.** ⚠ That is a real coverage limit of `E0`, worth
knowing, and it is precisely why the new rail exists rather than a regenerated baseline.

### 🔴🔴 Premise 2 — **my own rail was VACUOUS, and the probe caught it. Fourth time in five batches.**

The first draft derived the registrar side as `FNV(FullName)` — **its own rule**, not the generator's.
⛔ Reverting the analyzer to the simple-name key left it **green**. ⭐ It now **runs the generated
`HsmActionRegistrar`** and reads the ids out of `HsmActionDispatcher`'s tables.
📌 *Ask the artefact, not the thing that produced it* — 68 counted methods, 69 scanned a signature,
70 read an expectation out of the field under test, **72 recomputed the rule under test.**

🔴 **Revert-goes-red (against the FIXED rail): keying back on the simple name reddens 3 of 4 corpus
assets.**

---

## 3. ⚠ Item 1's regression — **caught by the gate, and it was my miss**

`HsmDispatcherIdAnalyzerTests` searched for a **simple name** hashing to a target. Under (A) that is a
string the generator no longer hashes.

⛔⛔ **Worse than a moved number:** the fixture varied `[HsmAction(Name=…)]` while every method stayed
`Method{i}` ⇒ under (A) **every fixture in a multi-name test collapsed to ONE id**, so the collision
tests would have been **testing the fixture, not the analyzer**.

⭐ Fixed: the fixture varies the **method name**, drops the override, and `NameHashingTo` searches over
`{container}.{candidate}`.
⚠ **My miss, plainly:** item 1 swept for consumers that **address** an action by name — ⛔ **not for
tests that assert the old key.**

---

## 4. ⛔⛔ Item 2 — `E3` **STOP.** The collision is real; the seam is not a signature widening

> ⭐ **The standing ask again: this changes the item's size, so it is escalated, not half-built.**

| the handoff's premise | measured |
|---|---|
| ⭐ *"`r` and `current` are already in scope"* | ✅ **true** — `slotIndex`, `stateId` in `HsmKernelCore` |
| ⛔ *"a signature widening, not a data-flow redesign"* | 🔴 **false, twice over** |

1. 🔴 **The thunk cannot receive the occurrence.** `HsmActionDispatcher` dispatches through
   `delegate*<void*,void*,HsmCommandWriter*,void>`, and every registered id is a **static function
   pointer chosen at build time**. Regions are a runtime notion.
2. 🔴🔴 **And there is nowhere for a second occurrence's bytes to live.** The generated thunk resolves
   its DTO at `bb.BehaviorParameters[0] + <baked offset>` — a fixed offset into the entity's
   **single** `BrainBlackboard` (100 B, one per entity). ⇒ **two occurrences have one home by
   construction.**

⇒ ⭐⭐ **`E3` is a STORAGE MOVE** — per-occurrence bytes must come from the partition allocator under
`ComputeStatefulSlotKey(assetId, Scope.Node, occurrence, variableId)`, ⭐ **the route `Q34` §7 itself
recommends for `E5`** — **plus** the delegate widening. That spans `Fhsm.Kernel`, the analyzer's thunk
emission and the allocator, and reaches `ExtDeps`.

⭐ **Landed instead:** the handoff's rail in the only committable form — three tests asserting the GAP
with the mechanism named, one of them reading the **analyzer's source** rather than restating the rule.
⚠ **Invert them when `E3` lands**; `HsmOrthogonalRegions` is already in the corpus to carry the
positive version.

⛔ **The two `STOP` questions you asked are therefore unanswerable as posed:** *"did the two-regions
rail fail first"* — it cannot be written until the storage exists; *"is the region/state pair stable
across ticks"* — no such pair reaches the thunk. The params-base half did **not** come free.

---

## 5. ⛔⛔ Item 3 — **NOT STARTED.** Reported rather than half-built

> 🔴 **This is the item you most wanted (`Q34` resolved, *"the user said BUILD IT NOW"*), and it is the
> one I did not do. Saying so plainly.**

📐 **Measured edit surface** *(so it can be re-dispatched without re-measuring)*:

| | |
|---|---|
| ⭐ **cheap by construction** | **187** `TryGetSlotOffset` call sites **all stay correct** — `Q34-C`'s 3-arg overload keeps meaning key `0`. ⭐ **That ruling does its job** |
| ⚠ **the real surface** | `BlueprintSlotEntry` + `SlotEntrySize` 16→20 · **three tier `const`s + their doc comments** · `Initialize` / `Migrate` / `TryAttach` / the 4-arg `TryGetSlotOffset` · `AttachInstanceBlueprintEvent` + `Replace` gain `InstanceKey` · `TryFindExistingTier` **per key** · `DetachFromEntity` **per key** ⇒ **~10 files** |
| 🔴 **and every payload-size assertion moves** | 928 / 3936 / 16368 → 912 / 3904 / 16032 |

⛔ **Why not attempted:** this is a **memory-layout change**, and this programme's own tracker records
the failure mode repeatedly — *"wrong offsets read plausible bytes from the wrong place."* Starting it
with the room I had left would likely have left it unfinished, and a half-applied slot-entry widening
is the worst possible intermediate state. ⭐ **It also has its own revert story**, which is the same
reasoning that kept Batch 52's store flip alone.

⭐ **Recommendation: re-dispatch item 3 as its own batch.** ⚠ And carry `AlreadyAttached`-per-key
forward as the item's headline, not a detail — your own note is right that leaving it makes the whole
capability pass vacuously.

---

## 6. ⭐ Item 4 — 26 registered, **and my own claim corrected**

⚠⚠ Batch 71 I measured *"three delegates ⇒ BTree is a registration, not a rewrite"*, and you made it a
line item **on that word**. 📐 **Half holds.**

| half | verdict |
|---|---|
| ⭐ **canonicalize** | ✅ **exactly as promised** — 26 assets baselined, same format as the other two corpora |
| ⛔ **emit** | 🔴 **not pure** — `BTreeJsonGenerator` builds a `structSizeResolver` from a Roslyn `Compilation` **and** calls `BTreeDeactivatorScanner.Scan(compilation, …)`. The resolver-less overloads exist, ⛔ **but their output is not what ships** |

⇒ ⭐ **the emit tier is unregistered and the REASON is asserted**, with the HSM kind as the contrast so
the limit is specific rather than *"we did not get to it"*. It needs a `CSharpGeneratorDriver` harness
— a real item. ⭐ Gate can fail · determinism verified **across two processes** · count pinned at **26**.

⚠ **Ordering note:** you asked for this last so items 1–2's baseline movement would not tangle with 26
new files. ⭐ **Items 1–2 moved no baseline at all** (§2), so that reason had evaporated and it landed
while it was cheap.

⛔ **No BTree emitter non-determinism found** — but ⚠ **that is not evidence**: the emit tier is not
registered, so the emitter was not exercised. The **shape** tier is deterministic across processes.

📌 **The HSM `Dictionary.Values` ordering** you invited me to fix: ⛔ **not done.** The objection you
retired was *"item 1 moves the baseline anyway"* — and item 1 **did not**, so fixing it would move the
HSM emit baseline on its own, for a change with no other observable. ⭐ Better as a one-line item with
its own diff.

---

## 7. ⭐ IDs allocated *(rule 5)*

| kind | allocated |
|---|---|
| tracker rows | ⭐ **`BP-284` · `BP-285` · `BP-286` · `BP-287`** |
| diagnostics · architect questions | ⛔ none |

---

## 8. ⭐⭐ Debt rows touched

| row | what happened |
|---|---|
| ⚠ **`DEBT-AIB-030`** | **two** gizmo registry tests this batch, both green under `--filter`. ⭐ Confirms your widening again |

⛔ **No other partition row touched.**

---

## 9. Not done

⛔ **Item 3 in full** *(§5)* · **`E3`'s fix** *(§4, escalated)* · **BTree's emit tier** *(§6)* · the HSM
`Dictionary.Values` ordering · `E5` · `E7a` · `E7b`'s runtime half · `BP-281` · the `InspectorWindow`
"STATIC PARAMETERS" retirement · the Track C visual check.
