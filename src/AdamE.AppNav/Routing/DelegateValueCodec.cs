using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// The codec of a type the application registered with RouteTableBuilder.AddValueCodec: its own parser and its own
// writer. Neither is trusted to agree with the other, so every text it writes is read back by its parser before it goes
// into a link, and a parser that accepts a text but gives null is reported as the mistake in the codec that it is.
internal sealed class DelegateValueCodec<TValue> : ValueCodec<TValue>
{
    private readonly RouteValueParser<TValue> _tryParse;
    private readonly Func<TValue, string?> _format;

    // Named by its simple name, as an enum is, since the message for a value that is not valid is read by whoever sent
    // the link, and the declaring types say nothing to them.
    public DelegateValueCodec(RouteValueParser<TValue> tryParse, Func<TValue, string?> format)
        : base(RouteValueTypes.SimpleName(typeof(TValue)))
    {
        _tryParse = tryParse;
        _format = format;
    }

    public override string NoTextReason => "its codec gives no text for the value";

    // The test for null boxes nothing for a value type, which can never be null here, since a nullable type cannot be
    // registered. An exception from the parser propagates unchanged.
    public override bool TryParse(string text, [MaybeNullWhen(false)] out TValue value)
    {
        if (!_tryParse(text, out TValue? parsed))
        {
            value = default;
            return false;
        }

        if (parsed is null)
        {
            throw new InvalidOperationException(
                $"The codec registered with RouteTableBuilder.AddValueCodec for the type " +
                $"'{RouteValueTypes.DisplayName(typeof(TValue))}' accepted a text but gave a null value. A parser " +
                "that accepts a text must give a value; return false for a text that is not valid.");
        }

        value = parsed;
        return true;
    }

    public override bool TryFormat(TValue value, [NotNullWhen(true)] out string? text)
    {
        text = _format(value);
        return text is not null;
    }

    // The check does not compare the value read back with the one written: equality is the type's own, and a type
    // without value equality would fail it for every value. That the parser accepts the text is what a link needs.
    public override ValueTextProblem Write(TValue value, out string? text)
    {
        if (!TryFormat(value, out text))
            return ValueTextProblem.NoText;

        return TryParse(text, out _) ? ValueTextProblem.None : ValueTextProblem.NotReadBack;
    }
}
