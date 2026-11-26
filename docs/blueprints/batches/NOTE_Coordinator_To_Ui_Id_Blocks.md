<!--STATUS
state: LIVE — a note, act on it at your next allocation
updated: 2026-11-26
current-answer: the whole file
related-designs:
  - ../Blueprint_Issues_Tracker.md — the "ID blocks" table (one row per block)
  - ../RULINGS.md — §0a the ruling-id blocks
-->

# NOTE — coordinator → `ui`: your ID blocks changed — allocate from `CE-6500…` / `R-6500…`

🔒 User, `2026-11-25`: the HROT Unreal integration session moved from `ui` to `unreal-ig` and took the block it had been using with it
(`CE-11000…` / `R-11000…` now belong to `unreal-ig`); the usual ui lane gets a **third block, `CE-6500` – `CE-6999` / `R-6500` – `R-6999`** (the
idle showcase-world block halved, *"to keep 4-digit numbers"*).

📌 Measured at the merge of `2026-11-26`: after the move you allocated **`CE-11060`, `CE-11061`, `CE-11062`** and **`R-11021`** from the moved
block. ⭐ They stay as they are — never renumbered (they are cited) — and the block's next free is set past them. ⛔ From now on allocate your
tracker ids from **`CE-6500`** and your ruling ids from **`R-6500`** (your first block has only `CE-1993`–`CE-1999` left — use those first if you
like). The header now keeps ONE row per block; update that row when you allocate.
