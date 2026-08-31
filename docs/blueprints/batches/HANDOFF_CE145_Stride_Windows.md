<!--STATUS
state: LIVE
updated: 2026-08-31
current-answer: this whole file. It is a HANDOFF to a WINDOWS/Visual-Studio session for the three items
  the Linux cloud session cannot do. It is a dispatch pointer, not a design — the design is
  docs/DESIGN_Entity_Creation_Unification.md §3.3 (AS-BUILT block).
stale-below: nothing.
-->
# HANDOFF — `CE-145` + the Stride-tree verification *(WINDOWS / Visual Studio session)*

> 🔒 **Dispatched at `f27717262`.** Your scope is FROZEN at that sha. Documents that change after it are
> **FYI only** — if a later document appears to invalidate an item here, **STOP that item and report it**;
> ⛔ do not adapt and do not revert.

⭐ **Why you exist:** the Linux cloud session cannot build the Stride tree
*(`Microsoft.WindowsDesktop.App` is unavailable)*, so **8 files** in `Stride/**` are only ever
*statically* checked there. These three items all need a machine that can actually compile it.

📄 **Read first:** `docs/DESIGN_Entity_Creation_Unification.md` **§3.3's AS-BUILT block** *(what was just
built and the three premises that turned out false)*. ⭐ Also obey **RULE ZERO** in `.claude/CLAUDE.md`:
read `docs/blueprints/RULINGS.md` in full, then run `python3 scripts/design-digest.py` and
`python3 scripts/rulings-check.py`.

## 0. ⭐ Branch — **use your OWN branch, do not push to the UI lane**

```bash
git fetch origin claude/reset-working-branch-qd1qpv
git checkout -B claude/ce145-stride-namespace-win f27717262
git commit --allow-empty -m "chore: started CE-145 Windows batch at f27717262"   # rule 1b marker
git push -u origin claude/ce145-stride-namespace-win
```

⛔⛔ **Do NOT push to `claude/reset-working-branch-qd1qpv`.** The cloud session is working on it
concurrently and will **merge your branch** when it is green. 📌 Reason: exactly one file is in both
lanes' reach — see §4.

## 1. 🔴🔴 FIRST, BEFORE ANY RENAME — **does the Stride tree still build?**

⭐⭐ **This is the highest-value thing you can do, and it is a clean checkpoint on its own.** Commit 4 of
the cloud session's work (`f27717262`, pack step 4) moved ten animation TKB descriptor types into
`Fdp.Toolkits` and rewrote `UrbanCombatNewScenario`. ⚠ **8 Stride-tree files reference those types and
were never compiled.**

```
Stride/Hrot.Stride.Animation/StrideAnimationBridge.cs
Stride/Hrot.Stride.Animation/StrideAnimationBackend.cs
Stride/Hrot.Stride.Animation.Tests/StrideAnimationBackendBehaviorTests.cs
Stride/Hrot.Stride.Core/BulletCharacterMotor.cs
Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs
Stride/HrotStrideApp.Game/MannequinAnimationBinder.cs
Stride/HrotStrideApp.Game/StrideAnimationHarnessCases.cs
Stride/HrotStrideApp.Game.Tests/MannequinAnimationDefIntegrationTests.cs
```

| ⭐ do this | |
|---|---|
| **①** | build the Stride solution/projects **as-is at `f27717262`**, with **no changes** |
| **②** | run `MannequinAnimationDefIntegrationTests` and the Stride animation tests |
| **③** | ⭐⭐ **REPORT THE RESULT EVEN IF GREEN** — *"Stride builds at `f27717262`"* is the fact the cloud session cannot obtain, and it de-risks everything after it |

⚠ **Expected to be green** — the move preserved namespaces precisely so nothing downstream changes. ⛔ If
it is red, **stop and report before doing §2**: the rename would bury the cause.

## 2. ⭐⭐ `CE-145` — rename the moved types' namespaces

📐 **What moved** *(cloud session, `f27717262`)* — ten types, from `Hrot.MuscleCharacter.Animation` into
**`FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/`**, keeping their old namespaces so nothing had to change:

| file | types | current namespace |
|---|---|---|
| `Fdp.Toolkits/Tkb/Domain/CharacterAnimationDefDto.cs` | `CharacterAnimationDefDto`, `SlotDefDto`, `MontageDefDto`, `MontageNotifyRefDto`, `NotifyMarkerDefDto`, `StanceTransitionDto`, `AimConfigDto`, `SlotCompositingMode` | `Hrot.MuscleCharacter.Animation.Descriptors` |
| `Fdp.Toolkits/Tkb/Domain/AnimNotifyCategory.cs` | `AnimNotifyCategory` | `Hrot.MuscleCharacter.Animation.Contracts` |
| `Fdp.Toolkits/Tkb/Domain/StanceId.cs` | `StanceId` | `Hrot.MuscleCharacter.Animation.Components` |

⇒ 🔴 **The smell to remove:** `Hrot.*` namespaces now live inside `Fdp.Toolkits.dll`, which is a layering
lie — FDP is the lower layer. **Target namespace: `Fdp.Toolkit.Tkb.Domain`** *(what every neighbouring TKB
descriptor DTO in that folder already uses — see `SensorCapabilitiesDto.cs`)*.

### ⚠ Scope — **regenerate the list, do not trust a number**

⛔ **The cloud session quoted this as 24 files, then 53, then 56** — each time it added a type name it had
missed. ⭐ **Get the real set yourself:**

```bash
grep -rlE '\b(CharacterAnimationDefDto|SlotDefDto|MontageDefDto|MontageNotifyRefDto|NotifyMarkerDefDto|StanceTransitionDto|AimConfigDto|SlotCompositingMode|StanceId|AnimNotifyCategory)\b' \
  --include=*.cs . | grep -v '/obj/\|/bin/' | sort
```

⭐ **Prefer Visual Studio's refactor** *("Move to namespace" / Rename)* over find-and-replace — it fixes
`using` directives and fully-qualified references it can see, which is the whole reason this item waited
for a VS session.

⚠ **Watch for these, which a blind replace will get wrong:**

| ⚠ | |
|---|---|
| **the three moved files' own header comments** | each carries a block explaining *"NAMESPACE IS DELIBERATELY … not `Fdp.Toolkit.Tkb.Domain`"* and citing `CE-145`. ⭐ **Delete or rewrite those blocks** — after your change they are false |
| `Hrot.MuscleCharacter.Animation/Components/ReplicatedComponents.cs` | carries a comment saying `StanceId` moved out; update it to name the new namespace |
| **fully-qualified uses** | e.g. `Hrot.MuscleCharacter.Animation.Descriptors.CharacterAnimationDefDto` written out in full, and in **XML doc `<see cref=…>`** — ⚠ a broken cref is a warning that survives the batch |
| `Hrot/Engine/Hrot.Core/Tkb/UrbanCombatTkbCatalog.cs` | has three `using Hrot.MuscleCharacter.Animation.*;` lines that collapse to one `using Fdp.Toolkit.Tkb.Domain;` |
| `FDP/Examples/Fdp.Examples.Scenarios/Fdp.Examples.Scenarios.csproj` | its `Hrot.MuscleCharacter.Animation` ProjectReference carries a note saying it *"may now be redundant"*. ⭐ **Check and drop it if nothing else in that project needs the subsystem** — that is a real cleanup this item enables |

✅ **Low risk, measured:** `grep` found **no** code serialising these types by `FullName` /
`AssemblyQualifiedName`, and `[TkbDescriptor("Anim.CharacterDef")]` is a **stable string** attribute, not a
type name. ⇒ **no wire, file or TKB-JSON impact expected.** ⚠ Still confirm no scenario/TKB asset embeds a
namespace string.

## 3. ⭐ `EditorStrideSubsystem` — join the shared catalogue

📐 **Measured at `f27717262`, `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs:~584`:**

```csharp
TkbDb = new TkbDatabase();
UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(TkbDb);
```

⇒ ⚠ **This host builds its own bare database**, so it gets the UrbanCombat templates but **misses
`NedTkbCatalog` and the route templates that `HrotEnvironment.CreateTkb()` seeds.** ⭐ Unchanged from
before step 4, so **not a regression** — but it is the one host still outside the shared catalogue.

| ⭐ change | |
|---|---|
| **①** | `TkbDb = Hrot.Map.Common.HrotEnvironment.CreateTkb();` |
| **②** | ⛔⛔ **DELETE the `RegisterUrbanCombatTkbTemplates(TkbDb)` line.** 🔴 `TkbDatabase.Register` **THROWS** on a duplicate name or type *(`Fdp.Toolkits/Tkb/TkbDatabase.cs:24-28`)*, and `CreateTkb()` already seeds those five ⇒ **leaving it in crashes the Stride editor at startup.** 📌 This is exactly the defect that removed the same call from `EditorSubsystem` |
| **③** | ⭐ **Run the Stride editor** and confirm UrbanCombat entities still get their visuals — the templates now carry `StrideRenderModelDefDto` on **all five** *(the drifted render-less copy was deleted in `f27717262`)*, so this is where that fix gets its first real exercise |

⚠ **Behaviour change to expect and verify, not to fear:** the Stride editor's catalogue **gains**
`NedTkbCatalog`'s templates and the route plan. ⭐ Additive content; per `tkb-1/DESIGN.md` §6.5b **gate ②**
a node that does not register a component silently skips it.

## 4. ⛔⛔ FENCES — **the one file both lanes can reach**

| ⭐ **YOURS** | ⛔ **NOT yours — the cloud session is editing these** |
|---|---|
| all of `Stride/**` | `Hrot/Subsystems/Hrot.CGF/Systems/**` *(obstacle ① moves the request systems)* |
| the `CE-145` rename, anywhere it reaches | `Hrot/Engine/Hrot.Core/Network/**` *(their destination)* |
| `Fdp.Examples.Scenarios.csproj`'s redundant reference | `docs/DESIGN_Entity_Creation_Unification.md` · `docs/blueprints/Architect_Question_65_*.md` · `docs/blueprints/RESUME_UI_Lane.md` |

⚠⚠ **`Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` is the collision point** — the cloud session's
next item *(obstacle ①)* would otherwise need to update its `using` for
`CreateEntityRequestSystem` at `:600`. 🔒 **Agreed split: the cloud session will NOT touch `Stride/**` at
all**; it will leave that one `using` stale on its branch and fix it after merging yours. ⇒ ⭐ **the file is
100% yours.**

⭐ **If your rename needs to touch a file in the right-hand column, STOP and report** — do not edit it.
📌 `R-106`: stop *that item*, not the batch; do everything else.

## 5. ⭐ Report back — the gate contract

📄 `.claude/CLAUDE.md`'s **GATE REPORT CONTRACT**. Per item:

| # | report |
|---|---|
| **1** | one row per gate: **verbatim command · pass/fail/skip counts · delta vs baseline** |
| **2** | ⭐ a `--no-build` column, and which gates had to build |
| **3** | ⭐⭐ **the Stride result at `f27717262` BEFORE any change** *(§1)* — state it even if green |
| **4** | ⭐ every RED **confirmed pre-existing against `f27717262`**, named |
| **5** | working tree clean after every suite run |
| **6** | ⭐⭐ **the actual file count the rename touched**, and any type name the grep above missed |
| **7** | ⭐ `python3 scripts/rulings-check.py` and `python3 scripts/design-digest.py --check` |
| **8** | ⭐ any `EditorStrideSubsystem` behaviour difference you saw **running the editor**, not just compiling |

⭐ **Then fold the as-built into the design** *(obligation ⑤)*: `CE-145` is named in
`DESIGN_Entity_Creation_Unification.md` §3.3 and in the three moved files' headers — **update all of them
to say the rename is DONE**, and mark this handoff `state: SUPERSEDED`.

⛔ **Do not open a pull request.** Push the branch and tell the user; the cloud session merges it.
