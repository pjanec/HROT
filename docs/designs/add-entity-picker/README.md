<!--STATUS
state: LIVE
updated: 2026-11-01 (coordinator — first mockup)
current-answer: the two mockup files below; the decisions are docs/DESIGN_Add_Entity_Picker.md §2f (rev 8, G1–G10)
stale-below: nothing
known-rot: none — ⚠ a MOCKUP; the thumbnails are colour placeholders, names and numbers illustrative; §2f wins where they differ
known-conflict: none
related-designs:
  - docs/DESIGN_Add_Entity_Picker.md — OWNS the Add Entity picker; §2f is this gallery
  - docs/blueprints/Architect_Question_108_Run_Mode_Picker.md — D26: the generic picker's extensions (P1–P5) this gallery and the scenario picker share
  - docs/designs/scenario-picker/README.md — the sibling picker mockup
-->

# Add entity — the thumbnail gallery (mockup)

| file | shows |
|---|---|
| [`mockup/EntityGallery.html`](mockup/EntityGallery.html) | the gallery: the SIDE chosen once at the top (Friendly · Hostile · Neutral) · search over all groups · List ⇄ Thumbnails toggle + tile size · LEFT a short list of GROUPS with counts (Recent & favourites, Soldiers, Ground vehicles, Aircraft, Weapon systems, Units, Civilians, Tactical symbols, Areas & routes, Props) · CHIPS for the next level inside a group (Tanks · IFV / APC · Trucks …) · a GRID of tiles, each a thumbnail + name + ONE line (role · key fact) · a tile with no thumbnail shows the DIS glyph on a dashed tile · RIGHT the selected type: big picture, description, facts from the TKB, *with crew*, **Place on map** (Shift = many, or drag a tile onto the map, ☆ favourite) |
| [`mockup/EntityGallerySearch.html`](mockup/EntityGallerySearch.html) | search across groups (each tile names its group) · the keyboard · TACTICAL SYMBOLS drawn by the map's own symbol painter in the chosen side's colour · the LIST view — same groups, today's text rows, one toggle away |

Open a file in any browser (self-contained). Also on the claude.ai canvas [Rules View Mockup](https://claude.ai/artifact/VLfa5GxVGh3Z7nYsGbf65k) under *Add entity*; the committed files win.
