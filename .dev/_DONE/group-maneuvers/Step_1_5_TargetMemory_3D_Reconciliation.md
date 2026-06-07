# Step 1.5 — `TargetMemory` 3D Reconciliation

> **Status:** Implementation instruction for a coding agent.
> **When:** After **both** Utility AI and the 3D Cognitive Spatial Awareness Promotion are
> implemented and merged, and **before** Squad Coordination begins.
> **What it is:** A single, small, atomic reconciliation PR. Utility AI and 3D promotion were built
> independently and blind to each other. They collide on exactly one shared structure: `TargetMemory`.
> 3D promotion widens it to carry altitude (Z); Utility AI reads it. This step makes Utility's readers
> altitude-aware and proves nothing regressed.
> **Size:** small. The widening is *additive* (Z is appended; X/Y unchanged), so this is "point a few
> readers at the new field + regression-check," not a port.

---

## 1. Background (why this step exists)

- **Utility AI** defines input readers that read `TargetMemory` — at least `ContactThreatLevel`,
  `DistanceToContext`, `ContactHealthFraction`, and any line-of-sight / position-derived readers in
  the Utility input catalog (the `[UtilityInput]` methods).
- **3D promotion** widened `TargetMemory` so contacts carry a true 3D position (X, Y, **Z**), and
  widened `ThreatEvaluationSystem` → `TargetMemory.AddOrUpdateTarget(...)` to pass Z.
- Because the two were built independently, Utility's readers were written against the **2D**
  `TargetMemory`. After the 3D merge they still compile (the change is additive), but two things are
  now wrong on multi-level terrain:
  1. Any reader that reconstructs a position as `new Vector3(x, 0f, y)` is hardcoding altitude to
     zero — the exact flat-earth bug 3D promotion exists to remove.
  2. Distance readers compute 2D distance, ignoring the altitude difference.
- On **flat terrain** these are harmless (Z is constant ≈ 0). On **multi-level terrain** (bridges,
  decks, overpasses) they are wrong. This step fixes them.

> If a duplicate scorer/assignment system was built anywhere (e.g. a squad track started early and
> reimplemented Utility's matrix), that is a **separate, larger** reconciliation and is **out of scope
> here**. This step is only about `TargetMemory` readers. Do not address scorer duplication here.

---

## 2. Preconditions (verify before starting)

1. Utility AI is merged: the `[UtilityInput]` readers and the Utility scoring core exist.
2. 3D promotion is merged: `TargetMemory` stores a 3D position; `AddOrUpdateTarget` takes/stores Z;
   `EqsResult`/`EqsCognitiveBuffer` are 3D; `SimTransform.Position.Z` is authoritative.
3. The 3D promotion's **flat-terrain golden regression** (its design §6, Axis-1) is green — i.e. EQS
   behavior on flat maps is provably unchanged. This step adds the equivalent check for Utility.

---

## 3. The task

### 3.1 Find every Utility reader that touches `TargetMemory`

Search the Utility input catalog (the `[UtilityInput]` reader methods, typically in
`StandardInputs.cs` or equivalent) for:

- direct reads of `TargetMemory` fields / `TargetMemory` accessors;
- any `new Vector3(... , 0f, ...)` or `new Vector3(x, 0f, y)`-style position reconstruction;
- any 2D distance computation (`Vector2.Distance`, manual `sqrt(dx*dx + dy*dy)`, XZ-only distance)
  applied to a contact position.

A `grep` for `TargetMemory` within the Utility input-reader source is the fast way to bound the set.
The set is expected to be **small and localized** (a handful of named readers). If it turns out
scattered/large, stop and flag it — the plan assumed localized reads, and a large surface changes the
risk profile.

### 3.2 Make each reader altitude-aware

For each reader found:

- **Position reconstruction:** replace any hardcoded `0f` altitude with the contact's **real Z** from
  the now-3D `TargetMemory`. Use the 3D position the struct now carries; do not synthesize Z.
- **Distance readers** (e.g. `DistanceToContext`): compute **3D distance** (`Vector3.Distance`)
  instead of 2D. Apply the same normalization the reader already used (the reader owns its 0–1
  normalization range; only the distance metric changes from 2D to 3D).
- **LOS / threat / health readers:** if they only read scalar fields (threat score, health), they
  need no change. Only position- and distance-derived readers change.

Do **not** change reader names, signatures, the `In.*` accessors, weights, or curves — only the
internal computation from 2D to 3D. This keeps it invisible to authored decisions and the source
generator.

### 3.3 Atomicity

Land this as **one atomic PR**. There must be no intermediate state where a 3D `TargetMemory` is read
by a reader still assuming 2D. (If 3D promotion and this reader-fix can be combined into the 3D
promotion's own atomic PR, that is even better — but if 3D already merged, this is a single follow-up
PR done in one pass.)

---

## 4. Validation (the safety gate)

This is the step that proves the reconciliation is safe.

1. **Flat-terrain parity (required).** Run Utility AI's existing decision tests / starter-pack
   integration tests on a **flat-terrain** fixture (Z constant ≈ 0). Assert decision outputs —
   selected option, ranked results, scores — are **bit-or-tolerance-identical** to pre-step behavior.
   On flat terrain a 3D distance equals the 2D distance and a real Z equals the old `0f`, so nothing
   may move. If anything moves on flat terrain, a reader changed more than its distance metric — fix
   it.

2. **Multi-level correctness (proves the point).** Add/maintain a **multi-level fixture** (e.g. a
   contact on a bridge deck above a contact on the street, same X/Y, different Z). Assert that
   `DistanceToContext` and any position reader now distinguish them — the under-bridge and on-deck
   contacts produce different distances/scores, where pre-step they were identical. This is what the
   whole 3D effort is *for*; flat parity alone would pass even if the readers still ignored Z.

3. **No authoring/codegen drift.** Confirm the Utility source generator output is unchanged (reader
   names, `In.*` accessors, hashes identical) — this step touches only reader *bodies*, not the
   catalog surface, so the generated registrar must be byte-identical.

---

## 5. Done criteria

- Every Utility `TargetMemory` reader uses real Z (no `0f` altitude hardcodes; 3D distance where
  applicable).
- Flat-terrain Utility regression is green (behavior provably unchanged).
- Multi-level fixture proves readers now distinguish altitude-separated contacts.
- Generated Utility registrar unchanged (no catalog-surface drift).
- One atomic PR; no intermediate 2D-reader-on-3D-struct window.

After this step, Squad Coordination may begin: it consumes the now-3D `TargetMemory` (for threat
reasoning) and the Utility scoring core/matrix (for role/fire/maneuver assignment), both reconciled.

---

## 6. Explicitly out of scope

- **Scorer/matrix duplication.** If any track built its own scoring or assignment logic, unifying it
  onto the Utility core is a separate, larger task — not this step.
- **EQS readers.** The EQS cover/position path was made 3D *inside* the 3D promotion itself; it does
  not need fixing here. This step is only Utility's `TargetMemory` readers.
- **Re-tuning decision weights/curves for multi-level play.** Multi-level terrain may shift utility
  scores enough to warrant a tuning pass, but that is **authoring**, not this code step, and is
  deferred until multi-level content exists.

---

*End of Step 1.5. A single atomic reconciliation PR between (Utility AI + 3D promotion) and Squad
Coordination. Small because `TargetMemory`'s 3D widening is additive; the gate is the flat-terrain
parity regression.*
