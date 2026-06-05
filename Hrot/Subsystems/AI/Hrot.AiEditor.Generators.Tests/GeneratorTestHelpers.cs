using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Hrot.AiEditor.Generators.Tests;

/// <summary>
/// Minimal <see cref="AdditionalText"/> implementation backed by a string —
/// used in <see cref="CSharpGeneratorDriver"/> tests.
/// </summary>
internal sealed class StringAdditionalText : AdditionalText
{
    private readonly string _path;
    private readonly string _content;

    public StringAdditionalText(string path, string content)
    {
        _path    = path;
        _content = content;
    }

    public override string Path => _path;

    public override SourceText? GetText(CancellationToken cancellationToken = default) =>
        SourceText.From(_content);
}

/// <summary>
/// Extension helpers for working with <see cref="ImmutableArray{T}"/> in tests.
/// </summary>
internal static class ImmutableArrayExtensions
{
    public static ImmutableArray<AdditionalText> ToImmutableArrayCompat(
        this IEnumerable<AdditionalText> items) =>
        items.ToImmutableArray();
}
