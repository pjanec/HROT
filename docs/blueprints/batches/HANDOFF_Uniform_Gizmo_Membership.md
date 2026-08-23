<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-23
current-answer: dispatch pointer — every host declares all six gizmo projector families, and the schema
  pack widens from 5 components to the full 15. ⛔ Carries no design: see
  DESIGN_Uniform_Gizmo_Membership.md, which holds the matrix, the two invariants and the UML.
known-conflict: none. The preview-rewind batch (HN-, Area J) owns PreviewClusterOpHandler and the
  allocator; the net's part C owns Hrot.SystemTests/Goldens. No file overlap (§3).
-->
# HANDOFF — **uniform gizmo membership** *(every host, every family)*

> 📌 **Dispatched at `<STAMP>`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`ST-`**, tracker **Area I** — 📐 the series stands at **`ST-026`**, so start at
> `ST-027`.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`DESIGN_Uniform_Gizmo_Membership.md`](../../DESIGN_Uniform_Gizmo_Membership.md)** — `READY-TO-BUILD`.
⭐ **§1 is the ruling + THE MATRIX** *(read the matrix first — it is the whole reason this exists)*, **§2
the two invariants**, **§3 the five design rules**, **§4 the UML**, **§5 the rails**, **§6 the risks.**
⭐ Report per obligation ③; ⭐⭐ **fold deviations into the design** *(obligation ⑤)* — 📌 your last batch's
Q52 §6 is the standard, keep it.

> 🔒 **The ruling, user `2026-08-23`:** *"replaybrowser is no exception, should use same full set of gizmos
> as everyone else, same philosophy (component presence defines applicable gizmos). same for ig."*
> ⇒ ⭐⭐ **This is UXI-23's gizmo half** *(`docs/UX/UX_Feature_Map_Parity.md` §3.2, ruled `2026-08-10`:
> "all hosts share the FULL set… never set membership")*.

⭐⭐ **Your batch produced this.** `ST-024`'s *"replaybrowser is NOT COVERED"* is what the user ruled on;
⛔ **the answer is not "add replaybrowser to the rail" — it is "every host declares everything."**

## 1. ⭐⭐⭐ THE ITEMS — **`①` BEFORE `②`, and that is not a preference**

| # | task | design | gate |
|---|---|---|---|
| 🔴🔴 **⓪** | ⚠ **FIND `replaybrowser`'S REGISTRATION PATH — do not invent one.** 📐 Your own `ST-024`: a grep for `RegisterComponent<`/`ComponentRegistry` across that subsystem returns **nothing**, yet it **boots** ⇒ it inherits a world registered elsewhere. ⇒ ⭐⭐ **establish where its world comes from and report it** | §3 ④ | ⭐ **the answer, stated** — ⛔ *"I put the call somewhere plausible"* is not one |
| 🔴🔴 **①** | ⭐⭐⭐ **WIDEN `MapSchemaPack` from 5 components to ALL 15**, and call it from **every** host's component-registration phase. 📐 The 15 are enumerated in §1's inventory | §3 ①/③ | ⭐⭐ **every mode still starts** — `ModeStartupRails` **8 / 8**. ⛔ **Land this in its OWN commit and prove it before touching `②`** |
| ⭐⭐ **②** | ⭐⭐⭐ **`MapGizmoPack.RegisterAll(...)` declaring ALL SIX families + `Hrot.Presentation.Gizmos`**, replacing **five** hand-rolled per-host lists *(editor 6 · ig 4 · replaybrowser 3 · simhost 1 · cgf 1)*. ⛔ Home: `Hrot.Common.Diagnostics.Gizmos`, beside `MapSchemaPack` | §3 ② | ⭐⭐ **8 / 8 modes**, and the matrix in §1 becomes **6 / 6 for every host** |
| ⭐⭐ **③** | ⭐⭐⭐ **THE NEW RAIL — invariant `B`: every host declares all six.** ⭐ Extend `GizmoSchemaFollowsDeclarationRails`; ⛔ **do not add a second file** *(invariant `A` already lives there)* | §2, §5 | ⛔⛔ **Assert against the ENUMERATED family set** *(reflection over `[GizmoProjector]` namespaces)*, ⛔ **not a hardcoded six** — ⭐ **a seventh family must FAIL this**, not be silently excluded |
| ⭐ **④** | ⭐⭐ **Prove BOTH rails can fail:** remove one family from `MapGizmoPack` ⇒ `B` reddens **naming the family**; remove one component from `MapSchemaPack` ⇒ `A` reddens **naming the projector** | §5 | ⭐ **report both**, as your `ST-019`/`ST-023` reports did |

## 2. ⚠⚠ WHAT WILL BITE — **measured, so you do not re-derive it**

| ⚠ | |
|---|---|
| 🔴🔴 **ORDER: SCHEMA BEFORE DECLARATION, PER HOST** | ⭐ Phase 2 *(`RegisterDomainComponents`)* before Phase 6d *(the registrars)* — 📄 `MapSchemaPack`'s own doc says so, and 📌 **`ST-020` IS what happens when declaration outruns schema.** ⛔ `②` before `①` kills four hosts in bootstrap |
| 🔴 **FOUR HOSTS GAIN FAMILIES THEY HAVE NEVER DECLARED** | 📐 simhost **1 → 6** · cgf **1 → 6** · replaybrowser **3 → 6** · ig **4 → 6**. ⇒ ⚠ **the blast radius is every subsystem's bootstrap** — that is why `①` lands alone and proves itself first |
| ⭐⭐ **`Common` is the SEVEN-projector family, and simhost + cgf miss it entirely** | ⭐ This independently confirms **UXI-22** *("costing them the selection ring, the map entity context menu, health bars, LOS, vis-cone, spatial grid")* ⇒ ⭐⭐ **expect those to APPEAR on simhost and cgf.** ⚠ **That is the feature, not a regression** — ⭐ say so in the report |
| ⚠ **`Hrot.Presentation.Gizmos` holds ZERO projectors yet all five hosts call it** | ⭐ **keep calling it** *(support all)*; ⛔ **do not "clean it up"** — ⚠ **measure what it DOES register** and state it. 📌 *Unreferenced is not unintentional* |
| ⭐⭐ **your own `ST-023` limit still holds** | 📐 the schema rail's profiles call the registries **directly** ⇒ it is **blind to whether a host WIRES the pack**. ⇒ ⭐⭐⭐ **`ModeStartupRails` is the wiring gate.** ⛔ A green schema rail does not mean the call site exists |
| ⚠ **the editor's INLINE IG registrations** | 📐 `EditorSubsystem.cs:864`/`:868` hand-pick `CullingState`/`VisualEffectState` instead of calling `IgRoleComponentRegistry` *(`ST-024`)*. ⭐ Once `①` covers all 15 they are **redundant** — ⛔ **note it, do not chase it** |
| ⚠ **`Hrot.IG.Tests` has a ROTATING-IDENTITY FLAKY FAMILY** | 📐 **`ST-026`**: four observations gave 6 / 8 / 5 / 4 failures. ⭐ **The four `EntityInfoTranslatorTests.CS011_*` are the STABLE reds**; ⛔ **do not quote a total for that suite** — gate on those four by name, or run it isolated |

## 3. ⛔ LANE & SCOPE

⭐ **Yours:** `Hrot.Common/Diagnostics/Gizmos/` *(`MapSchemaPack`, the new `MapGizmoPack`)* · the five hosts'
registrar call sites *(`EditorSubsystem` · `IgApplication` + `Hrot.IG/Gizmos/GizmoRegistrar` · `SimHostApp` ·
`CgfSubsystem` · `ReplayBrowserSubsystem`)* · `Hrot.ClusterRunner.Tests/GizmoSchemaFollowsDeclarationRails.cs`.

⚠ **TWO OTHER BATCHES MAY BE LIVE:** 📄 `HANDOFF_Preview_Leaves_No_Trace.md` *(`HN-`, Area J — owns
`PreviewClusterOpHandler`, `INetworkIdAllocator`, and **`EditorSubsystem.cs`'s nested allocator + preview
controller**)* · 📄 `HANDOFF_Regression_Net_Part_C.md` *(`HN-`/`MX-`, Area J — owns `Hrot.SystemTests/Goldens`)*.
⚠⚠ **YOU AND THE PREVIEW BATCH BOTH TOUCH `EditorSubsystem.cs`** — ⭐ **different regions** *(they: `:525-560`
the preview controller and nested allocator; you: `:864/:868` and `:1431-1445` the registrars)* ⇒ **a textual
merge should be clean, but ⭐ rule 4: pull the coordinator branch before your final commit and re-run.**
⛔ **Do not touch `ModeStartupRails.cs`'s content** *(it is your file, but the preview batch may add to
`Hrot.SystemTests`)*.

⛔ **Not this batch:** `MapInteractionPack` itself *(UXI-23's actions, selection, rubber-band, layer control —
📄 design §3's closing note: **`MapGizmoPack` is its gizmo half, and the pack should later CALL it**)* ·
the `TagMask` layer filter *(UXI-28)* · `ST-026`'s flakiness · `HN-011`'s loader leak.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs the
dispatch sha** · a `--no-build` column · every RED confirmed pre-existing **by name** ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · **the `ST-` ids you
allocated**. ⭐ **File every id in the same commit that uses it.**

⭐⭐ **Row 8 — the integration invariant.** ⭐⭐⭐ **This change touches EVERY subsystem's bootstrap**, so the
integration gate is **`ModeStartupRails` (all 8 modes)** — name it and run it **after `①`** and again
**after `②`**, ⛔ not once at the end. ⭐ Plus `Hrot.IG.Tests` *(⚠ per `ST-026`, by-name)*, `Hrot.Editor.Tests`,
`Hrot.SimHost.Tests`, `Hrot.CGF.Tests` if it exists, and `Hrot.SystemTests` *(📐 **baseline `57 / 57`**)*.

⭐ **And §6's measurement:** ⛔ *"it is free"* is exactly the claim this programme keeps retracting ⇒
**report the mode-rail startup time before and after**, so *"a projector with no matching entity costs
nothing"* is measured rather than assumed.

⚠ **Known baseline quirks:** `tracker-counts.py --check` is blind to `ST-` rows. `Fdp.Presentation.Tests`
crashes ~18–20 cases in *(`BP-419`, `R-131`)*. `tools/ai-debug-mcp` `verify.mjs` fails pre-existing.
`rulings-check.py` emits **2 staleness WARNs** — ⭐ already named, **not yours**.
⚠ **`mermaid-check.mjs` needs an `npm install` you may not have** — ⭐ your last report handled this
correctly: say it was skipped and whether you added any Mermaid.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Uniform_Gizmo_Membership.md`](../../DESIGN_Uniform_Gizmo_Membership.md)** —
⭐⭐⭐ **especially §1's MATRIX, updated to the post-change state**, which is the fact the next session needs.
⭐ Also answer §6's two open questions *(what `Hrot.Presentation.Gizmos` registers; the runtime cost)*.
⛔ Design content in the design; the report points at it.
