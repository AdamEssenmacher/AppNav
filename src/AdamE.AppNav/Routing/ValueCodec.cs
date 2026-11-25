using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// Reads text as a value of one type for the typed reads of RouteMatchContext, and writes a value as text for the format
// functions of RouteFormatBuilder, for a type that is not built in. A table keeps its codecs by the type they read, so
// a read or a write for an unconstrained T finds a ValueCodec<T> with a reference cast and calls it with neither
// reflection nor a box around the value.
internal abstract class ValueCodec<T>
{
    protected ValueCodec(string typeName)
    {
        TypeName = typeName;
    }

    // The type as a message names it, as in "is not a valid OrderStatus".
    public string TypeName { get; }

    // Never throws for the text: a value that is not valid returns false, and the caller decides what that means.
    public abstract bool TryParse(string text, [MaybeNullWhen(false)] out T value);

    // Writes the one text for the value that TryParse reads back to an equal value, so that equal values always give
    // the same path. Never throws for the value: one that has no such text returns false, and NoTextReason says why.
    public abstract bool TryFormat(T value, [NotNullWhen(true)] out string? text);

    // Ends the message for a value that TryFormat cannot write. It never names or describes the value itself, which
    // can be a secret.
    public abstract string NoTextReason { get; }
}
