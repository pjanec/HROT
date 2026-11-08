<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — ui, the next batch ([HANDOFF](HANDOFF_Coordinator_To_Ui_Next.md), dispatched at `f9bc67abb`)

| item | what | state | sha | notes |
|---|---|---|---|---|
| Z-1 | AAR checkpoint flag tooltip | DONE | `334c3393a` | `RunModeChoice.AarCheckpointReason`; gates 122/0 + 6/0 |
| Z-2 | B-14 rest — U4 + Edit ▸ History | DONE | `88671275b` | the take editor pushes named steps onto the ONE `EditHistory` (GizmoUndoStack untouched, Q6); `EditHistoryWindow` + Edit ▸ History… (mockup panel C + new E). Gates: Presentation 341/0 (Xvfb), Editor.Tests 83/0/1 pre-existing skip, AiShared 27/0. Deviation: menu items draw no glyph (menu tree takes atlas keys only — as Undo / Redo before) |
| Z-3 | A7 — Details tabs Requires / Modes / Level | DONE | `7add2b2ee` | `ActionAvailability` / `ActionOffer` shared by ActionDecl and tabs; `DetailsViewRegistry.Evaluate` (AppliesTo → Requires AND → mode / level → Bound); Rules / Orders / Event registered once on every host; golden `details-tabs.golden.txt` (8 selections × 4 hosts × 4 modes + seats). Gates: AiShared 2281/0 (+9), Editor 839/0, Presentation 1621/0, AiShared Details+Orders under Xvfb 114/0, Integration TheRunChoiceIsOnEveryHost 14/0; Blueprints 4 reds pre-existing at 972df1958. Built leans (design-cited): Orders greyed where no mission facade (D2), Orders level Controller (Editing UX §3.6), Macro Edit+Review only (§3.3); Components tab level left unset |
| Z-4 | H6 — Side-controller seat, Observer camera | IN PROGRESS | — | |
| Z-5 | K2 rest + K3 + K7 | QUEUED | — | follow ANSWERED (R-4169, letters stay on while following) — build fully |
| Z-6 | K5 + K6 | QUEUED | — | |
| Z-7 | K4 | QUEUED | — | |
| Z-8 | L-10 Run setup on the mode menu | QUEUED | — | ⏳ needs backend Y-1 (CE-4326) |
