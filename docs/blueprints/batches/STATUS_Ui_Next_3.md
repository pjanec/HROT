<!--STATUS state: LIVE · updated: 2026-11-11 · current-answer: the table -->
# STATUS — ui, Next_3 ([HANDOFF](HANDOFF_Coordinator_To_Ui_Next_3.md), dispatched at `82ef92069`)

| item | id | state | sha | notes |
|---|---|---|---|---|
| C-0 | CE-4342 … CE-4352 | DONE | (this commit) | ticked 4342 4343 4346 4347 4348 4349 4351 4352 with their Next_2 shas (4344/4345/4350 were already closed by other lanes) |
| C-1 | CE-4356 | DONE | (this commit) | `DeployScreen`: Observing + server ⇒ no centred dialog; the status-bar section draws an eye glyph + *Observing · Deploy…* (SmallButton) ⇒ `StopObserving` ⇒ Choosing ⇒ the dialog. Frame rail `CE4356_…` (dialog absent, entry drawn, click reopens, 0 B steady; the flow's Observing state set by reflection — the wire round trip is not the subject); red-proved (`Visible` for Observing+server restored ⇒ red). The no-server case unchanged. Inventory: search_code/find.sh DeployScreen (graph 5 files, grep 7) |
| C-2 | CE-4357 | IN PROGRESS | — | |
| C-3 | CE-4358 | QUEUED | — | |
| C-4 | CE-4289 rest | QUEUED | — | |
| C-5 | CE-4258 | QUEUED | — | |
| C-6 | CE-4199 | QUEUED | — | |
| C-7 | CE-4130 · CE-4138 | QUEUED | — | |
| C-8 | CE-3330 | QUEUED | — | |
| C-9 | CE-4285 | QUEUED | — | |
| C-10 | CE-4036 | QUEUED | — | |
