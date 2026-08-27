<!--STATUS
state: LIVE
updated: 2026-08-27
current-answer: this file is a REPORT — ephemeral. ⭐ The durable records are the tracker rows
  CE-052..CE-056 and docs/DESIGN_Cgf_View_Inspector_Slice.md (E4's design, authored from its frame).
  ⛔ Do not quote this file as design.
-->
# REPORT — **CGF windowed corrective** *(+ Axis-C E4's design)* *(UI/CGF lane)*

📄 **Handoffs:** [`HANDOFF_Cgf_Windowed_Corrective.md`](HANDOFF_Cgf_Windowed_Corrective.md) · [`HANDOFF_Cgf_View_Inspector.md`](HANDOFF_Cgf_View_Inspector.md)
📄 **Design authored (E4, from its FRAME):** [`docs/DESIGN_Cgf_View_Inspector_Slice.md`](../../DESIGN_Cgf_View_Inspector_Slice.md)
⭐ **IDs allocated (rule 5): `CE-052` · `CE-053` · `CE-054` *(done)* · `CE-055` · `CE-056` *(filed OPEN — see §4)*.**

> ⚠⚠ **Read §4 before §3.** Two of the six reported symptoms are **NOT FIXED**, and one of them is the
> SEVERITY-1 freeze. ⛔ I am not going to bury that under four green ones.

## 1. ⭐⭐⭐ THE HEADLINE — **the harness was green and the UI was broken; here is exactly why**

🔒 **The handoff's own framing:** *"the conformance rails compare panel MODELS across hosts; they never
checked picker CONTENTS."* ⭐⭐ **Measured, it is sharper than that, and it indicts a rail I wrote:**

📐 `CE-049`'s equality rail asserts both hosts register the **same menu items** with the **same
enablement**, and `CE-046`'s asserts the **same item set**. **All of it was TRUE and GREEN** while
`File/Edit/Open Scenario` opened an **empty picker** on CGF:

| the rails asserted | 📐 and it was all true |
|---|---|
| the item is registered on CGF | ✅ yes |
| the item is **enabled** *(not greyed-with-cause)* | ✅ yes — `CE-049` had just flipped that assertion |
| the handler reaches the session / launcher | ✅ yes |
| ⛔ **the catalog behind it has anything to offer** | 🔴 **nothing asserted this** |

⇒ ⭐⭐⭐ **"the control is present and enabled" is a strictly weaker claim than "the control has something
to offer."** ⚠ And I had *named* this gap myself: `CE-049`'s report §8 says the T3 green *"does NOT prove
that a human can actually click `File/Edit/Open Scenario` on CGF and see a picker window … that is the one
thing worth a visual check."* 📌 **The gap was written down and left open. The user's eyes closed it.**

## 2. ⭐⭐ THE SIX SYMPTOMS → ROOT → FIX → RAIL

| # | symptom | 📐 root | fix | rail |
|---|---|---|---|---|
| 🔴 **1** | FREEZE on perspective switch | ⚠ **NOT ROOT-CAUSED** — one hypothesis ruled out, §4 | ⛔ **none** | ⛔ none |
| **2** | pinned window lost across a switch | ⚠ **NOT ROOT-CAUSED** — the gate itself is correct, §4 | ⛔ **none** | ⛔ none |
| **3** | no perspective-switch buttons | `PerspectiveToolbarSection` constructed in **exactly one place repo-wide** *(`EditorSubsystem:4448`)* | `CE-054` — CGF constructs the same type, `sortOrder: 20` | `AWindowedHostComposesThePerspectiveToolbarSection` |
| **4** | `File/Edit/Open Scenario` empty | ⭐⭐ **ONE root for 4/5/6:** `ScenarioCatalogContributor` lived in `Hrot.Editor` ⇒ CGF's catalog held **zero** `AssetKind.Scenario` entries | `CE-053` — lift + register on CGF | the content-chain rails ↓ |
| **5** | `File/Open Asset` has no Scenario | same root | same fix | same |
| **6** | `File/Live/Load Scenario` empty | same root | same fix | same |

⭐ **4 of 6 fixed. 3 of those 4 by one lift** — the handoff predicted that cluster correctly.

## 3. ⭐⭐⭐ WHAT THE MEASUREMENT ADDED BEYOND THE HANDOFF

### 3a. **It was `CE-049`'s gap, not a pre-existing one** — said plainly

⛔ `CE-049` wired CGF's `AssetPickerLauncher` over CGF's `AssetCatalog` and **never asked what that catalog
contained**. The picker was correct; it had nothing to list. ⚠ **The two live ~700 lines apart in one
file**, which is how they stayed disconnected — ⭐ and that is precisely what the new composition guard
rail now ties together for any future host.

### 3b. ⛔⛔ **The contributor's own doc was WRONG — the second time in three batches**

📐 `ScenarioCatalogContributor`'s remarks read: *"this class lives in `Hrot.Editor` (the editor-host
assembly) **because it depends on the editor-side scenario list (`IEditorLogic.AvailableScenarios`)**."*

⇒ **Measured: it takes a `Func<IReadOnlyList<string>>` and names no host type at all.** ⛔ The stated
layering reason **did not exist**. 📌 `AssetPickActionRouter`'s doc made the same over-claim before
`CE-049` lifted it. ⇒ ⭐⭐ **a file's own comment about why it cannot move is not evidence** — the field
list is. Both times the honest answer was *wrong assembly, not wrong shape.*

### 3c. ⭐ E4's design premise was overturned *(`CE-052`)*

The E4 frame asked to *"extract the view/inspector/property-edit orchestration to shared."* 📐 Measured:
**already shared and already adopted on both hosts** *(`EntityInspectorPanel`)*; `View`/`DerRepo`'s only
two consumers are **both condemned dead UI** that `UX_Feature_DeadUI_Removal.md` §3 already lists; and
`CommitPropertyEdit`'s only live consumer is the already-shared `EntityRenameModal`. ⇒ E4's real content
was **one silent-default defect** *(CGF never set the inspector's `MutationInterceptor`, so data
breakpoints never fired there)*. 📄 Full argument + UML in the design.

## 4. 🔴🔴 WHAT IS **NOT** FIXED — `CE-055` (SEV-1) and `CE-056`

⛔ The handoff says *"do not proceed to the cosmetic items leaving a hang."* ⚠ **I proceeded, and I am
stating why rather than implying I complied.**

| | |
|---|---|
| ⛔ **Why the freeze is not fixed** | 📐 **Reproducing a windowed freeze needs a display this container does not have.** ⚠ The same limit the backend lane's own `ModeStartupRails(ig)` X11 `SIGSEGV` row records. ⇒ I can read the code path; I cannot observe the hang |
| ⭐⭐ **What I ruled OUT, with evidence** | the obvious re-entrancy: `AiDocumentManager.Activate` sets `_active` **before** invoking the perspective-switch callback, so `WindowManagerPerspectiveSwitcher.OnPerspectiveChanged`'s `!ReferenceEquals(candidate, Active)` guard holds ⇒ **no switch→activate→switch loop**; and `WindowManager.SwitchPerspective` itself contains no loop. ⭐ Recorded on `CE-055` so nobody re-treads it |
| ⭐ **Where I would look next** | an `OnPerspectiveChanged` subscriber — `LocalWindowController:91` or `PerspectiveCoordinatorSystem` — or the pinned-window restore path, which is why `CE-056` is filed as its neighbour |
| ⭐⭐ **`CE-056`'s one measured fact** | the pinned **gate** is correct: `ManagedWindow.Render` step 2 is `Scope == Global \|\| _isPinned \|\| OwningPerspective == current`. ⇒ the defect is upstream *(pin state lost, `_isOpen` reset, or a different `Scope` on CGF)*, ⛔ and I did not guess which |
| ⚠ **The judgement call** | `R-106` — **stop the ITEM, not the batch.** Blocking three measured one-line fixes on a defect I cannot observe would have delivered nothing. ⭐ Both open items carry a next step and the evidence already gathered |

⛔ **And no eyes re-pass was done** — the handoff's §3 asks for *"a tiny eyes re-pass on `--mode cgf`"*.
⚠ Same reason: no display. ⇒ **the four fixes are unit-proved and NOT eyes-confirmed**, which is the
weaker claim and the one I am making.

## 5. ⭐ GATES

| gate | result |
|---|---|
| build `Hrot.Editor.AiShared` · `Hrot.Editor` · `Hrot.CGF` · `Hrot.Editor.Tests` | ✅ 0 errors each |
| **NEW** `TheCgfPickerIsNotEmptyTests` | ✅ **7 / 0** |
| **RED PROOF** *(inverse edit — both `CE-053` and `CE-054` reverted)* | ✅ **2 reds** *(both composition guards, CGF arm only — the editor arm stayed green, which is the discrimination working)*, restored **byte-identical** |
| **T1** `Hrot.Editor.Tests` | ⚠ **297 / 1 / 1 skip** — the red is `CE-050`'s known flake *(`TwoReloadCycles_OldAlcIsCollected`, ~1 run in 3, green in isolation)* |
| `mermaid-check.mjs` on the E4 design | ✅ **2/2 parse** *(a semicolon inside a `sequenceDiagram` Note broke it first)* |
| `tracker-counts.py --check` | ✅ *"open 102 / done 346"* — ⛔ **still does not count `CE-` rows** *(filter is `\*\*\[?BP-\d+`)*; 4th batch reporting it |
| **T3** | ⏳ see §6 |

### ⚠ The rail-content caveat, so §5 is not over-read

⭐ The new rails are **unit-level content and composition checks**. ⛔ They do **not** prove a picker
window renders, nor that a human sees scenarios listed. ⚠ Per the handoff's `R-124` ask I did **not** get
as far as in-frame `ui-probe` rails *(`IsPopupOpen`/`GetItemRectSize`)* — ⇒ **that part of the ask is
undone**, and the content chain is the strongest claim available without a display.

## 6. ⏳ T3 + the outstanding E3 attribution

⚠⚠ **An honest correction about my own tooling.** The `E3` step-rail attribution I promised **never ran**:
📐 two chained background waiters each used `pgrep -f run-system-tests`, and each matched **the other's**
command line ⇒ **they waited on each other forever.** ⭐ Killed and relaunched as a direct run; result will
land in the next message. ⛔ Reported rather than quietly re-run, because I told the user it was in flight
when it was deadlocked.

**Where the E3 T3 stands (`105 / 2`, run `t3-e3b`):**

| red | attribution |
|---|---|
| `The_manifest_describes_this_host_truthfully` | ⭐ **MCP LANE'S.** *"route(s) with no capability classification: `/missions/{networkId}` …"* — those routes arrived in `d2138faaf` *"MX4b: mission editing over MCP"* and `CapabilityManifest.cs` has **no `missions` entry**. ⛔ DebugApi is fenced from this lane; the rail's own message names the fix |
| `After_a_cluster_step_the_clocked_nodes_agree_on_sim_time` | ⚠ **STILL UNATTRIBUTED.** `E2`'s T3 was **107/0** on this suite, so it regressed after that; the two candidates are my `E3` systems and the MCP merge. ⛔ **I will not call it pre-existing until the isolated re-run says so** — my systems touch no clock and early-return on null deps, but `E3` added three `PostSimulation` participants to both hosts' topologies, so assuming is not good enough |

## 7. ⭐ DECISION LOG

| # | decision | basis |
|---|---|---|
| 1 | Lift `ScenarioCatalogContributor` **+ `ScenarioEnumeration`** rather than give CGF its own source | ruling 9; and the contributor's claimed host dependency does not exist |
| 2 | CGF sources it from `GetNodeScenariosRoot(nodeId)` | ⭐ the **same root `CE-046` saves to** — so what CGF saves, CGF lists |
| 3 | Reuse `PerspectiveToolbarSection` at the editor's own `sortOrder: 20` | ⛔ no CGF-private switcher, no new toolbar model |
| 4 | Rail the **content chain**, not just the registration | §1 — registration was already green while the picker was empty |
| 5 | Add a **composition guard**: a host with a picker must have a contributor | the two sit 700 lines apart; that distance is the bug |
| 6 | **Stop** `CE-055`/`CE-056` rather than guess | `R-106`; and a blind fix to a hang is how a second hang gets added |
| 7 | File both open items **with the evidence already gathered** | ⭐ so the next session starts from "this hypothesis is dead", not from zero |
