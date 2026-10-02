# REPORT — Batch 80: **Track C reaches the running editor**

> 📌 **Started at `6183030`** *(rule 1b marker, pushed before any code)*.
> ⚠ **No handoff file was dispatched** — scope taken from **`PLAN_Remaining_Work.md` revision 24**,
> which names it: *"Batch 80 is two call sites plus a rail on the PRODUCTION composition root."*
> ⭐ **Rule 4:** re-pulled before the final commit and merged `RESUME_START_HERE`; nothing in it
> changes this batch.
> ⭐ **IDs allocated: NONE.** ⭐ **`DEBT-AIB` rows touched: NONE.**
> ⭐ **Quarantine: 12 scenario · 0 FastHSM** — unchanged.

---

## 0. ⭐⭐⭐ Your finding was right, and it was mine

📐 **Confirmed before touching anything:** `EditorSubsystem.cs:2121 / :2137 / :2155` — **three**
registrars, **zero** `hostKind:`. ⇒ ⛔ **in the running editor the outline was never constructed and
the Variables window was never routed.**

⚠⚠ **And every Batch-79 rail was green throughout** — because each one built its own registrar and
passed the argument production did not. ⭐ **That is the sharpest version of this programme's recurring
lesson yet:** *a component with passing tests is not a component someone can use* — **including when
the tests are about the wiring.**

---

## 1. 🛠 Three fixes, in increasing order of how much they matter

### ① the two call sites — ⭐ *what the plan asked for*

`EditorSubsystem` now passes `hostKind` for **BTree** and **HSM**, with the reason at the call site.

### ② ⭐⭐⭐ the CLASS of defect, removed — *the fix that matters*

⭐ **The host kind is now DERIVED from the perspective name**, which the registrar already knows:

```csharp
var effectiveHost = hostKind ?? HostKindOf(perspectiveName);   // "BTree" | "HSM" | null
```

⇒ ⛔ **there is no argument left for a caller to forget.** The parameter survives **as an override**.
⭐ **Case-insensitive on purpose** — a casing difference silently dropping a panel is precisely the
failure being removed.

⚠ **This is why the rails assert the DEFAULT PATH:** a registrar built **exactly** the way the
composition root builds one — ⛔ **no `hostKind`, no resolver, no `Retarget`.** If any of those becomes
required again, they go red.

### ③ ⭐⭐ the two remaining *"someone must remember"* seams

| seam | before | now |
|---|---|---|
| **the outline's asset** | waited for a `Retarget` **nobody called** | ⭐ **FOLLOWS the selection store**, exactly as `BlackboardAuthoringWindow` always has |
| **the routing** | inert unless the host called `SetSectionSourceResolver` — ⛔ **no production caller did** | ⭐ **a DEFAULT resolver over the same store**, installed by the registrar. A host may still override |

---

## 2. ⚠ Two things measured along the way

| | |
|---|---|
| ⛔⛔ **`SectionVariableRowSource` does NOT filter** | 📐 it takes `_schema.Variables` **wholesale** and uses the section only as a **label on the origin** ⇒ routing through it would have shown **the whole blackboard under every heading**. ⭐ **`BlackboardSectionRowSource` is new for this**, and ⭐⭐ **there is a rail for the filtering specifically** — ⛔ without it the routing test would pass for a source that ignores its section |
| ⭐⭐ **one classification, not two** | membership is `BlackboardMyBlueprintModel.SectionOf` — **the same predicate the outline uses** ⇒ a variable cannot sit under one heading in the tree and another in the table |
| ⭐ **and one row-kind rule** | the auto-managed → node-owned / read-only → passthrough precedence now lives once, in **`VariableRow.KindOf`**, called by **both** sources. ⛔ **Two sources spelling it themselves is how `BP-306` happened** |
| ⚠ **`(pending)`, not `<unreadable>`** | at authoring time there is no entity ⇒ `HasEverBeenWritten = false`. ⛔ **Rendering a decode failure that never happened** would send a designer hunting a bug in their type |

---

## 3. 🔴 Revert-goes-red — **against Batch 79's own behaviour**

| probe *(all three at once — that IS Batch 79)* | |
|---|---|
| remove the derivation | ⇒ `ARegistrarBuiltWithoutAHostKind_StillGetsItsOutline` ×2 |
| stop passing the store | ⇒ `TheOutline_FollowsTheActiveAsset_…`, `TheOutline_ClearsWhenTheAssetGoesAway` |
| remove the default resolver | ⇒ `SelectingASection_FiltersTheTable_WithNoResolverSupplied` |

📐 **5 of 15 red under the probe · 15 / 15 after.** ⛔ Un-applied by the inverse edit, never by
`git checkout --`.

---

## 4. Gates — all seven reports

### 1 + 2 · one row per gate, with the `--no-build` column

| gate | command | `--no-build`? | result | Δ |
|---|---|---|---|---|
| solution | `dotnet build IOS-IG-SimHost.sln -t:Rebuild` | — | ✅ **0 err / 69 warn** | = |
| ⭐ **AiShared** | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build` | yes | ✅ **1318 / 1318** | **+15** |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build` | yes | ✅ **3681 / 3691, 10 skipped** | = |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build` | yes | ✅ **615** | = |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build` | yes | ✅ **551** | = |
| Generators | `dotnet test …/Hrot.AiEditor.Generators.Tests.csproj --no-build` | yes | ✅ **270** | = |
| Breakpoints | `dotnet test …/Hrot.Diagnostics.Breakpoints.Tests.csproj --no-build` | yes | ✅ **134** | = |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build` | yes | ✅ **136** | = |
| Examples.Scenarios | `dotnet test …/Fdp.Examples.Scenarios.Tests.csproj --no-build` | yes | ✅ **56 / 68, 12 skipped** | = |
| Examples.UrbanCombat | `dotnet test …/Fdp.Examples.UrbanCombat.Tests.csproj --no-build` | yes | ✅ **29** | = |
| ⚠ **Toolkits** | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build` | yes | 🔴 **1 red on run 1, green on runs 2–3** | ④ |
| ⭐ **NodeEditor.Core** | `dotnet test …/NodeEditor.Core.Tests.csproj` | ⛔ **NO** | ✅ **208** | = |
| ⭐ **NodeEditor.UI** | `dotnet test …/NodeEditor.UI.Tests.csproj` | ⛔ **NO** | ✅ **131** | = |
| ⭐ **Fhsm.Tests** | `dotnet test …/Fhsm.Tests.csproj` | ⛔ **NO** | ✅ **300, 0 skipped** | = |
| tracker | `python3 scripts/tracker-counts.py --check` | — | ✅ **open 64 / done 180 (+1 refuted)** | = |

### 3 · ⭐⭐⭐ Golden movement — **as a diff shape**

```
$ git diff --stat 6183030..HEAD -- '*Snapshot*' '*Golden*' '*golden*' '*.cs.txt'
(no output)
```

⇒ ⭐ **ZERO baselines.** The change is **9 files, +466 / −28**, and ⛔ **no emitter, DTO or asset is in
it at all** — so `persistence-shape`, the 43 `Emit/*.cs.txt` and `StructureHash` **could not** have
moved.

| file | ± | |
|---|---|---|
| `TrackCReachesTheEditorTests.cs` | **+266** | new — the production-path rails |
| `BlackboardSectionRowSource.cs` | **+88** | new — the filtering source |
| `AiMyBlueprintWindow.cs` | +37 | follows the selection store |
| `PerspectiveWorkspaceRegistrar.cs` | +35 | derivation + default resolver + store |
| `AiWatchBreakpointsWindowTests.cs` · `PerspectiveWorkspaceRegistrarTests.cs` | +35 / −… | ⚠ **the only edits to EXISTING assertions** — ⑤ |
| `VariableRow.cs` · `VariableRowSources.cs` | +22 / −… | the row-kind rule, moved to one home |
| `EditorSubsystem.cs` | +11 | ⭐ **the two call sites** |

### 4 · every RED confirmed pre-existing — base `6183030`

| red | verdict |
|---|---|
| `Gizmos` — one test, run 1 only | ⭐⭐ **`DEBT-AIB-030`.** Runs 2–3 fully green *(`1964 / 1964`)*, `--filter Gizmos` **`187 / 187`**. 📐 **`git diff --name-only 6183030..HEAD -- FDP/` is EMPTY** ⇒ ⛔ **the diff cannot reach that assembly** |

### 5 · working tree after every suite run

`git status --short` ⇒ **clean** *(only the intended edits before commit)*. ⛔ No golden regenerated.

### 6 · quarantine — **12 scenario · 0 FastHSM**, unchanged. ⛔ No new skip.

### 7 · **ids: none** · **started at `6183030`**

---

## ⑤ The four assertions I changed, and why

| test | 79 → 80 | why it is not a weakening |
|---|---|---|
| `Perspective_NoManager_…` | 7 → **8** | built for `"HSM"`; the host kind is now **derived**, so it gets an outline — ⭐ **that IS the fix** |
| `Perspective_WithManager_RegistersEightWindows` | 9 → **10** | ⭐ the name is kept so the `AIE-034` scenario stays traceable |
| `WatchAndBreakpointsWindowIds_…` · `ThreeRegistrars_…` | 27 → **29**, 21 → **23** | ⭐⭐ **+2, not +3** — Blueprint keeps `BlueprintMyBlueprintWindow`. ⭐ **The property under test is DISTINCTNESS and it still holds** |

---

## 5. Carried forward

⭐⭐ **The visual check is now genuinely unblocked** — ⛔ **parts C and D of the guide were blocked on
exactly this.** · 🔴 **`2.7`'s persistence and `2.40`/`2.41`'s budget indicator are NOT BUILT** *(settled
last batch)* · ⭐ **`Q38`** — one mode-switching Details panel · `BP-309` *(filed, not adopted)* · the
six `STILL REAL` `DEBT-AIB` rows · the producer picker's runtime · the 12 quarantined scenario tests.
⛔⛔ **Everything multi-level stays parked** — `E3` · `E5` · `E7a` · `Q36` · `Q37`.

⚠ **One process note:** there was **no handoff for this batch**. ⭐ The plan's revision 24 was specific
enough to act on, and I took its scope verbatim — ⛔ **but the started-marker records that**, so if the
intended scope was wider, this is where it diverged.
