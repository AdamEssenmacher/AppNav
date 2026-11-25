using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// Reads text as a value of one type for the typed reads of RouteMatchContext, for a type that is not built in. A table
// keeps its codecs by the type they read, so a read for an unconstrained T finds a ValueCodec<T> with a reference cast
// and calls it with neither reflection nor a box around the value.
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
}
