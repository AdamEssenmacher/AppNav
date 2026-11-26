using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// Reads text as a value of one type for the typed reads of RouteMatchContext, and writes a value as text for the format
// functions of RouteFormatBuilder, for a type that is not built in. A table keeps its codecs by the type they read, so
// a read or a write for an unconstrained T finds a ValueCodec<T> with a reference cast and calls it with neither
// reflection nor a box around the value.
internal abstract class ValueCodec<T> : IBoxedValueCodec
{
    protected ValueCodec(string typeName)
    {
        TypeName = typeName;
    }

    // The type as a message names it, as in "is not a valid OrderStatus".
    public string TypeName { get; }

    // Ends the message for a value that Write finds has no text. It never names or describes the value itself, which
    // can be a secret.
    public abstract string NoTextReason { get; }

    // A value that is not valid returns false, and the caller decides what that means.
    public abstract bool TryParse(string text, [MaybeNullWhen(false)] out T value);

    // Writes the text for the value. A codec of the library writes one text for each value, which TryParse reads back
    // to an equal value; a codec the application registered promises only what its own functions do, which Write
    // checks in part. A value that has no text returns false, and NoTextReason says why.
    public abstract bool TryFormat(T value, [NotNullWhen(true)] out string? text);

    // The one entry point that the format functions call, for a single value and for each element of a list alike,
    // so that a codec that must check what it writes checks it in one place for both.
    public virtual ValueTextProblem Write(T value, out string? text)
    {
        return TryFormat(value, out text) ? ValueTextProblem.None : ValueTextProblem.NoText;
    }

    public bool TryParseBoxed(string text, out object? value)
    {
        if (TryParse(text, out T? parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    public ValueTextProblem WriteBoxed(object value, out string? text)
    {
        return Write((T)value, out text);
    }
}
