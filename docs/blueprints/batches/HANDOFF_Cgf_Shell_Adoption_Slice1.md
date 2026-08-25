<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-25
current-answer: dispatch pointer for cgf==editor SLICE 1 — CGF constructs the AiShared shell and registers
  the asset-perspective windows (watch · MyBlueprint · graph canvas · breakpoints · inspector), proven
  headlessly editor-vs-cluster via the conformance suite. Carries NO design: cites
  DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md (classDiagram + sequenceDiagram + the test method).
known-conflict: ⚠ AMENDED BY STEER 2026-08-25 — STEER_Cgf_Shell_Adoption_Slice1.md supersedes the
  "read/diagnostics only" framing (§1 NOT-row "asset editing"; §5 item ③): take the windows WHOLESALE
  incl. their native editing; do not artificially gate. Everything else here stands.
  Coordinator is JOINED with both lanes at the dispatch sha (94g + HN-037 all merged). This slice CONSUMES
  Hrot.Editor.AiShared and must NOT modify it (freeze owner = the variable-model lane).
-->
# HANDOFF — **cgf==editor slice 1: CGF adopts the AiShared shell** *(CGF / backend lane)*

> 📌 **Dispatched at `df8efa938`** — a JOINED coordinator state *(94g and HN-037 both merged; both lanes are
> in)*. ⛔ **Scope FROZEN at that sha.** ⭐ **Branch fresh from `claude/blueprint-authoring-status-6sr5ld`**
> *(rule 7)*; **rule 1b: push an empty started-marker naming `df8efa938` BEFORE any code.** ⛔ **No PR.**
> ⭐ **You allocate the ids** *(rule 3)* — a **`CE-`** prefix for the cgf==editor programme keeps it clear of
> `BP-`/`HN-`/`TM-`; state every id you allocate *(rule 5)*.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER
📄 **[`DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md`](../../DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md)**
*(LIVE, READY-TO-BUILD)* — **§2** the verified construct diff, **§3** the `classDiagram`, **§4** the
`sequenceDiagram`, **§5** the items, **§5b** the headless test method, **§6** the gates. ⭐ Build what §3/§4
draw and **report the match** *(obligation ③)*; fold any deviation back into the design *(obligation ⑤)*.
📄 Context: [`PROGRAMME_Unification_And_Harness.md`](../../PROGRAMME_Unification_And_Harness.md) *(charter,
Step 4)* · [`PROGRAMME_Cgf_Equals_Editor_Gap_Map.md`](../../PROGRAMME_Cgf_Equals_Editor_Gap_Map.md) §0.5
*(this slice has NO open design — it is wiring)*.

## 1. ⛔⛔ NEW BUILD/TEST RULES APPLY
📄 `.claude/CLAUDE.md` → THREE TEST TIERS → the `2026-08-24` rule. ⛔⛔ **Never `dotnet build <the.sln>` in
the fix loop** — build the AFFECTED PROJECT *(`Hrot.CGF` + the touched test projects; 8 s vs 115 s)*.
⛔ **The E2E/system suite is T3 — ASYNC, never a foreground blocker.** ⭐ Prove each fix through the rail
that reddens for it; ⛔ do NOT re-run the whole suite "to be sure."

## 2. ⭐⭐⭐ WHAT TO BUILD *(all in the design §5)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Construct the shell in `CgfSubsystem`**, mirroring `EditorSubsystem` `:2545-2948`: `WindowManagerPerspectiveSwitcher` · `AssetCatalog` · `PerspectiveWorkspaceServices` · `AiDocumentManager` | ⛔ **construct only — do NOT modify AiShared.** ⚠ supply CGF's REAL `facetEditService` + `isSimUp`/`isFrozen` clock signals — ⛔ never a silent default *(SILENT-DEFAULT rule)* |
| ⭐ **②** | **`CreateRegistrar` per asset perspective** *(Scenario/BTree/HSM/Blueprint)* and register the windows *(graph canvas · MyBlueprint · watch · breakpoints · inspector)* under `OwningPerspective` | ⭐ perspectives are **emergent** from registration; pass **null** `liveValueProvider`/`writeLive` for BTree/HSM *(honest, per the signature)*. ⛔ don't add a `CGF` perspective — D1 rules the asset perspectives |
| ⭐ **③** | **Flip the capability-manifest cells** absent→present for the registered READ/diagnostics endpoints; shrink the `known-absent` baseline by exactly those | ⛔ read/diagnostics only — leave write/edit cells absent *(charter D3)* |
| ⚠ **④** | confirm `perspectiveMap` maps `Scenario → CGF` *(`DESIGN_Perspective_Unification.md` D1)* | — |

## 3. ⭐⭐⭐ HOW TO TEST — **capture editor goldens FIRST, then diff CGF via MCP** *(design §5b)*
✅ **The suite is LANDED and is your acceptance vehicle:** `Hrot.SystemTests/Conformance/ClusterConformanceRails.cs`
runs the **same binary in two modes, diffed by `PanelKind`**, three-way *(SAME / DIFFERENT / NOT-PRESENT)*
plus the fourth *"DIFFERENT BY DESIGN"*; `--mode all` answers MCP via `PerspectiveScopedDispatcher`
*(`ClusterRunner/Program.cs`)*; `GET /panels` reads `PanelSnapshot`. ⭐⭐ **The windows are ALREADY
`PanelSnapshot`-instrumented in AiShared** ⇒ constructing them on CGF makes them publish **for free**.

| step | do |
|---|---|
| **T0** | **Confirm the conformance suite is GREEN at the dispatch sha** *(baseline)* before you touch anything — name the base result. |
| **T1** | **Capture editor-mode goldens** for the asset panels *(MyBlueprint · graph canvas · watch)* per `PanelKind` if the net does not already carry them — the reference *(charter job ①)*. |
| **T2** | Build §2. |
| **T3** *(acceptance)* | Run the conformance suite **editor-mode vs `--mode all`** ⇒ **`SAME` per `PanelKind`** for the registered panels *(charter job ④)*. ⚠ It is **model-level, not pixels**. |
| **T4** | The editor goldens also guard the editor did not regress *(job ③)*. |

⭐⭐⭐ **YOU MAY EXTEND THE HARNESS / MCP** *(user, `2026-08-25`)* — if a panel is not reachable over MCP, a
`PanelKind` is not captured, the conformance suite lacks a case, or `--mode all` does not expose what the
test needs, **build it**: add the `GET /panels`/route surface, the `ClusterConformanceRails` case, the
golden, the `PanelSnapshot` registration. ⛔ **Do NOT fake a pass by narrowing the diff** — extend the
capability, and ⚠ if the extension touches AiShared internals, STOP and coordinate with the variable-model
lane *(§4)*.

## 4. ⭐ LANE, SCOPE & COLLISION
⭐ **Yours (CGF/backend lane):** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` *(the composition block)* ·
`ClusterRunner/Program.cs` if the manifest/dispatch needs a cell · `Hrot.SystemTests/**` *(goldens + the
conformance case)* · the capability-manifest baseline.
⛔⛔ **DO NOT modify `Hrot.Editor.AiShared`** — it is the freeze owner's *(variable-model lane)*. You
CONSTRUCT + REGISTER its windows; if a construct needs an AiShared change *(a ctor overload, a new
registration hook)*, that is a **STOP-and-coordinate**, not an edit. ⚠ Both lanes are merged at the dispatch
sha, so no live collision — but **re-pull coordinator before your final commit** *(rule 4)*.
⛔ **Not this slice:** asset EDITING/hot-reload writes · map/entity parity *(Axis B)* · new authoring
features *(AQ25-A/B/C/E)*.

## 5. GATES *(rule 8 contract)*
One row per gate · verbatim command · pass/fail/skip · **delta vs `df8efa938`** · `--no-build` column ·
every RED pre-existing **by name** · goldens as a diff shape · `tracker-counts.py --check` ·
`rulings-check.py` · `design-digest.py --check` · **the `CE-` ids allocated**.
⭐⭐ **Row 8 — the rails that prove it:**
- ✅ **the headline:** conformance **`SAME` per `PanelKind`** for graph canvas · MyBlueprint · watch, editor-vs-`--mode all` *(shown RED by reverting the registration)*.
- a rail: `WindowManager.GetPerspectives()` on `--mode all` **includes** the CGF asset perspectives.
- a rail: the flipped **manifest** cells report present on CGF and `known-absent` shrank by exactly those.
- ⛔⛔ **name + run the integration/conformance suite** that exercises the `--mode all` panels *(`ClusterConformanceRails`; run filtered if flaky, or state with base-sha evidence why it cannot gate)*.

## 6. ⭐ WHEN DONE
⭐⭐ **Fold the as-built into [`DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md`](../../DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md)**
*(the construct block as built · per-perspective null decisions · the manifest cells flipped · any harness/MCP
extension)*, and flip the gap-map §2 Axis-A rows from 🔌 to ✅ for the windows landed. ⭐ State the `CE-` ids;
⛔ design content in the design, the report points at it. ⭐ Report per obligation ③: *"§3 carries N classes,
§4 M sequences; built matches / deviates HERE."*
