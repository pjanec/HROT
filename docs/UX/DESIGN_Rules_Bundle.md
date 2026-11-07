<!--STATUS
state: LIVE
build-state: READY-TO-BUILD — every decision is an existing ruling (R-174, R-4131 H5, R-4133 Q2, R-4119, R-4128); no open question
updated: 2026-11-07 (first version — the user: "How can be something built just for an editor when the bootstrap is unified and shared and features are available everywhere by role and suppressed just by feature settings or role settings?" — CE-4292)
current-answer: §1 what is there · §2 the design (diagrams) · §3 rules · §4 stages
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - DESIGN_Rule_Editor_UX.md — OWNS what the rule editor shows (RX1–RX6, the List view, the Details tabs); this design only makes every host compose it the same way
  - DESIGN_Seats_And_Hats.md — H5 OWNS "the Instructor seat's windows on the fused server and CGF (rules, game master, End)"; this is how H5 is built
  - DESIGN_Edit_Framework.md — OWNS the one write path (`Edits.Submit`, R-4128) the shared factory's writes go through
  - DESIGN_Edit_Mode_Undo.md — Q2 (R-4133): undo only in the editor's Edit mode — so the history is passed only where Edit mode exists
  - ../DESIGN_Subsystem_Composition_Unification.md — §5b OWNS `IUiBundle` (windows, panels, menus, toolbar entries — never a module or translator)
  - ../DESIGN_Rules_View_UI.md — the Rules window as first built (R2 / R5a / R5b), whose three compositions this replaces
-->

# DESIGN — the rules windows as ONE bundle every host composes; editing decided by the seat, not the host

## 0. Why

🔒 *"How can be something built just for an editor when the bootstrap is unified and shared and features are available everywhere by role and
suppressed just by feature settings or role settings?"* (user, `2026-11-07`) — the ui lane had reported CGF cannot edit rules because *"the
editor's composition is editor-specific"*. Measured: that is a missing unification, not a necessity (CE-4292).

## 1. What is there *(graph + grep, `2026-11-07`)*

Queries: `search_graph ".*(Bundle|Rule(s)?(Window|Details|Editor|Services|Commands|Bundle)).*" Class` (bundles: `EntityDetailsBundle`,
`DiagnosticsWindowsBundle`, `ShellCommandCoreBundle`, `AiEditorAdapterBundle`; no rules bundle) · grep for each rules-family window's
construction and for `RuleEditorServices` / `EntityAiEditModel`. ⚠ Coverage could not be checked.

| what | where | ⇒ |
|---|---|---|
| `RulesWindow` built by hand **three times** | `EditorSubsystem.cs:5004` · `CgfSubsystem.cs:1350` · `ReplayBrowserSubsystem.cs:961` | the composition is duplicated (R-174) |
| Mission summary, Trainee debrief, Game summary built in **Editor and Replay Browser** only; Game master in **Editor** only | the same subsystems | CGF / the fused server lack the instructor's windows (H5) |
| `RuleEditorServices` (the editing half) built only by the private `EditorSubsystem.BuildRuleEditorServices()` | `:293-322` (+ `BuildMacroPreviewServices :328`, `BuildRuleLiveServices :360`) | CGF passes none ⇒ read + control only |
| its edit-mode apply `EntityAiEditModel.ApplyTask` lives in the **editor subsystem's own assembly** | `Hrot.Editor/Scenario/EntityAiDetailsView.cs:28, :188-207` | CGF does not reference it — ⭐ the one real obstacle, and it is a file location |
| the other pieces are already shared | `AiFacetEditService` (`Hrot.Editor.AiShared`), `BrainSnapshot`, `EditHistory` (`Hrot.Presentation.Edits`), `MapPickBroker`, the pickers | nothing else is editor-only |
| the shared shape to copy | `EntityDetailsBundle : IUiBundle` + `EntityDetailsHostServices`, composed by Editor, CGF, Replay Browser, SimHost, Player (`UiBundleHost.Compose`) | ⭐ the precedent |
| the seat | `station.seat` (`StationSeat.Of(features)`, B-15 built): editor = Author, fused server = Instructor, Player = Player, Replay Browser = Reviewer | who may edit is a SEAT fact, not a host fact |

## 2. The design

```mermaid
classDiagram
  direction LR
  class IUiBundle { <<existing · Fdp.Presentation>> Compose(UiBundleContext) }
  class EntityDetailsBundle { <<existing — the precedent>> }
  class RulesBundle {
    <<★ new · Hrot.Editor.AiShared>>
    registers: Rules · Mission summary · Trainee debrief · Game summary · Game master
  }
  class RulesHostServices {
    <<★ new record>>
    World · TimelineSource · Cursor · IdPrefix · Perspective · Colour
    SelectEntity · ShowOnMap · Blocked?
    Seat · EntityCreation? · MacroExpander? · TimeTransport? · Pickers? · MapPicks? · History?
  }
  class RuleEditorServicesFactory {
    <<★ new · Hrot.Editor.AiShared>>
    Build(RulesHostServices) RuleEditorServices?
  }
  class RuleEditorServices { <<existing record · Hrot.Presentation>> }
  class EntityAiEditModel { <<existing · MOVED Hrot.Editor → Hrot.Editor.AiShared>> ApplyTask · ApplySop }
  class RulesWindow { <<existing>> }
  class GameMasterWindow { <<existing>> }
  class StationSeat { <<existing · B-15>> }
  IUiBundle <|.. RulesBundle
  IUiBundle <|.. EntityDetailsBundle
  RulesBundle --> RulesHostServices
  RulesBundle --> RuleEditorServicesFactory
  RuleEditorServicesFactory --> RuleEditorServices : null when the seat may not edit
  RuleEditorServicesFactory --> EntityAiEditModel : the edit-mode apply
  RuleEditorServicesFactory --> StationSeat
  RulesBundle --> RulesWindow
  RulesBundle --> GameMasterWindow
```

*What the picture shows that the prose hid: the only thing standing between CGF and rule editing is ONE class's file location
(`EntityAiEditModel`); everything else the editor uses is already shared — so the fix is a move plus a bundle, not new machinery.*

```mermaid
sequenceDiagram
  participant H as a host (Editor / CGF / Replay Browser)
  participant B as RulesBundle
  participant F as RuleEditorServicesFactory
  participant S as StationSeat
  participant W as RulesWindow
  H->>B: Compose(new RulesHostServices(world, cursor, seat, entityCreation, ...))
  B->>F: Build(host services)
  F->>S: may this seat edit rules? (Author, Instructor)
  alt allowed and not Blocked
    F-->>B: RuleEditorServices - writes through Edits.Submit, History only with Edit mode
  else any other seat or a recording
    F-->>B: null - the windows read and control as today
  end
  B->>W: new RulesWindow(..., editor) and the four other windows
```

```mermaid
graph TD
  E["Editor (seat Author)"] --> RB["RulesBundle"]
  C["CGF / fused server (seat Instructor)"] --> RB
  R["Replay Browser (seat Reviewer, Blocked = a recording)"] --> RB
  SH["SimHost"] -. "does not compose it" .-> RB
  P["Player (seat Player)"] -. "does not compose it" .-> RB
  RB --> W1["Rules · Mission summary · Trainee debrief · Game summary · Game master"]
  classDef dead stroke-dasharray: 4 4
  class SH,P dead
```

*Called each frame by the window manager like every bundle's windows; SimHost and the Player compose no rules windows (no seat that runs an
exercise sits there) — the dashed edges are deliberate, not a gap.*

## 3. Rules

| ⭐ rule | basis |
|---|---|
| **one composition**: the three hand-written `RulesWindow` sites and the Editor / Replay Browser summary windows go into `RulesBundle`; ids stay (`<prefix>rules`, …) | R-174 · Subsystem composition §5b |
| **who may edit = the seat** (Author, Instructor edit; everyone else reads and, where today, controls) — ⛔ no `if editor` anywhere | R-4131 (seats) · H5 |
| **writes through the one path** (`Edits.Submit`, B-2) — the live edit and the edit-mode apply both | R-4128 |
| **undo only where Edit mode exists**: the host passes its `EditHistory` (the editor); others pass none ⇒ no undo entries, the menu greyed with the reason | R-4133 Q2 · R-4119 |
| **a capability the host has, it passes** (entity creation, macro expander, time transport, pickers, map picks) — a forwarding rail per dependency, asserted on the constructed window | the silent-default rule (CLAUDE.md) |
| **a capability the host lacks** ⇒ that action greyed with its reason (*"this host cannot expand macros"*), never hidden | R-4119 |
| **a recording** ⇒ `Blocked = "a recording is read-only"` and no editor services; *End exercise* not drawn (R-4156) | R-4148 |
| no new look: the windows are the ones the rule editor designs already draw (`rule-list-visual.svg`) | R-4151 — this changes composition only |

## 4. Stages

| # | what | acceptance |
|---|---|---|
| **R1** | move `EntityAiEditModel` (+ its `EntityAiDetailsView` if it must travel with it) to `Hrot.Editor.AiShared`; the editor's AI section unchanged | the AI section's own suite green; nothing else moved |
| **R2** | `RulesHostServices` + `RuleEditorServicesFactory` (the body of `BuildRuleEditorServices` / `BuildMacroPreviewServices` / `BuildRuleLiveServices`, host pieces passed in, seat-gated) | a rail per seat: Author / Instructor get services, Reviewer / Player / Observer / Side controller get null |
| **R3** | `RulesBundle` registering the five windows; Editor, CGF and the Replay Browser compose it; the hand-written sites deleted | the window-set golden per host: Editor and Replay Browser unchanged; CGF gains Mission summary, Trainee debrief, Game summary, Game master (H5) |
| **R4** | a CGF live edit end to end | from CGF's Rules window a rule's param changed live reaches the owner and the journal says Applied (`RuleEditorR5bTests` + one cluster rail); a forwarding rail per dependency on CGF |
