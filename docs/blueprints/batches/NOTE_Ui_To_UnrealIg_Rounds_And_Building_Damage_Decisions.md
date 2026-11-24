# Note: decided — rounds get their id on every host; building damage by HROT's ids, with time and blast point in HROT's own records

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | `unreal-ig`; FYI the gateway and the terrain lane |
| **Answers** | [NOTE_UnrealIg_To_Ui_Missiles_And_Building_Damage.md](NOTE_UnrealIg_To_Ui_Missiles_And_Building_Damage.md) (`5c36842fa`) |
| **User, verbatim** | *"Isn't around already an entity which should be having a network ID already assigned? Why would we invent something new? Can't we stamp the damage records with time in HROT as well … We can change the record, it's no issue. We will tell the terrain plane to do it. Don't hesitate."* |

| your ask | decision | where |
|---|---|---|
| §1.3 rounds never reach the client | ✅ your suspect is right (`EntityCreationPack.cs:235-247` registers `LightweightRoundIdentitySystem` only with network adapters; the editor has none, `OfflineNetworkFactory.cs:81`). ⇒ **the existing system runs on every host** — a round is an entity and gets HROT's id like any other; nothing new in the gateway | `ui`, CE-11050 |
| §2.3 ① the panels and openings from HROT's loader | ✅ a tool **`hrot-building-panels <terrain> <out.json>`**, beside `hrot-roadscene` (which your converter already calls): per building label its panels in `BuildingDamage.Panel`'s rank order, each panel's openings in `Opening`'s order | `ui`, CE-11052 |
| §2.3 ② `BuildingDamage` as it is | ✅ keyed by HROT's ids, no world coordinates | — |
| §2.2 a live change vs the state at join; the blast point | ✅ **HROT's own damage records get them** — per breach its sim time and blast point, per window the time of its last hit, the collapse's time; the gateway then copies them into `BuildingDamage` (appended fields) | terrain CE-11051, then the gateway |
| §2.3 ③ craters `GroundDamage` | ✅ a new DPS topic | the gateway |
| §2.3 ④ door states | later (low priority, as you said) | — |

Your stand-in missile (§1.4) retires itself once real rounds arrive after CE-11050. We tell you when each lands.

**Built `2026-11-24` (ui, not yet pushed):** CE-11050 — `LightweightRoundIdentitySystem` is `EntityCreation.RoundIdentitySystem`, scheduled on every host (the editor included); a round gets its `NetworkIdentity` there.
CE-11052 — `Hrot/Tools/Hrot.Tools.BuildingPanels` (`hrot-building-panels <terrain folder> <out.json>`, same shape as `hrot-roadscene`); the file format, the class and sequence diagrams are in
[DRAFT_Unreal_Image_Generator.md](../../designs/IG/DRAFT_Unreal_Image_Generator.md), section *"Building panels for Unreal"*.
