<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-24
current-answer: dispatch pointer to FINISH entity pinning in the Watch window (UI lane) — (1) fix the
  inert pin persistence (BP-502 SILENT-DEFAULT), (2) build the approved AQ55 "pin on entity…" picker.
  Carries no design of its own: cites DESIGN_Variable_Watch_Pinning.md + Architect_Question_55 (which
  holds the classDiagram + sequenceDiagram). Restart survival (94g) is the NEXT slice, not this one.
known-conflict: none live. The prior watch-list batch (BP-499..502) is on the UI lane branch and NOT yet
  merged to coordinator — this batch CONTINUES that branch, it does not restart it. HN-037 is now merged
  into coordinator, so 94g's id-remap dependency is present for the next slice.
-->
# HANDOFF — **finish entity pinning in the Watch window** *(UI lane — the freeze owner)*

> 📌 **Dispatched at `c91b5c80f`.** ⛔ **Scope FROZEN at that sha.** ⭐ **CONTINUE the existing UI lane
> branch** `claude/reset-working-branch-qd1qpv` *(it already carries BP-499..502)* — do NOT branch fresh.
> ⭐ Rule 7: `git merge origin/claude/blueprint-authoring-status-6sr5ld` at the start *(it now carries
> HN-037 + the new build/test rules)*; **rule 1b: started-marker BEFORE any code.** ⛔ **No PR.**
> ⭐ ids **`BP-`**, series stands at **`BP-504`** *(BP-503/504 already filed open by the last batch)* — take
> the next free numbers and **state them** *(rule 5)*.

## 0. ⛔ THE DESIGN IS THE SOURCE — this file is a POINTER
📄 **[`DESIGN_Variable_Watch_Pinning.md`](../DESIGN_Variable_Watch_Pinning.md)** §3 *(the two-kind binding)*,
§5 *(persist the pin set)* + its **AS-BUILT** *(the last batch's five deviations)*.
📄 **[`Architect_Question_55_Watch_Concrete_Entity_Picker.md`](../Architect_Question_55_Watch_Concrete_Entity_Picker.md)**
— ✅ **APPROVED, READY-TO-BUILD**, carries the `classDiagram` + `sequenceDiagram`. ⭐ Item ② IS AQ55; build
what it draws and report the match *(obligation ③)*.

## 1. ⛔⛔ NEW BUILD/TEST RULES APPLY — read them first
📄 **`.claude/CLAUDE.md` → THREE TEST TIERS → the `2026-08-24` rule.** ⛔⛔ **Do NOT `dotnet build
<the.sln>` in the fix loop** — build the AFFECTED PROJECT *(`dotnet build Hrot/Editor/Hrot.Editor.AiShared
--no-restore`, ~8 s; `quick-check.sh` for the test project)*. ⛔ **The E2E/system suite is T3 — async,
never a foreground blocker.** ⭐ Prove each fix through the rail that reddens for it; do NOT re-run the
whole suite "to be sure."

## 2. ⭐⭐⭐ THE ITEMS

| # | task | the one thing not to get wrong |
|---|---|---|
| 🔴 **①** | **Fix the inert pin persistence (BP-502).** `DebugSessionPersistence.Save`'s `pinnedVariables` param is optional and defaults `null`; the sole production caller **`SaveDebugSession()` at `EditorSubsystem.cs:4864`** does NOT pass it ⇒ **no pin is ever written in the shipped editor.** Map `PinnedVariableRowSource.PinnedWithBindings()` → `PinnedVariableEntry` and pass it as the `pinnedVariables` argument at that call. Confirm the pin source is reachable at that call site *(if not, thread it — do not leave it defaulted)*. | ⛔ **This is the SILENT-DEFAULT PATTERN** *(`.claude/CLAUDE.md`)* — a caller that HAS the value must PASS it. ⛔⛔ **Also correct the prior report/tracker:** its reason *"Save has no production caller — only tests call it"* is FALSE — the caller exists and is wired to a debounced trigger; it just didn't pass the pins |
| ⭐ **②** | **Build AQ55 — the "pin on entity…" picker.** A watch action *(beside "pin chameleon" / "pin current selection")* that calls `IMapPickService.PickEntityAsync()` *(the `Hrot.Presentation/Facades` one — AQ55-B)*, takes the returned `NetworkId`, and creates a **concrete** pin carrying that `NetworkId` *(identity/persistence)* + the resolved in-session `Entity` *(display)*. No filter for v1 *(AQ55-E)*. | ⭐ **REUSE `PickEntityAsync` — it already returns the NetworkId** *(AQ55-A)*. ⛔ Do NOT build a watch-specific picker, and ⛔ NOT the declarative `MapPickableEntityAttribute` path *(AQ55-D)*. ⚠ AQ55 flags two ruling-9 duplicates — consume ONE, do NOT reconcile them here |

## 3. ⛔ DEFERRED — the NEXT slice, not this one
| ⛔ | ⭐ why |
|---|---|
| **RESTART SURVIVAL (94g)** — a stored `NetworkId` resolving back to a live `Entity` after a scenario reload | 📐 **now UNBLOCKED** *(HN-037's world-boundary/id-remap merged)* but it edits `DataBreakpointManager` *(`:1354` still `throw`s for `NetworkId`)* + consumes HN-037's remap ⇒ its own slice. ⭐ State plainly in the report: **a concrete pin persists across editor sessions but does NOT yet survive a scenario reload** |
| **the two ruling-9 duplicates** *(two `IMapPickService`, two `MapPickableEntityAttribute`)* | AQ55 §"Two ruling-9 duplicates" — each its own cleanup |

## 4. ⭐ LANE, SCOPE & COLLISION
⭐ **Yours:** `Hrot/Editor/Hrot.Editor.AiShared/{Windows,Variables}/*` · the one save call in
`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` *(`SaveDebugSession`, ~`:4864` — a DIFFERENT region from
HN-037's allocator wiring at `:1702`, no conflict)* · the map-pick adapter wiring.
⛔ **Not this batch:** 94g · the duplicate reconciliations · anything in the time/backend lanes.

## 5. GATES
⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs
`c91b5c80f`** · `--no-build` column · every RED pre-existing **by name** · goldens as a diff shape ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · **the `BP-` ids allocated**.
⭐⭐ **Row 8 — the rails that prove it:**
- **①** a **forwarding rail asserted on the CONSTRUCTED save file** — save with a pin present, reload, the pin is there; shown RED by reverting the argument pass *(the SILENT-DEFAULT control: assert on the object, not the source)*.
- **②** a rail: invoking the action → `PickEntityAsync` resolves a `NetworkId` → a **concrete** pin exists carrying that id *(fake the pick service to return a fixed id)*.
⛔ A rail never seen red is decoration. 📐 Baseline: `Hrot.Editor.AiShared.Tests` *(last measured 1999/2000)* — name yours.

## 6. ⭐ WHEN YOU ARE DONE
⭐⭐ **Fold the as-built into [`DESIGN_Variable_Watch_Pinning.md`](../DESIGN_Variable_Watch_Pinning.md)**
*(§5 persistence now WIRED; the picker path as built)* and into **AQ55** *(mark it BUILT, note any
deviation)* — obligation ⑤. ⭐ State the `BP-` ids; ⛔ design content in the design, the report points at it.
⭐ Report per obligation ③: *"the design/AQ55 carries N classes + M sequences; what I built matches / deviates HERE."*
