<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-23
current-answer: dispatch pointer for the StrideMock removal — relocate StrideNodeBootstrapper, then delete
  the mock subsystem, the fake app, the mode token, the InternalsVisibleTo grants and the solution entries.
  ⛔ Carries no design: the measurement is DESIGN_Perspective_Unification.md §1f, the owning design is
  DESIGN_Stride_Port.md.
known-conflict: none. ⚠ It shares ONE file with the perspectives batch — Program.cs's perspectiveMap — and
  that line is deliberately allocated to the OTHER lane (§3). Do not touch it.
-->
# HANDOFF — **StrideMock removal** *(the real Stride port superseded it)*

> 📌 **Dispatched at `89acf0f20`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`ST-`**, tracker **Area I** *(both already exist from the Stride port)* — ⛔ never
> `BP-` *(the perspectives lane, running in parallel)*, never `HN-`/`MX-`/`TM-`. ⭐ **You allocate the ids**
> *(rule 3)* — `S1…S5` are placeholders.

## 0. ⭐ WHY, AND THE ONE THING THAT MUST SURVIVE

🔒 **User, `2026-08-23`:** *"StrideMock is not needed anymore, it was just a temporary submodule used in
place of the real stride. we can remove whole subsystem (unless you find something great it provides we
should keep)."*

⭐⭐⭐ **There IS something, and it is why `S1` comes first:** `StrideNodeBootstrapper` is **used by the REAL
Stride app.** 📐 `StrideHrotGame` holds it *(`:96`)*, exposes it *(`:103`)* and takes it via
`AttachBootstrapper(...)` *(`:266`)*; `Hrot.Stride.Core.Tests/StrideGameReferenceTests` asserts it **is
resolvable**. ⛔ **It is a node composition root, not mock scaffolding** — it wires ModuleHost scheduling,
behavior, combat, gizmos, lifecycle, network spawning, orchestration, scenario, IG and SimHost systems.

📄 **Design basis:** the measurement is **[`DESIGN_Perspective_Unification.md`](../../DESIGN_Perspective_Unification.md) §1f**
*(what depends on StrideMock, and what does not)*; the **owning design is
[`DESIGN_Stride_Port.md`](../../DESIGN_Stride_Port.md)** — ⭐ **fold the as-built there**, not into the
perspective doc.

## 1. ⭐⭐ THE ITEMS — **S1 first, and it is not optional**

| # | task | measured surface | gate |
|---|---|---|---|
| 🔴🔴 **S1** | ⭐⭐⭐ **RELOCATE `StrideNodeBootstrapper`** to a home the Stride app may reference — ⭐ **lean: `Hrot.Common/Infrastructure/`, beside `SharedApplicationBootstrapper`**, whose own comment already says *"eliminating duplication across SimHost, IG, and StrideMock"*. ⛔ **Move, do not copy** | `Hrot/Subsystems/Hrot.StrideMock/StrideNodeBootstrapper.cs` → `Hrot/Engine/Hrot.Common/Infrastructure/` | ⭐ **`StrideGameReferenceTests` must be UPDATED to the new home and still pass** — ⛔ do not delete the assertion |
| ⭐⭐ **S2** | **Delete the mock subsystem and the fake app** — `Hrot.StrideMock` *(`StrideMockSubsystem` · `FakeStrideEntity` · `FakeStrideEffect` · `FakeStrideScript` · `SyncFdpToStrideScript`)*, `Hrot.StrideMock.Tests`, and **`Hrot.FakeStrideApp`** *(+ its `.Tests`)* | the project folders; **`Hrot.ClusterRunner.csproj:48`**'s `ProjectReference`; **4** entries in `IOS-IG-SimHost.sln` | the solution builds with **0 errors** and the project count drops by the number you removed — **state both numbers** |
| ⭐ **S3** | **Remove the `stridemock` mode token** | `HrotRunnerConfiguration.cs` — **4 sites**: the `[Option]` `HelpText` *(`:18`)*, the `validNames` set *(`:115`)*, and **two error-message strings** *(`:118`, `:123`)*. ⚠ **The messages list the valid modes — a stale list is a lie the user reads** | `--mode stridemock` now **throws** with a message that does **not** offer `stridemock` |
| ⭐ **S4** | **Drop the two `InternalsVisibleTo Hrot.StrideMock.Tests` grants** | `Hrot.Common.csproj:27` · `Hrot.Presentation.csproj:27` | both projects still build |
| **S5** | ⚠ **Check the reference guard still says something true** | `Hrot.Stride.Core.Tests/ReferenceGuardTests` asserts `Hrot.Stride.Core` references **no** Raylib/rlImGui/**StrideMock**. ⭐ After removal the StrideMock clause is vacuous | ⭐ **Keep the test; say in the report whether the StrideMock clause is now vacuous or still meaningful.** ⛔ Do not silently drop a clause |

## 2. ⛔⛔ WHAT YOU MUST NOT TOUCH — **the parallel lane owns it**

| ⛔ | why |
|---|---|
| 🔴🔴 **`Program.cs:256` — `["StrideMock"] = "StrideMock"` in `perspectiveMap`** | 📐 **This is the ONLY StrideMock line in `Program.cs`**, and the **perspectives batch is already editing that same dictionary literal** *(it renames `["CGF"]` → `["Scenario"]`)*. ⇒ ⭐⭐ **that lane deletes your line as a one-line courtesy**, so the two batches never conflict. ⛔ **Leave it. If it is still there when you finish, say so in the report — do not fix it** |
| ⛔ `WindowManager`, `FindResultsWindow`, `EditorSubsystem` registrations, layout defaults | the perspectives batch's surface |
| ⛔ `Hrot.MuscleCharacter.Animation.Fake` *(incl. `FakeAnimBackendInspectorWindow`)* and `.Stride` | ⚠ **separate projects, NOT part of StrideMock.** ⭐ The Fake animation backend is a different thing with its own design record *(`DD-Fake`)* — **out of scope** |

## 3. ⚠⚠ THE HONEST GATE PROBLEM — **read before you plan the gates**

⛔⛔ **You cannot build or run the Stride tree on this machine, and that is a PLATFORM limit, not a defect**
*(`ST-006`, measured at the port's base commit)*:

| 📐 | |
|---|---|
| `Stride/*` is **`net8.0-windows`** | compiles here **only** with `-p:EnableWindowsTargeting=true` |
| the suites **cannot RUN** | the test host needs the `Microsoft.WindowsDesktop.App` **runtime**, which has **no linux-x64 build** |
| **`HrotStrideApp.Windows` cannot build at all** | the Stride asset compiler exits **150** — ⭐ **confirmed PRE-EXISTING** |
| ⚠ the nine `Stride/*` projects are **not in `IOS-IG-SimHost.sln`** | ⇒ a green main-solution build says **nothing** about them |

⇒ ⭐⭐ **`S1` is the item this bites.** You are moving a type the Stride app consumes, and **you cannot compile
its consumer here.** ⇒ ⛔ **Do NOT report S1 as verified on a main-solution build.** Instead:
**①** compile `Stride/Hrot.Stride.Core` + `Hrot.Stride.Animation` with `-p:EnableWindowsTargeting=true` and
report the result; **②** report the `HrotStrideApp.Game` reference update as **REVIEWED, NOT COMPILED**, and
say so plainly; **③** name it as **owed a Windows check by the user**.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs the
dispatch sha** · a `--no-build` column · every RED confirmed pre-existing **by name** · goldens as a **diff
shape** · `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · **the `ST-` ids you
allocated** *(rule 5)*.

⭐⭐ **Row 8 — the integration invariant.** This is a **project/reference** change, so the invariant is *"the
runner still composes every mode"*: run `Hrot.ClusterRunner.Tests`, `Hrot.ClusterRunner.Integration.Tests`
`~TimeControlIntegrationTests`, and ⭐ **the harness smoke suite** *(`bash scripts/run-system-tests.sh`)*,
which boots the real binary and would notice a broken composition root.
⚠ **Also state the mode matrix**: `--mode editor`, `--mode all` and `--mode simhost` each still start.

⚠ **Known baseline quirks — do not re-derive:** `tracker-counts.py --check` counts only `**BP-` rows ⇒ **it
is blind to your `ST-` rows**, and its OK is not evidence about them. `Fdp.Presentation.Tests` crashes
~18–20 cases in *(`BP-419`, pre-existing, `R-131`)*. `tools/ai-debug-mcp` `verify.mjs` fails pre-existing
*(needs `npm install`)*.

⭐ **Rule 4/7:** re-sync and **pull the coordinator branch again before your final commit** — ⚠ **the
perspectives batch is running in parallel and will have landed changes to `Program.cs`.**

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Stride_Port.md`](../../DESIGN_Stride_Port.md)** — where
`StrideNodeBootstrapper` now lives and why, and that the mock is gone. ⭐ Also update
**[`DESIGN_Perspective_Unification.md`](../../DESIGN_Perspective_Unification.md) §1b/§1f**'s `stridemock`
rows to say REMOVED-as-built. ⛔ Do not put design content in your report.
