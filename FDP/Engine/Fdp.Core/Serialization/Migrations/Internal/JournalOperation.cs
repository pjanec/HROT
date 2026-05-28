using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations.Internal;

internal sealed record JournalOperation(
    JournalOpKind Kind,
    string Path,
    JsonNode? Value);   // non-null for Set, null for Remove
