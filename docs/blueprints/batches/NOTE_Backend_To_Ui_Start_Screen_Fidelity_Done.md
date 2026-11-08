# NOTE backend → ui — the Start screen fidelity request, done (CE-3412 rows 1, 2, 5)

Answers [NOTE_Ui_To_Backend_Start_Screen_Fidelity.md](NOTE_Ui_To_Backend_Start_Screen_Fidelity.md). Against `scripts/gen-live-session-storyboard.py` panels ① and ⑧. `StartSurfaceWindow` + `StartRecentRows`; rails `TheStartScreenShowsTheWallpaperTests.CE3414_…` (measured layout + the highlight pixel) and `StartRecentRowsTests.TheJustRunRow_…`.

| # | request | done |
|---|---|---|
| 1 | icons + ellipsis on the three actions | ✅ drawn as *+ New scenario... · folder Open scenario... · history Open recording...* (grey icons, words left-aligned, one width, stacked). Display only — the menu's own labels and bindings are untouched (`StartGateTests` still read *Open Scenario*). The ellipsis is three dots: the UI font has no `…`. *About* is KEPT (the left column has room; an info icon); *Exit* too (a window needs one) |
| 2 | large HROT title; drop the extra line | ✅ title at 1.7 × the body size (the picture's 22 px beside 13); the line *"Open a scenario or a recording to begin"* is no longer drawn in the Actions variant (the waiting / server-lost sentences stay — the picture has no such state) |
| 3 | exercise number | ⛔ SKIPPED — not on the wire (as you said); the sub line stays *Live · 10 min ago* / *never run* |
| 4 | footnote | nothing to do |
| 5 | just-run row highlighted + hint | ✅ the row whose latest run ENDED last, if that was within 15 minutes (`StartRecentRows.JustRunWindow`), has a lighter box and a pale frame; under the list: *"Night attack > AAR = the replay of the latest run, at its start"* (the `▸` is not in the font: `>`; "exercise 3" dropped with row 3) |

What ImGui could not match: the bold weight of the title (scaled, not bold), the proportional font, `…` and `▸`.
