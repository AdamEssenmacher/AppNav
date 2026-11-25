using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// The nullable form of a value type reads by exactly the rule of the type itself, to the same value, and its message
// names the type itself, as for the built-in types. It is kept under its own type, so that a read for T? finds it with
// the same lookup and cast as a read for T.
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
}
