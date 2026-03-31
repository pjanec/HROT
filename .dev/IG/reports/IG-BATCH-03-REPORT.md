# IG-BATCH-03-REPORT: Resolved Styles and Component Properties

**Batch:** IG-BATCH-03  
**Tasks Completed:** IG.2.1, IG.2.2  
**Test Results:** 59 / 59 passing (includes 37 from IG-BATCH-01 + IG-BATCH-02)  
**Status:** ✅ COMPLETE

---

## Summary of Changes

### Task IG.2.1 — ResolvedStyle ECS Component

New files:

- **`Hrot.IG/Components/ForceId.cs`** — `enum ForceId : byte { Unknown=0, Friend=1, Hostile=2, Neutral=3 }`. Byte-backed so it fits in the reserved affiliation slot without widening the struct. Deliberately not aliased to `eForceIdentifier` (DDS layer) to avoid coupling the component layer to the DDS model.

- **`Hrot.IG/Components/ResolvedStyleConstants.cs`** — Centralises all named constants per §CODE-STANDARDS §1: `TextureNameMaxBytes=16`, `LabelTextMaxBytes=24`, `MaxStyleBytes=64`; per-affiliation RGBA channel constants (Friend blue, Hostile red, Neutral green, Unknown white); `DamageMin=0f`, `DamageMax=100f`. This single file is the sole source of truth for `ResolvedStyle`, `StyleResolutionSystem`, and tests.

- **`Hrot.IG/Components/ResolvedStyle.cs`** — `[StructLayout(LayoutKind.Sequential, Pack=1)]` `unsafe struct`. Layout:
  ```
  fixed byte[16]  _textureName    16 bytes
  fixed byte[24]  _labelText      24 bytes
  byte TintR/G/B/A                 4 bytes
  ForceId Affiliation (byte)       1 byte
  float DamageLevel                4 bytes
  bool ShowTrail                   1 byte
  bool ShowSensors                 1 byte
                                ─────────
                          Total:  51 bytes  (<64 MaxStyleBytes ✓)
  ```
  Exposes `CreateDefault()`, `SetTextureName/GetTextureName`, `SetLabelText/GetLabelText` with null-terminated UTF-8 codec helpers.

- **`Hrot.IG/Components/IgSymbolOverride.cs`** — Managed class component (Tier 2). Holds `StyleSetId`, `TextureOverride`, `LabelOverride`, `ShowHistory` — fields that include strings and therefore cannot live in an unmanaged Tier-1 table (IG-DEBT-008). Exposes `StyleSetHostile/StyleSetFriend/StyleSetNeutral/StyleSetUnknown` string constants used by `StyleResolutionSystem.ResolveAffiliation`.

Modified:

- **`Hrot.IG/Hrot.IG.csproj`** — Added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`. Required for the `fixed byte` buffers in `ResolvedStyle`.

---

### Task IG.2.2 — StyleResolutionSystem

New files:

- **`Hrot.IG/Systems/MapUserConfig.cs`** — Plain C# class (not an ECS component). Injected into `StyleResolutionSystem` at construction. Provides `ForceHostile` (Layer-3 override: forces all entities to Hostile/red) and `HideLabels` (Layer-3 override: clears rendered label).

- **`Hrot.IG/Systems/StyleResolutionSystem.cs`** — `[UpdateInPhase(SystemPhase.Simulation)]`, implements `IModuleSystem`. Constructor receives `MapUserConfig`. Hot path:
  1. Queries `EntityMaster` ∧ `SimTransform`.
  2. **Layer 1 (TKB):** reads `IgVisualDef` managed component (applied to entity at spawn by TKB) — extracts `SymbolCode` into texture, parses `ColorHex` into RGBA.
  2. **Layer 2 (Network):** reads `IgSymbolOverride` — maps `StyleSetId` token → `ForceId` → RGBA tint; applies `TextureOverride`, `LabelOverride`, `ShowHistory`.
  3. **Layer 3 (User config):** if `MapUserConfig.ForceHostile` → overwrite tint and affiliation to Hostile; if `MapUserConfig.HideLabels` → clear label.
  4. **Damage:** clamps `EntityDamage.Damage` into `[DamageMin, DamageMax]` via `Math.Clamp`.
  5. Calls `cmd.AddComponent` on first write, `cmd.SetComponent` on subsequent frames.

---

### Test File

- **`Hrot.IG.Tests/StyleResolutionSystemTests.cs`** — 22 new test cases (19 `[Fact]` + 1 `[Theory]` × 3 `[InlineData]`):
  - 7 structural tests for `ResolvedStyle` (size, defaults, round-trip buffers)
  - 3 TKB-layer tests (texture from `IgVisualDef`, colour decode, missing-def fallback)
  - 5 network-override tests (hostile/friendly tint, texture override, label override, trail flag)
  - 2 user-config override tests (force-hostile wins over network friend, hide-labels clears)
  - 3 damage linear-scale theory cases (0 %, 50 %, 100 %)
  - 1 missing-damage test
  - 1 second-execution overwrite test

---

## Developer Insights

### Q1: Issues resolving ResolvedStyle to < 64 bytes

The tightest constraint was fitting two string buffers — texture name and label text — into the remaining budget after the RGBA, affiliation, damage, and flag fields.

Initial allocation of `fixed byte[32]` for each string exceeded the 64-byte cap before any numeric fields were added.  The solution was to profile real data:  
- Texture names are MIL-STD-2525 symbol codes (`SFGPUCIZ-------` = 15 chars). `TextureNameMaxBytes=16` (15 payload + 1 null) is tight but sufficient.  
- Labels are short tactical identifiers (`Alpha-1`, `Bridge-404`). `LabelTextMaxBytes=24` accommodates up to 23 UTF-8 characters.

Placing the fixed buffers first in declaration order (before the scalar fields) avoided implicit struct-padding gaps that would occur if mixed with differently-aligned fields. `Pack=1` was added as an explicit safeguard — it prevents the C# compiler from inserting padding bytes if layout rules change, and makes the 51-byte size deterministic regardless of target platform.

The remaining 13 bytes of headroom (`64 - 51 = 13`) should be preserved as a guard against future additions rather than consumed immediately.

---

### Q2: Performance constraints in the Simulation loop

Two potential hot-path allocations were identified and eliminated:

1. **`StyleSetId` comparison:** Using `string.Equals(..., StringComparison.OrdinalIgnoreCase)` avoids a `ToLower()` heap allocation per entity per frame. The comparison is against four short string constants, so it short-circuits quickly.

2. **`ParseColorHex`:** The method uses `span = hex.AsSpan(1)` and `byte.TryParse(span[0..2], NumberStyles.HexNumber, ...)` — all stack operations; no substring allocations. Only a six or eight character branch is taken; anything else falls through to the white default.

3. **`Math.Clamp` for damage:** A single float clamp is cheaper than a branch tree. The JIT inlines it as a `minss`/`maxss` pair on x86-64.

One remaining concern: `view.HasManagedComponent<T>` and `view.GetManagedComponentRO<T>` are dictionary lookups on the managed component table. With a large entity count (~10 k) and three lookups per entity per frame, this could be visible. Mitigation options (tracked as future work) are:  
(a) a combined `TryGetManagedComponent<T>` to halve the lookup count, or  
(b) tagging entities with an unmanaged bitset when any managed override is present so the system can skip the lookup entirely for clean entities.

---

### Q3: Unit test design without live TKB

The TKB's role in this system is to apply `IgVisualDef` as a managed component to an entity at spawn time. The system itself only reads that component from the entity — it has no direct dependency on `ITkbDatabase` or `TkbTemplate`.

This separation means tests can replicate the TKB effect with one line:
```csharp
repo.SetManagedComponent(entity, new IgVisualDef { SymbolCode = "SFGPUCIZ-------" });
```

The test then verifies that the system correctly reads and forwards that value — which is the actual contract being tested.  Spinning up a real `TkbDatabase` + `TkbTemplate` + `EntityLifecycleModule` pipeline would be testing TKB plumbing, not style resolution logic.  That integration path is already covered by `SpawningModuleIntegrationTests`.

---

### Q4: Damage bounds edge cases

Three edge cases required handling:

1. **Missing `EntityDamage`:** The most common case for freshly spawned entities. Handled by `view.HasComponent<EntityDamage>(entity)` guard; `DamageLevel` stays at `ResolvedStyleConstants.DamageMin` (0f). Tested by `StyleResolutionSystem_MissingEntityDamage_LeavesZeroDamage`.

2. **Out-of-range damage values:** The DDS `EntityDamage.Damage` field is a `float` with no enforcement at the wire level. A buggy SimHost could send values outside `[0, 100]`. `Math.Clamp` is applied unconditionally so the visualiser always receives a value in the safe range and can use it directly as a percentage without a second bounds check.

3. **Linear pass-through vs rescaling:** The spec says "damage outputs scale linearly" — meaning `ResolvedStyle.DamageLevel` is the raw damage value `[0, 100]`, not a normalised `[0, 1]` float. Keeping it in the `[0, 100]` space matches what operators expect from the DDS topic and avoids a lossy float rescale on the write side that would have to be inverted on the read side.
