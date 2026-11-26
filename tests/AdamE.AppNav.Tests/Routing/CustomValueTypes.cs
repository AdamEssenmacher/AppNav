using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using AdamE.AppNav.Routing;

namespace AdamE.AppNav.Tests.Routing;

// Types of an application's own, each of a different shape, with the codecs that the tests of custom value types
// register for them, shared so that reads and writes are held to the same codecs.
internal static class CustomValueTypes
{
    // Registers a codec for each of the types below but Circle, which only the tests of exact types register.
    public static RouteTableBuilder Register(RouteTableBuilder builder)
    {
        return builder
            .AddValueCodec<ProductId>(ProductId.TryParse, id => id.Value < 0 ? null : Format(id.Value))
            .AddValueCodec<Slug>(Slug.TryParse, slug => slug.Value)
            .AddValueCodec<Point>(Point.TryParse, point => Format(point.X) + "x" + Format(point.Y))
            .AddValueCodec<Stage>(TryParseStage, FormatStage)
            .AddValueCodec<IShape>(TryParseShape, FormatShape)
            .AddValueCodec<Note>(
                (string text, out Note? note) =>
                {
                    note = new Note(text);
                    return true;
                },
                note => note.Text)
            .AddValueCodec<Outer<int>.Inner>(
                (string text, out Outer<int>.Inner inner) =>
                {
                    bool parsed = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value);
                    inner = new Outer<int>.Inner(value);
                    return parsed;
                },
                inner => Format(inner.Value));
    }

    public static RouteTableBuilder Builder()
    {
        return Register(new RouteTableBuilder());
    }

    public static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    // The kebab-case spellings an app gives its enum, which AddEnumValueCodec cannot.
    private static bool TryParseStage(string text, out Stage stage)
    {
        (bool parsed, stage) = text switch
        {
            "not-started" => (true, Stage.NotStarted),
            "in-progress" => (true, Stage.InProgress),
            "done" => (true, Stage.Done),
            _ => (false, default(Stage)),
        };
        return parsed;
    }

    private static string? FormatStage(Stage stage)
    {
        return stage switch
        {
            Stage.NotStarted => "not-started",
            Stage.InProgress => "in-progress",
            Stage.Done => "done",
            _ => null,
        };
    }

    private static bool TryParseShape(string text, [MaybeNullWhen(false)] out IShape shape)
    {
        shape = text switch
        {
            "circle" => new Circle(),
            "square" => new Square(),
            _ => null,
        };
        return shape is not null;
    }

    private static string? FormatShape(IShape shape)
    {
        return shape switch
        {
            Circle => "circle",
            Square => "square",
            _ => null,
        };
    }

    // A struct id: digits only, written without leading zeros; a negative id has no text.
    public readonly record struct ProductId(int Value)
    {
        public static bool TryParse(string text, out ProductId id)
        {
            bool parsed = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value);
            id = new ProductId(value);
            return parsed;
        }
    }

    // A reference type: lower-case ASCII letters, digits and hyphens, at least one. Its writer writes any value as it
    // is, so a slug made with other characters is written as a text its parser rejects.
    public sealed record Slug(string Value)
    {
        public static bool TryParse(string text, [NotNullWhen(true)] out Slug? slug)
        {
            slug = text.Length > 0 && text.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
                ? new Slug(text)
                : null;
            return slug is not null;
        }
    }

    // A mutable struct with no equality of its own beyond what every struct has.
    public struct Point
    {
        public int X;
        public int Y;

        public static bool TryParse(string text, out Point point)
        {
            point = default;
            int separator = text.IndexOf('x', StringComparison.Ordinal);
            return separator > 0
                && int.TryParse(text[..separator], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out point.X)
                && int.TryParse(text[(separator + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out point.Y);
        }
    }

    public enum Stage
    {
        NotStarted,
        InProgress,
        Done,
    }

    public interface IShape;

    public sealed record Circle : IShape;

    public sealed record Square : IShape;

    // Any text, the empty one and one with a lone surrogate included, is a note, and a note is written as its text.
    public sealed record Note(string Text);

    public static class Outer<T>
    {
        public readonly record struct Inner(int Value);
    }
}
