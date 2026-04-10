## Editing Invariants (Non-Negotiable)

1. Preserve all existing comments exactly unless they are wrong, not matching the intentions or code.
- Do not delete, rewrite, reflow, or “clean up” comments unless explicitly requested.
- When moving code, move its comments with it unchanged.

2. Preserve existing Unicode and text encoding exactly.
- Do not normalize/convert Unicode characters.
- Do not replace typographic symbols (e.g., ×, →, ─, ≤, em dashes, etc.).
- Do not introduce mojibake by changing file encoding.

3. Minimize textual diffs.
- Only change lines required for the functional fix.
- Avoid unrelated formatting/comment/whitespace edits.

