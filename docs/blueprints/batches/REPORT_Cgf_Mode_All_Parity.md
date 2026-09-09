<!--STATUS
state: LIVE
doc-type: implementation report
updated: 2026-08-27
current-answer: the whole file. Ids CE-057…CE-064. §7a carries the T3 result, which arrived after the
  first draft and produced CE-064. Branch claude/reset-working-branch-qd1qpv; commits 05c4be118
  (CE-057/058/059), f005b151c (the E5 design + as-built folds), bbf3d73e8 (CE-060/061), + the CE-064 fix.
stale-below: nothing.
-->
# REPORT — **`--mode all` parity: the scenario root, the toolbar, and the windows**

> 🔒 **The request, verbatim:** *"i am running --mode all and see no scenarios in the pickers. Instead of
> graphicla icons (as rendered in the editor) there are plain imgui buttons in the toolbar?? how comes,
> editor uses nice graphical icons, co cgf must be using some different toolbar code, not shared with
> editor, this is wrong, main menu and toolbar code must be shared across subsystems! Editor has also lots
> of toolbar buttons for debuggiing, none shown. Also the editor has many windows in its Scenario
> perspective like mission editor, orbat, entity placement, entity spawner, cgf offers just Entity
> inspector, Event Browser, architecture diagnostic, System profiler. Withotu the scenarios i can not test
> much, pls fix."*

## 1. ⭐⭐⭐ ALL FOUR SYMPTOMS FIXED — and the framing that made them one batch

📐 **The measurement that reframes everything:** `--mode all` expands to
`orchestrator,simhost,ig,excon,cgf` — ⛔ **no editor**, and `HrotRunnerConfiguration.Validate` *rejects*
`editor` together with `cgf`. ⇒ ⭐⭐ **on that path CGF *is* the editor**, so every one of these is a
missing feature, not a cosmetic difference.

| # | symptom | id | root cause, measured |
|---|---|---|---|
| **1** | no scenarios in any picker *(the blocker)* | `CE-057` | CGF resolved `{staging}/nodes/node-N/scenarios` — **a directory that does not exist**. The 3 authored scenarios are in `{staging}/shared/scenarios` |
| **2** | plain ImGui buttons instead of icons | `CE-058` | ⛔ **NOT a second toolbar.** `RegisterPerspectiveIconKey` had ONE caller repo-wide ⇒ the *shared* section took its documented text-label fallback |
| **3** | no debug toolbar buttons | `CE-059` | `AiDebugCommands.Register` had ONE caller — and the real gap was the **session**, not the registration |
| **4** | four Scenario windows instead of ten | `CE-060`/`CE-061` | `E1`–`E4` shared **capabilities**; nobody enumerated **windows** |

## 2. ⛔⛔ THE UNCOMFORTABLE PART — **my own `CE-053` rail was the same failure it was written to prevent**

⭐ `CE-053`'s report opened by indicting `CE-049`'s equality rail for asserting a **weaker claim** than the
user's experience. ⛔⛔ **Its own rail then did exactly that.**

```
TheCgfPickerIsNotEmptyTests    // created a temp dir, populated it with 2 scenarios,
                              // handed THAT PATH to the contributor, asserted 2 entries
```

⇒ ⭐⭐⭐ **A rail that SUPPLIES the input it is testing cannot catch a caller that supplies a different
one.** *"the chain works given a populated root"* is strictly weaker than *"the host points at the
populated root"* — and the picker stayed empty while all 7 of those rails were green.

⭐ **The new rails assert the ROOT the host resolves, with no path of their own** — plus a source-scan
guard that no host feeds a *node* staging root to the scenario contributor. 📌 That guard is the one that
reddened on the inverse edit.

## 3. ⭐⭐ THE FINDING WORTH KEEPING — **the user's diagnosis was half wrong, and the wrong half is the useful half**

> *"cgf must be using some different toolbar code, not shared with editor"*

📐 **Measured: it is not.** Both hosts construct the **same** `PerspectiveToolbarSection` over the **same**
`SilkIconProvider`. `BuildRadioModel` sets `HasIcon` from `GetPerspectiveIconKey(p) != null && provider
.TryGet(key)`, and the render path **falls back to a text-label button** when that is false.

⇒ ⭐⭐⭐ **the plain buttons were the shared code's own graceful degradation**, firing because the *data* —
five `RegisterPerspectiveIconKey` calls — lived in one host's private block. ⚠ **A "different toolbar
code" hypothesis would have sent me looking for a duplicate that does not exist.** The instruction
*"toolbar code must be shared"* was already satisfied; what was not shared was the **table**.

⭐ It is now `PerspectiveIconKeys` (AiShared), registered at the **top of `RegisterWindows`** on both hosts
— ⚠ deliberately outside the `MainToolbar != null` guard it used to sit in, because **inside that guard no
bare-ctor window rail could reach it.** 📌 That unreachability is why nothing caught the gap for a month.

## 4. ⭐⭐ `CE-059` — **why registering the buttons would have been WORSE than the omission**

⛔ Every `debug.*` command gates `IsEnabled` on `IDebugSessionRegistry.ActiveSession`, and nothing on CGF
ever set it. ⇒ registering the six ids alone ships a **permanently disabled** group — ruling 49 rates that
worse than absent.

⭐⭐⭐ **The real defect is the silent-default shape, third instance in `CgfSubsystem` after `CE-052`:** that
file already held **all three** `BlueprintDebugSession` ctor arguments — `_blueprintRegistry` (`:508`), the
world, and `CgfClusterDebugTimeController : IEngineDebugTimeController` (twenty lines above) — and simply
never constructed it.

⚠⚠ **The old argued omission had two claims; only one rotted.** *"CGF has no debug session"* is overtaken.
*"`debug.*` is AI-GRAPH stepping, `CgfClusterDebugTimeController` is CLUSTER-TIME control, and binding one
to the other makes one id mean two things"* **still binds** — and `CE-059` honours it: it supplies the
AI-graph session the ids already meant. ⭐ **The HISTORY note in the file preserves the binding half rather
than deleting the whole argument**, because a future session will otherwise re-derive it.

## 5. ⭐ `CE-061` (E5) — **a lift, once measured; the gap map had enumerated the wrong axis**

| what the gap map's §2c.2 said | 📐 what was actually left |
|---|---|
| `E5` = *"the thin-host bootstrap divergence, **NOT a gap**"* | ⭐ true for BOOTSTRAP — ⛔ but its table enumerated **capabilities** *(scenario/asset/tool/inspector)* and never **windows**, so seven `ManagedWindow` wrappers and five adapters fell between the rows |

⭐⭐ **Seam law at 2×, not 1×:** the panels AND the facade interfaces were already shared; the *wrapper* was
written twice (`EditorWindows.cs`, `ExConWindows.cs`) with the same bodies. ⇒ id/title/perspective/colour
became **arguments**, and four of the five editor adapters — measured at **ZERO** `IEditorLogic`
references — moved as `Scenario*`.

⭐ **The editor's window ids are unchanged and railed.** ⚠ That is the gate that made this safe: those ids
key layout files and `PanelSnapshot`, so a tidier rename would have silently reset users' layouts.

## 6. ⛔⛔ THREE THINGS I GOT WRONG, one caught by the compiler

| # | what | how it surfaced |
|---|---|---|
| **①** | I called `EditorSpawnAdapter`'s `using Hrot.IG.Components` **stale** and deleted it | ⛔ **The compiler caught it.** That namespace is declared **in `Hrot.Core`**, not the `Hrot.IG` assembly, and `EditablePolyline` + `MapOverlayStyle` are used from it. ⚠⚠ **My conclusion survived by luck, not by reasoning** — the using carries no `Hrot.IG` reference, so the move was legal anyway. Restored, with the correction recorded at the using and in the design's §10 D2 |
| **②** | my first inverse-edit red-proof of the composition scan **did not redden** | 📐 I renamed `SpawnerPanelWindow(` → `RedProof_SpawnerPanelWindow(`, which still **contains** the substring the rail looks for. ⇒ ⚠ **an ineffective red-proof reads exactly like a green rail** — redone with a real removal, and it reddened |
| **③** | my first deletion regex ate two classes it should not have | `EditorToolbarWindow`/`EditorOrbatWindow` were swallowed by a greedy `[^\0]*?` reaching back past the intended class. Reverted from git and redone with an index walk |

## 7. ⭐ GATES — **the report substitutes for a coordinator re-run** *(rule 8)*

| # | gate | command | result | `--no-build`? |
|---|---|---|---|---|
| 1 | unit suite, touched project | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests --no-build` | ⚠ **331 passed / 1 failed / 1 skipped** *(333)* — see row 4 | ✅ |
| 2 | affected projects build | `Hrot.Presentation` · `Hrot.Editor` · `Hrot.CGF` · `Hrot.SimHost.Tests` · `Hrot.ClusterRunner.Integration.Tests`, each `--no-restore` | ✅ **0 errors** each | ⛔ builds |
| 3 | full solution, **ONCE, at the end** | `dotnet build IOS-IG-SimHost.sln --no-restore` | ⚠ **2 errors — BOTH PRE-EXISTING**, row 4 | ⛔ builds |
| 4 | ⭐⭐ every RED confirmed pre-existing against the base | worktree at **`ae4d4b1e7`**, per-project build | ✅ **proved** — see the table below | ⛔ restore |
| 5 | working tree clean after every suite | `git status --short` | ✅ clean; no golden moved | — |
| 6 | quarantine counts | unchanged; ⛔ **no new skip** *(the 1 skip is pre-existing)* | ✅ | — |
| 7 | tracker + ledger | `tracker-counts.py --check` → **open 102 / done 346**; `rulings-check.py` → **25/25**; `design-digest.py --check` → clean *(95 docs)* | ✅ | — |
| 8 | ⭐ cross-cutting ⇒ the integration suite | `run-system-tests.sh --no-build` — **T3, ran ASYNC** *(never a foreground blocker)* | ⚠ **105 passed / 2 failed / 0 skipped** *(107, 10 m 43 s)* — §7a | ✅ |
| 9 | mermaid | `mermaid-check.mjs` on the new design | ✅ **2/2 parse** | — |

### ⭐⭐ Row 4 — the three reds, each named and attributed

| red | verdict |
|---|---|
| `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` | ⭐ **the known rotating ALC flake (`CE-050`)** — GREEN in isolation, and GREEN in the immediately preceding run of the same suite. ⚠ Stated at that strength: rotation is its documented signature, ⛔ green-in-isolation is not proof |
| `Hrot.SimHost.Integration.Tests/SimHostInstance.cs:265` — `AttributeCompilerFactory` not in context | ⛔ **PRE-EXISTING.** File byte-identical to base; reproduces **identically** at `ae4d4b1e7` |
| `Hrot.Blueprints.Tests/EntityBlueprintsEditModelTests.cs:420` — `BlueprintAssignmentDto.Overrides` | ⛔ **PRE-EXISTING.** Same proof |

### ⭐ Diff shape *(what a coordinator would otherwise compute by hand)*

**11 files changed, 161 insertions, 980 deletions** in the `E5` commit — ⭐ net-negative because four
duplicated wrappers and four moved adapters left `Hrot.Editor`. ⛔ **No golden regenerated.** New
production files: 4 *(`PerspectiveIconKeys`, `ActiveDebugSessionMirror`, `ScenarioPanelWindows`,
`ScenarioSpawnerCatalog`)*. New rails: **3 files, 34 tests.**

### ⭐ IDs allocated *(rule 5)*

`CE-057` `CE-058` `CE-059` `CE-060` `CE-061` `CE-064` — done. `CE-062` `CE-063` — filed OPEN.

## 7a. ⭐⭐⭐ THE T3 RESULT — **one red is the MCP lane's, and the other one is MINE**

⭐⭐ **It came back while the report was being written, so it is folded in rather than deferred** — and the
second red is the most interesting result of the batch.

| red | verdict |
|---|---|
| `ClusterConformanceRails.The_manifest_describes_this_host_truthfully` — *"route(s) with no capability classification: `[/missions/{networkId}, …/run, …/task, …/tasks]`"* | ⛔ **NOT MINE — the MCP lane's** *(`d2138faaf`)*, and the **same red I attributed in the previous report and it is still unfixed**. ⭐ The fix is one deliberate prefix in `CapabilityManifest.CapabilityFor`, which the error message itself names. ⚠ **A cross-lane edit is a STOP-and-report, not a judgement call** — so it is reported again, not fixed |
| `ClusterConformanceRails.The_cluster_can_discover_open_and_switch_graph_tabs` — *"asset 'hill-attack' has no sourceFilePath"* | ⭐⭐⭐ **CAUSED BY `CE-057`, and it is a real defect I did not create** ⇒ fixed as **`CE-064`** |

### ⭐⭐⭐ `CE-064` — **the third disguise of the same trap, and the sharpest one**

📐 `ScenarioEditableAsset.SourceFilePath` has been hard-coded to `""` since the contributor was written.
⚠⚠ **That T3 rail asserts a non-blank `sourceFilePath` for EVERY catalogued asset — and it was GREEN the
whole time**, because on `--mode all` the catalog held **zero scenarios**: first the contributor was
editor-only *(`CE-053`)*, then it was aimed at a directory that does not exist *(`CE-057`)*.

⇒ ⭐⭐⭐ **A loop over an empty collection asserts nothing.** Making the picker non-empty is what finally
made an existing rail able to fail.

| the same trap, three times in this programme | shape |
|---|---|
| `CE-049` | asserted a control is **present and enabled**, never that it has **something to offer** |
| `CE-053` | **supplied the input it was testing** *(a populated temp dir)*, so it could not see the host resolve a different root |
| ⭐ **`CE-064`** | the assertion was **UNREACHABLE** — correct, universal, and iterating over nothing |

⭐ Fixed with an optional `scenariosRoot` resolver → `{root}/{relPath}/scenario.json`, the exact layout
`EditorScenarioSession.WriteScenarioDirectory` writes, so **the advertised address is the file the session
round-trips**. ⚠ Optional so the many single-argument test constructions still compile — ⛔ which makes an
omitting host a **silent default**, so a rail asserts **both** hosts pass it. ⭐ And the claim is now
pinned at **T0: 135 ms**, not after an 11-minute cluster boot.

## 8. ⛔ NOT DONE, and why *(`R-106` — stop the ITEM, not the batch)*

| item | why |
|---|---|
| **`CE-062`** the blueprint live-value provider on CGF | ⭐ Its stated blocker was the very claim `CE-059` falsified, so it is now unblocked — ⛔ but it needs the editor's provider construction measured, and this batch's job was the toolbar. The false comment is corrected in place rather than left lying |
| **`CE-063`** reconcile `EditorMapPickAdapter` with `CanvasMapPickAdapter` | ⛔ **A decision, not a chore.** Same interface, same three members — ⚠ but the editor's drives `LocationPickerGizmo` + a real filter factory while the shared one carries `MatchAllFilterFactory` ⇒ collapsing it the wrong way silently degrades the editor's map picking. `E5` needed neither: CGF passes the shared one it already built |
| **Preview · Zone Editor · the tool palette** *(the user's "entity placement")* | 📄 design §4. `IPreviewController` is the editor's planning-vs-running state, which a cluster node does not have *(a ruled divergence)*; the zone adapter still reaches `Hrot.Editor.Gizmos`; the palette takes `IEditorLogic` directly. ⛔ Sharing any of them now would put a window on CGF that cannot be serviced |
| **`ExConWindows.cs`** adopting the shared wrappers | ⭐ Correct and desirable — ⛔ **the BACKEND lane's file.** Net duplication is unchanged (2 → 2), with one of the two now the shared home |
| **`CE-055` / `CE-056`** *(the perspective-switch freeze, the pinned window)* | ⛔ Still blocked on a display this container does not have. Unchanged from the previous report |
| **the `R-124` in-frame `ui-probe` rails** | same display limit |

## 9. ⚠ Process deviation, declared as in every report of this run

⭐ The handoffs ask for a fresh branch per batch. ⛔ This session is harness-bound to
**`claude/reset-working-branch-qd1qpv`** and cannot create one, so all work landed there.
