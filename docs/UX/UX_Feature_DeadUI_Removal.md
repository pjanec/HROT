# Feature design — Removing superseded UI, and the namespace that lies

> **Design for [UXI-01](UX_Issues.md#uxi-01) 🔴 · drafted 2026-08-10 · needs no architect round.**
> Evidence: [UX_Current_UI_Architecture.md §5](UX_Current_UI_Architecture.md#5-dead-weight-inflating-the-apparent-shared-surface).
> **Status: ✅ designed — ready to break into `UXT` tasks.**

## 0. Prior art — ✅ re-verified 2026-08-10 against the [Seam Inventory](UX_Seam_Inventory.md)

**This design deletes, so the prior-art question runs backwards: is anything being deleted load-bearing?**

> ### ✅ **All 20 public types declared in `Hrot.UI.Common` have a twin. Zero would break on delete.**
>
> | | |
> |---|--:|
> | Twin in `Hrot.Presentation` | **19** |
> | Twin in **`Hrot.Core`** — ⚠ `MissionCommitResult`, *not* where the rest live | **1** |
> | **No twin, and has production consumers** | **0** |

⚠ **Scope of the check:** `public` types only. Non-public types, partial-class members and non-code
assets are not covered — confirm the build after the delete, as the design already requires.

⚠ **Two twins are now load-bearing for [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)** —
`Menus/SharedContextMenuPopulator.cs` and `Facades/IEntityActionController.cs`. In both pairs the
`Hrot.Presentation` file is the live one. See the trap below.

## 0b. ⭐ Two more dead files, found 2026-08-10 — **outside** the `Hrot.UI.Common` sweep

| File | Evidence |
|---|---|
| `Hrot.Editor/UI/EditorOrbatPanel.cs` (27 L) | constructed at `EditorSubsystem.cs:1559`, **field read nowhere** (not even nulled in teardown) |
| `Hrot.Editor/Windows/EditorWindows.cs` — `EditorOrbatWindow` | **never registered**; `new EditorOrbatWindow` has **0 hits** repo-wide |

The Editor's live ORBAT is `SharedOrbatPanel` + `EditorOrbatAdapter`, registered at `:3580`.

> 🔴 **This is the file [Correction 5](UX_Tasks_Detail.md#corrections) was generalised from.** The
> programme's stated root cause — *"no right-click affordances on objects"* — came from
> `EditorOrbatPanel`, which **never reaches the screen**. Not merely over-generalised from one file:
> generalised from a **dead** one.

⇒ Add both to the delete list. They live in `Hrot.Editor`, so the 20-file `Hrot.UI.Common` sweep above
does not reach them. Found by [UXI-04](UX_Feature_Cross_Surface_Actions.md)'s prior-art pass.

## 1. What we are changing, and why

**Not "tidying up".** One specific hazard: a developer told to *"fix the shared ORBAT panel"* has even
odds of editing a file that **compiles into nothing**.

| Fact | Evidence |
|---|---|
| `Hrot.UI.Common` is referenced by **no `.csproj` and no `.sln`** — it never builds | verified: `grep` for the `.csproj` across all projects and for the name across all `.sln` both return empty |
| Yet **27 files inside `Hrot.Presentation`** declare `namespace Hrot.UI.Common.*` | `Panels/`, `Facades/`, `Menus/`, `Models/`, `Adapters/` |
| The two copies have **already drifted** | `SharedOrbatPanel` differs by a `vehicleId` local and reworded docs; the **`Hrot.Presentation` side is newer** |

⇒ Navigating by namespace lands you in the dead copy. **This is the trap, and it is the whole point of
doing this first** — every later stage edits these files or their live twins.

> ### 🔴 The trap fired, on this programme, before the deletion landed — 2026-08-10
>
> A scan reported that ExCon uses the **`Hrot.UI.Common`** copy of `SharedContextMenuPopulator` and that
> the `Hrot.Presentation` copy has no caller. **Backwards.** Both files declare
> `namespace Hrot.UI.Common.Menus`, so the namespace cannot disambiguate them — only
> `Hrot.ExCon.csproj:15`, which references **`Hrot.Presentation` alone**, can. Acting on the report would
> have deleted the **live** file. [Corrections 14](UX_Tasks_Detail.md#corrections).
>
> 🔒 **Rule while the twins exist: resolve a duplicated type by *project reference*, never by namespace.**
> Two of the twins are now load-bearing for [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) —
> `Menus/SharedContextMenuPopulator.cs` and `Facades/IEntityActionController.cs`. In both pairs the
> **`Hrot.Presentation` file is the live one** and the `Hrot.UI.Common` file is among the 20 being deleted.

## 2. ⭐ The design decision: delete now, rename later (or never)

The obvious instinct is *"delete the dead project **and** rename the namespace so it stops lying."*
**Measured, those are two very different changes:**

| | Delete the dead project | Rename the namespace |
|---|---|---|
| Files touched | **20** (all deleted) | **~87** — 27 declarations + ~60 consumers |
| Projects touched | none — nothing references it | Editor, ExCon, IG, SimHost, `Hrot.Presentation`, **+4 test projects** |
| Co-owned files? | no | **yes** — `EditorSubsystem.cs` ([SHARED_SURFACES](SHARED_SURFACES.md)) |
| Risk | ~zero | mechanical but wide; conflicts with parallel sessions |
| Removes the hazard? | ✅ **yes, completely** | only cosmetic once the project is gone |

> ### The finding that settles it
>
> **Once the dead project is deleted, the namespace no longer *lies* — there is nowhere wrong to
> navigate to.** `Hrot.UI.Common.Panels` living inside `Hrot.Presentation` becomes merely **inaccurate**,
> not **hazardous**. The urgency collapses entirely.

⇒ **This feature deletes. The rename becomes a separate, optional issue** — cheap to do later during
any wide touch of these files, and never on the critical path.

⚠ **Do not bundle them.** A 20-file deletion is reviewable in one sitting and revertible in one command;
an 87-file rename is neither, and merging the two makes the safe change carry the risky one's risk.

## 3. Scope

### In — delete entirely

| Item | Size | Why it is safe |
|---|--:|---|
| `Hrot/Engine/Hrot.UI.Common/` (whole project, 20 files) | 1,171 L | In no `.csproj`, no `.sln`. Cannot be referenced |
| `Hrot.ExCon/Panels/InspectorPanel.cs` + `DataMonitorPanel.cs` | 435 L | `[Obsolete]`, *"retained only for reference"*; zero non-test instantiations. ExCon uses `DerEntityInspectorPanel` |
| `Hrot.Editor/UI/EditorOrbatPanel.cs` + `EditorOrbatWindow` | ~40 L | Constructed at `EditorSubsystem.cs:1559`, **never registered**. `EditorSharedOrbatWindow` is the live one |
| `Hrot.Editor/UI/EntityPropertyInspector.cs` | 48 L | Never instantiated |

**Total ≈ 1,700 lines.**

### Out — explicitly not in this feature

| | Why |
|---|---|
| The namespace rename | §2 — separate issue, no longer urgent |
| `ScenarioEditorModule`, `SelectionRenderSystem`, `WorkspaceMenuBuilder`, `EditorTool.Select` | **half-built, not superseded** — each encodes an intent. [UXI-02](UX_Issues.md#uxi-02) |
| Any behaviour change | this feature must be a **no-op at runtime** |

⚠ **Deleting `Hrot.UI.Common` is not a verdict on the idea it encodes.** A neutral shared-UI project may
well be the right destination for [UXI-03](UX_Issues.md#uxi-03)'s descriptors. We remove a **stale copy**,
not the intent — and §2's rename issue is where that intent would be revisited.

## 4. How we prove each deletion is safe

**Per item, before deleting** — the codebase's own trap #8 is *"an optional dependency that looks wired
and is not"*, so absence of a reference is the thing to establish, not assume:

| Check | Command shape |
|---|---|
| No `ProjectReference` | grep the `.csproj` name across all `.csproj` |
| Not in any solution | grep the project name across all `.sln` |
| No construction site | grep `new <Type>(` outside the type's own file and its tests |
| Not registered as a window | grep `new <Window>(` and `RegisterWindow` |
| Tests that reference it are deleted **with** it | grep the type across `*Tests*` |

🔴 **`EditorOrbatPanel` is the one to be careful with** — it *is* constructed (`EditorSubsystem.cs:1559`)
but its window is never registered. Deleting the class means also removing that construction line. **The
construction site is the evidence it looks alive; the missing registration is the evidence it is not.**

## 5. Acceptance

| | |
|---|---|
| **Build** | Solution builds with zero new warnings. `TreatWarningsAsErrors=true` is on, so an orphaned `using` fails the build — a useful ratchet |
| **Gates** | Every suite green, including `Hrot.ExCon.Tests` (heaviest consumer of the affected namespaces) and `Hrot.Presentation.Tests` |
| **Behaviour** | 🔒 **Nothing changes at runtime.** Launch `--mode editor`, `--mode simhost`, `--mode excon`: identical windows, identical menus |
| **The trap is gone** | Grep `Hrot.UI.Common` → hits only in `Hrot.Presentation` namespace declarations. No file outside a `namespace` line resolves to a non-building project |
| **Revert check** | Not applicable — this is a deletion with no new behaviour to red-test. The gate *is* the build plus the suites |

## 6. Risks

| Risk | Mitigation |
|---|---|
| A deletion is not as dead as measured | The §4 checks per item; `TreatWarningsAsErrors` catches orphaned usings at compile time |
| A parallel session is mid-edit in one of these files | They are dead — nobody should be. But announce via [SESSION_SYNC](../SESSION_SYNC.md) before pushing |
| Reviewer cannot tell deletions from moves | **Deletions only, no moves in the same commit.** One commit per group in §3 |
| Someone later "restores" the dead project | The [SESSION_SYNC facts table](../SESSION_SYNC.md) and [Trap U3](UX_RESUME.md#5-traps) both record why it went |

## 7. Shape of the implementation tasks

*Not the tasks themselves — those get cut into [UX_Tasks_Detail.md](UX_Tasks_Detail.md) once this design
is agreed.* Expect **four**, one per §3 row, each independently revertible, in this order:

1. `Hrot.UI.Common` (largest, zero risk — nothing can reference it)
2. ExCon's `[Obsolete]` pair (+ their tests)
3. `EditorOrbatPanel` + window + **the construction line at `EditorSubsystem.cs:1559`**
4. `EntityPropertyInspector`

⚠ Task 3 touches `EditorSubsystem.cs`, which is **co-owned** — a one-line deletion, but it goes through
[SHARED_SURFACES](SHARED_SURFACES.md) like any other edit there.

## 8. Open questions — ✅ both resolved by the user, 2026-08-10

| | Decision |
|---|---|
| File the namespace rename as its own `UXI`? | ✅ **Filed as [UXI-20](UX_Issues.md#uxi-20), `P2`** — so it is not rediscovered as a surprise, and never blocks anything |
| Delete the tests for deleted types? | ✅ **Delete them.** A test for a type nobody constructs asserts nothing about the product. They go in the same commit as their subject |
