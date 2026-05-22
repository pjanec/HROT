using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Hrot.Blueprints.Core.Compiler.Roslyn;

internal static class EmbeddedTextHelper
{
    public static EmbeddedText Create(string virtualPath, string sourceText)
    {
        var text = SourceText.From(sourceText, Encoding.UTF8);
        return EmbeddedText.FromSource(virtualPath, text);
    }
}
