# Feature design — the default perspective (and what restore must *not* do)

> **Design for [UXI-06](UX_Issues.md#uxi-06) · drafted 2026-08-10 · ⚠ re-scoped the same day after user
> review.** **Status: ✅ designed — ready to break into `UXT` tasks.**

## ⚠ Re-scoped: the Editor behaviour is correct and must be preserved

> **User ruling, 2026-08-10:** *"The way perspective switching works now in the Editor is **exactly as I
> want**: Scenario perspective as the default, BTree/HSM/Blueprint as document-driven perspectives."*

**The first draft of this design got the problem backwards.** It filed *"a saved `BTree` is silently
dropped on restart"* as the defect and leaned toward restoring it. But documents are **not persisted**
(`AiDocumentManager` has no save/load of any kind), so restoring `BTree` lands the user in an empty graph
workspace. **Falling back to `Editor` is the desired behaviour, not the bug.**

🔴 **The originally proposed fix would have broken it** — validating against `GetPerspectives()` makes
`BTree` *valid*, so restore would have started honouring it. [Corrections 19](UX_Tasks_Detail.md#corrections).

⇒ **The requirement is now explicit rather than accidental:**

| Perspective class | On restart |
|---|---|
| **Durable** — `Editor`, `IG`, `SimHost`, `ExCon`, `CGF`, `StrideMock`, `ReplayBrowser` | ✅ restore |
| **Document-driven** — `BTree`, `HSM`, `Blueprint` | 🔒 **never restore.** They are a function of the open document (`AiDocumentManager.Activate` → `AssetKind.ToPerspectiveName()`), and no document survives a restart |

## 🔴 The real defect: the *default* perspective can be a non-perspective

```csharp
// LocalWindowController.cs:81-84
var first = _subsystems.Skip(1).FirstOrDefault();
string defaultPersp = first?.Name ?? "Default";
bool valid = !string.IsNullOrEmpty(persisted) && _subsystems.Any(s => s.Name == persisted);
wm.SwitchPerspective(valid ? persisted! : defaultPersp);
```

`defaultPersp` is an **`ISubsystem.Name`**. For a single-subsystem mode that happens to equal a real
perspective. For `--mode all` it does not:

```
--mode all  →  "orchestrator,simhost,ig,excon,cgf"          HrotRunnerConfiguration.cs:77-78
subsystems   =  [PerspectiveCoordinator, Orchestrator, SimHost, IG, ExCon, CGF]
Skip(1)      →  Orchestrator            ⇒ defaultPersp = "Orchestrator"
```

**`"Orchestrator"` is not a perspective.** All three of its windows are `WindowScope.Global` with
`owningPerspective: string.Empty` (`OrchestratorWindow.cs:16`, `DiagnosticsWindow.cs:15`,
`ClusterControlWindow.cs:17`), so it never appears in `GetPerspectives()`.

And **no `fdp_windows.json` is committed** — verified, `git ls-files` returns nothing — so a first run has
nothing persisted.

> ### ⇒ First launch of `--mode all` / `demo` hides **22** perspective-bound windows
>
> `SwitchPerspective("Orchestrator")` runs; no window's `OwningPerspective` matches. SimHost 5 + IG 8 +
> ExCon 6 + CGF 3 = **22 windows invisible**. The user sees three Global orchestrator windows and an
> app that looks broken. Recoverable via the perspective menu — **if you know it is there.**
>
> ⚠ **`demo` is the shorthand a new user is most likely to run first.**

### Two lesser faults, same root cause

| | |
|---|---|
| **False positive on restore** | `Orchestrator` / `PerspectiveCoordinator` are valid `ISubsystem.Name`s, so a persisted value of either would **pass** and blank the UI. ⚠ Latent — only reachable by hand-editing the JSON, since every `SwitchPerspective` caller sources from real perspectives |
| **Positional default** | `Skip(1)` encodes "skip the always-injected `PerspectiveCoordinator`" (`Program.cs:211`) by index. Reorder composition and the default silently changes |

## 0. Prior art — ✅ checked ([rule 6](UX_Issues.md#rules))

| Exists? | What | Bearing |
|:--:|---|---|
| ⭐ | **`WindowManager.GetPerspectives()`** (`:178-186`) — distinct `OwningPerspective` over `PerspectiveBound` windows; its doc comment calls it *"the testable seam for perspective enumeration"* | feeds the perspective menu; **the default/restore path does not call it** |
| ✅ | `AssetKind.ToPerspectiveName()` (`AssetKindExtensions.cs:28-34`) — the **authoritative list of document-driven perspectives** | ⇒ the "never restore" set is **derivable, not hand-maintained** |
| ✅ | `RegisterPerspectiveLabel(id, label)` (`:228`) — `Editor` displays as `Scenario` | ids stay stable; labels are cosmetic |
| ✅ | Save/load round-trip (`:368-411`) | storage layer is fine |
| ❌ | Validation inside `SwitchPerspective` (`:164-171`) | accepts any string; unknown ⇒ every bound window hides |

## The design

### 1. The default must be a real perspective

```csharp
var known = wm.GetPerspectives();          // populated: RegisterWindows(wm) ran ~30 lines earlier
string defaultPersp = known.FirstOrDefault() ?? "Default";
```

Drops `Skip(1)` entirely — the coordinator contributes no windows, so it cannot appear in `known`. ✅ Fixes
the `--mode all` first-run blanking.

⚠ **`known` is `OrderBy(p => p)` — alphabetical.** For `--mode all` that yields `CGF`. Acceptable, but if a
specific landing perspective is wanted, prefer the first *requested subsystem that owns one*:
`config.RequestedSubsystems` order intersected with `known`. **Recommended** — it preserves today's
"first requested subsystem wins" intent without the positional hack.

### 2. Restore honours durable perspectives only

```csharp
var documentDriven = AssetKindExtensions.AllPerspectiveNames();   // BTree, HSM, Blueprint
bool valid = !string.IsNullOrEmpty(persisted)
          && known.Contains(persisted)
          && !documentDriven.Contains(persisted);
```

✅ Preserves the Editor behaviour the user wants — **by design now, not by a vocabulary accident.**
✅ Closes the false positive: `Orchestrator` is not in `known`.

### 3. `SwitchPerspective` refuses an unknown id

Log and no-op instead of silently hiding every bound window. Defence in depth; the callers above are the
real fix.

## Migration & gates

| Step | Gate |
|--:|---|
| 1 — default from `GetPerspectives()` | 🔴 **first run of `--mode all` shows the perspective-bound windows** (delete `fdp_windows.json` first). Single-subsystem modes unchanged |
| 2 — restore excludes document-driven | Editor: switch to BTree, quit, relaunch → **lands on Scenario**, exactly as today |
| 3 — `SwitchPerspective` guard | hand-corrupt the JSON → app still usable |

**Test seam:** `GetPerspectives()` is already *"the testable seam"*. Assert it returns
`{CGF, ExCon, IG, SimHost}` for `--mode all` and `{Editor, BTree, HSM, Blueprint}` for `--mode editor`,
and assert the restore predicate against it — never against subsystem names.

## 🔒 Out of scope

| | Why |
|---|---|
| Document/session restore | would make BTree restorable *correctly*, but needs persistence that does not exist. A separate feature; nothing here blocks it |
| `perspectiveMap` → `SwitchMapOwner` (`Program.cs:244-251`) | **correct as-is** — those five own a network map; unmapped names are a deliberate no-op |
| Renaming `Editor` → `Scenario` | the label seam handles display; renaming the **id** invalidates every persisted setting |

## Risk — and the dependency [UXI-05](UX_Feature_Menu_Follows_Focus.md) has here

⚠ UXI-05 binds menu items to perspective ids. If the default lands on a non-perspective, **every
perspective-bound menu item vanishes too** — the same 22-window failure, extended to the menu bar. 🔒 **Do
UXI-06 first.**

⚠ The perspective set is **emergent from windows**. A perspective with menu items but no windows would be
unreachable. None exists today; if UXI-05 ever needs one, the set must become **declared** — the
prerequisite [Q26-D](Architect_Question_26_Entity_Action_Model.md#q26-d--is-perspective-the-right-profile-key)
flagged.
