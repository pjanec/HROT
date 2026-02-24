# Code Standards — Standing Rules

Quick reference. Apply these rules in all non-test `.cs` files unless explicitly stated otherwise.

---

## 1. No Magic Numbers in Production Code

Every literal that represents a domain concept must be a named constant. No exceptions in production code.

| Category | ❌ Bad | ✅ Good |
|---|---|---|
| Fixed buffer sizes | `fixed byte Params[32]` | `fixed byte Params[BehaviorConstants.ActionParamsByteSize]` |
| Size budgets in tests | `Assert.True(size <= 96)` | `Assert.True(size <= BehaviorConstants.MaxChannelSizeBytes)` |
| Lookup table capacity | `new IActionExecutor[64]` | `new IActionExecutor[BehaviorConstants.MaxActionTypes]` |
| Grid dimensions | `SpatialHashGrid.Create(150, 150, 5.0f, ...)` | Use `const int GridWidth = 150; const float CellSizeM = 5.0f;` |
| Numeric states | `if (channel.ActiveAction != 0)` | Use an enum or named `const` |

**Rule of thumb:** Changing a buffer size or threshold should be a **one-line edit** to a named constant.
If it requires a grep, there are magic numbers.

**Tests are exempt** for simple assertions of expected numeric outcomes (e.g., `Assert.Equal(28, Unsafe.SizeOf<SimTransform>())`).
But when a production constant already exists, tests should reference it.

---

## 2. Coordinate System — Use SimMath

The FDP world is **right-handed, X=east, Y=north, Z=up**.

| Term | Axis | Zero direction |
|---|---|---|
| Yaw | Z | East (+X) |
| Pitch | Y | Horizontal (0 = level) |
| Roll | X | Level (0 = level) |

`System.Numerics.Quaternion.CreateFromYawPitchRoll` uses a **different convention** (yaw around Y).
**It is banned in production code.**

Always use:
```csharp
SimMath.FromYaw(yawRad)                                          // ground vehicles
SimMath.FromYawPitchRoll(y, p, r)                               // full 3D
SimMath.FacingNorth / FacingEast / FacingWest / FacingSouth     // tests / setup
```

`Quaternion.Identity` == facing east == `SimMath.FacingEast`. Use whichever is clearer.

---

## 3. ECS Mutation — GetComponentRW vs GetComponentRO

| Context | Read | Write |
|---|---|---|
| **Main-thread synchronous system** | `World.GetComponent<T>` or `GetComponentRW<T>` | `ref var c = ref World.GetComponentRW<T>(e);` — in-place, zero copy |
| **Async / background module** (read-only snapshot) | `view.GetComponentRO<T>(e)` | `cmd.SetComponent(e, modifiedCopy)` via `IEntityCommandBuffer` |

**Rules:**
- Background systems **never touch the main world directly**
- Background systems **never write to the snapshot**
- Copy cost through command buffer (~32–96 bytes) is intentional — do not circumvent it
- Do NOT add `GetComponentRW` to `ISimulationView` — it is intentionally absent

---

## 4. Zero Allocation on Hot Path

- No `new` inside `OnUpdate` loops
- Pre-allocate all arrays, pools, and scratch buffers in `OnCreate`
- Use `stackalloc` for small temporary spans (neighbour lists, etc.)
- No LINQ in simulation loops (`foreach` over a query is fine; `.Where().Select()` is not)

---

## 5. Component Design Rules

- All ECS components are **unmanaged value types** (`struct`, no reference fields)
- `[StructLayout(LayoutKind.Sequential)]` on every component struct
- Use `fixed byte` buffers with **named size constants** (see §1)
- Stay within the 256-component kernel limit — use inline fixed buffers (`Params[N]`) rather than many tiny components
