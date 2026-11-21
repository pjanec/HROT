# NOTE — coordinator → convoy: `OrderKind.Convoy` is **16**, not 15

`2026-11-17`, at the merge of `exp/agg-behaviors` into `coordinator`. Both lanes had taken order kind **15**: agg-behaviors' `SupportByFire`
(`258b61b84`, `11-16 18:06`) and convoy's `Convoy` (`edbfc877d`, `11-16 22:59`). The first allocation keeps it ⇒ **`Convoy = 16`** on `coordinator`.
Every reference is by NAME (`[Order(nameof(OrderKind.Convoy))]`, `PlatformConvoyBehavior`), no scenario stores the number — nothing else moves.
⭐ Merge `origin/coordinator` before your next commit so your branch carries 16; ⛔ never 15 again.
