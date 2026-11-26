using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// Writes the nullable form of a value type that has a codec registered with AddValueCodec, by that codec. TNullable is
// that nullable form, which the codec of the underlying type cannot be cast to, and which no constraint lets this class
// name as T?, so the value goes through the codec boxed: a nullable value that has a value boxes as its underlying
// type, and a box of the underlying type unboxes as its nullable form. A format function binds one of these once, when
// the table is built; each value written then costs one box.
internal sealed class BoxingNullableValueCodec<TNullable> : ValueCodec<TNullable>
{
    private readonly IBoxedValueCodec _codec;

    public BoxingNullableValueCodec(IBoxedValueCodec codec)
        : base(codec.TypeName)
    {
        _codec = codec;
    }

    public override string NoTextReason => _codec.NoTextReason;

    public override bool TryParse(string text, [MaybeNullWhen(false)] out TNullable value)
    {
        if (_codec.TryParseBoxed(text, out object? boxed))
        {
            value = (TNullable)boxed!;
            return true;
        }

        value = default;
        return false;
    }

    // Every write goes through Write, which hands the value to the codec of the underlying type, so that codec's own
    // checks apply; nothing calls TryFormat of a codec but its own Write and a wrapper that holds it by its type,
    // and no wrapper holds this one.
    public override bool TryFormat(TNullable value, [NotNullWhen(true)] out string? text)
    {
        throw new UnreachableException("A boxing nullable codec is written only through Write.");
    }

    // A route writer leaves a value out for null before it asks for text, so the box is never null here.
    public override ValueTextProblem Write(TNullable value, out string? text)
    {
        return _codec.WriteBoxed(value!, out text);
    }
}
