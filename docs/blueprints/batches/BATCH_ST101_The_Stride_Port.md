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
| **`S4`** | Stride visual binding + animation backend on a host | ⚠ **PARTIAL** — standalone host ✅; ⛔ hosted-real-editor mode **cross-lane** *(`ST-007`)* |
| **`S5`** | verify nothing broke + a runnable host | ✅ regression gate green; ⚠ "runnable" is **Windows-only and always was** *(`ST-006`)* |

## ⭐⭐ The port method, and what it actually cost

🔒 Per the handoff: `git checkout origin/stride-integ-1 -- <paths>`, then **let the build surface the
drifts**. 📐 It surfaced **four**, all reconciled measured rather than guessed:

| drift | what it was | fix |
|---|---|---|
| `IRaycastBackend` missing | ⛔ **not an API drift — a design GAP**: a third shared-side file §1 never listed | ported it + its `RaycastSolverSystem` consumer *(`ST-003`)* |
| `CycloneDDS.NET` 0.2.3 → **0.3.2** | needs Roslyn ≥ 4.8.0; `Stride.Core.Assets.CompilerApp` pins **= 3.6.0** *(NU1107 × 3 projects)* | NuGet's own named remedy — pin 4.8.0 directly *(`ST-009`)* |
| 26 errors in `EditorStrideSubsystem` | ⛔ **twelve `EditorSubsystem` members that do not exist here** | guarded, **not deleted** *(`ST-007`)* |
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
| add the twelve members to `EditorSubsystem` | **`R-128` cross-lane** — the UI lane's live file *(`ST-007`)* |
| adopt Bepu | ⛔ `SD2`, user ruling: **Bullet** |
| wire the MCP harness | out of scope by instruction |
| delete the hosted-editor code | ⛔ *"unreferenced is not unintentional"* — it is a **capability**, guarded and one property from returning |
