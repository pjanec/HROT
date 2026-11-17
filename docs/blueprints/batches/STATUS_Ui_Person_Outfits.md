<!--STATUS
state: DONE — S2 + thumbnails built; the scenarios of S3 / S4 are not in this half
updated: 2026-11-17
current-answer: the step table + the lane decisions
related-designs:
  - ../../designs/person-looks/DESIGN_Person_Outfits.md — the design (OF-4 / OF-5 garments); the mockups sit beside it
  - ../../DESIGN_Map_3D_Mode.md — §6q the block figure's outfits (updated to the as-built at the end)
-->
# STATUS — ui: PERSON OUTFITS, S2 the garments (CE-4648)

Handoff: [`HANDOFF_Coordinator_To_Ui_Person_Outfits.md`](HANDOFF_Coordinator_To_Ui_Person_Outfits.md). Started at `91171cabc` (merged coordinator@34d17c34c, backend@4073e0a90).

| # | step | state |
|---|---|---|
| 1 | mockups (13 garments, front + side, `docs/designs/person-looks/person-outfits-*.svg`, generator `scripts/gen-person-outfits-mockup.py`) | ✅ approved by the coordinator (checkpoint 1, two rounds) |
| 2 | the real drawing in `Outfits.Details` + the Native American enum members (checkpoint 2 approved, then the lit-frame / feather fixes) | ✅ `904b0216f`, `e20fcc859` |
| 3 | thumbnails: 1105–1110, 2010–2012 added, the Insurgent 2003 replaced (atlas 91 → 100 pictures); the Native American type is not registered yet ⇒ its thumbnail waits | ✅ |
| 4 | as-built folded into the design §9 + Map 3D §6q.2 (prior state SUPERSEDED); CAPABILITIES row (scenario ⏳) | ✅ |

## Lane decisions (for the user's review)

| id | decision | what would change it |
|---|---|---|
| R-1062 | the Native American garment kinds are added to the outfit enums by ui (`OutfitTop.Buckskin`, `OutfitLegs.Breechcloth`, `OutfitHead.Headband`, hair `Braids`) — backend's catalogue rows name them later | backend declaring different member names |
| R-1063 | FACE PAINT lives in the BEARD slot (`BeardKind.Paint`) — adding a slot would add a draw and reshuffle every existing person; the stripe colours come from a hash of the seed, no extra draw | a wish for authored paint colours per type ⇒ a new slot and a re-baselined fixture |

Further decisions: the sleeve fringe of the buckskin is DROPPED (accepted at checkpoint 2); the catalogue off-white `White` is now (244,243,238) — it is PRIVATE to the Middle-East wardrobes (`Shirt` and every existing garment are untouched; the existing-person identity probe `CE4648_EveryExistingPersonType…` passes), only the new types and the re-dressed 2003 use it.

## Gates (rule 8) — all `--no-build` after `dotnet build Hrot.Presentation.Tests`, display rails under `xvfb-run -a`

| gate | result |
|---|---|
| `PersonOutfitsRail` (geometry · 0 B for 500 people × 13 garments · lit 3/4 frames, thobe ≥ the white shirt) | 3 / 0 |
| red-proofs (one mutation each, reverted, tree clean): back drape removed · an allocation in `Garments` · thobe torso darkened ×0.7 | the geometry, the 0 B and the frame rail each went RED |
| `Map3DTests` (incl. `CE4648_*` baseline probe seeds 1–500 of every existing person type, OF-2 / 6 / 7 / 11 / 12, the updated OF-5) | green except the 3 known reds `S1_TheIconFallback…`, `CE1309_TheMindStateBar…`, `CE1086_TheFpvDrone…` (confirmed pre-existing; named by the handoff) |
| `Map3DFrameRail` (every civilian / child / elderly / police / soldier frame rail) | 52 / 2 — `CE4388_TheCachedWireSphere…` (red at base `4fa79837f`) and `E2b_V_ARandomTerrain…` (terrain, no person) |
| `EntityTypeCatalogTests` + `CE2138` + `PersonBodyParityTests` + `AirCombatLooksRail` | 44 / 0; the atlas count line moved 91 → 100 (exactly the 9 added thumbnails) |
| existing figures unchanged | proved by the data probe (every old type × seeds 1–500 × side × own colour) AND the diff of `Outfits.cs`: only code for NEW kinds was removed; a PNG diff cannot prove it (frames differ between two baseline runs) |
| not run | `civ-middle-east-market` / `me-fighters-ambush` scenarios (not in this half's scope); the Native American type's thumbnail (not registered) |

Ids: `R-1062`, `R-1063` (no `CE-` ids allocated).

## Native American look + Afghan people (`CE-1957`, `2026-11-17`)

| step | state |
|---|---|
| mockups (two checkpoints reviewed; fixes: baggy tunban, palette shown, armbands mid-arm, braids from the head) | ✅ |
| drawing + `Extra` slot (R-1064), `NativeAmericanWarrior` (R-1065), `Afghan*` wardrobes + types 1111–1116 / 2016–2018 (R-1066) | ✅ |
| rails: odds over seeds 1–500, the Extra-last pin, garment geometry, 0 B (all garments), lit frames | ✅ — red-proofs: the Afghan odds (chest rig 75→10 %), the Extra draw moved before the beard, the armband removed, the tunban narrowed — each went red, reverted |
| goldens | `CE2138…` moved by exactly 9 lines (1111–1116, 2016–2018); atlas count |
| scenario `civ-afghan-village` (36 villagers + 3 fighters; checks.json; preview.jpg) | `scripts/scenario-regression.sh civ-afghan-village` PASS (30 s sim) |
| thumbnails (typical seeds) 1111–1116, 2016–2018 | ✅ |
| docs: design §10, Map 3D §6q.3, CAPABILITIES, tracker CE-1957, `NOTE_Ui_To_Backend_Native_American_Look.md` | ✅ |

Decisions: **R-1064** the `Extra` slot (last draw); **R-1065** `NativeAmericanWarrior` one wardrobe, backend stamps it; **R-1066** Afghan wardrobes and ids. Final warrior frame: `/tmp/claude-0/final/warrior-front-lit.png` (not committed — no screenshots in git).
