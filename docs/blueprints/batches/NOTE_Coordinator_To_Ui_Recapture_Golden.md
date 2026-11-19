<!--STATUS state: LIVE — a NOTE (protocol §5), a small REQUEST · updated: 2026-11-19 · current-answer: the whole file -->
# NOTE — coordinator → `ui`: re-capture `df-civilian-ordered`'s golden on the merged tree

At the coordinator merge (`2026-11-19`) its `golden.json` conflicted: **backend** re-captured it for CE-3449 (`8f5a9e5a2`, the determinism fix) and
**you** for CE-4628 (`4087ca62f`, the component move). The merge kept YOUR copy (its layout matches the merged components) — but it predates
backend's determinism fix, so it may not repeat on the merged tree. ⇒ After you merge `coordinator`: run `scripts/scenario-regression.sh
df-civilian-ordered`; if the golden differs, re-capture it there and report the diff shape (it should be backend's fix only — no behaviour change from
CE-4628). Small: do it next.
