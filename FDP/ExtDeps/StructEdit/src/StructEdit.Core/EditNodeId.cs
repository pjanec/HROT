namespace StructEdit.Core;

/// <summary>
/// Stable integer identity assigned once at document build time.
/// The render loop binds to EditNodeId — never to string paths.
/// </summary>
public readonly record struct EditNodeId(int Value);
