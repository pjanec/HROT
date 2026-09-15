<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-25
current-answer: dispatch pointer for slice 3 / 94g — a CONCRETE watch pin survives a scenario RELOAD.
  Carries no design: cites DESIGN_Variable_Watch_Pinning.md §5 (mechanism), §8 (measured costs + the
  user-ruled callback sink), §8a (the classDiagram + sequenceDiagram). UI lane (freeze owner).
known-conflict: none live — the backend lane (HN-037) that owned StagingEntityExtractor/EditorSubsystem
  is FINISHED and merged. This slice makes the small cross-subsystem touches §5/§8 already scoped.
-->
# HANDOFF — **94g: a concrete watch pin survives a scenario reload** *(UI lane — the freeze owner)*

> 📌 **Dispatched at `544bf52b3`.** ⛔ **Scope FROZEN at that sha.** ⭐ **CONTINUE the UI lane branch**
> `claude/reset-working-branch-qd1qpv` *(it carries BP-499..507)* — do NOT branch fresh. ⭐ Rule 7:
> `git merge origin/claude/blueprint-authoring-status-6sr5ld` at the start; **rule 1b: started-marker
> BEFORE any code.** ⛔ **No PR.** ⭐ ids **`BP-`**, series at **`BP-507`** ⇒ start **`BP-508`**; state them.

## 0. ⛔ THE DESIGN IS THE SOURCE — this file is a POINTER
📄 **[`DESIGN_Variable_Watch_Pinning.md`](../DESIGN_Variable_Watch_Pinning.md)** — **§5** *(persistence &
restart survival BY TRANSLATION)*, **§8 ①②** *(the two measured costs; the user-ruled **callback sink**)*,
**§8a** *(the `classDiagram` + `sequenceDiagram` — build what they draw, report the match per obligation ③)*.
⭐ The mechanism is already RULED; this is a wiring slice, not a design.

## 1. ⛔⛔ NEW BUILD/TEST RULES APPLY
📄 `.claude/CLAUDE.md` → THREE TEST TIERS → the `2026-08-24` rule. ⛔⛔ **Never `dotnet build <the.sln>` in
the fix loop** — build the AFFECTED PROJECT *(measured 115 s → 8 s)*. ⛔ **The E2E/system suite is T3 —
async, never a foreground blocker.** ⭐ Prove each fix through the rail that reddens for it.

## 2. ⭐⭐⭐ THE ITEMS *(all specified in §5/§8/§8a)*

| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Publish `oldToNewMap`.** Add an optional `Action<IReadOnlyDictionary<long,long>>` **sink** to `StagingEntityExtractor` *(the map is a local at `:204`)*; the **subsystem** wires it to the orchestration bus *(`PublishManaged` a managed dict — the bus takes it as-is)*. | ⛔⛔ **Do NOT move or copy the remap CODE** *(ruling 9 · `R-79` — the most safety-critical map in the system)*; **only the map is published.** ⛔ The extractor must NOT take a bus *(keeps `Hrot.CGF` bus-free, separately deployable)*. ⚠ **Cross-subsystem touch** *(`Hrot.CGF` + subsystem)* — named here; §5/§8 already scoped it |
| ⭐ **②** | **Consolidate `FindEntityByNetworkId` into ONE `NetworkIdResolver`** in `FDP/Toolkits/Fdp.Toolkits/Replication/` *(it already owns `NetworkIdentity`)*. Best-of-four: **filtered query + `GetComponentRO` + null guard.** | ⛔⛔ **There are FOUR copies, not two** *(`R-77` corrected, `M-26`)* — replace all four, ⛔ **do NOT add a FIFTH.** ⛔ **No index, no cache** — §4's two-clocks rule makes the linear scan correct |
| 🔴 **③** | **The concrete pin stores the STAGING `NetworkIdentity`**, resolved at BIND time *(selection-change / load only)* through the published map → `NetworkIdResolver`. | ⚠ **AS-BUILT deviation ③ CORRECTION:** today `EntityBinding` stores the RUNTIME id ⇒ breaks on reload. This flips it to staging + translate. ⛔ Resolve on the two-clocks boundary, **never on the tick** |
| ⚠ **④** | **`DataBreakpointManager:1354` stops throwing for `NetworkId`** — resolve through the same seam. | ⛔ **§8a's OPEN DECISION: the consolidated `NetworkIdResolver` scan vs the maintained `NetworkEntityMap` index** *(in-degree 131)*. ⭐ **Report which you chose and WHY** *(per-tick ⇒ index; two-clocks ⇒ scan)* — do not silently pick |
| ⚠ **⑤** | **Re-attach a RESTORED pin to its Watch window** once the owning asset is open. `PinnedVariablePersistence.Restore` produces descriptors that **nothing consumes** today. | ⭐ Same resolution problem as ①–③; a row is rebuilt by the source that owns its asset. ⛔ If this is bigger than a wiring line, SPLIT it and say so — do not stall ①–④ |

## 3. ⭐ LANE, SCOPE & COLLISION
⭐ **Yours (UI lane, freeze owner):** `Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` · the pin
binding *(`EntityBinding`, `Hrot.Editor.AiShared`)* · `PinnedVariablePersistence`.
⚠ **Cross-subsystem touches this slice legitimately makes** *(named, §5/§8-scoped)*: the `OnRemap` sink on
`Hrot.CGF/Orchestration/StagingEntityExtractor.cs` + the wiring in the subsystem; the new
`NetworkIdResolver` in `FDP/Toolkits/Fdp.Toolkits/Replication/`. ⭐ **Confirm at start no other lane is live
on those files** *(HN-037 is merged/finished — it should be clear)*; ⛔ if one is, STOP and report.
⛔ **Not this batch:** the two ruling-9 duplicates AQ55 flagged · `HsmDebugSession`/`BTreeDebugSession`
wiring *(§8 slice 4, `R-70`)*.

## 4. GATES
⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs
`544bf52b3`** · `--no-build` column · every RED pre-existing **by name** · goldens as a diff shape ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · the `BP-` ids allocated.
⭐⭐ **Row 8 — the rails that prove it:**
- 🔴 **the headline:** a rail that pins a concrete variable, **reloads the scenario**, and asserts the pin
  **re-binds to the NEW runtime entity** *(store staging → translate via the published map)*; shown RED by
  reverting item ③. ⛔ This is the acceptance criterion — a pin that survives a reload.
- **①** a rail asserting the sink fires and the map reaches the bus reader.
- **②** a rail asserting `NetworkIdResolver` is the sole resolver *(the four call sites now route through it; no fifth)*.
- **④** a rail asserting `DataBreakpointManager` resolves a `NetworkId` instead of throwing.
⛔⛔ **CROSS-CUTTING ⇒ NAME THE INTEGRATION SUITE** *(rule 8 row 8)*: the reload path crosses CGF→bus→editor,
so name and run *(isolated/filtered if flaky)* the integration suite that proves an entity stays resolvable
across a reload — or state, with base-sha evidence, why it cannot gate.

## 5. ⭐ WHEN YOU ARE DONE
⭐⭐ **Fold the as-built into [`DESIGN_Variable_Watch_Pinning.md`](../DESIGN_Variable_Watch_Pinning.md)** §5/§8/§8a
*(the sink as built, the resolver's home, the `DataBreakpointManager` decision, the ⑤ outcome)* — obligation ⑤;
mark slice 3 **BUILT**. ⭐ State the `BP-` ids; ⛔ design content in the design, the report points at it.
⭐ Report per obligation ③: *"§8a carries N classes + M sequences; built matches / deviates HERE."*
