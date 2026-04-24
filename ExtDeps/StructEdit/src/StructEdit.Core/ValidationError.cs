namespace StructEdit.Core;

/// <summary>
/// A single validation error with a JSONPath location and a message.
/// </summary>
public sealed record ValidationError(string JsonPath, string Message);
