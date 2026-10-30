<!--STATUS
state: LIVE — coordinator → ui: four editor fixes from the user's testing — CE-4111 (the armed-mode indicator disagrees with its menu), CE-4114 (Windows under View, Help as an icon), CE-4115 (the loading splash and the player's wait screen), CE-4112 (the Start screen wallpaper)
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Blueprint_Issues_Tracker.md — CE-4111, CE-4112, CE-4114, CE-4115 (the user's words, the measurements, the done-when — the rows are the spec)
  - ../Architect_Question_108_Run_Mode_Picker.md — D12a / §2.14 (the armed mode, one cluster-wide fact, R-3068), §2.6 (the Start surface, CE-4096)
  - ../../DESIGN_Cluster_Load_Phase.md — §11.7 ④ (the loading screen, CE-1442 / CE-1445) — fold CE-4115's as-built here
  - ../../UX/UX_Feature_Menu_Follows_Focus.md — the top-level order (CE-4085) — fold CE-4114's as-built here
-->
# HANDOFF — coordinator → ui: four editor fixes (indicator, menu, splash, wallpaper)

**Dispatched at** the commit that adds this line (`coordinator`); scope frozen there. Merge `coordinator` first (it carries `ui` and `backend`
as of today); push an empty `chore: started editor-polish at <sha>`. ⭐ **Sonnet** for all four — they are specified; one agent at a time, kept
warm and reused between them; your own model reviews each diff.

🔒 **User, `2026-10-30`:** *"write the handoff for ui fixes - splash screen, wait screen, thr armed mode confusion, windows menu"*

## The items, in this order

| # | id | what | the seam to use (measured in the row) | ⛔ do not |
|---|---|---|---|---|
| 1 | **CE-4111** | opening a recording right after start: the indicator shows **AAR**, its menu has **Preview** checked and **AAR** greyed | ⭐ **measure first** — the row names the two suspects (icon from `ClusterView.ArmedMode`, check mark from a local default; AAR's enable read before the inventory arrives). ⭐ Fix = icon, check mark and enabled state from ONE read of the run choice | a second "sync" between the two readers — remove the second reader |
| 2 | **CE-4114** | `Windows` moves under `View`; top-level segments not on the list go after the rightmost listed menu; **Help becomes the silk question-mark icon only**, flush right | `GlobalMenuRegistry` (`TopLevelOrder`, `FirstTrailingMenu` goes), `WindowManager.MenuIcons` / `TopIcon` | a special path for "unrecognised" items — the ordinary slash-path trie places them |
| 3 | **CE-4115** | the loading screen → a full-window **semi-transparent dark veil** (the live view barely visible), big subtitle-sized centred lines on the golden section (≈ 0.382), a modest Cancel under them (operator only); the **player's wait screen** (CE-1445, same class) gets the same look without Cancel | `LoadingScreen`'s model (`Text` / `CanCancel` / `Cancel()`) stays — only the draw changes; size via `SubtitleBand.TextSizeFor`, veil as in `DirectorAdapters.DrawSubtitle`; a borderless full-display window so it blocks input | the wallpaper here (SUPERSEDED by the user); a second text-size rule; a foreground-draw-list-only layer (it draws but lets clicks through) |
| 4 | **CE-4112** | the Start screen shows `FDP/Data/Icons/HROT-wallpaper.png` behind its menu, scaled to cover, aspect kept | embed like `famfamfam-silk.png` (`Fdp.Presentation.csproj:33`); draw in the ONE `StartSurfaceWindow` | a second copy of the image or a per-host drawer |

## Gates (CLAUDE.md, Rule 8)

- T-1 first: each feature's own suite — `TopLevelMenuOrderTests`, `TheMenuBarIsOneTreeTests`, the CE-1442 / CE-1445 loading-screen rails, the
  CE-4096 Start-screen rails, the run-mode indicator rails; new rails go INTO those suites (each row names them), each red-proved.
- ⭐ All four are display code ⇒ run the frame rails under **`xvfb-run -a`** and report real pass counts (a skip is not a pass).
- One live look per item (the editor over HTTP or a screenshot), named in the report.
- Fold the as-builts: CE-4115 → `DESIGN_Cluster_Load_Phase.md` §11.7; CE-4114 → `UX_Feature_Menu_Follows_Focus.md`; CE-4111 → AQ108 §2.14 if the
  owner of the armed mode on the host changes.

⛔ Still out of scope (user): change indication beyond what is built · CE-4099 autosave · CE-4100 ① · CE-3339 · CE-4095 · CE-4092 · the ExCon
retirement · CE-4105.

_coordinator lane, 2026-10-30_
