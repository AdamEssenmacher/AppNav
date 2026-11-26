using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// The natural ways to give AddValueCodec a parser, for a struct and for a reference type. That these compile, with
// nullable warnings as errors, is the point: a change to the parser delegate that breaks one of them fails the build
// of the tests, and each also reads a value, so that the shape is used and not only declared.
public sealed class RouteValueParserCallShapeTests
{
    [Fact]
    public void AStructParserCanBeALambdaWithOrWithoutTypesOrAMethodGroup()
    {
        AssertReads(
            new RouteTableBuilder().AddValueCodec<Code>((text, out code) => Code.TryParse(text, out code), Write),
            "7",
            new Code(7));
        AssertReads(
            new RouteTableBuilder().AddValueCodec<Code>(
                (string text, out Code code) => Code.TryParse(text, out code),
                Write),
            "7",
            new Code(7));
        AssertReads(new RouteTableBuilder().AddValueCodec<Code>(Code.TryParse, Write), "7", new Code(7));
    }

    [Fact]
    public void AReferenceTypeParserCanBeALambdaWithOrWithoutTypesOrAMethodGroup()
    {
        AssertReads(
            new RouteTableBuilder().AddValueCodec<Name>((text, out name) => Name.TryParse(text, out name), Write),
            "a",
            new Name("a"));
        AssertReads(
            new RouteTableBuilder().AddValueCodec<Name>(
                (string text, out Name? name) => Name.TryParse(text, out name),
                Write),
            "a",
            new Name("a"));
        AssertReads(new RouteTableBuilder().AddValueCodec<Name>(Name.TryParse, Write), "a", new Name("a"));
        AssertReads(new RouteTableBuilder().AddValueCodec<Tag>(Tag.TryParse, tag => tag.Text), "a", new Tag("a"));
    }

    [Fact]
    public void AMethodGroupOfTheBaseClassLibraryCanBeTheParser()
    {
        AssertReads(
            new RouteTableBuilder().AddValueCodec<Version>(Version.TryParse, version => version.ToString()),
            "1.2.3",
            new Version(1, 2, 3));
    }

    private static string Write(Code code)
    {
        return code.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Write(Name name)
    {
        return name.Text;
    }

    private static void AssertReads<T>(RouteTableBuilder builder, string text, T expected)
    {
        T? read = default;
        RouteTable table = builder
            .Map("/{v}", c =>
            {
                read = c.Path<T>("v");
                return new SettingsRoute();
            })
            .Build();

        Assert.True(table.Match(new Uri("/" + text, UriKind.Relative)).IsSuccess);
        Assert.Equal(expected, read);
    }

    private readonly record struct Code(int Value)
    {
        public static bool TryParse(string text, out Code code)
        {
            bool parsed = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value);
            code = new Code(value);
            return parsed;
        }
    }

    // The shape that the TryParse methods of the base class library have.
    private sealed record Name(string Text)
    {
        public static bool TryParse(string text, [NotNullWhen(true)] out Name? name)
        {
            name = text.Length > 0 ? new Name(text) : null;
            return name is not null;
        }
    }

    // A parser that declares its value nullable and says no more.
    private sealed record Tag(string Text)
    {
        public static bool TryParse(string text, out Tag? tag)
        {
            tag = text.Length > 0 ? new Tag(text) : null;
            return tag is not null;
        }
    }
}
