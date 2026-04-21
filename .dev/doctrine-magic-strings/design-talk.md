i see in the code base there are doctrine names as magic string all over the code. This is unmauntainable. Is there any more elegant way, like deriving doctrine class from a marking interface or marking it with an attribute (like what category of doctrine it is - infantry, insurgent etc.)  and automatically register all doctrines found and NEVER use doctrine names as magic string?

Your criticism of magic strings is entirely justified. Scattering raw string literals like `"MoveToLocation"` across the codebase in `DoctrineCatalog`, `CgfDoctrineSetup`, and your UI registries is a severe violation of the Single Source of Truth principle. It guarantees future schema drift and makes refactoring brittle.

However, your proposed solution—deriving a "doctrine class" from a marking interface—is architecturally flawed. In our ECS engine, doctrines are not OOP classes. They are data-driven configuration blobs (FastHSM definitions or FastBTree JSON blobs) executed by generic systems like `BTreeTickSystem` and `HsmTickSystem`. There is no `MoveToLocationDoctrine` class to decorate with an attribute. 

To solve this elegantly without violating the data-oriented design, you must anchor the metadata to the only C# types that map 1:1 with specific doctrines: the **Parameter DTOs** (e.g., `FireAtTargetParamsJsonDto`).

Here is the clean architecture approach to eliminate the magic strings via auto-discovery.

### 1. Define the Contract Attribute & Categories
Introduce a `DoctrineContractAttribute` in your `Hrot.Core` domain (alongside the DTOs) to encapsulate the integer ID, the string ID, and the valid entity categories.

```csharp
[Flags]
public enum DoctrineCategory
{
    None = 0,
    Civilian = 1 << 0,
    MilitaryApc = 1 << 1,
    Infantry = 1 << 2,
    Insurgent = 1 << 3,
    AllMilitary = MilitaryApc | Infantry | Insurgent
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DoctrineContractAttribute : Attribute
{
    public int DoctrineId { get; }
    public string BehaviorId { get; }
    public DoctrineCategory ValidCategories { get; }

    public DoctrineContractAttribute(int doctrineId, string behaviorId, DoctrineCategory categories)
    {
        DoctrineId = doctrineId;
        BehaviorId = behaviorId;
        ValidCategories = categories;
    }
}
```

### 2. Decorate the DTOs
Apply this attribute to your parameter DTOs. For parameterless doctrines (like `WanderCivil` or `Idle_HSM`), you must create empty marker DTO classes. This creates a unified type system for all doctrines.

```csharp
[DoctrineContract(CgfDoctrineIds.FireAtTarget_BT, "FireAtTarget", DoctrineCategory.AllMilitary)]
public class FireAtTargetParamsJsonDto { ... }

[DoctrineContract(CgfDoctrineIds.MoveTo_BT, "MoveToLocation", DoctrineCategory.AllMilitary)]
public class MoveToLocationParamsJsonDto { ... }

[DoctrineContract(DoctrineIds.WanderCivil, "WanderCivil", DoctrineCategory.Civilian)]
public class WanderCivilParamsJsonDto { } // Empty DTO acts as the schema anchor
```

### 3. Build the Auto-Discovery Engine (Cold Path)
Replace the manual string-based registrations in `BehaviorUiSetup` and `CgfDoctrineSetup` with a reflection-based discovery pass that runs exactly once at application startup.

```csharp
public static class DoctrineSchemaDiscovery
{
    public static void AutoRegister(BehaviorUiRegistry uiRegistry, ScenarioBehaviorRemapper remapper)
    {
        // Obtain the open generic Register<T> methods
        var uiRegMethod = typeof(BehaviorUiRegistry).GetMethod(nameof(BehaviorUiRegistry.Register))!;
        var remapMethod = typeof(ScenarioBehaviorRemapper).GetMethod(nameof(ScenarioBehaviorRemapper.Register))!;

        var dtoTypes = typeof(DoctrineContractAttribute).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<DoctrineContractAttribute>() != null);

        foreach (var type in dtoTypes)
        {
            var attr = type.GetCustomAttribute<DoctrineContractAttribute>()!;

            // Dynamically invoke the generic registration methods
            uiRegMethod.MakeGenericMethod(type).Invoke(uiRegistry, new object[] { attr.BehaviorId });
            remapMethod.MakeGenericMethod(type).Invoke(remapper, new object[] { attr.BehaviorId });
        }
    }
}
```

### 4. Dynamic Doctrine Catalog
Finally, eradicate the hardcoded string arrays in `DoctrineCatalog`. The catalog should build itself by reflecting over the `[DoctrineContract]` attributes, grouping the valid `BehaviorId` strings by `DoctrineCategory`.

```csharp
public static class DoctrineCatalog
{
    private static readonly Dictionary<DoctrineCategory, List<string>> _categoryMap = BuildMap();

    private static Dictionary<DoctrineCategory, List<string>> BuildMap()
    {
        var map = new Dictionary<DoctrineCategory, List<string>>();
        foreach (var cat in Enum.GetValues<DoctrineCategory>())
            map[cat] = new List<string>();

        var dtos = typeof(DoctrineContractAttribute).Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<DoctrineContractAttribute>())
            .Where(a => a != null);

        foreach (var attr in dtos)
        {
            foreach (var cat in Enum.GetValues<DoctrineCategory>())
            {
                if (cat != DoctrineCategory.None && attr!.ValidCategories.HasFlag(cat))
                    map[cat].Add(attr.BehaviorId);
            }
        }
        return map;
    }

    public static IReadOnlyList<string> GetValidDoctrines(long tkbType)
    {
        var category = MapTkbTypeToCategory(tkbType);
        return _categoryMap.TryGetValue(category, out var list) ? list : _categoryMap[DoctrineCategory.None];
    }

    private static DoctrineCategory MapTkbTypeToCategory(long tkbType) => tkbType switch
    {
        TkbEntityTypes.CivilianPedestrian => DoctrineCategory.Civilian,
        TkbEntityTypes.MilitaryApc => DoctrineCategory.MilitaryApc,
        TkbEntityTypes.Insurgent => DoctrineCategory.Insurgent,
        _ => DoctrineCategory.None
    };
}
```

### Architectural Summary
By adopting this pattern, the DTO class becomes the absolute Single Source of Truth for a doctrine's schema, string identifier, numeric ID, and tactical applicability. 

The `ScenarioBehaviorRemapper`, the `BehaviorUiRegistry`, and the `DoctrineCatalog` will automatically absorb new doctrines simply by defining the DTO and compiling the project. The reflection overhead is amortised entirely into the startup phase, preserving your strict zero-allocation budget on the hot path.




The search for doctrine name magic strings reveals that the magic string is leaking across four distinct architectural boundaries. Here is how the attribute-driven approach eliminates the magic string in each of them:

### 1. Composition Roots (`BehaviorUiSetup.cs`, `CgfDoctrineSetup.cs`)
**Current state:** Manual `Register<FireAtTargetParamsJsonDto>("FireAtTarget")` calls.
**Resolution:** These files will no longer contain manual registrations. The `DoctrineSchemaDiscovery.AutoRegister()` method extracts the string directly from the `[DoctrineContract]` attribute and dynamically invokes the generic `Register<T>` methods. The magic strings are eradicated here entirely.

### 2. Domain Catalogs (`DoctrineCatalog.cs`)
**Current state:** Hardcoded string arrays like `s_militaryApcDoctrines = ["ConvoyEscort", "MoveToLocation", "FollowRoute", "FireAtTarget"]`.
**Resolution:** The static dictionaries are eradicated. As shown in the previous design, `DoctrineCatalog.BuildMap()` reflects over the `DoctrineCategory` bitmask defined in the attribute and builds the lists automatically.

### 3. FastBTree Asset Definitions (`CgfNodes.cs`)
**Current state:** Hardcoded JSON strings like `"TreeName": "FireAtTarget"`.
**Architectural Critique:** The `TreeName` inside a FastBTree JSON blob is fundamentally a different domain concept than the network `BehaviorId`. It is the internal asset identifier used by `TreeCompiler.CompileFromJson`. While they currently share the same string value, coupling an internal AI tree name directly to a network wire contract is a leaky abstraction. 
**Resolution:** If you insist on unifying them to avoid duplication, you must transition your `const string` JSON blobs to interpolated strings or use a compiled constant. However, the cleanest approach is to define a public `const string` inside your DTO, and reference it in both the attribute and the JSON template:

```csharp
[DoctrineContract(CgfDoctrineIds.FireAtTarget_BT, BehaviorId, DoctrineCategory.AllMilitary)]
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
public static class DoctrineTestHelper
{
    public static string GetBehaviorId<TDto>()
    {
        var attr = typeof(TDto).GetCustomAttribute<DoctrineContractAttribute>();
        return attr?.BehaviorId ?? throw new InvalidOperationException("Missing DoctrineContractAttribute");
    }
}
```

Then update your tests to eliminate the magic string:
```csharp
// Before
remapper.Register<FireAtTargetParamsJsonDto>("FireAtTarget");
var result = remapper.RemapJson("FireAtTarget", json, map);

// After
string behaviorId = DoctrineTestHelper.GetBehaviorId<FireAtTargetParamsJsonDto>();
remapper.Register<FireAtTargetParamsJsonDto>(behaviorId);
var result = remapper.RemapJson(behaviorId, json, map);
```

By establishing the DTO and its `[DoctrineContract]` attribute as the absolute Single Source of Truth, you eliminate the string literals from your composition roots, UI catalogs, and test suites simultaneously.

