<!--STATUS
state: LIVE
build-state: DESIGN — decision-shaped, awaiting the user's approval. ⛔ NOT ready to dispatch: the UML
  belongs with the chosen option, and the option is not chosen yet.
updated: 2026-08-26
current-answer: ⭐⭐⭐ §7 (the ATTRIBUTE / DESCRIPTOR SPLIT, user 2026-08-26) is the CURRENT answer and it
  REVISES §3 and §4. Read §7 FIRST, then §4's revised leans, then §8. Nothing here is approved yet.
  ⭐⭐ §8 answers two follow-ups: (a) the multiple-tables issue is NOT solved — AX-018 went 4 tables to 3
  and added a RAIL, which detects drift rather than preventing it; (b) Heading vs GeoHeading is a real
  trap with measured cost, and the fix is to rename the CONSTANT (source-only) never the PATH (wire).
stale-below: ⛔ §3's "6-tuple" is SUPERSEDED by §7.2 — it is TWO tuples joined by component identity.
  ⛔ §4's Q59-A1 (6 fields) and Q59-C2 (adopt ATTR-DESIGN Phase 6) are SUPERSEDED by §7.4/§7.5.
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

## 3. ⛔ THE ARCHITECTURAL DIAGNOSIS *(SUPERSEDED by §7.2 — kept because §7 argues against it)*

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

---

## 7. ⭐⭐⭐ THE ATTRIBUTE / DESCRIPTOR SPLIT — **user, `2026-08-26`. It changes the design, and SIMPLIFIES it.**

> ⭐⭐⭐ **User, verbatim:** *"we might need to differentiate between attributes and descriptors. attributes
> are entity-related, network agnostic. In contrary, descriptors are Ned network concept and descriptor
> compiler/translator belongs to network namespace. Does that change the design?"*
>
> ⭐⭐⭐ **Yes — materially. ⛔ And it retires three things I built in the last two steps.**

### 7.1 ⭐⭐ Applying the split to §1.1's five surfaces

| # | surface | ⭐ attribute *(FDP)* or descriptor *(NED)*? | ⚠ verdict |
|---|---|---|---|
| ① | `Build()` — path → component + setter **+ ordinal** | **attribute** | 🔴 **but it carries a DESCRIPTOR ORDINAL** |
| ② | `BuildEdgeCompiler()` — path → id + kind | **attribute** | ✅ clean |
| ③ | the three installers — id → component + setter **+ ordinal** | **attribute** | 🔴 **same leak** |
| ④ | `DescriptorMapper.MapToComponents` | ⭐ **descriptor** | ✅ **already in `Hrot.Network.NED/…/Map/Utils/`** — exactly where the user says it belongs |
| ⑤ | `ExportSchema` | **attribute** | ✅ clean |

⇒ ⭐⭐ **The split cleanly separates them, and ④ is already on the right side of the line.**
⛔ **The violation is the opposite of what I assumed: it is `DescriptorOrdinal` sitting in FDP.**

### 7.2 ⭐⭐⭐ THE CORRECTED DIAGNOSIS — **two tuples, joined by COMPONENT IDENTITY**

⛔ §3 said an attribute is a **6-tuple**. ⭐⭐ **Under the split it is TWO tuples:**

| | tuple | owner |
|---|---|---|
| ⭐ **attribute** | *(JSON path, `AttributeId`, value kind, ECS component, field setter)* — **5 fields, NO ordinal** | **FDP** |
| ⭐ **descriptor** | *(descriptor ordinal, the ECS components it covers, the wire struct)* | **NED** |
| ⭐⭐⭐ **the join** | ⭐ **the ECS COMPONENT** — the one thing both sides legitimately share, and `ComponentTypeRegistry` is already that shared identity | both |

### 7.3 ⭐⭐⭐ AND THE SEAM ALREADY EXISTS — **`IDescriptorTranslator` declares BOTH halves**

📐 **Measured `2026-08-26`** *(`FDP/Engine/Fdp.Core/Abstractions/IDescriptorTranslator.cs`)*:

```csharp
long DescriptorOrdinal { get; }                                        // line 57
IReadOnlyList<int> TargetComponentIds => System.Array.Empty<int>();    // line 75
```

⇒ ⭐⭐⭐ **the component→descriptor map is the INVERSE of what the network layer ALREADY declares, per
translator.** 📌 **This is the seam law in its purest form** — *"we need a shared X"* meant X existed.

⚠⚠ **And it is UNDER-ADOPTED: `search_graph` → 41 egress translators; only 9 declare `TargetComponentIds`.**
⛔ **With a silent EMPTY default** — 📌 the *"a production caller that HAS a dependency must PASS it"* rule,
so a derived map would be **silently sparse**. 🔴 **`EntityInfoEgressTranslator` is one of the 32 that
declare nothing — the exact descriptor `AX-015` was about.**

⭐⭐ **Note the shape `IDescriptorTranslator` already gets right, because it is the model for the fix:**
**FDP declares the SEAM** *(an opaque `long`, network-agnostic)*; **NED declares the VALUES**
*(`(long)EDescriptorType.dtWorldPos`)*. ⇒ ⭐ that is precisely what ① and ③ should have done and did not.

### 7.4 ⛔⛔ WHAT THIS RETIRES — **three things I built in the last two steps**

| built | ⛔ verdict under the split |
|---|---|
| `Fdp.Toolkit.Replication.DescriptorOrdinal` *(`AX-017`)* | ⛔ **unnecessary.** FDP should have no descriptor vocabulary at all. ⚠ I justified it as *"an ordinal is just a bit index, network-agnostic"* — **mechanically true, and it misses the point: its MEANING is a NED grouping** |
| `DescriptorOrdinalConversion` *(`AX-017`)* | ⛔ **unnecessary** — nothing left to convert |
| `IEntityPatchContext.MarkDescriptorDirty` *(`AX-015`)* | ⛔ **unnecessary.** 📐 **Measured: every one of the 5 call sites sits immediately after `GetUnmanagedComponent<T>()` on the SAME component**, and the ordinal is a constant function of that type ⇒ **100 % derivable** |

⭐⭐⭐ **`AX-015` had a strictly better fix available, and I missed it.** 📐 `EcsPatchContext` **already** holds
`_ordinalByType` and **already** calls `RecordOrdinal(typeof(T))` inside `GetUnmanagedComponent<T>()`. The
binary path failed only because `EcsPatchContext.Create` passes `s_emptyRoutes` ⇒ an **empty map**.
⇒ ⭐⭐ **give that context the map and the defect disappears with ZERO installer changes and no new seam
member.** ⚠ What I shipped works and is railed — ⛔ but it added a member to a public interface to carry
information the context could already derive.

### 7.5 ⛔⛔ `Q59-C2` IS WITHDRAWN — **`ATTR-DESIGN` Phase 6 is architecturally WRONG under the split**

⚠ §4 recommended *"finish Phase 6: route `DescriptorMapper` through the attribute compiler, in its own
batch."* ⛔ **The split says do NOT.** 📐 A descriptor is a **wire-shaped BUNDLE** *(`dtEntityInfo` carries
Name **and** ForceId together)*; an attribute is an **individually addressed FIELD**. ⇒ the two mappings
share their **field-level conversion**, ⛔ **not their addressing** — and Phase 6 conflates exactly that.
⚠ **Phase 6 predates this split**; it is not wrong so much as **written before the distinction existed.**

⭐⭐ **What IS shared, and must be:** the field-level conversion helpers — `HeadingDegToRotation`,
`IGeographicTransform.ToCartesian`, `MapAffiliation*`. ⭐ All FDP, all network-agnostic, and **both sides
should CALL them.** ⇒ 📌 **that is exactly what `F3` and `F5` are about**, so `C1` absorbs the useful half of
Phase 6 and the rest is dropped.

### 7.6 ⭐⭐ THE REVISED RECOMMENDATION

| # | ⭐ revised lean | change vs §4 |
|---|---|---|
| **`A1′`** | ⭐⭐⭐ **one `AttributeDefinition` with FIVE fields — no ordinal.** ①②③ derived from it | ⭐ **simpler than `A1`**: the hardest field to share is gone |
| **`E`** ⭐ NEW | ⭐⭐⭐ **the ordinal map is INJECTED by the network layer**, assembled from the translator registry's `{DescriptorOrdinal, TargetComponentIds}`; both patch contexts get it; `RecordOrdinal` already does the rest. ⭐ On a **networkless** host the map is legitimately **empty** — nothing to republish | ⭐ **replaces `MarkDescriptorDirty`** and retires the FDP descriptor enum |
| **`E-pre`** ⭐ NEW | ⚠⚠ **PREREQUISITE — adopt `TargetComponentIds` on the translators that need it, and RAIL it:** any translator gating on `SmartEgressUtil.ShouldPublish` with `MarkDirty` **must** declare it. ⛔ Without this the derived map is silently sparse *(9/41 today)* | 🔴 **the real cost of `E`, and it must not be hidden** |
| **`B1`** | ⭐⭐ unchanged — derive `ExportSchema` from the definitions | — |
| **`C1`** | ⭐⭐⭐ unchanged and now **more** important: fix `F3`, and make **both** the descriptor and attribute paths call the shared FDP conversion | ⭐ absorbs the sound half of Phase 6 |
| **`C2`** | ⛔⛔ **WITHDRAWN** *(§7.5)* | ⭐ was *"worth doing, separate batch"* |
| **`D1`** | ⭐ unchanged | — |

⚠⚠ **One caveat that `E` must not gloss:** ⭐ **a component maps to a SET of ordinals, not one.** 📐 Measured:
`GlobalComponentIds.SimTransform` is declared by **both** `BdcWorldPosTranslator` and
`GeoSpatialEgressTranslator`; `NetworkIdentity`/`TkbIdentity` by **both** `BdcEntityMasterTranslator` and
`EntityMasterEgressTranslator`. ⇒ marking **every** covering descriptor dirty is the correct behaviour, ⛔ but
it is a real change from today's one-ordinal-per-component and `_ordinalByType` must become
`Dictionary<int, long[]>`.

### 7.7 ⭐ REVISED SEQUENCING

| order | item | why |
|---|---|---|
| **1** | ⭐⭐⭐ `C1` — fix `F3`; both paths call the shared conversion | ⛔ a live wrong rotation outranks all refactoring |
| **2** | ⭐⭐ `E-pre` — adopt + rail `TargetComponentIds` | ⛔ `E` is unsound without it |
| **3** | ⭐⭐ `E` — inject the ordinal map; retire `MarkDescriptorDirty`, `DescriptorOrdinal`, `DescriptorOrdinalConversion` | ⭐ **removes FDP's descriptor vocabulary entirely** |
| **4** | ⭐ `A1′` + `B1` | ⭐ the attribute tuple, now 5 clean fields |

⛔ **`C2` withdrawn · `D2` not scheduled.**

---

## 8. ⭐⭐ TWO FOLLOW-UP QUESTIONS, ANSWERED WITH MEASUREMENTS *(user, `2026-08-26`)*

### 8.1 ⛔ *"the multiple tables for same thing issue — was that already solved?"* — **NO.**

📐 **Measured, current `HEAD`:** all **six** rows are still spelled out in **three** hand-maintained tables.

| | before `AX-018` | now | designed fix |
|---|---|---|---|
| tables | **4** | ⭐ **3** *(IG now calls the factory)* | ⭐⭐ **1** — `A1′` + `E` |
| enforcement | ⛔ **a code comment** | ⚠ **a rail** *(`TheFourRoutingTablesAgreeTests`)* | ⭐⭐⭐ **construction** |

⇒ ⭐⭐ **`AX-018` removed the duplicate and made drift DETECTABLE. ⛔ It did not make drift IMPOSSIBLE.**
⚠ **That is the honest status:** *"solved"* is `A1′`+`E`, which is **designed and not built.** ⛔ Do not read
the green rail as the problem being closed — 📌 it is the `R-131`-adjacent trap of mistaking a detector for a
fix.

### 8.2 ⭐⭐⭐ *"what about `Heading` vs `GeoHeading` … between json and binary way?"* — **a real trap, and it costs something measurable**

⭐⭐ **There are TWO naming mismatches, not one:**

| JSON path *(the wire-visible authoring name)* | `AttributeIds` constant | |
|---|---|---|
| `Name` · `Affiliation` | `Name` · `Affiliation` | ✅ identical |
| `GeoPosition.Latitude` / `.Longitude` / `.Altitude` | `GeoLat` · `GeoLon` · `GeoAlt` | ⚠ **dotted + full ↔ flat + abbreviated** |
| ⭐ `Heading` | 🔴 **`GeoHeading`** | 🔴 **the path LACKS the prefix the id HAS** |

#### 🔴 What it costs — **measured, `2026-08-26`**

| # | measurement |
|---|---|
| **①** | 🔴🔴 **GUESSING IS SILENT.** `{"GeoPosition":{"Heading":90.0}}` ⇒ `HasAppliedAny = **False**`, **no exception, no log**; `{"Heading":90.0}` ⇒ `True`. ⇒ ⛔ **the id name `GeoHeading` ADVERTISES a path that does not exist**, and reading it alongside the `GeoPosition.*` family leads to exactly the guess that silently does nothing. ⚠ **And the corpus says nothing about unknown paths** — the silence is **undesigned**, not deliberate forward-compat |
| **②** | 🔴 **`ExportSchema` — the one artefact that could tell a client the real paths — is MALFORMED.** 📐 Actual output: `"GeoPosition"` appears as a property key **THREE TIMES** *(the three geo paths collapse to their root segment)* and **every** type is `"string"`, including four `Float64` paths. ⇒ a consumer sees **4 properties instead of 6**, all mistyped. ⚠ **Nobody subscribes today** *(only generated reader extensions exist)* — ⛔ so it cannot even be caught in the field |

#### ⭐⭐⭐ THE ASYMMETRY THAT DECIDES THE FIX

| | on the wire? | ⇒ renaming is |
|---|---|---|
| ⭐ the **JSON path** | ✅ **YES** — external senders write it *(ExCon, the debug API, authoring JSON)* | ⛔ **a BREAKING contract change** |
| ⭐⭐⭐ the **`AttributeIds` constant NAME** | ⛔ **NO** — 📐 the wire carries the `ushort` **13**; `search` finds **no `.idl`** naming it, and all 91 `Geo*` references are C# | ✅ **FREE — source-only** |

⇒ ⭐⭐ **Fix the id name, never the path.** ⛔ And note the path `Heading` is arguably *more* correct than
`GeoPosition.Heading` would be — **heading is orientation, not position** *(the wire itself splits them:
`WorldPos.Pos` vs `WorldPos.Ori.Heading`)*.

#### ⭐ The options

| option | |
|---|---|
| **N1** ⭐⭐⭐ **RECOMMENDED — rename the constant `GeoHeading` → `Heading`** | ⭐ removes the exact trap ① describes: the id stops advertising a path that does not exist. 📐 **Source-only, ~11 files, zero wire impact**, and no `AttributeIds.Heading` exists to collide with. ⛔ Leaves `GeoLat ↔ GeoPosition.Latitude` non-derivable — ⭐ **accepted deliberately, see below** |
| **N2** make every id name mechanically derived *(path minus dots ⇒ `GeoPositionLatitude`…)* and **rail** it | ⭐ the only version that can be *enforced*. ⛔ **Not recommended:** 91 references of churn, verbose names, ⚠ **and it duplicates a guarantee `A1′` already gives** — the 5-tuple holds the path AND the id, so the definition IS the mapping. ⇒ a name rule would be a **second** mechanism enforcing the same thing |
| **N3** ⭐⭐ **RECOMMENDED alongside N1 — make the paths DISCOVERABLE instead of guessable** | ⭐ fix `ExportSchema` *(`B1`)* so it emits one property per **full** path with the **real** type, derived from `A1′`. ⇒ ⭐⭐ **the artefact answers *"what paths exist?"***, which is the actual need behind the naming question |
| **N4** ⚠ make an unregistered path LOUD | ⛔ **needs a ruling, not a lean.** ⭐ Unknown-key tolerance is genuinely valuable across mixed-version nodes ⇒ the right shape is a **count + one log line**, ⛔ never a throw. 📌 And the corpus is silent, so this is a NEW decision |

⭐⭐ **Why N1 and not N2, stated plainly:** ⛔ **name symmetry is the symptom, not the goal.** ⭐ Once `A1′`
exists, the id constant's *name* is internal convenience and the **definition** is the mapping; `ExportSchema`
is the external answer. ⇒ ⭐ N1 is worth doing anyway because it costs nothing and removes a live trap — ⛔ but
chasing full derivability would spend 91 edits to re-guarantee what one declaration already guarantees.

### 8.3 ⭐ SEQUENCING — where these land in §7.7

| order | item | change |
|---|---|---|
| **1** | `C1` — fix `F3`'s rotation | unchanged |
| **1b** ⭐ NEW | **N1** — rename `GeoHeading` → `Heading` | ⭐ **cheap and independent; fold into `C1`'s batch** |
| **2** | `E-pre` — adopt + rail `TargetComponentIds` | unchanged |
| **3** | `E` — inject the ordinal map, retire FDP's descriptor vocabulary | unchanged |
| **4** | `A1′` + `B1`/**N3** + `F4`/`F5` | ⭐ `B1` now carries the duplicate-key and wrong-type fixes |
| **?** | **N4** — loud unknown paths | ⛔ **needs your ruling first** |
