using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// How a declaration of DeclareDisjointConstraints changes the check that templates can be told apart: a declared
// pair keeps templates apart at a required position and nowhere else, a pair not declared still conflicts, and the
// message offers the declaration where it would help and never for a constraint with itself. It also pins what a
// false declaration that no sample catches does to Format.
public sealed class RouteTableAmbiguousDisjointConstraintTests
{
    private const string Ambiguous = "rank equally and both match the path";
    private const string MayOverlap = "rank equally and could both match a path";
    private const string TreatedAsOverlapping = "treated as overlapping every constraint";
    private const string OnlyDeclared =
        "only a pair declared with DeclareDisjointConstraints is taken to accept no text in common";
    private const string DeclaredAlternatives =
        "where the constraints already declared disjoint with DeclareDisjointConstraints keep the alternatives apart";
    private const string RanksEqually = "which ranks equally and was mapped first";
    private const string CorrectDeclaration = "Correct or remove that declaration";
    private const string GeneralAdvice = "Keep them apart at a required position with a different literal";
    private const string OptionalAdvice = "never keep templates apart";
    private const string LiteralForAlternatives = "keeping the alternatives apart with a different literal";
    private const string DeclareCall = "DeclareDisjointConstraints(\"";
    private const string MatchedFirst = "is matched first by the template";

    // lower accepts ASCII lowercase letters, upper uppercase ones, and bang a text of two or more characters that ends
    // with '!', so no text satisfies two of them; lower accepts the samples a and true, and upper and bang none.
    private static readonly (string Name, Func<string, bool> IsSatisfiedBy)[] Added =
    [
        ("lower", value => value.Length > 0 && value.All(char.IsAsciiLetterLower)),
        ("upper", value => value.Length > 0 && value.All(char.IsAsciiLetterUpper)),
        ("bang", value => value.Length > 1 && value[^1] == '!'),
    ];

    // Declared a-b and b-c, the pair a-c is still not declared, and conflicts.
    [Fact]
    public void APairNotDeclaredAmongOthersStillConflicts()
    {
        RouteTableBuilder builder = Builder()
            .DeclareDisjointConstraints("lower", "upper")
            .DeclareDisjointConstraints("upper", "bang")
            .Map("/p/{a:lower}", _ => new SettingsRoute())
            .Map("/p/{b:upper}", _ => new FileRoute(null))
            .Map("/p/{c:bang}", _ => new ProductRoute("p"));

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;

        Assert.Contains(MayOverlap, message, StringComparison.Ordinal);
        Assert.Contains("the constraints 'lower' and 'bang' at segment 2", message, StringComparison.Ordinal);
        AssertInOrder(message, "'/p/{a:lower}'", "'/p/{c:bang}'");
        Assert.DoesNotContain("'/p/{b:upper}'", message, StringComparison.Ordinal);
    }

    // A declaration keeps two templates apart at the position where they have its two constraints. Another pair of
    // templates that differs elsewhere, in constraints not declared, still conflicts there.
    [Fact]
    public void ADeclaredPairAtOnePositionDoesNotKeepApartTemplatesThatConflictAtAnother()
    {
        RouteTableBuilder builder = Builder()
            .DeclareDisjointConstraints("lower", "upper")
            .Map("/p/{a:lower}/{b:upper}", _ => new SettingsRoute())
            .Map("/p/{a:upper}/{b:upper}", _ => new FileRoute(null))
            .Map("/p/{a:lower}/{b:bang}", _ => new ProductRoute("p"));

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;

        AssertInOrder(message, "'/p/{a:lower}/{b:upper}'", "'/p/{a:lower}/{b:bang}'");
        Assert.Contains(MayOverlap, message, StringComparison.Ordinal);
        Assert.Contains(
            "the constraints 'upper' and 'bang' at segment 3 are not known",
            message,
            StringComparison.Ordinal);
        Assert.Contains("DeclareDisjointConstraints(\"upper\", \"bang\")", message, StringComparison.Ordinal);
    }

    // With x, y and z added and only x-y declared, z conflicts with both x and y, and the earliest is reported.
    [Fact]
    public void TheEarliestConflictIsReportedWithDeclarationsPresent()
    {
        RouteTableBuilder builder = Builder()
            .DeclareDisjointConstraints("lower", "upper")
            .Map("/p/{x:lower}", _ => new SettingsRoute())
            .Map("/p/{y:upper}", _ => new FileRoute(null))
            .Map("/p/{z:bang}", _ => new ProductRoute("p"));

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;

        AssertInOrder(message, "'/p/{x:lower}'", "'/p/{z:bang}'");
        Assert.DoesNotContain("'/p/{y:upper}'", message, StringComparison.Ordinal);
        Assert.Contains("the constraints 'lower' and 'bang'", message, StringComparison.Ordinal);
    }

    // Where no text is known that both accept and the pair is not declared, the advice offers the declaration, named
    // for the first position where it would help, beside the literal, in either mapping order.
    [Theory]
    [InlineData("/p/{a:upper}", "/p/{b:int}", "upper", "int", 2)]
    [InlineData("/p/{a:bang}", "/p/{b:upper}", "bang", "upper", 2)]
    [InlineData("/p/x/{a:upper}", "/p/x/{b:guid}", "upper", "guid", 3)]
    [InlineData("/p/{a:upper}/{b:upper}", "/p/{c:upper}/{d:bang}", "upper", "bang", 3)]
    public void TheAdviceOffersTheDeclarationWhereItWouldHelp(
        string first,
        string second,
        string firstConstraint,
        string secondConstraint,
        int segment)
    {
        foreach ((string earlier, string later, string x, string y) in new[]
                 {
                     (first, second, firstConstraint, secondConstraint),
                     (second, first, secondConstraint, firstConstraint),
                 })
        {
            string message = BuildMessage(earlier, later);

            Assert.Contains(MayOverlap, message, StringComparison.Ordinal);
            Assert.Contains(OnlyDeclared, message, StringComparison.Ordinal);
            Assert.Contains(GeneralAdvice, message, StringComparison.Ordinal);
            Assert.Contains(
                $"the constraints '{x}' and '{y}' at segment {segment}, by declaring that with " +
                $"DeclareDisjointConstraints(\"{x}\", \"{y}\")",
                message,
                StringComparison.Ordinal);
        }
    }

    // A constraint with itself can never be declared disjoint, so where that is the only position no text in common
    // is known, the declaration is not offered; nor is it where a sample both accept already disproves it.
    [Theory]
    [InlineData("/p/{a:upper}/{b:int}", "/p/{c:upper}/{d:long}", MayOverlap)]
    [InlineData("/p/{a:bang}/{b:int}", "/p/{c:bang}/{d:decimal}", MayOverlap)]
    [InlineData("/p/{a:lower}", "/p/{b:alpha}", Ambiguous)]
    [InlineData("/p/{a:lower}/{b:upper}", "/p/{c:alpha}/{d:upper}", MayOverlap)]
    public void TheAdviceDoesNotOfferADeclarationThatCannotHelp(string first, string second, string kind)
    {
        foreach ((string earlier, string later) in new[] { (first, second), (second, first) })
        {
            string message = BuildMessage(earlier, later);

            Assert.Contains(kind, message, StringComparison.Ordinal);
            Assert.Contains(GeneralAdvice, message, StringComparison.Ordinal);
            Assert.DoesNotContain(DeclareCall, message, StringComparison.Ordinal);
        }
    }

    // With built-in constraints alone, a declaration on the builder changes nothing in the message.
    [Fact]
    public void AMessageAboutBuiltInConstraintsIsUnchangedByDeclarations()
    {
        string without = Assert.Throws<InvalidOperationException>(new RouteTableBuilder()
            .Map("/p/{a:int}", _ => new SettingsRoute())
            .Map("/p/{b:long}", _ => new FileRoute(null))
            .Build).Message;
        string with = Assert.Throws<InvalidOperationException>(Builder()
            .DeclareDisjointConstraints("lower", "upper", "int")
            .Map("/p/{a:int}", _ => new SettingsRoute())
            .Map("/p/{b:long}", _ => new FileRoute(null))
            .Build).Message;

        Assert.Equal(without, with);
        Assert.DoesNotContain("DeclareDisjointConstraints", with, StringComparison.Ordinal);
    }

    // Optional parameters never keep templates apart, so a declaration about the alternatives does not either: a path
    // that leaves them out matches both. When every added constraint among the alternatives is in a declared pair, the
    // message does not say how added constraints are treated, as though the pair were not declared, and says that the
    // declarations keep the alternatives apart once each is mapped as its own template.
    [Theory]
    [InlineData("/p/{a:lower?}", "/p/{b:upper?}", "/p")]
    [InlineData("/p/{a:upper?}", "/p/{b:int?}", "/p")]
    [InlineData("/p/{a:int}/{o:upper?}", "/p/{b:int}/{q:bang?}", "/p/0")]
    [InlineData("/p/{a:upper?}/{o:int?}", "/p/{b:bang?}/{q:long?}", "/p")]
    public void ADeclarationDoesNotKeepOptionalAlternativesApart(string first, string second, string path)
    {
        foreach ((string earlier, string later) in new[] { (first, second), (second, first) })
        {
            string message = Assert.Throws<InvalidOperationException>(Builder()
                .DeclareDisjointConstraints("lower", "upper", "bang")
                .DeclareDisjointConstraints("upper", "int")
                .Map(earlier, _ => new SettingsRoute())
                .Map(later, _ => new FileRoute(null))
                .Build).Message;

            Assert.Contains(Ambiguous, message, StringComparison.Ordinal);
            Assert.Contains($"'{path}'", message, StringComparison.Ordinal);
            Assert.Contains(OptionalAdvice, message, StringComparison.Ordinal);
            Assert.Contains(DeclaredAlternatives, message, StringComparison.Ordinal);
            Assert.Contains("or keep them apart with a different literal", message, StringComparison.Ordinal);
            Assert.DoesNotContain(TreatedAsOverlapping, message, StringComparison.Ordinal);
            Assert.DoesNotContain(LiteralForAlternatives, message, StringComparison.Ordinal);
            Assert.DoesNotContain(GeneralAdvice, message, StringComparison.Ordinal);
            Assert.DoesNotContain(DeclareCall, message, StringComparison.Ordinal);
        }
    }

    // Alternatives with an added constraint in a pair not declared, or with itself, keep the advice for added
    // constraints, even beside a declared pair.
    [Theory]
    [InlineData("/p/{a:upper?}", "/p/{b:long?}")]
    [InlineData("/p/{a:upper?}/{o:upper?}", "/p/{b:int?}/{q:upper?}")]
    public void AlternativesWithAnUndeclaredAddedConstraintKeepTheAdviceForAddedConstraints(
        string first,
        string second)
    {
        string message = Assert.Throws<InvalidOperationException>(Builder()
            .DeclareDisjointConstraints("upper", "int")
            .Map(first, _ => new SettingsRoute())
            .Map(second, _ => new FileRoute(null))
            .Build).Message;

        Assert.Contains(TreatedAsOverlapping, message, StringComparison.Ordinal);
        Assert.Contains(LiteralForAlternatives, message, StringComparison.Ordinal);
        Assert.DoesNotContain(DeclaredAlternatives, message, StringComparison.Ordinal);
    }

    // When the alternatives would be kept apart by a declaration once each is mapped as its own template, the advice
    // for optional parameters offers it; for a constraint with itself, it does not.
    [Fact]
    public void TheAdviceForOptionalAlternativesOffersADeclarationOnlyWhereItWouldHelp()
    {
        string offered = BuildMessage("/p/{a:lower?}", "/p/{b:bang?}");
        Assert.Contains(Ambiguous, offered, StringComparison.Ordinal);
        Assert.Contains(OptionalAdvice, offered, StringComparison.Ordinal);
        Assert.Contains(LiteralForAlternatives, offered, StringComparison.Ordinal);
        Assert.Contains("DeclareDisjointConstraints(\"lower\", \"bang\")", offered, StringComparison.Ordinal);

        string notOffered = BuildMessage("/p/{a:upper?}/{b:int?}", "/p/{c:upper?}/{d:long?}");
        Assert.Contains(OptionalAdvice, notOffered, StringComparison.Ordinal);
        Assert.Contains(LiteralForAlternatives, notOffered, StringComparison.Ordinal);
        Assert.DoesNotContain(DeclareCall, notOffered, StringComparison.Ordinal);

        string withoutPath = BuildMessage("/p/{x:upper}/{a:lower?}", "/p/{y:upper}/{b:bang?}");
        Assert.Contains(MayOverlap, withoutPath, StringComparison.Ordinal);
        Assert.Contains("both have the constraint 'upper' at segment 2", withoutPath, StringComparison.Ordinal);
        Assert.Contains(TreatedAsOverlapping, withoutPath, StringComparison.Ordinal);
        Assert.Contains(OptionalAdvice, withoutPath, StringComparison.Ordinal);
        Assert.Contains("DeclareDisjointConstraints(\"lower\", \"bang\")", withoutPath, StringComparison.Ordinal);
    }

    // A false declaration that no sample catches: five or more lowercase letters, and five or more letters of either
    // case, which both accept hello. Build trusts it, so the templates tie, and the one mapped first wins every path
    // both match. Format of the route of the template mapped later, for a value both accept, then throws, because the
    // earlier template matches the path first; Format of the earlier one's route writes it. In both mapping orders.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFalseDeclarationNoSampleCatchesLetsTheEarlierTemplateWin(bool lowerFirst)
    {
        const string Value = "secretvalue";
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("lower", value => value.Length >= 5 && value.All(char.IsAsciiLetterLower))
            .AddConstraint("word", value => value.Length >= 5 && value.All(char.IsAsciiLetter))
            .DeclareDisjointConstraints("lower", "word");
        Action<RouteTableBuilder>[] maps =
        [
            b => b.Map("/p/{a:lower}", c => new ProductRoute(c.Path("a")), f => f.Path("a", r => r.Id)),
            b => b.Map("/p/{b:word}", c => new ProductByNameRoute(c.Path("b")), f => f.Path("b", r => r.Name)),
        ];
        maps[lowerFirst ? 0 : 1](builder);
        maps[lowerFirst ? 1 : 0](builder);
        RouteTable table = builder.Build();

        AppRoute earlier = lowerFirst ? new ProductRoute(Value) : new ProductByNameRoute(Value);
        AppRoute later = lowerFirst ? new ProductByNameRoute(Value) : new ProductRoute(Value);
        RouteMatchResult result = table.Match(new Uri("/p/" + Value, UriKind.Relative));
        Assert.Equal(earlier, result.Route);

        Assert.Equal("/p/" + Value, table.Format(earlier));
        var baseUri = new Uri("https://example.com");
        Assert.Equal(new Uri("https://example.com/p/" + Value), table.FormatUri(earlier, baseUri));

        ArgumentException format = Assert.Throws<ArgumentException>(() => table.Format(later));
        ArgumentException formatUri = Assert.Throws<ArgumentException>(
            () => table.FormatUri(later, baseUri));
        foreach (ArgumentException exception in new[] { format, formatUri })
        {
            Assert.Equal("route", exception.ParamName);
            Assert.Contains(MatchedFirst, exception.Message, StringComparison.Ordinal);
            Assert.Contains(RanksEqually, exception.Message, StringComparison.Ordinal);
            Assert.Contains("DeclareDisjointConstraints", exception.Message, StringComparison.Ordinal);
            Assert.Contains(CorrectDeclaration, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ranks higher", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Value, exception.Message, StringComparison.Ordinal);
        }

        // Mapped later, word still writes a value that lower does not accept, since lower does not match its path.
        if (lowerFirst)
            Assert.Equal("/p/Mixed", table.Format(new ProductByNameRoute("Mixed")));
    }

    // A template that genuinely ranks higher is still reported in the words it always was, with or without
    // declarations on the builder.
    [Fact]
    public void ATemplateThatRanksHigherIsReportedAsBefore()
    {
        RouteTable table = Builder()
            .DeclareDisjointConstraints("lower", "upper")
            .Map("/products/new", _ => new SettingsRoute())
            .Map("/products/{name}", c => new ProductRoute(c.Path("name")), f => f.Path("name", r => r.Id))
            .Build();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => table.Format(new ProductRoute("new")));

        Assert.Equal("route", exception.ParamName);
        Assert.Contains(
            "the path it writes is matched first by the template '/products/new', which ranks higher, so a link to " +
            "it would be matched by that template instead. Change the templates so that they cannot match the same " +
            "path, for example by constraining a parameter or by giving one of them a literal of its own.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(RanksEqually, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("DeclareDisjointConstraints", exception.Message, StringComparison.Ordinal);
    }

    private static RouteTableBuilder Builder()
    {
        var builder = new RouteTableBuilder();
        foreach ((string name, Func<string, bool> isSatisfiedBy) in Added)
            builder.AddConstraint(name, isSatisfiedBy);

        return builder;
    }

    private static string BuildMessage(string first, string second)
    {
        RouteTableBuilder builder = Builder()
            .Map(first, _ => new SettingsRoute())
            .Map(second, _ => new FileRoute(null));

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
        AssertInOrder(message, $"'{first}'", $"'{second}'");
        return message;
    }

    private static void AssertInOrder(string message, string first, string second)
    {
        int firstIndex = message.IndexOf(first, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"{first} is not in: {message}");
        Assert.True(
            message.IndexOf(second, firstIndex + first.Length, StringComparison.Ordinal) >= 0,
            $"{second} does not follow {first} in: {message}");
    }
}
