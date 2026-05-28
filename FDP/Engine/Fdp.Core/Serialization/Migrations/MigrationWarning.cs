/// <summary>
/// A non-fatal warning raised by a migrator. The <see cref="Path"/> is the
/// JSONPath active at the moment the warning was raised
/// (<see cref="MigrationContext.CurrentPath"/>).
/// </summary>
namespace Fdp.Core.Serialization.Migrations;

public sealed record MigrationWarning(string Message, string Path);
