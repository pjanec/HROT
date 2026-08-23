<!--STATUS
state: LIVE
updated: 2026-08-23
current-answer: the whole file — the batch report for HANDOFF_Uniform_Gizmo_Membership.md.
  ⛔ PARTIAL: items 0 and 1 delivered; item 2 is BLOCKED by the reference graph and items 3/4 depend
  on it. Design content is in DESIGN_Uniform_Gizmo_Membership.md §7.
known-conflict: none.
-->
# REPORT — uniform gizmo membership *(PARTIAL — item ② blocked)*

> 📌 **Dispatch:** [`HANDOFF_Uniform_Gizmo_Membership.md`](HANDOFF_Uniform_Gizmo_Membership.md), frozen at
> **`ba472e0ce`**. Branch `claude/blueprint-macro-feature-sdmspn` (BACKEND lane).
> **ids allocated: `ST-027`, `ST-028`, `ST-029`, `ST-030`** *(rule 5)*.

## 1. OUTCOME — **read the middle row first**

| item | outcome |
|---|---|
| **ⓠ** — `ST-027` | ✅ **`replaybrowser`'s registration path FOUND, not invented** — `RepositoryPriming.RegisterDiscoveredComponents` |
| **①** — `ST-027` | ✅ **`MapSchemaPack` = all 15, every host calls it.** Own commit, proven: `ModeStartupRails` **8 / 8** |
| 🔴🔴 **②** — `ST-028` | ⛔ **NOT BUILT — the reference graph forbids ANY single compile-time pack**, not just the specified home. §3 |
| ⛔ **③** | **held** — invariant `B` is false on 4 of 5 hosts until ② lands ⇒ adding it now is a permanent red (`R-131`) |
| ⭐ **④** | ✅ **invariant `A` re-proven red**; ⛔ `B` unprovable while unbuilt |
| ⚠ **findings** | `ST-029` the design's inventory undercounts *(22/**7**, not 18/6)* · `ST-030` a second rotating-flaky suite |

⭐ **Obligation ③:** the design carried **1 `classDiagram` (10 boxes)** + **1 `sequenceDiagram`**. The
sequence is built as drawn. ⛔ The diagram's `MapGizmoPack` is **not** built. All folded into
[`DESIGN_Uniform_Gizmo_Membership.md`](../../DESIGN_Uniform_Gizmo_Membership.md) **§7**
*(`build-state: PARTIALLY-BUILT`; §7.2 supersedes §1's inventory, §7.3 supersedes §3 ②'s home)*.

## 2. 🔴🔴 WHY ② IS BLOCKED — **and why picking a different home does not fix it**

§3 ② fixes the home as `Hrot.Common.Diagnostics.Gizmos`, *"for the reason `ST-022` already argued."*
⚠ **That reason holds for the SCHEMA and fails for the DECLARATION:** component types are low-level, but a
declaration must reference the **projector assemblies**.

| 📐 measured | |
|---|---|
| the seven families live in | `Hrot.Common` **8** · `Hrot.Presentation` *(`ScenarioEditor.Gizmos` **7** + `Presentation.Gizmos` **1**)* · `Hrot.AI.Behaviors` **1** · `Hrot.IG` **3** · `Hrot.SimHost` **1** · `Hrot.CGF` **1** |
| `Hrot.IG` · `Hrot.SimHost` · `Hrot.CGF` **all →** `Hrot.Common` | ⇒ a pack in `Hrot.Common` cannot reach three of them |
| **no assembly references all six** | `Hrot.CGF` is closest and still misses `Hrot.IG` |
| ⭐⭐⭐ **the general contradiction** | the pack must be **referenced BY** every host *(so it sits BELOW them)* while **referencing** IG/SimHost/CGF *(which ARE hosts)*. `Hrot.Presentation` fails identically — IG/SimHost/CGF all → Presentation |

⇒ ⛔ **Not built, and no entry point guessed** — the same discipline §3 ④ demanded for `replaybrowser`, applied
to the design itself.

**The path forward is measured and non-cyclic** *(detail in §7.3)*: the five projectors inside host
assemblies are barely coupled to them, so **consolidate the projector files into `Hrot.Presentation` keeping
their namespaces** *(the generator groups by namespace, not assembly, so every existing call site still
compiles — as `VisualEffectState` just demonstrated)*, then add `Hrot.Presentation` → `Hrot.Common` and
→ `Hrot.AI.Behaviors`, **neither of which is a cycle**. ⚠ **5 cross-assembly file moves + 2 new project
edges** is a materially different blast radius from *"one new file"* ⇒ **the coordinator's call.**

## 3. ⭐⭐ §GATES

| # | gate — verbatim command | `--no-build`? | result | delta vs `ba472e0ce` |
|---|---|---|---|---|
| 1 | `dotnet build IOS-IG-SimHost.sln` | builds | ✅ **0 errors**, 74 warnings | none |
| 2 | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build --filter Category=SystemModes` **after ①** | `--no-build` | ✅ **8 / 0** | none — ⭐ the integration gate, run after ① as §4 required |
| 3 | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build` *(whole)* | `--no-build` | ✅ **57 / 0** | ⭐ **exactly the stated baseline** |
| 4 | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests --no-build --filter FullyQualifiedName~GizmoSchemaFollowsDeclarationRails` | `--no-build` | ✅ **5 / 0** | profiles synced to the new call sites |
| 5 | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests --no-build` | `--no-build` | ✅ **234 / 0** | none |
| 6 | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build` | `--no-build` | ⚠ **650 / 4** | 🔴 **from 646 / 8** — rotating-flaky, see row B |
| 7 | `dotnet test Hrot/Subsystems/Hrot.IG.Tests --no-build --filter FullyQualifiedName~EntityInfoTranslatorTests` | `--no-build` | ⚠ **10 / 4** | none — the `ST-026` stable four, by name as the handoff directed |
| 8 | `python3 scripts/tracker-counts.py --check` | n/a | ✅ OK — open **99** / done **333** | ⚠ blind to `ST-` rows |
| 9 | `python3 scripts/rulings-check.py` | n/a | ✅ **24/24** | 2 known staleness WARNs, not mine |
| 10 | `python3 scripts/design-digest.py --check` | n/a | ✅ clean | none |

⛔ **`Hrot.CGF.Tests` does not exist** — the handoff's *"if it exists"*; it does not.
⚠ **`mermaid-check.mjs` SKIPPED** *(needs an `npm install` this session lacks)*. ⭐ **No Mermaid block added
or edited** — §7 is prose and tables — so nothing new is unvalidated.

### Every RED, confirmed **by name**

| | red | evidence |
|---|---|---|
| **A** | `Hrot.IG.Tests` — the four `EntityInfoTranslatorTests.CS011_*` | ⭐ `ST-026`'s stable four; the handoff names them as the by-name gate |
| **B** | `Hrot.SimHost.Tests` — stable core: `CgfLogicPack_EmptyWorld_...` · `CgfLogicPack_SingleGroupOverload_...` · `CgfLogicPack_TwoGroupOverload_...` · `BranchedRecording_CapturesHistoricalStateAsKeyframe` | 📐 **baseline at `ba472e0ce`: 646 / 8**, mine **650 / 4**; a second run on my own tree gave a **different 7-name set** ⇒ a rotating family (`ST-030`). ⭐ Mine is **no worse on either count or name set** |

⭐ **Working tree CLEAN after every suite run.** No golden added or touched. **No skips added; quarantine
count unchanged at 0.**

## 4. ⭐⭐⭐ RED PROVEN — and the first probe was not discriminating

| probe | result |
|---|---|
| remove `EqsSensor` from the widened pack | ⭐ **exactly the `ig` case reddens** — *"EqsSensor (required by EqsSensorGizmo in Hrot.IG.Gizmos)"*, 4 / 1. Restored → 5 / 0 |
| ⚠ remove `TargetMemory` first | **nothing reddened** — `IgRoleComponentRegistry` and SimHost's registries also supply it, so the host is still satisfied. ⭐ **That is the rail being right**, not a hole: it checks the host's TOTAL registration, not the pack's contents ⇒ a red probe must pick a component **only the pack provides** |

⛔ **`B` could not be proven either way** — it does not exist, because ② does not.

## 5. ⭐ §6's TWO OPEN QUESTIONS — both answered by measurement

| question | answer |
|---|---|
| **what does `Hrot.Presentation.Gizmos` register?** | 🔴 **It is NOT settings-only.** It holds **one projector**, `CanvasContextMenuGizmo` ⇒ it is a **seventh family**, and all five hosts already call it. ⛔ The design's *"holds ZERO projectors"* is wrong, and **uniform membership must include it** — so item ③'s rail must assert **seven**, not six *(`ST-029`)* |
| **does declaring a family you have no systems for cost anything?** | 📐 **Measured, not asserted.** Bootstrap span *(banner → `SlaveSyncController` initialised, `--mode simhost`, n=3)*: **before 172/178/176 ms · after 172/173/191 ms** ⇒ **indistinguishable, inside run-to-run variance.** ⚠ **This measures the SCHEMA half only** — the declaration half is unmeasured because it is unbuilt |

⭐ **And the CGF risk §6 raised:** the perspective/unification work does not touch gizmo families — ① added a
`MapSchemaPack` call to `CgfApplication` and `CgfSubsystem` with no other change, and `Hrot.Editor.Tests`
*(which constructs CGF's registries)* is **234 / 0**.

## 6. WHAT I DID NOT DO, AND WHY

| ⛔ | |
|---|---|
| **② `MapGizmoPack`** | §2 — structurally impossible as specified, path forward recorded, **not guessed at** |
| **③ invariant `B`** | it is false on four of five hosts until ② lands ⇒ a permanent red, which `R-131` forbids. ⭐ `B` is ②'s lock and belongs in ②'s batch |
| **did not remove the editor's inline `CullingState`/`VisualEffectState`** | ⭐ now redundant with ①, as §3 ⑤ predicted — **noted, not chased**, exactly as instructed |
| **did not touch `Hrot.Presentation.Gizmos`** | 🔒 support all; ⛔ *"do not clean it up"*. ⭐ And it turned out to hold a projector |
| **did not touch `ModeStartupRails.cs`'s content, `Goldens/`, `ST-026`'s flakiness, `MapInteractionPack`, `TagMask`** | out of scope per §3 |

⚠ **Shared-file touch, declared:** `EditorSubsystem.cs` — one line added at `:859`, in the registration
region *(`:857-869`)*, **not** the preview batch's region *(`:525-560`)*. Rule 4 re-pull done before the
final commit; no conflict.

## 7. RULE COMPLIANCE

| rule | |
|---|---|
| **1b** started-marker | ✅ pushed before any code, naming `ba472e0ce` |
| **3 / 5** ids | ✅ `ST-027`…`ST-030`, starting at `ST-027` as the dispatch said; **filed in the same commits that use them** |
| **4 / 7** re-sync | ✅ merged at the start and again before the final commit |
| **8** gate report | ✅ §3, with the integration gate run **after ①** as §4 required |
| ⭐ **item ordering** | ✅ ⓠ → ① *(own commit, proven)* → then stopped at ② |
