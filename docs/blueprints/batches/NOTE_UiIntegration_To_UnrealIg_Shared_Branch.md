# Note: we now share `unreal-ig` — the HROT side of the integration moved here from `ui`

| | |
|---|---|
| **From** | the HROT-integration session (until now on `ui`), 2026-11-25 |
| **For** | `unreal-ig` (the Unreal session on the user's machine) |
| **User's decision** | `ui` goes back to the usual HROT ui lane; the HROT-integration session and the Unreal session share `unreal-ig`; the integration session may commit and push to `unreal-ig` |
| **Moved at** | `unreal-ig` = `origin/unreal-ig` + a merge of `origin/ui` (its last integration commit `9fe2faa8d`) |

## 1. Two writers on one branch — the rule

⭐ **Before EVERY push: `git fetch origin unreal-ig && git merge --no-edit origin/unreal-ig`, then push.** Merge, never rebase, never force-push. Both of us
follow it, so neither loses the other's commits.

## 2. Who writes what

| the Unreal session | the HROT-integration session |
|---|---|
| `tools/unreal`, `scripts/unreal`, `assets/terrain-input`, its `NOTE_UnrealIg_To_*` notes | the HROT engine, the gateway side (in SimNetGateway, branch `ccr-458b1360-afzl8f` — unchanged), its `NOTE_UiIntegration_To_*` / `NOTE_Ui_To_*` notes and handoffs |

Neither edits the other's files. A change wanted in the Unreal client still comes as a NOTE to you, as before; a change wanted in HROT or the gateway as a NOTE to us.

## 3. Ids

| | tracker | rulings |
|---|---|---|
| yours | `CE-12000…` | `R-12000…` |
| ours (moved from `ui` with the work) | `CE-11000…` | `R-11000…` |

Each adds new tracker rows at the END of its own area. Neither allocates from the `ui` lane's blocks.
