<!--STATUS
state: LIVE
updated: 2026-11-06
current-answer: whole note
related-designs:
  - docs/DESIGN_Subsystem_Composition_Unification.md — §4.1ag (CE-4270): the host unification this rename followed
-->
# NOTE backend → ui: the IG node is now the Player (CE-3371)

🔒 User, `2026-11-06`: *"the IG disappeared and Player appeared, there is no real relation between them but the code transformation"* — no alias, no compatibility shim.

| was | is |
|---|---|
| `Hrot/Subsystems/Hrot.IG/` · `Hrot.IG.Tests/` | `Hrot/Subsystems/Hrot.Player/` · `Hrot.Player.Tests/` (project + assembly + namespace `Hrot.Player`) |
| `Hrot/Network/Hrot.Network.NED/IG/` · namespace `Hrot.Network.NED.IG` | `…/Player/` · `Hrot.Network.NED.Player` |
| `Hrot.IG.Components` (the shared map components in `Hrot.Core`) | ⭐ `Hrot.Map.Common.Components` (the namespace their folder already used) |
| `IgSubsystem`, `IgApplication`, `IgNodeBootstrapper`, `Ig*` | `PlayerSubsystem`, `PlayerApplication`, `PlayerNodeBootstrapper`, `Player*` |
| `IgSymbolOverride` · `IgVisualDef` | `MapSymbolOverride` · `MapVisualDef` |
| `--mode ig`, subsystem / perspective `"IG"`, window ids `ig_*` | `--mode player`, `"Player"`, `player_*` — ⛔ `ig` is an invalid mode now |

⚠ **If you have an open branch touching `Hrot.IG`:** merge `backend` first; git follows the folder rename for edited files, but a NEW file you added under `Hrot/Subsystems/Hrot.IG/` lands in a folder that no longer exists — move it to `Hrot.Player/` and change its namespace. Saved ImGui window layouts keyed on `ig_*` ids reset once.
