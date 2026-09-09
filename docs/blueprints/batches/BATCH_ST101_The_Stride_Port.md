<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: this file is a BATCH — scope, items, gates, verdicts. It carries NO design.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐ BATCH ST-101 — **the (Bullet) Stride port**

> ⛔ **A batch, not a design** *(CLAUDE.md ①b)*. ⭐ **Design:**
> [`docs/DESIGN_Stride_Port.md`](../../DESIGN_Stride_Port.md) — §1–§5 the map, **§6 the AS-BUILT**,
> including the two premises the port measured FALSE.
> 📄 Dispatch: [`HANDOFF_Stride_Port.md`](HANDOFF_Stride_Port.md), stamped `fedab5937`.
> ⭐ Started marker pushed *(rule 1b)*: `chore: started Stride port batch at fedab5937` (`128eb68c`).
> ⚠ Ids **`ST-`**, tracker **Area I** — ⛔ not Area H.

| # | task | verdict |
|---|---|---|
| **`S1`** | the two Bullet/Stride libraries | ✅ **done** *(`ST-001`)* — `Animation` needed ZERO reconciliation; `Core` needed ONE |
| **`S2`** | the additive nav pieces | ✅ **done** *(`ST-002`)* — ⚠ **plus a third the design did not name** *(`ST-003`)* |
| **`S3`** | `CrowdAgentUpdateSystem` authority-conditional | ✅ **done** *(`ST-004`)* — per ENTITY, 3 rails |
| **`S4`** | Stride visual binding + animation backend on a host | ✅ **done** — standalone host ✅ **and** the hosted-real-editor mode, after the user challenged my first verdict *(`ST-007` → `ST-010`)*. ⭐ and the mannequin's animation descriptor *(`ST-011`)* — **no stops remain** |
| **`S5`** | verify nothing broke + a runnable host | ✅ regression gate green; ⚠ "runnable" is **Windows-only and always was** *(`ST-006`)* |

## ⭐⭐ The port method, and what it actually cost

🔒 Per the handoff: `git checkout origin/stride-integ-1 -- <paths>`, then **let the build surface the
drifts**. 📐 It surfaced **four**, all reconciled measured rather than guessed:

| drift | what it was | fix |
|---|---|---|
| `IRaycastBackend` missing | ⛔ **not an API drift — a design GAP**: a third shared-side file §1 never listed | ported it + its `RaycastSolverSystem` consumer *(`ST-003`)* |
| `CycloneDDS.NET` 0.2.3 → **0.3.2** | needs Roslyn ≥ 4.8.0; `Stride.Core.Assets.CompilerApp` pins **= 3.6.0** *(NU1107 × 3 projects)* | NuGet's own named remedy — pin 4.8.0 directly *(`ST-009`)* |
| 26 errors in `EditorStrideSubsystem` | ⛔ **twelve `EditorSubsystem` members that do not exist here** | ⚠ first guarded *(`ST-007`)*, then **PORTED** once measured *(`ST-010`)* |
| `app.manifest` missing | ⚠ gitignored, committed on **neither** branch | `<ApplicationManifest>` made conditional *(`ST-008`)* |

⭐⭐ **Only the second is a true drift.** ⛔ The first is a design gap, the third a lane boundary, the
fourth a pre-existing repo defect — ⚠ **and calling them all "drifts" would have hidden three
findings.**

## ⛔⛔ Two premises of the design measured FALSE

| premise | measured |
|---|---|
| *"`CrowdMotorIntent` id **265** is FREE — coord ids stop at 264"* | ⛔ **`NavFakeIds` claims 262–279 and RESERVED 265**. ⭐ But the constant has **no component attached** ⇒ a reservation, not a claim ⇒ moved to 269 so 265 has one claimant *(`ST-005`)* |
| §1's seam table is complete | ⛔ **`IRaycastBackend` is a third shared-side piece** and appears in neither §1 nor `S2` *(`ST-003`)* |

⭐ Both folded into the design *(obligation ⑤)* — §6.2 and §6.3.

## ⭐⭐⭐ `S3` — the substantive design choice, argued *(obligation ③)*

⭐ The design said *"authority-conditional"* and did not say **what selects the arm.** ⇒ built as
**the entity's `CrowdMotorIntent`**: present ⇒ intent only; absent ⇒ the pre-port behaviour, unchanged.

⭐⭐ **Per ENTITY, not per node** — the component is added only by the host that also runs the motor
and the reverse-sync, so **its presence IS the marker**. ⛔ A node-level flag would be a second thing
to keep in step with the first, and its failure is **silent**: an agent that stops moving with
nothing to point at. 📐 Railed with a **mixed world** — one agent of each kind in one repository.

## Gate results

| gate | `--no-build`? | baseline | after | Δ |
|---|---|---|---|---|
| main solution build *(`IOS-IG-SimHost.sln`)* | builds | 0 errors | ✅ **0 errors** | **0** |
| ⭐⭐ **`Fdp.Toolkits.Tests` — `~Navigation`** *(the regression surface)* | `--no-build` | 292 / 0 | ✅ **295 / 0** | **+3 rails** |
| ⭐ **`Fdp.Toolkits.Tests` — `~Physics`** *(`RaycastSolverSystem`)* | `--no-build` | 31 / 0 | ✅ **31 / 0** | **0** |
| `Hrot.MuscleCharacter.Animation.Tests` | `--no-build` | — | ✅ **195 / 0** | — |
| `Hrot.MuscleCharacter.Animation.Fake.Tests` | `--no-build` | — | ✅ **15 / 0** | — |
| `Hrot.MuscleCharacter.Animation.Stride.Tests` | `--no-build` | — | ✅ **31 / 0** | — |
| ⭐⭐ **animation contract ZERO-DIFF** | — | — | ✅ **no change** under `Hrot.MuscleCharacter.Animation/`, `Fdp.Core/CoreComponents/`, `Fdp.Toolkits/Behavior/Components/` | — |
| `Hrot.SimHost.Tests` **FILTERED** *(`TM-036`)* | `--no-build` | stable core 8 red | ✅ **63 / 8**, same 8 names | **0** |
| time standing gates *(`~ClusterTimeObservation\|~HaltReason\|~MasterSyncController`)* | `--no-build` | 64 / 0 | ✅ **64 / 0** | **0** |
| ⭐⭐⭐ **`Hrot.Blueprints.Tests` — `~Hrot.Blueprints.Tests.Editor`** *(NEW row: the suites that CONSTRUCT `EditorSubsystem` — the `ST-010` gate)* | `--no-build` | — | ✅ **1032 / 0**, 9 skipped | — |
| ⭐ **`BreakpointSubsystemWiringTests`** *(integration, builds a real editor)* | `--no-build` | — | ✅ **25 / 0** | — |
| ⭐ **`TimeControlIntegrationTests`** *(rule 8 row 8 — the cross-node invariant)* | `--no-build` | 9 / 0 | ✅ **9 / 0** | **0** |
| ⚠ **`Hrot.Editor.Tests`** | `--no-build` | ⛔ **the 209 / 0 was STALE — see `ST-012`** | ⚠ **207 / 2**, both **confirmed pre-existing by stash + REBUILD** | **0** |
| ⭐ `Hrot.Presentation.Tests` filtered *(+`~Selection`, for `DefaultSelectionState.Version`)* | `--no-build` | — | ✅ **27 / 0** | — |
| ⭐ **`Fdp.Examples.Scenarios.Tests`** *(NEW row — `ST-011` changes that project)* | `--no-build` | — | ✅ **56 / 0**, 12 skipped | — |
| ⭐⭐ **all 3 Stride test projects compile with ZERO `<Compile Remove>`** | idem | ⛔ 3 files excluded | ✅ **none excluded** | **−3** |
| ⭐ **`Hrot.Stride.Core` build** | `-p:EnableWindowsTargeting=true` | — | ✅ **0 errors** | new |
| ⭐ **`Hrot.Stride.Animation` build** | idem | — | ✅ **0 errors** | new |
| ⭐ **`HrotStrideApp.Game` build** | idem | — | ✅ **0 errors** | new |
| the 3 Stride **test projects** build | idem | — | ✅ **0 errors** | new |
| ⛔ the 3 Stride test projects **RUN** | — | — | ⛔ **IMPOSSIBLE HERE** — see `ST-006` | — |
| ⛔ `HrotStrideApp.Windows` build | idem | ⛔ **already red at base** | ⛔ **red** — asset compiler, exit 150 | **0 — pre-existing** |
| `tracker-counts.py --check` | — | OK | ✅ **OK** | — |
| `rulings-check.py` | — | — | ⚠ **1 staleness WARN on `.claude/CLAUDE.md`** — arrived with the coordinator merge, not this diff | — |
| `mermaid-check.mjs` on the design | — | 1 block | ✅ **1 block parses** | **0** |
| working tree after every suite | — | clean | ✅ **clean** | — |
| goldens | — | — | ⛔ **none moved** | — |

### ⛔⛔ `ST-006` — **"verify it works" has a hard ceiling in this session, and it is not the port's fault**

📐 Three measurements, each run rather than reasoned:

| # | fact |
|---|---|
| ① | `net8.0-windows` compiles here **only** with `-p:EnableWindowsTargeting=true`, on **restore AND build** |
| ② | the Stride suites **cannot run**: the test host needs the `Microsoft.WindowsDesktop.App` **runtime**, which has no linux-x64 build — *"No frameworks were found"* |
| ③ | `HrotStrideApp.Windows` **cannot build**: `Stride.Core.Assets.CompilerApp` runs `--platform=Windows --compile-property:StrideGraphicsApi=Direct3D11`, exit **150** |

⭐⭐⭐ **③ confirmed PRE-EXISTING** — built the base commit `128eb68c` in a throwaway worktree with only
the manifest condition applied: **same failure, before a single port file existed.**

⇒ ⭐⭐ **What this batch can honestly claim: the shared side is REGRESSION-TESTED green, and the new
Stride code is COMPILE-verified.** ⛔ **Its behaviour is unproven anywhere** — the first run of those
three suites will be on the user's Windows machine.
📄 [`docs/Stride_Host_Visual_Test.md`](../../Stride_Host_Visual_Test.md) — the launch command and what to look at.

## ⛔ What I did NOT do, and why

| ⛔ | why |
|---|---|
| ~~add the twelve members to `EditorSubsystem`~~ | ⚠ **RETRACTED — I DID, after the user challenged it.** See the correction below *(`ST-010`)* |
| ~~port the animation DESCRIPTOR DTOs~~ | ⛔⛔ **RETRACTED — the family was ALREADY HERE.** My "does not exist" came from grepping only `FDP/Toolkits/`. The real gap was one method *(`ST-011`)* |
| adopt Bepu | ⛔ `SD2`, user ruling: **Bullet** |
| wire the MCP harness | out of scope by instruction |
| delete the hosted-editor code | ⛔ *"unreferenced is not unintentional"* — and it is now **built**, not merely preserved |


## ⛔⛔ THE CORRECTION — **`ST-007` was wrong, and the user caught it**

🔒 **User:** *"were they added on the original stride integration branch? what does that have to do
with the UI branch, can't you do it yourself? (UI branch is solving the Details window stuff, largely
orthogonal)"*

⭐⭐ **All three implied claims were right, and I had measured none of them.** 📌 I applied the
file-level lane rule *("no cross-lane files")* **without measuring what the change actually was.**

| 📐 then measured | |
|---|---|
| **① added BY the Stride integration, FOR this** | the branch's own header: *"Public host-integration surface … for external host assemblies … **without reflection**"* |
| **② five of twelve already existed here, as `internal`** | ⇒ the change is **`internal` → `public`** — the branch says *"behavior is identical to the former internal accessors"* |
| **③ the UI lane has no live edit on that file** | `git diff HEAD origin/claude/hrot-implementation-j1jvin -- EditorSubsystem.cs` ⇒ **EMPTY** |

⇒ ⭐ **Ported. Guard removed. Two hosted-mode suites re-enabled.** ⚠ The one behaviour branch
(`MuscleModuleFactory`) keeps its null arm **byte-for-byte**, and ⛔ I did **not** adopt the branch's
`foreach RegisterModule` on the default arm — it would have registered one module more than this line
does.

⭐⭐ **The lesson, stated plainly: "same file" is not "same work."** ⛔ A lane rule about FILES is a
proxy; the real question is what the edit does and whether anyone is standing on it — and both are
measurable in two commands.

### ⚠⚠ `ST-012` — **and the gate that said 209 / 0 was a STALE GREEN**

📐 I recorded `Hrot.Editor.Tests` **209 / 0** during the port gates. ⛔ **That dll predated the
coordinator merge** — nothing in the port touched `Hrot.Editor`, so `--no-build` never rebuilt it. The
moment `ST-010` changed `EditorSubsystem.cs`, the project rebuilt and **`ScenarioMenuTests` went 2 red**.
⭐⭐ **Confirmed pre-existing**: stashed, **rebuilt**, same two failures. ⇒ ⚠ **a `--no-build` green is
only as fresh as the last thing that forced a rebuild** — 📌 second catch of this shape in one session.


## ⛔⛔ AND A SECOND RETRACTION — **`ST-011` was my own false negative**

📌 I reported the `CharacterAnimationDefDto` family as *"does not exist on this line at all"* and
called it a genuine stop. 📐 **It exists, byte-identically** — the descriptor file's diff against the
branch is **empty**. ⛔ **I had grepped only `FDP/Toolkits/`; it lives in `Hrot/Subsystems/`.**

⚠⚠ **That is TWICE in one batch that a design-blocking absence claim was made without the
enumeration to back it** — §6.2's id 265 *(the design's)* and this one *(mine)*. ⭐ Both were caught by
the same move: **look where the thing actually lives before saying it is not there.**

⇒ ✅ The real gap was **one static factory + four call sites + one project reference**, and
`HrotStrideApp.Game.Tests` now builds with **zero exclusions**.
