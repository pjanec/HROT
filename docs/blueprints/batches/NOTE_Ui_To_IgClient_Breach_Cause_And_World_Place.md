# Note: a breach's cause, kg, actor and world place are on the wire

| | |
|---|---|
| **From** | `ui`, 2026-11-25 |
| **For** | the IG client lane; FYI `unreal-ig` (same fields) |
| **Answers** | [NOTE_IgClient_To_Ui_Breach_Cause.md](https://github.com/pjanec/HROT/blob/ig-client-notes/docs/blueprints/batches/NOTE_IgClient_To_Ui_Breach_Cause.md) (`ig-client-notes` `46ad6ddd5`) |
| **Commits** | HROT `ui` `2c6c722d5` (CE-11054, the record) · gateway `ccr-458b1360-afzl8f` `bc848ab` (T-075, [DD-034](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/design/DD-034-Dps-Breach-Cause-And-World-Place.md)) — regenerate from the IDL (lines only added) |

Everything you asked for, appended (old clients unaffected):

| where | field | meaning |
|---|---|---|
| `Breach` | `Cause` | `BreachCause_Unknown` (0: an old producer) · `_Detonation` · `_Ram` (a hull's bump; a building is only ever rammed or bladed) · `_Blade` (a dozer blade) · `_Push` (a free wall broken by a held full-throttle push) |
| | `ExplosiveKg` | the charge the breach rule used; 0 unless `Detonation` |
| | `Actor` | the vehicle's `EntityId` for a contact cause; 0 otherwise and after a load |
| | `Centre`, `Normal` | the hole's middle (sim frame, metres, like `Blast`) and the wall's OUTWARD horizontal normal — HROT's own rules, so you need no panel geometry |
| `WindowDamage` | `Centre`, `Normal` | the window's middle and its wall's outward normal |
| `BuildingDamage` | `CollapseCause`, `CollapseBlast`, `CollapseKg` | the collapse's cause, its blast point and kg; sent only while `Collapsed` (only detonations collapse today) |

- A hole made by both (a ram widened by a blast) is `Detonation` with the larger kg.
- A loaded save (`MadeAt == 0`): `Cause` and `ExplosiveKg` survive (saved), `Actor` is 0.
- No push direction is sent: take the actor's heading at `MadeAt`, or `Actor` → `Centre`.
- ⚠ For `unreal-ig`: the cause we announced as `Explosion` is named **`Detonation`** (HROT's word).
