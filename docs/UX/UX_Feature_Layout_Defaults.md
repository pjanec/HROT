# Feature design — the shipped default layout

> **Design for [UXI-08](UX_Issues.md#uxi-08) · drafted 2026-08-10.** Implements
> [UXR-04](UX_Requirements.md#uxr-04). **Status: ✅ designed — ready to break into `UXT` tasks.**
>
> Specified by the user, 2026-08-10: *"a main menu item to save the current setting as the default; and
> during the development stage — practically always now — auto-revert the user setting to the repo
> committed default on each new run, i.e. copy the default to the user folder force-overwriting whatever
> was there, so ImGui loads the user copy as usual but reset to default, and on exit save to the user
> folder as now."*

## 0. Prior art ([rule 6](UX_Issues.md#rules))

| Exists? | What | Bearing |
|:--:|---|---|
| ❌ | Any settings-path seam (`SettingsPath`, `IniPath`, layout-profile type) | **the gap** — `SetupImGui()` takes no parameter |
| ⚠ | A **283-line `imgui.ini` committed at the repo root** | 🔴 **an accident, not a default** — see below |
| ✅ | `WindowManager.SaveSettings()` / `LoadSettings()` round-trip (`:368-411`) | **reused unchanged** — this design only decides *which file* is there at startup |
| ⭐ | `ResolveAiBehaviorsDir` walks up from `BaseDirectory`/`CurrentDirectory` to find the source tree (`EditorSubsystem.cs:693-708`) | **the precedent for locating the repo from a running exe** — needed by "save as default" |

### 🔴 The committed `imgui.ini` is contradictory state — do not repurpose it

| Fact | Evidence |
|---|---|
| It is **tracked** | `git ls-files` returns it; 283 lines of real window geometry |
| It is **also `.gitignore`d** | `.gitignore:54` — so it predates the ignore rule and nothing curates it |
| **No build-copy rule** | no `.csproj`/`.props`/`.targets` mentions it — it never reaches the output directory |
| **The app never reads it** | `SetupImGui` hardcodes `%LocalAppData%\HROT\imgui.ini` ([Correction 4](UX_Tasks_Detail.md#corrections)) |
| Last touched by a **docs** commit | `877fc7c` |

⇒ **Delete it** and introduce a deliberately-named, deliberately-located asset. Leaving a tracked-but-ignored
file that looks like a default is worse than having none.

## 🔴 The finding that widens the issue: layout state lives in **two** files, in **two** roots

| File | Holds | Root **today** |
|---|---|---|
| `imgui.ini` | docking geometry, window positions/sizes | `%LocalAppData%\HROT\` (`RaylibPresentationShell.cs:128-131`) |
| `fdp_windows.json` | **open/closed state, active perspective, UI scale** | ⚠ `AppDomain.BaseDirectory` — *next to the exe* (`WindowManager.cs:437-438`) |

> ⇒ **Resetting one without the other gives a half-reset**: default geometry with your old windows open,
> or vice versa. 🔒 **The two must be treated as one unit — "the layout".** The issue as filed named only
> `imgui.ini`.

### 🔒 RULED by the user, 2026-08-10 — one directory, both files, both places

> *"The json must live next to the `imgui.ini` — **both places** (user and default)."*

| | Location |
|---|---|
| **User** | `%LocalAppData%\HROT\` — `imgui.ini` **+ `fdp_windows.json`** |
| **Default** | the shipped asset directory — both files, side by side |

⇒ The split root disappears, and with it the *"a clean rebuild wipes your layout"* hazard that this design
previously only flagged.

### ⭐ And the seam for it **already exists, unused**

```csharp
public void    SaveSettings(string? filePath = null)   // WindowManager.cs:368
public string? LoadSettings(string? filePath = null)   // :388
       filePath ??= DefaultSettingsPath;               // :370, :390
```

**Both already take a path. Nobody passes one** — `LocalWindowController.cs:75,94` call them bare, so the
`BaseDirectory` fallback wins by default.

| | |
|---|---|
| ✅ **No `WindowManager` change** | pass the path at the two existing call sites |
| ✅ **Constraint 1 satisfied for free** | `Fdp.Presentation` never learns what "HROT" is — the **ClusterRunner** owns the directory and injects it |
| ⚠ `DefaultSettingsPath` becomes production-dead | it stays as the parameterless fallback; note it, do not chase it |

⚠ **One-time migration:** on first run, if the new path has no `fdp_windows.json` **and** the old
exe-adjacent one exists, copy it across. Cheap, and it stops existing users silently losing their
arrangement.

⚠ **And the ini path is duplicated** — `RaylibPresentationShell.cs:131` and `FdpApplication.cs:93` compute
it independently. Two apps, one convention, no shared helper.

## The design

### 1. One shipped asset pair, authored in the source tree

```
layout/default/imgui.ini          ← committed, copied to output (CopyToOutputDirectory)
layout/default/fdp_windows.json   ← committed, copied to output
```

🔒 **Same filenames, different directory** — so "the layout" is *a directory*, and reset is a directory
copy rather than two special-cased files. Adding a third layout file later needs no design change.

### 2. Startup — the user's own mechanism, unchanged load path

```
if (resetLayoutOnRun)            // dev default: ON
    copy both defaults over the user copies, force-overwrite
load as today                     // ImGui reads the user imgui.ini; WindowManager reads fdp_windows.json
```

🔒 **Nothing in the load path changes.** The reset is a file copy *before* load — exactly as specified,
and it means `SaveSettings`/`LoadSettings` and ImGui's own persistence stay untouched.

### 3. Exit — unchanged

Save to the user location, as today.

### 4. `File ▸ Layout ▸ Save current as default`

Copies the **user** pair → the **source-tree** pair.

⚠ **Only valid when running from the repo.** Locate it by walking up from `BaseDirectory`, exactly as
`ResolveAiBehaviorsDir` already does (`EditorSubsystem.cs:693-708`). If no source tree is found, the item
is **disabled with a reason** — not hidden, so the absence is explainable.

### 5. The toggle

| | |
|---|---|
| `--reset-layout` / `--no-reset-layout` | explicit, per run |
| Persisted preference | remembers the choice; **defaults ON** while the layout is still evolving |
| ⚠ Must be discoverable | a `File ▸ Layout` item showing the current mode, so a user who loses their layout every run can find out *why* |

### 6. Fix the duplication while here

Extract the ini-path computation used by `RaylibPresentationShell.cs:131` and `FdpApplication.cs:93` into
one helper. ⚠ **Small, but it is the seam this issue says is missing** — `SetupImGui()` gaining a path
parameter is what makes the whole feature testable.

## Acceptance

| # | Case | Cls |
|---|---|:--:|
| 1 | Delete both user files → launch → **the shipped default is used**, not an empty layout | I |
| 2 | Move a window, quit, relaunch with reset **OFF** → the move survives | I |
| 3 | Same with reset **ON** → the layout returns to default | I |
| 4 | *Save current as default* → the **source-tree** files change; relaunch with reset ON → the new default appears | I |
| 5 | Running outside the repo → the menu item is **disabled with a reason** | H |
| 6 | 🔒 Reset restores **both** files — geometry *and* open/closed/perspective/scale | I |
| 7 | The default assets **reach the output directory** on build | H |
| 8 | 🔒 `fdp_windows.json` is written **next to `imgui.ini`**, not beside the exe | I |
| 9 | Migration: an old exe-adjacent `fdp_windows.json` is copied across **once**, then left alone | I |

⚠ Cases 1-4, 6 are **I** rather than **H** because they are file-system round-trips; none needs a human
eye, so they still run in CI.

## 🔒 Out of scope

| | |
|---|---|
| Per-perspective layout defaults | one default pair for now; the shape does not preclude it |
| Multiple named layouts / workspaces | a bigger feature ([UXI-21](UX_Issues.md#uxi-21) is adjacent) |
| Migrating existing users' layouts | there is no format change |

## Risks

| | |
|---|---|
| ⚠ **Writing into the source tree from a running app** | only in a dev checkout; guarded by the walk-up probe and disabled otherwise |
| ⚠ **Reset ON is destructive by design** | the user loses their arrangement every run — hence the discoverable indicator in §5 |
| ⚠ Moving `fdp_windows.json` changes where an existing user's file is looked for | mitigated by the one-time migration; ⚠ **a user who downgrades afterwards silently gets the old file back** — acceptable, worth one line in the changelog |
