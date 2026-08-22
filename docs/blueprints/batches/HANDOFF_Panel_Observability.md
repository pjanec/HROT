<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-22
current-answer: dispatch pointer for the UI lane — the panel model-view / observability programme. PHASE 1
  U-obs-1 (the PanelSnapshot contract + one pilot, HANDS-ON) then push it green as a checkpoint; PHASE 2 the
  panel fan-out via SONNET subagents. THE DESIGN is DESIGN_UI_Observability_Snapshot.md; build from its §UML.
known-conflict: none.
-->
# HANDOFF — UI lane · **panel model-view migration (`PanelSnapshot`), run freely**

> 📌 **Dispatched at `5843055e7`** *(the joined commit — both lanes' last batches merged, solution builds
> 0 errors)*. ⭐ Branch **fresh from the coordinator branch** *(rule 7)*; **rule 1b: started-marker FIRST.**
> ⛔ **Scope FROZEN at this sha.** ⭐ **Run freely — you wait for NOTHING.** ⭐ ids **`BP-`**, a **new tracker
> area** *(Area K — Panel observability)*; the UI/variable frozen area is yours, panels across the app are yours.

## 0. ⛔⛔ READ THE DESIGN FIRST

📄 **[`DESIGN_UI_Observability_Snapshot.md`](../../DESIGN_UI_Observability_Snapshot.md)** — read it whole.
⭐⭐⭐ **§UML** the contract *(classDiagram + sequenceDiagram)* · **§APIs** the signatures · **§Example** a real
before/after · ⛔⛔ **the INVARIANT** *(the draw renders ONLY from the VM — the load-bearing rule)* · **§Adoption**
the value order. ⭐ Context: the umbrella **[`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md)**
— this programme is step 2, the critical path that unblocks the whole visual de-risk story.
⭐ **Obligation ③:** check what you build against §UML; **⑤:** fold any deviation back into the design.

# ═══ PHASE 1 — `U-obs-1`: the contract + one pilot (HANDS-ON, Opus) ═══

⛔⛔ **Do NOT fan this out.** The contract is foundational, novel work — the pattern every later conversion
mirrors. ⭐ Build it hands-on, prove it on ONE panel, and only THEN delegate.

| # | task | design | gate |
|---|---|---|---|
| **U1a** | **`IPanelViewModel`** *(`+string PanelId`, `+JsonNode Dump()`)* + the **`PanelSnapshot`** static singleton *(`CaptureEnabled`, `Register`, `TryGet`, `DumpAll`, `RegisteredPanels`)*. ⭐ **Lean: home it in `Fdp.Diagnostics.Contracts`, beside `DebugPrimitiveBuffer`** *(referenceable by every panel assembly, `Hrot.Editor`'s DebugApi, and the harness)* — confirm the assembly and say so | §APIs | it compiles; a unit test round-trips `Register`→`DumpAll` |
| **U1b** | ⭐⭐ **the opt-in registry** — `RegisteredPanels` distinguishes *"panel drew empty"* from *"not instrumented"* *(else un-converted panels give false greens)*; the **flag gates the DUMP, not the build** | §"Perf & correctness" | a test: an unregistered panel id is absent, not empty |
| **U1c** | ⭐⭐⭐ **ONE pilot panel converted end-to-end** — ⭐ **lean: a DRAW-ONLY panel** *(e.g. `EntityBlueprintsPanel`, the §Example)* because that is the pattern the fan-out mirrors; split build-VM from render, **render ONLY from the VM**, register when `CaptureEnabled` | §Example, §Invariant | a test reads the pilot's model via `PanelSnapshot` and asserts a field; the panel still draws identically |
| **U1d** | ⛔ **stable panel id** — use the window-manager registration id, identical across frames *(and, later, across hosts — conformance depends on it)* | §"Perf & correctness" | the id is stable frame-to-frame |

### ⭐⭐ CHECKPOINT — push `U-obs-1` GREEN, then keep going

⭐ When U1a–U1d are green: **push `chore: U-obs-1 contract green at <sha>`** with a one-line status.
⭐⭐ **This is what unblocks the time lane's Group T** *(it reads `PanelSnapshot`)* — so push it as soon as it's
solid. ⛔ **Then CONTINUE to phase 2 in the same run — do not wait for anyone.**

# ═══ PHASE 2 — the fan-out (SONNET subagents) ═══

⭐ Now the contract is fixed, the per-panel conversions are **mechanical mirror-pattern work** — exactly what
the token-thrift rule says to **delegate to Sonnet subagents**, with **Opus reviewing the real diff and
re-running the panel's gates**.

| # | task | design |
|---|---|---|
| **U-obs-2** | ⭐ convert the **high-risk unified surfaces FIRST** — Details / blackboard / watch *(they already have `VariableTableModel`; make it `IPanelViewModel` + register — the lightest conversions and where cross-host risk concentrates)* | §Adoption `U-obs-2` |
| **U-obs-3** | wire the **gizmo/map peer feed** — register `DebugPrimitiveBuffer.GetFrame()` under a well-known id into the same snapshot | §Adoption `U-obs-3` |
| **U-obs-5+** | convert further panels **value-ordered**; ⛔ never refactor a static label | §Adoption `U-obs-5` |

⭐⭐ **The Sonnet recipe** *(put the pilot in front of each subagent as the template)*: "convert panel X to
build-VM / render-from-VM / register — mirror the pilot; add a `PanelSnapshot` dump test." ⛔⛔ **Opus's review
gate on every returned diff: the INVARIANT** — *any drawn value that did not come from the VM is a defect.* ⭐
Re-run the touched panel's tests before accepting.

## 3. ⛔ LANE & NOT-THIS-BATCH

⛔ **Do NOT touch:** `Hrot.Editor/DebugApi/*`, the MCP wiring in `EditorSubsystem` *(the time lane owns those —
and `PanelSnapshot` is a **static singleton panels register themselves into**, so it needs **NO `EditorSubsystem`
edit** — keep it that way)*, the engine/kernel, the `Hrot.SystemTests` harness, the parked Stride tree.
⛔ **`GET /panels` (Group T) is the TIME lane's, AFTER your `U-obs-1` merges** — do not build the endpoint here.
⚠ A cross-lane edit is a STOP-and-report *(`R-128`)*.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · delta vs base
`5843055e7` · `--no-build` column · every RED confirmed pre-existing · goldens as a diff shape ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` on any
design you fold a deviation into · the **`BP-` ids you allocated** · `R-106` verdicts. ⭐ Rule 4/7: re-sync +
pull the coordinator branch around the batch. ⭐ Rule 1b: started-marker before code; ⭐ push the U-obs-1
checkpoint before phase 2.
