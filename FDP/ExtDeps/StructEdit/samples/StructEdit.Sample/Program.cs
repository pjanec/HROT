using StructEdit.Core;
using StructEdit.Json;
using StructEdit.Reflection;
using StructEdit.Reflection.Editors;

Console.WriteLine("=== StructEdit Sample Application ===");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────────────────────
// S1 — Basic struct editing (WholeComponent scope)
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("--- Scenario 1: Basic struct editing ---");
{
    var service = new ComponentEditServiceBuilder().Build();
    using var session = service.Open(
        new Bullet { Speed = 100f, Damage = 50, IsActive = true },
        typeof(Bullet));

    PrintDocument(session.Document);

    var speedNode = session.Document.Root.Children.First(n => n.Name == "Speed");
    speedNode.Binding!.SetBoxed(200f);

    var result = (Bullet)session.Commit();
    Console.WriteLine($"[S1] Speed={result.Speed} Damage={result.Damage} Active={result.IsActive}");
}

Console.WriteLine();

// ─────────────────────────────────────────────────────────────────────────────
// S2 — Scoped editing (ForField, single property)
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("--- Scenario 2: Scoped editing ---");
{
    var service = new ComponentEditServiceBuilder().Build();
    using var session = service.Open(
        new Character { Level = 5, Health = 100f, Mana = 80f },
        typeof(Character),
        scope: EditScope.ForField("$.Health"));

    PrintDocument(session.Document);
    Console.WriteLine($"[S2] Root children count: {session.Document.Root.Children.Count}");
}

Console.WriteLine();

// ─────────────────────────────────────────────────────────────────────────────
// S3 — Record editing
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("--- Scenario 3: Record editing ---");
{
    var service = new ComponentEditServiceBuilder().Build();
    var stats = new PlayerStats(100, 3, false);
    using var session = service.Open(stats, typeof(PlayerStats));

    var scoreNode = session.Document.Root.Children.First(n => n.Name == "Score");
    scoreNode.Binding!.SetBoxed(999);

    var result = (PlayerStats)session.Commit();
    Console.WriteLine($"[S3] Score={result.Score} Lives={result.Lives} GameOver={result.GameOver}");
}

Console.WriteLine();

// ─────────────────────────────────────────────────────────────────────────────
// S4 — Validation + error handling
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("--- Scenario 4: Validation + error handling ---");
{
    var service = new ComponentEditServiceBuilder()
        .RegisterValidator<WeaponConfig>(new WeaponValidator())
        .Build();

    using var session = service.Open(
        new WeaponConfig { Damage = 100, FireRate = 5f },
        typeof(WeaponConfig));

    var dmgNode = session.Document.Root.Children.First(n => n.Name == "Damage");
    dmgNode.Binding!.SetBoxed(9999);

    try
    {
        session.Commit();
    }
    catch (EditValidationException ex)
    {
        Console.WriteLine($"[S4] Validation failed: {ex.Result.Errors[0].Message}");
    }

    // Fix it
    dmgNode.Binding!.SetBoxed(500);
    var result = (WeaponConfig)session.Commit();
    Console.WriteLine($"[S4] Fixed: Damage={result.Damage}");
}

Console.WriteLine();

// ─────────────────────────────────────────────────────────────────────────────
// S5 — Dynamic array editing
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("--- Scenario 5: Dynamic array editing ---");
{
    var service = new ComponentEditServiceBuilder().Build();
    using var session = service.Open(new Inventory(), typeof(Inventory));

    var arrayNode = session.Document.Root.Children.First(n => n.Name == "ItemIds");
    var arrayBinding = (IContainerBinding)arrayNode.Binding!;
    Console.WriteLine($"[S5] Initial count: {arrayBinding.Count}");

    arrayBinding.Resize(5);
    session.MarkStructuralChange();
    session.RebuildDocument();

    arrayNode = session.Document.Root.Children.First(n => n.Name == "ItemIds");
    arrayBinding = (IContainerBinding)arrayNode.Binding!;
    Console.WriteLine($"[S5] After resize: {arrayBinding.Count} items");

    var result = (Inventory)session.Commit();
    Console.WriteLine($"[S5] Committed count: {result.ItemIds.Count}");
}

Console.WriteLine();

// ─────────────────────────────────────────────────────────────────────────────
// S6 — JSON round-trip
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("--- Scenario 6: JSON round-trip ---");
{
    var service = new ComponentEditServiceBuilder().Build();
    using var session = service.Open(
        new SpawnPoint { X = 1.5f, Y = 0f, Z = -3.2f },
        typeof(SpawnPoint));

    string json = session.ToJson();
    Console.WriteLine($"[S6] JSON:\n{json}");

    using var session2 = service.Open(new SpawnPoint(), typeof(SpawnPoint));
    session2.LoadJson(json);
    var result = (SpawnPoint)session2.Commit();
    Console.WriteLine($"[S6] Loaded: X={result.X:F2} Y={result.Y:F2} Z={result.Z:F2}");
}

Console.WriteLine();

// ─────────────────────────────────────────────────────────────────────────────
// S7 — Custom field editor (Guid + DateTime)
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("--- Scenario 7: Custom field editors ---");
{
    var service = new ComponentEditServiceBuilder()
        .RegisterFieldEditor(typeof(Guid), new GuidFieldEditor())
        .RegisterFieldEditor(typeof(DateTime), new DateTimeFieldEditor())
        .Build();

    var ev = new EventData { EventId = Guid.NewGuid(), OccurredAt = DateTime.UtcNow };
    using var session = service.Open(ev, typeof(EventData));

    var idNode = session.Document.Root.Children.First(n => n.Name == "EventId");
    var dtNode = session.Document.Root.Children.First(n => n.Name == "OccurredAt");

    Console.WriteLine($"[S7] EventId node kind: {idNode.Kind}");
    Console.WriteLine($"[S7] OccurredAt node kind: {dtNode.Kind}");
    Console.WriteLine($"[S7] EventId: {idNode.Binding!.GetBoxed()}");
    Console.WriteLine($"[S7] OccurredAt: {dtNode.Binding!.GetBoxed()}");
}

Console.WriteLine();
Console.WriteLine("=== All scenarios completed successfully ===");

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────
static void PrintDocument(EditDocument doc)
{
    Console.WriteLine($"  Document: scope={doc.Scope}, rootType={doc.RootComponentType.Name}");
    PrintNode(doc.Root, "  ");
}

static void PrintNode(EditNode node, string indent)
{
    var value = node.Binding != null ? $" = {node.Binding.GetBoxed()}" : "";
    Console.WriteLine($"{indent}[{node.Id.Value}] {node.Name} ({node.Kind}){value}");
    foreach (var child in node.Children)
        PrintNode(child, indent + "  ");
}

// ─────────────────────────────────────────────────────────────────────────────
// Component types
// ─────────────────────────────────────────────────────────────────────────────

struct Bullet { public float Speed; public int Damage; public bool IsActive; }

struct Character { public int Level; public float Health; public float Mana; }

record PlayerStats(int Score, int Lives, bool GameOver);

struct WeaponConfig { public int Damage; public float FireRate; }

class Inventory
{
    public List<int> ItemIds { get; set; } = new() { 1, 2, 3 };
}

struct SpawnPoint { public float X; public float Y; public float Z; }

struct EventData { public Guid EventId; public DateTime OccurredAt; }

// ─────────────────────────────────────────────────────────────────────────────
// Validator
// ─────────────────────────────────────────────────────────────────────────────

class WeaponValidator : IComponentValidator
{
    public ValidationResult Validate(EditValidationContext ctx)
    {
        var box = ctx.Buffer.Box();
        if (box is WeaponConfig wc && wc.Damage > 1000)
            return ValidationResult.Fail(new[]
            {
                new ValidationError("$.Damage", "Damage cannot exceed 1000")
            });
        return ValidationResult.Ok();
    }
}

