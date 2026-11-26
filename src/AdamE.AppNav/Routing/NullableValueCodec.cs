using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// The nullable form of a value type reads and writes by exactly the rule of the type itself, to the same value and the
// same text, and its message names the type itself, as for the built-in types. It is kept under its own type, so that
// a read or a write for T? finds it with the same lookup and cast as one for T.
internal sealed class NullableValueCodec<T> : ValueCodec<T?>
    where T : struct
{
    private readonly ValueCodec<T> _codec;

    public NullableValueCodec(ValueCodec<T> codec)
        : base(codec.TypeName)
    {
        _codec = codec;
    }

    public override bool TryParse(string text, [MaybeNullWhen(false)] out T? value)
    {
        if (_codec.TryParse(text, out T parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    // A route writer leaves a parameter out for null before it asks for text, so null is never written here; it has
    // no text, as for a value of the type itself that has none.
    public override bool TryFormat(T? value, [NotNullWhen(true)] out string? text)
    {
        if (value.HasValue)
            return _codec.TryFormat(value.GetValueOrDefault(), out text);

        text = null;
        return false;
    }

    // Forwards to the Write of the wrapped codec, so that Write is the one write entry point whatever is wrapped.
    public override ValueTextProblem Write(T? value, out string? text)
    {
        if (value.HasValue)
            return _codec.Write(value.GetValueOrDefault(), out text);

        text = null;
        return ValueTextProblem.NoText;
    }

    public override string NoTextReason => _codec.NoTextReason;
}
