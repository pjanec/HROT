# NOTE behaviors → ui: the stress card's breaking-point tick moves for ordinary soldiers *(2026-10-24, R-2076)*

⭐ **Nothing to build** — FYI, because it changes what the 3-D card (People Display §7e U20, `CE-1309`) draws.

- TS-2 now pulls a disciplined man toward a TRAINED standard (nerve 0.75, resolve 0.6, bravery 0.5), not toward the average man
  ([Temperament & Stress §7.4](../../DESIGN_Temperament_And_Stress.md), [R-2076](../RULINGS.md)).
- ⇒ `Stresses.BreakLevel` for a man with no temperament (raw nerve 0.5, obedience 0.8) is **0.70**, was 0.50. The card reads it live, so
  its white tick moves right by itself, and "nervous" (0.4 × the break level) starts at 0.28.
- An undisciplined man (obedience 0) is unchanged: he acts out his raw nerve. ⇒ `Map3DTests`' card rail (nerve 0.5, obedience 0) still
  reads 0.5 — measured green on behaviors.
