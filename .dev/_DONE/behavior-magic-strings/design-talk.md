i see in the code base there are behavior names as magic string all over the code. This is unmauntainable. Is there any more elegant way, like deriving behavior class from a marking interface or marking it with an attribute (like what category of behavior it is - infantry, insurgent etc.)  and automatically register all behaviors found and NEVER use behavior names as magic string?

Your criticism of magic strings is entirely justified. Scattering raw string literals like `"MoveToLocation"` across the codebase in `BehaviorCatalog`, `CgfBehaviorSetup`, and your UI registries is a severe violation of the Single Source of Truth principle. It guarantees future schema drift and makes refactoring brittle.

However, your proposed solution—deriving a "behavior class" from a marking interface—is architecturally flawed. In our ECS engine, behaviors are not OOP classes. They are data-driven configuration blobs (FastHSM definitions or FastBTree JSON blobs) executed by generic systems like `BTreeTickSystem` and `HsmTickSystem`. There is no `MoveToLocationBehavior` class to decorate with an attribute. 

To solve this elegantly without violating the data-oriented design, you must anchor the metadata to the only C# types that map 1:1 with specific behaviors: the **Parameter DTOs** (e.g., `FireAtTargetParamsJsonDto`).

Here is the clean architecture approach to eliminate the magic strings via auto-discovery.

### 1. Define the Contract Attribute & Categories
Introduce a `BehaviorContractAttribute` in your `Hrot.Core` domain (alongside the DTOs) to encapsulate the integer ID, the string ID, and the valid entity categories.

```csharp
[Flags]
public enum BehaviorCategory
{
    None = 0,
    Civilian = 1 << 0,
    MilitaryApc = 1 << 1,
    Infantry = 1 << 2,
    Insurgent = 1 << 3,
    AllMilitary = MilitaryApc | Infantry | Insurgent
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class BehaviorContractAttribute : Attribute
{
    public int BehaviorId { get; }
    public string BehaviorId { get; }
    public BehaviorCategory ValidCategories { get; }

    public BehaviorContractAttribute(int behaviorId, string behaviorId, BehaviorCategory categories)
    {
        BehaviorId = behaviorId;
        BehaviorId = behaviorId;
        ValidCategories = categories;
    }
}
```

### 2. Decorate the DTOs
Apply this attribute to your parameter DTOs. For parameterless behaviors (like `WanderCivil` or `Idle_HSM`), you must create empty marker DTO classes. This creates a unified type system for all behaviors.

```csharp
[BehaviorContract(CgfBehaviorIds.FireAtTarget_BT, "FireAtTarget", BehaviorCategory.AllMilitary)]
public class FireAtTargetParamsJsonDto { ... }

[BehaviorContract(CgfBehaviorIds.MoveTo_BT, "MoveToLocation", BehaviorCategory.AllMilitary)]
public class MoveToLocationParamsJsonDto { ... }

[BehaviorContract(BehaviorIds.WanderCivil, "WanderCivil", BehaviorCategory.Civilian)]
public class WanderCivilParamsJsonDto { } // Empty DTO acts as the schema anchor
```

### 3. Build the Auto-Discovery Engine (Cold Path)
Replace the manual string-based registrations in `BehaviorUiSetup` and `CgfBehaviorSetup` with a reflection-based discovery pass that runs exactly once at application startup.

```csharp
public static class BehaviorSchemaDiscovery
{
    public static void AutoRegister(BehaviorUiRegistry uiRegistry, ScenarioBehaviorRemapper remapper)
    {
        // Obtain the open generic Register<T> methods
        var uiRegMethod = typeof(BehaviorUiRegistry).GetMethod(nameof(BehaviorUiRegistry.Register))!;
        var remapMethod = typeof(ScenarioBehaviorRemapper).GetMethod(nameof(ScenarioBehaviorRemapper.Register))!;

        var dtoTypes = typeof(BehaviorContractAttribute).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<BehaviorContractAttribute>() != null);

        foreach (var type in dtoTypes)
        {
            var attr = type.GetCustomAttribute<BehaviorContractAttribute>()!;

            // Dynamically invoke the generic registration methods
            uiRegMethod.MakeGenericMethod(type).Invoke(uiRegistry, new object[] { attr.BehaviorId });
            remapMethod.MakeGenericMethod(type).Invoke(remapper, new object[] { attr.BehaviorId });
        }
    }
}
```

### 4. Dynamic Behavior Catalog
Finally, eradicate the hardcoded string arrays in `BehaviorCatalog`. The catalog should build itself by reflecting over the `[BehaviorContract]` attributes, grouping the valid `BehaviorId` strings by `BehaviorCategory`.

```csharp
public static class BehaviorCatalog
{
    private static readonly Dictionary<BehaviorCategory, List<string>> _categoryMap = BuildMap();

    private static Dictionary<BehaviorCategory, List<string>> BuildMap()
    {
        var map = new Dictionary<BehaviorCategory, List<string>>();
        foreach (var cat in Enum.GetValues<BehaviorCategory>())
            map[cat] = new List<string>();

        var dtos = typeof(BehaviorContractAttribute).Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<BehaviorContractAttribute>())
            .Where(a => a != null);

        foreach (var attr in dtos)
        {
            foreach (var cat in Enum.GetValues<BehaviorCategory>())
            {
                if (cat != BehaviorCategory.None && attr!.ValidCategories.HasFlag(cat))
                    map[cat].Add(attr.BehaviorId);
            }
        }
        return map;
    }

    public static IReadOnlyList<string> GetValidBehaviors(long tkbType)
    {
        var category = MapTkbTypeToCategory(tkbType);
        return _categoryMap.TryGetValue(category, out var list) ? list : _categoryMap[BehaviorCategory.None];
    }

    private static BehaviorCategory MapTkbTypeToCategory(long tkbType) => tkbType switch
    {
        TkbEntityTypes.CivilianPedestrian => BehaviorCategory.Civilian,
        TkbEntityTypes.MilitaryApc => BehaviorCategory.MilitaryApc,
        TkbEntityTypes.Insurgent => BehaviorCategory.Insurgent,
        _ => BehaviorCategory.None
    };
}
```

### Architectural Summary
By adopting this pattern, the DTO class becomes the absolute Single Source of Truth for a behavior's schema, string identifier, numeric ID, and tactical applicability. 

The `ScenarioBehaviorRemapper`, the `BehaviorUiRegistry`, and the `BehaviorCatalog` will automatically absorb new behaviors simply by defining the DTO and compiling the project. The reflection overhead is amortised entirely into the startup phase, preserving your strict zero-allocation budget on the hot path.




The search for behavior name magic strings reveals that the magic string is leaking across four distinct architectural boundaries. Here is how the attribute-driven approach eliminates the magic string in each of them:

### 1. Composition Roots (`BehaviorUiSetup.cs`, `CgfBehaviorSetup.cs`)
**Current state:** Manual `Register<FireAtTargetParamsJsonDto>("FireAtTarget")` calls.
**Resolution:** These files will no longer contain manual registrations. The `BehaviorSchemaDiscovery.AutoRegister()` method extracts the string directly from the `[BehaviorContract]` attribute and dynamically invokes the generic `Register<T>` methods. The magic strings are eradicated here entirely.

### 2. Domain Catalogs (`BehaviorCatalog.cs`)
**Current state:** Hardcoded string arrays like `s_militaryApcBehaviors = ["ConvoyEscort", "MoveToLocation", "FollowRoute", "FireAtTarget"]`.
**Resolution:** The static dictionaries are eradicated. As shown in the previous design, `BehaviorCatalog.BuildMap()` reflects over the `BehaviorCategory` bitmask defined in the attribute and builds the lists automatically.

### 3. FastBTree Asset Definitions (`CgfNodes.cs`)
**Current state:** Hardcoded JSON strings like `"TreeName": "FireAtTarget"`.
**Architectural Critique:** The `TreeName` inside a FastBTree JSON blob is fundamentally a different domain concept than the network `BehaviorId`. It is the internal asset identifier used by `TreeCompiler.CompileFromJson`. While they currently share the same string value, coupling an internal AI tree name directly to a network wire contract is a leaky abstraction. 
**Resolution:** If you insist on unifying them to avoid duplication, you must transition your `const string` JSON blobs to interpolated strings or use a compiled constant. However, the cleanest approach is to define a public `const string` inside your DTO, and reference it in both the attribute and the JSON template:

```csharp
[BehaviorContract(CgfBehaviorIds.FireAtTarget_BT, BehaviorId, BehaviorCategory.AllMilitary)]
public class FireAtTargetParamsJsonDto 
{ 
    public const string BehaviorId = "FireAtTarget"; 
    // ... properties
}

// In CgfNodes.cs:
private static readonly string FireAtTargetJson = $$"""
{
  "TreeName": "{{FireAtTargetParamsJsonDto.BehaviorId}}",
  "Root": { ... }
}
""";
```

### 4. Unit Tests (`BehaviorRemappingTests.cs`, `MissionPanelTests.cs`, etc.)
**Current state:** Tests are littered with hardcoded string assertions and setups.
**Architectural Critique:** Duplicating magic strings in your test suite creates brittle tests that will fail when the domain schema evolves. Tests must validate the behavior of the system, not re-declare the system's magic strings.
**Resolution:** Your tests must query the schema directly from the DTO. You should implement a test helper to extract the `BehaviorId` from the type:

```csharp
public static class BehaviorTestHelper
{
    public static string GetBehaviorId<TDto>()
    {
        var attr = typeof(TDto).GetCustomAttribute<BehaviorContractAttribute>();
        return attr?.BehaviorId ?? throw new InvalidOperationException("Missing BehaviorContractAttribute");
    }
}
```

Then update your tests to eliminate the magic string:
```csharp
// Before
remapper.Register<FireAtTargetParamsJsonDto>("FireAtTarget");
var result = remapper.RemapJson("FireAtTarget", json, map);

// After
string behaviorId = BehaviorTestHelper.GetBehaviorId<FireAtTargetParamsJsonDto>();
remapper.Register<FireAtTargetParamsJsonDto>(behaviorId);
var result = remapper.RemapJson(behaviorId, json, map);
```

By establishing the DTO and its `[BehaviorContract]` attribute as the absolute Single Source of Truth, you eliminate the string literals from your composition roots, UI catalogs, and test suites simultaneously.

