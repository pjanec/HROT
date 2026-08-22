<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-22
current-answer: dispatch pointer for the TIME-lane session (idle since T1-T7/W1-W2) to PORT the Bullet-based
  Stride integration from origin/stride-integ-1 onto the coordinator line, verify it breaks nothing, and
  leave a runnable Stride host for the user's visual test. THE DESIGN is DESIGN_Stride_Port.md; build from it.
known-conflict: none.
-->
# HANDOFF — TIME-lane session · **port the (Bullet) Stride integration**

> 📌 **Dispatched at `fedab5937`.** ⭐ Branch from the coordinator branch *(rule 7)*; **rule 1b:
> started-marker FIRST.** ⛔ **Scope FROZEN at this sha.**
> ⚠⚠ **NEW WORK AREA — not time work.** This session is *repurposed* for Stride; ids **`ST-`** *(not `TM-`)*,
> a **new tracker area**, ⛔ **NOT Area H**. The variable-model freeze does not touch this; the UI lane is on
> Details/menus *(different files)* — no overlap.

## 0. ⛔⛔ READ THE DESIGN FIRST — it holds the risk map, the seams, and the tasks

📄 **[`DESIGN_Stride_Port.md`](../../DESIGN_Stride_Port.md)** — read it whole. Key findings you build against:
- ⭐⭐⭐ **The animation contract (brain → stride-node) is CLEAN + ADDITIVE** — byte-identical shared ECS data
  on both branches; the Stride node is a new consumer behind the existing `IAnimationBackend`. **Do not
  touch the shared animation seam.**
- ⭐⭐ **The crowd is a STUB-REPLACEMENT, not a breakage** *(the CORRECTION section)*: the coordinator's
  `EngineBackedDtCrowdProvider` is a no-op returning zero; the real DotRecast crowd exists only on
  `stride-integ-1`. ⇒ **the whole port is ADDITIVE** — nothing live on the coordinator it would break.
- ⭐ The task breakdown **`S1`–`S5`** and decisions **`SD1`–`SD5`** *(SD2: **Bullet**, not Bepu — user ruling)*.
⭐ **Obligation ③:** check what you build against the design; **obligation ⑤:** fold any deviation back in.

## 1. ⭐ THE PORT — how, not a git merge

🔒 **Source: `origin/stride-integ-1`** *(disjoint history — a `git merge` is impossible; PORT the files,
exactly as the MCP port did)*. ⭐ **Method:** `git checkout origin/stride-integ-1 -- <paths>` for the new
projects + additive files, then **let the BUILD surface the drifts and reconcile them MEASURED, not guessed**
*(the MCP port reconciled 3 API drifts this way)*. ⛔ **Keep Bullet** — do NOT adopt trunk's Bepu for this.
⚠ Brings a **DotRecast dependency** *(the crowd/navmesh providers in `Hrot.Stride.Core`)* — add it as the
branch has it.

| # | task | design ref | note |
|---|---|---|---|
| **S1** | port the two new projects **`Stride/Hrot.Stride.Core`** *(Bullet physics, DotRecast navmesh/crowd, vehicle nav, 3D debug render)* + **`Stride/Hrot.Stride.Animation`** *(backend, bridge, LocomotionBlend, blend-tree)*; add to the solution | §1, §4 `S1` | new paths, additive |
| **S2** | add the additive nav pieces: **`CrowdMotorIntent`** *(component id **265** — FREE, no collision)*, the `IDtCrowdProvider.RegisterAgent(entity, params, startPos)` overload, and the `EngineBackedDtCrowdProvider`/`FakeDtCrowdProvider`/`NavigationContractsComponentIds`/`NavigationIntentBridgeSystem` deltas | §3, §4 `S2` | additive |
| **S3** | apply the `CrowdAgentUpdateSystem` change **authority-conditionally** *(defensive; the coordinator crowd is a stub so this is not load-bearing — see the CORRECTION)* | §3, §4 `S3` | ⚠ the one shared-file behaviour touch; rail it |
| **S4** | wire the Stride visual binding + animation backend on a **Stride host**; confirm `StrideAnimationBridge` reads `SimVelocity` and drives the blend tree; discrete montages fire off `OffMeshTraversalStartedEvent` | §2, §4 `S4` | additive |
| **S5** | **verify nothing broke + leave a runnable Stride host** *(§3 below)* | §4 `S5` | the deliverable's proof |

## 2. ⭐⭐ "TEST THAT IT DID NOT BREAK EXISTING STUFF" — the regression gate

⭐ Because the port is **additive** *(animation untouched; crowd was a stub)*, the regression bar is: **the
existing build + suites stay green, and non-Stride nodes are unchanged.** Report, per the gate contract:
- ⭐⭐ **full solution build green** *(the port + reconciled drifts)*.
- ⭐⭐ **the navigation suites** — `Fdp.Toolkits.Tests` **Navigation/** *(EngineBackedProviderTests, crowd/
  offmesh/bridge tests)* — pass; ⛔ **a `CrowdAgentUpdateSystem` behaviour change must keep the non-Stride
  path intact** *(rail it: a stub/fake provider still yields the prior no-movement behaviour; nothing that
  moved before stops moving)*.
- ⭐ **`Hrot.SimHost` + animation suites** *(`Hrot.MuscleCharacter.Animation*`)* — unchanged, green *(the
  shared animation contract was not touched — prove it by a zero-diff on those files)*.
- ⭐⭐ **the integration invariant** *(rule 8 row 8)*: name and run the suite that would break if a shared
  nav/animation contract broke *(the crowd/nav integration tests)*, or state with base-sha why it cannot.
- ⭐ goldens as a diff shape; `tracker-counts.py --check`; `rulings-check.py`; the **`ST-` ids you allocated**.

## 3. ⭐⭐ "PREPARE FOR VISUAL TESTS" — leave a runnable Stride host

⭐ The user will visually verify 3D on the next baseline *(as they did L6)*. Deliver:
- ⭐⭐ **a launch path for a Stride host** that renders 3D + animates entities from brain output — document the
  exact command *(mirror `docs/Editor_Headless_Xvfb.md`'s form; note whether it needs a real display or runs
  under Xvfb+GL)*.
- ⭐ **a short `docs/` note** — what to look at *(entities render as 3D models; locomotion blends idle/walk/run
  from `SimVelocity`; a jump/traversal plays its montage)* and the run command.
- ⚠ **do NOT wire the MCP harness to it** — that is a later step; just make it launchable and describe what a
  human should see.

## 4. ⛔ LANE & NOT-THIS-BATCH

⛔ **Do not touch:** the UI/variable frozen area *(`Hrot.Editor.AiShared`, variables, blackboard, Details)*,
the coordinator's MCP wiring in `EditorSubsystem`, or the UI lane's Details/menu files *(their live batch)*.
⭐ **Your surface:** `Stride/Hrot.Stride.*` *(new)* · `FDP/Toolkits/Fdp.Toolkits/Navigation/*` *(additive +
the one conditional `CrowdAgentUpdateSystem` change)* · a Stride host wiring *(SimHost/CGF side)*.
⛔ **Not this batch:** Bepu adoption; the vehicle/3D-debug polish beyond what S1 brings; the MCP-driven tests.
⚠ **A cross-lane edit is a STOP-and-report** *(`R-128`)*.

## 5. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · delta vs base · the
`--no-build` column · every RED confirmed pre-existing against the base sha · goldens as a diff shape ·
`tracker-counts.py --check` · `rulings-check.py` · the **`ST-` ids you allocated** · `R-106` verdicts · **the
integration-suite row** *(row 8)*. ⭐ Rule 4/7: re-sync + pull the coordinator branch around the batch. ⭐
Rule 1b: push the started-marker before writing code.
