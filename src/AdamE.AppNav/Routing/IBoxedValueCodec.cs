namespace AdamE.AppNav.Routing;

// A codec seen without its type argument, so that the nullable form of a value type with a codec of its own, which
// AddValueCodec cannot register under its own type without reflection, is read and written by the codec of the
// underlying type. Each call boxes the value once, which only that nullable form pays.
internal interface IBoxedValueCodec
{
    string TypeName { get; }

    string NoTextReason { get; }

    // On success, the value boxed as the type the codec reads, which unboxes to that type and to its nullable form.
    bool TryParseBoxed(string text, out object? value);

    // The value is a box of the type the codec writes, never null.
    ValueTextProblem WriteBoxed(object value, out string? text);
}
