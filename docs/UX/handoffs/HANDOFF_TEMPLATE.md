# HANDOFF — `UXT-nn` <short title>

> **Copy this file to `HANDOFF_UXT-nn_<slug>.md` and fill it in. Delete the italic guidance lines.**
>
> ## 📖 Read before starting — non-negotiable
>
> 1. **[../UX_Programme_Briefing.md](../UX_Programme_Briefing.md)** — the big picture and the work
>    habits. **Read it in full; it is short.** It tells you what this programme is for, the five
>    questions we design against, which engine patterns we copy, when to delegate to Sonnet, the
>    architect gate, and the traps that have already cost this codebase real time.
> 2. **This handoff**, below.
> 3. **The task's entry in [../UX_Tasks_Detail.md](../UX_Tasks_Detail.md)** — evidence, scope,
>    acceptance.
>
> Nothing else is required reading. If you find yourself needing more, that is a defect in this
> handoff — say so in the report.

## The one paragraph of context

*HROT's authoring infrastructure works; the authoring experience does not. An ordinary scenario
author cannot get from "new scenario" to "saved, reloaded, running scenario with behaviors still
attached" without knowing which ImGui window to open in what order, and without hitting controls that
silently do nothing. This programme fixes the shell around the (already good) graph editors. Your task
is one step of that.*

---

## Task

| | |
|---|---|
| **ID** | `UXT-nn` → [detail entry](../UX_Tasks_Detail.md#uxt-nn) |
| **Requirement** | [`UXR-nn`](../UX_Requirements.md#uxr-nn) — *one-line restatement* |
| **Design decision** | [`UXD-nn`](../UX_Design.md#uxd-nn) — *must be `DECIDED` or `LEAN`; if `OPEN`, stop and say so* |
| **Question improved** | *1 Where am I / 2 What's in my world / 3 What is this / 4 What can I do / 5 Did it work* |
| **Complexity** | `WIRING` \| `RW-L` \| `RW-M` \| `RW-H` |
| **Branch** | `claude/reset-working-branch-qd1qpv` *(unless stated otherwise)* |
| **Base commit** | *`<sha>` — verify you are on it before starting* |

## What to build

*Concrete and complete. Name the files you expect to change. State the intended end state, not the
steps — the implementer decides those.*

## Evidence — what is broken now

*`file.cs:line` citations. Mark anything ⚠ unverified.*
*⚠ **Re-derive these from code before building.** The register that opened this programme was built
from a code scan, but the sibling blueprint audit was wrong ten times. If a claim here is wrong, fix
this doc and the detail entry in the same commit as your work, and add a row to
[Corrections](../UX_Tasks_Detail.md#corrections).*

## Acceptance — the test a person performs

*The observable check in the running editor (`--mode editor` / `run_Editor.bat`). Must satisfy:*

- *the requirement's own acceptance criterion;*
- ***A1** — reachable in ≤2 clicks from the previous golden-path state, no window-opening detour;*
- ***A2** — the UI states the outcome. Silence is a defect.*

## Out of scope

*What a reader would reasonably assume is included and is not. Be explicit — this is where scope creep
enters.*

## Shared-panel hosts to check

*⚠ **Trap U1.** `MissionPanel`, the entity inspector and the ORBAT panel are consumed by ExCon / IG /
CGF as well as the editor. List every host of every panel you touch, and gate on their suites. If you
touch none, write "none".*

## Gates

*Which suites must be green, with their expected counts. At minimum the suites covering the projects
you changed, plus every host listed above. Build must be 0 errors.*

- [ ] `<suite>` — *expected: n passed*
- [ ] build — 0 errors

## Required steps before reporting done

- [ ] **Revert-to-red confirmed.** Revert your fix, watch the new tests fail, restore.
      *([Briefing §5.5](../UX_Programme_Briefing.md#55-revert-to-watch-it-go-red)) — required, not optional.*
- [ ] **Visual check performed** in the running editor. Record what you actually saw.
      *([Briefing §5.10](../UX_Programme_Briefing.md#510-visual-verification-is-mandatory)) — a green suite
      proves nothing about usability. If the editor cannot be launched in your environment, say so
      explicitly rather than skipping this silently.*
- [ ] **No dead controls added.** Every control you render works, or is visibly disabled with a stated
      reason.
- [ ] **Assertions are on effects, not on `Success`.** *(Trap #5)*
- [ ] **Production construction sites grepped** for inert-default dependencies. *(Trap #8)*
- [ ] [../UX_Tasks_Detail.md](../UX_Tasks_Detail.md) `DONE` note written — what shipped, what the visual
      check showed, what the task exposed.
- [ ] [../UX_Task_Tracker.md](../UX_Task_Tracker.md) row ticked and counts updated, **in the same commit
      as the work**.
- [ ] [../UX_RESUME.md](../UX_RESUME.md) §2 Status and §3 Next up refreshed.

## Delegation guidance

*Per [Briefing §5.1](../UX_Programme_Briefing.md#51-model-delegation-token-thrift): use a **Sonnet**
subagent for mirror-an-existing-pattern slices, mechanical edits across call sites, and broad
evidence-gathering searches. Stay hands-on for novel design, anything touching the ECS mutation/undo
model, and the final diff review. **Review the real diff and re-run the gates yourself** — never accept
a subagent's "all green" without seeing the output.*

## Report back

1. What shipped, as a diff summary.
2. Gate output — actual numbers. **If a gate is red, say so with the output.** Do not round up.
3. What the visual check showed, in the author's terms ("I clicked X, saw Y").
4. What this task **exposed** — the next defect, a wrong estimate, a doc that lied. *In this codebase a
   wiring fix reliably exposes the next one; that finding is often worth more than the fix.*
5. Anything you left undone, and why.
