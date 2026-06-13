# Toolbar Debug Icons — Activation Wiring — Task Tracker

The main-toolbar AI-debug icons (Continue/StepOver/StepInto/StepOut/Pause/StepBack) are permanently
dark/unclickable because their `IsEnabled` predicates read `IDebugSessionRegistry.ActiveSession`, which
**nothing in production ever sets** (`TryAcquireSession` is only called in tests). The working in-perspective
blueprint debug controls use `_blueprintDebugSession` directly and never touch the registry.

**Fix of record:** drive `ActiveSession` off the active document's kind via `AiDocumentManager.ActiveChanged`,
using a new side-effect-free `SetActiveSession` (NOT `TryAcquire`/`Release`, which would `Detach()` the
always-attached blueprint session). Scope = **Blueprint only**; BTree/HSM debug sessions are not yet
attached/working (lead decision 2026-06-13) → map them to `null` for now.

One line per task. `[ ]` open · `[~]` in progress · `[x]` done (built + tests green + lead-reviewed + committed) · `[!]` blocked.

---

## Working agreement — implementing agent (Zoo) — MANDATORY, restate in every batch

1. **One task per batch.** Touch ONLY the named files. Do not edit files owned by other tasks/workstreams
   (esp. the `ai-hsm-btree-vis-edit-2` thread).
2. **No cheating to pass the build/tests.** NEVER weaken an assertion, suppress a diagnostic, `#pragma warning
   disable`, or stub a feature to dodge a hard error. If blocked, **STOP and write the blocker in the report.**
3. **Finish without asking.** Build + run the named test project(s), diagnose root causes, fix, repeat until
   `Failed: 0` and **0 warnings**, then write the report. Nothing "done" while red.
4. **Headless only.** Verify via build + unit tests. Tasks marked **[RUNTIME GATE]** need the lead to confirm
   behaviour in the running editor — Zoo is NOT responsible for that, but MUST make logic headless-testable.
5. **Litter-free.** No scratch files, no leftover `Console.WriteLine`/`File.WriteAllText`. Clean tree.
6. **Report = truth.** The report must match the diffs. The lead reviews diffs + assertions, not prose.
7. **Do NOT use codebase-memory MCP tooling.** Read the actual source.

**Verification baseline (run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`):** `Hrot.Editor` 0/0,
`Hrot.Editor.Tests` 185/0, `Hrot.Editor.AiShared.Tests` green.

**Folders:** batch → `batches/BATCH-TDA-XX-INSTRUCTIONS.md`; report → `reports/BATCH-TDA-XX-REPORT.md`.

---

## Tasks

- [!] **TDA-01** (superseded by TDA-02) — blocked: `BlueprintDebugSession` does not implement `IAiDebugSession`
      (parallel interface hierarchy), so it can't be assigned to `SetActiveSession(IAiDebugSession?)`. Zoo
      correctly stopped (no files changed). See `reports/BATCH-TDA-01-REPORT.md`.
- [ ] **TDA-02** Prereq + wiring (supersedes TDA-01): make production `BlueprintDebugSession` implement
      **both** `IBlueprintDebugSession` + `IAiDebugSession` (mirror the established `FakeBlueprintDebugSession`
      dual-interface pattern — explicit interface impls for the 4 colliding breakpoint members), then add
      `SetActiveSession` to `DebugSessionRegistry` (+ interface) and wire `AiDocumentManager.ActiveChanged` →
      `debugRegistry.SetActiveSession(active-doc kind → session)` in `EditorSubsystem` (Blueprint →
      `_blueprintDebugSession`; BTree/HSM/none → `null`). **[RUNTIME GATE]**
      → batch: [BATCH-TDA-01-INSTRUCTIONS.md](./batches/BATCH-TDA-01-INSTRUCTIONS.md)
      *(Blast radius: the only production reader of `ActiveSession` is the toolbar `AiDebugCommands` — confirmed
      by grep — so this is toolbar-scoped; the new `IAiDebugSession` breakpoint/trace members are never called
      via this path but must be honest, NOT throw.)*
- [ ] **REVIEW-TDA** *(lead/user RUNTIME GATE)* — run a blueprint: confirm the toolbar **Pause** icon lights
      while running, and **Continue/Step/StepBack** light when paused at a breakpoint; in-canvas debug controls
      still work unchanged.

## Done-definition
With a blueprint active in the running editor, the main-toolbar debug icons enable/disable live in step with the
blueprint debug session's `IsAttached`/`IsPaused` state; the existing in-perspective `DebugStepControls` path is
untouched and the always-attached blueprint session is never detached by this change.
