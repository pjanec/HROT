<!--STATUS
state: LIVE
build-state: DESIGN — decision-shaped, awaiting the user's approval. ⛔ NOT ready to dispatch: the UML
  belongs with the chosen option, and the option is not chosen yet.
updated: 2026-08-26
current-answer: §4 carries a RECOMMENDED answer per sub-question. Nothing here is approved yet.
stale-below: nothing.
known-conflict: none. §5 F3 is a NEW defect this investigation found; it has no prior design record.
-->
# ⭐⭐⭐ `Q59` — **One attribute, declared how many times?** *(consistency · architectural correctness · redundancy)*

> ⭐⭐⭐ **User, `2026-08-26`:** *"so what can we do to achieve consistency and architectural correctness and
> eliminated redundancy?"*
>
> ⭐⭐ **Context:** `AX-018` fixed three *symptoms* with a rail. ⛔ **A rail detects drift; it does not make
> drift impossible.** This asks the structural question instead.

---

## 1. ⭐⭐⭐ INVENTORY — *what actually exists* **(`search_graph`, not grep)**

```
search_graph(name_pattern=".*AttributeInstaller.*|.*AttributeIds.*|.*JsonToRecordCompiler.*|.*Installer$")
  → total: 42.  Production IBinaryAttributeInstaller implementations: EXACTLY THREE
    (EntityDataAttributeInstaller · SimTransformAttributeInstaller · SimTransformHeadingInstaller)
    + one test DelegateInstaller.
search_graph(name_pattern=".*AttributeSchema.*|.*ExportSchema.*|.*AttributeManifest.*|.*AttributeDescriptor.*|.*EdgeSchema.*")
  → total: 20.  NO manifest/descriptor type exists. The only "schema" surfaces are
    JsonAttributeCompiler.ExportSchema (lossy — see §5 F4), EdgeSchemaEntry (id+kind),
    the EntityAttributeSchema DDS topic, and EntityAttributeSchemaPublisherSystem.
```

⭐⭐ **Design corpus read** *(`R-129` — intent before code)*:
`docs/designs/attribs-to-ecs/ATTR-DESIGN.md` · `docs/designs/attribs2/ATTR2-DESIGN.md` ·
`docs/DESIGN_Cgf_AxisB_Rotation_Slice.md` §16/§17.

### 1.1 ⭐⭐ ONE attribute is currently described in **five** places

| # | surface | what it declares | derived from anything? |
|---|---|---|---|
| ① | `AttributeCompilerFactory.Build()` | JSON path → ECS component + field setter + **descriptor ordinal** | ⛔ no |
| ② | `AttributeCompilerFactory.BuildEdgeCompiler()` | JSON path → **`AttributeId`** + value kind | ⛔ no |
| ③ | the three installers | `AttributeId` → ECS component + field setter + **descriptor ordinal** | ⛔ no |
| ④ | `DescriptorMapper.MapToComponents` *(2-arg overload)* | wire descriptor → ECS component + field, **hand-coded** | ⛔ no |
| ⑤ | `JsonAttributeCompiler.ExportSchema()` | the published JSON-Schema | ⭐ from ① only, **lossily** |

⇒ ⭐⭐⭐ **Adding one attribute correctly means editing ①②③ and possibly ④, and ⑤ then under-describes it.**
📐 **`AX-018` measured what that costs: `Heading` reached ① and ③ and NEITHER edge table**, for months.

---

## 2. ⛔ WHAT THE OWNING DESIGNS ALREADY SAY — **the principle exists; it was never extended**

| where | verbatim |
|---|---|
| ⭐⭐⭐ `ATTR-DESIGN.md` §3.8 | *"the same `JsonAttributeCompiler` routing table and delegate set serves both entity creation and live entity updates — **a single source of truth**."* |
| ⭐⭐ `ATTR2-DESIGN.md` §3.4 | `AttributeId` is *"A static well-known table **shared** between the Edge Compiler and the Binary Interpreter to ensure the two components agree on IDs."* |
| ⭐⭐ `ATTR2-DESIGN.md` §3.2 | the edge compiler *"Registers the same … paths … so the JSON→ECS and JSON→Binary pipelines **stay in perfect sync**."* |
| 🔴 `ATTR-DESIGN.md` **"Phase 6: Unified Descriptor Routing (Advanced)"** | *"`DescriptorMapper` reuses the same compiled delegates; field-mapping logic is defined **in one place**."* ⚠ **marked optional — and only HALF built** |

⇒ ⭐⭐⭐ **The single-source-of-truth principle is ALREADY the stated architecture.** ⛔ It was honoured for
*ids* (②③ share `AttributeIds`) and for *one* table (①), and abandoned for the mapping itself.
⚠ **This is the seam law:** the seam exists and is under-adopted — ⛔ **do NOT invent a new concept.**

---

## 3. ⭐⭐ THE ARCHITECTURAL DIAGNOSIS, in one line

> ⭐⭐⭐ **There is no single declaration of *"what an attribute IS."*** An attribute is a **6-tuple**
> — *(JSON path, `AttributeId`, value kind, ECS component, field setter, descriptor ordinal)* — and the code
> stores **projections** of that tuple in five hand-maintained tables.

⭐ **Every defect this session found is a consequence, not a coincidence:**

| defect | which projection disagreed |
|---|---|
| `AX-015` — a binary rename never republished | the **ordinal** projection was missing on the binary side |
| `AX-018 D1` — a heading could be applied but never emitted | ② lacked a row ①③ had |
| `AX-018 D2` — `{"Affiliation":2}` threw | the **kind** projection was single-valued where the consumer accepts two |
| ⭐ **`Q59 F3`** *(§5, NEW)* | ④'s hand-coded field mapping **diverged numerically** |

---

## 4. ⭐⭐⭐ THE DECISION — sub-questions, each with a RECOMMENDED answer

> ⭐⭐ **`A`–`D` are ordered so each is useful alone.** ⛔ Nothing here is approved; ⭐ reply *"approved"*, or
> name the one to change.

### `Q59-A` — ⭐⭐⭐ Do we introduce a single attribute declaration that ①②③ are DERIVED from?

| option | |
|---|---|
| **A1** ⭐⭐⭐ **RECOMMENDED — one `AttributeDefinition` list; the three tables are BUILT from it** | one `record` per attribute carrying the 6-tuple; `Build()`, `BuildEdgeCompiler()` and the installer registrations all **iterate the same list**. ⇒ ⭐⭐ **half-registration becomes UNREPRESENTABLE** — the `UXI-30`/`AX-001` shape *(move the obligation to where it cannot be skipped)*, which is strictly stronger than `AX-018`'s rail |
| **A2** keep three tables + the rail | ⭐ zero risk, already shipped. ⛔ But drift is *detected*, not *prevented*, and ⚠ **the rail's vocabulary list is itself hand-maintained** *(it is pinned to ①, so it cannot rot silently — but it is a 4th place to edit)* |
| **A3** codegen the tables from a manifest file | ⛔ **rejected.** ⚠ A build-time generator for **6 attributes** is a large mechanism and a new failure mode; ⭐ revisit only if the vocabulary reaches tens of entries |

⭐ **Why A1 and not A2:** 📐 `AX-018` is the *second* time these tables silently disagreed, and the first
went unnoticed for months. ⚠ **Honest cost:** the setter delegates have different *shapes* per path
*(`ValueAttributeSetter<T>` vs a binary handler)*, so a definition holds **two delegates**, not one — ⛔ the
tuple does not collapse to a single lambda, and any design claiming it does is wrong.

### `Q59-B` — ⭐⭐ Is `ExportSchema` the natural CONSUMER of that declaration?

| option | |
|---|---|
| **B1** ⭐⭐ **RECOMMENDED — yes; derive it from the definitions and make it truthful** | 📐 today it emits `"type": "string"` for **every** path *(including the three float geo paths)* and drops the id and the kind. ⇒ ⭐ with `A1` it becomes correct **for free**, and the DDS `EntityAttributeSchema` topic finally describes the real contract |
| **B2** leave it | ⛔ it stays a published, wrong schema |

### `Q59-C` — ⭐⭐⭐ What about `DescriptorMapper`'s hand-coded arm? *(this is where the live defect is)*

| option | |
|---|---|
| **C1** ⭐⭐⭐ **RECOMMENDED — fix the divergence NOW as its own item; adopt Phase 6 LATER** | 📐 §5 `F3` is a **live wrong rotation** on the CGF spawn path; ⭐ the one-line fix is *"call `HeadingDegToRotation`"*, which is small, reviewable and independent of `A1` |
| **C2** finish `ATTR-DESIGN` Phase 6 *(route ④ through the compiler)* in the same batch | ⚠ the 3-arg overload already exists and is **used only by tests**; ⛔ the 2-arg one is production. Switching the production caller is a **real behaviour change on the spawn path** ⇒ ⭐ worth doing, ⛔ **not in the same batch as a correctness fix** |
| **C3** delete the hand-coded overload | ⛔ **rejected — "no rush removals."** ⚠ It is the production path; retiring it is `C2`'s outcome, not its premise |

### `Q59-D` — ⭐ The third `eForceIdentifier` copy?

| option | |
|---|---|
| **D1** ⭐⭐ **RECOMMENDED — leave the two pre-existing Hrot copies; the `AX-017` rail already pins all three** | ⭐ cheapest correct answer. ⛔ Consolidating `Hrot.Core.Mission` vs `Hrot.NED.Descriptors` touches mission code far outside this lane |
| **D2** consolidate | ⚠ a separate slice with its own blast radius |

---

## 5. 🔴 FINDINGS THIS INVESTIGATION PRODUCED — **measured, and `F3` is NEW**

| # | finding |
|---|---|
| 🔴🔴 **`F3`** | **`DescriptorMapper.MapToComponents` computes a DIFFERENT — and wrong — heading rotation from every other path, on the LIVE CGF spawn path.** 📐 Canonical *(`SimTransformBridgeSystem.HeadingDegToRotation`, documented *"X=East, Y=North, 0=North, 90=East, clockwise"*)*: `axis Z, angle (90−h)`. 📐 `DescriptorMapper:118`: `CreateFromYawPitchRoll(−h·π/180, 0, 0)` = **yaw about Y, no 90° offset**. ⇒ **measured forward vectors:** `h=0` → canonical **North** `(0,1,0)` vs mapper **East** `(1,0,0)`; ⛔ `h=90` → canonical **East** vs mapper **straight UP** `(0,0,1)`. ⚠ **It disagrees at EVERY heading and is not merely a different convention — it rotates in the wrong plane.** 🔴 **Live:** `NedCgfEntityLifecycleAdapters.cs:70` calls the 2-arg overload for `msg.InitialDescriptors`, and **nothing overwrites it** — the per-tick `SimTransformBridgeSystem.Execute` *"has been removed"* per its own class doc ⇒ this is the entity's persisted initial rotation |
| ⚠ **`F4`** | **`ExportSchema` publishes a schema that says `"type": "string"` for every path**, including the three `Float64` geo paths, and omits the id and kind entirely. ⭐ It is derived from `_registeredPaths` — a `string[]` — so it **cannot** be truthful without `Q59-A`. 📌 Also carries a **leaked, unused `Utf8JsonWriter`** over a throwaway `MemoryStream` *(lines 383–386)* — dead code |
| ⚠ **`F5`** | **`AttributeCompilerFactory.Build()`'s `"Heading"` handler INLINES the conversion math** instead of calling `HeadingDegToRotation`. 📐 Numerically identical **today** *(both `axis Z, (90−h)`)*, so ⛔ not a defect — ⚠ but it is the third copy of one formula, and `F3` is what the third copy of a formula eventually becomes. 📌 `AttributeIds`' own doc claims *"no new conversion math was written; the installer reuses the bridge"* — ⭐ **true of the installer, false of the JSON arm** |

---

## 6. ⭐ IF APPROVED — sequencing *(no UML yet: obligation ② says draw it AFTER the option is chosen)*

| order | item | why here |
|---|---|---|
| **1** | ⭐⭐⭐ `C1` — fix `F3`'s rotation, with a rail asserting **all** heading paths agree | ⛔ a live correctness defect outranks a refactor |
| **2** | ⭐⭐ `A1` — the `AttributeDefinition` list; ①②③ derived | ⭐ makes `AX-018`'s class of defect unrepresentable |
| **3** | ⭐ `B1` + `F4`/`F5` cleanup | ⭐ falls out of `A1` almost free |
| **4** | ⚠ `C2` — Phase 6 adoption for `DescriptorMapper` | ⛔ its own batch: a real behaviour change on the spawn path |

⛔ **`D2` is not scheduled.**
