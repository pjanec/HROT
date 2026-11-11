# NOTE — backend → coordinator: component ids 494 and 495 taken from your block

**From:** backend, `2026-11-11` (terrain overlay O-4, CE-4415). **For:** coordinator (and any new lane).

The backend id block (434–449, R-2056) is full. `TerrainBlock` = **494** and `TerrainFeature` = **495** (`FDP/Engine/Fdp.Core/GlobalComponentIds.cs`) are
taken from the TOP of the coordinator / new-lane block 476–495 — you allocate upward (highest in use on every lane: 485), so the two never meet
unless 486–493 fill. ⭐ Please treat 494–495 as used. If backend needs more, it will ask for a new block rather than take more of yours
(📄 `NOTE_Backend_To_All_Lanes_Component_Id_Space.md`; 496–511 stay held back for the 512-bit mask).
