using System.Text.RegularExpressions;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// Which text a constraint added with AddConstraint accepts cannot be known, so Build treats it as overlapping every
// constraint. Two templates that rank equally and differ only in such constraints are rejected: with a path when one
// is known that both match, found among the samples the built-in constraints are compared with, and without one
// otherwise, naming the constraints instead.
public sealed class RouteTableAmbiguousAddedConstraintTests
{
    private const string Ambiguous = "rank equally and both match the path";
    private const string MayOverlap = "rank equally and could both match a path";
    private const string TreatedAsOverlapping = "treated as overlapping every constraint";
    private const string GeneralAdvice = "Keep them apart at a required position";
    private const string OptionalAdvice = "never keep templates apart";

    // The remedies that an added constraint makes useless, which a message must not offer where one is involved.
    private const string ConstraintRemedy = "constraints there that no text satisfies together";
    private const string AlternativesConstraintRemedy = "giving the alternatives constraints that no text satisfies";
    private const string LiteralForAlternatives = "keeping the alternatives apart with a different literal";

    // The added constraints of these tests: slug accepts the sample 0 and the sample a, any accepts every text, and
    // upper and shout accept no sample.
    private static readonly (string Name, Func<string, bool> IsSatisfiedBy)[] Added =
    [
        ("slug", IsSlug),
        ("any", _ => true),
        ("upper", value => value.Length > 0 && value.All(char.IsAsciiLetterUpper)),
        ("shout", value => value.Length > 1 && value[^1] == '!'),
    ];

    // A text the sample 0 or a stands for in a path both templates match: an added constraint beside a built-in one
    // that shares a sample with it, or with itself.
    [Theory]
    [InlineData("/p/{x:slug}", "/p/{y:int}", "/p/0")]
    [InlineData("/p/{x:slug}", "/p/{y:decimal}", "/p/0")]
    [InlineData("/p/{x:slug}", "/p/{y:alpha}", "/p/a")]
    [InlineData("/p/{x:any}", "/p/{y:guid}", "/p/00000000-0000-0000-0000-000000000000")]
    [InlineData("/p/{x:any}", "/p/{y:bool}", "/p/true")]
    [InlineData("/p/{x:slug}", "/p/{y:any}", "/p/0")]
    [InlineData("/p/{a:slug}/{b:int}", "/p/{c:SLUG}/{d:long}", "/p/0/0")]
    public void AnAddedConstraintThatSharesASampleIsRejectedWithAPathBothMatch(
        string first,
        string second,
        string path)
    {
        foreach ((string earlier, string later) in new[] { (first, second), (second, first) })
        {
            string message = BuildMessage(earlier, later);

            Assert.Contains(Ambiguous, message, StringComparison.Ordinal);
            Assert.Contains($"'{path}'", message, StringComparison.Ordinal);
            Assert.Contains(GeneralAdvice, message, StringComparison.Ordinal);
            Assert.Contains(TreatedAsOverlapping, message, StringComparison.Ordinal);
            Assert.DoesNotContain(ConstraintRemedy, message, StringComparison.Ordinal);
            AssertMatchesAlone(earlier, path);
            AssertMatchesAlone(later, path);
        }
    }

    // Without a sample both accept, no path is invented: the message names the two constraints instead, and why an
    // added constraint keeps no templates apart.
    [Theory]
    [InlineData("/p/{x:upper}", "/p/{y:shout}", "upper", "shout")]
    [InlineData("/p/{x:upper}", "/p/{y:int}", "upper", "int")]
    [InlineData("/p/{x:shout}", "/p/{y:guid}", "shout", "guid")]
    [InlineData("/p/{x:upper}", "/p/{y:slug}", "upper", "slug")]
    public void AddedConstraintsWithNoSampleInCommonAreRejectedWithoutAPath(
        string first,
        string second,
        string firstConstraint,
        string secondConstraint)
    {
        foreach ((string earlier, string later, string x, string y) in new[]
                 {
                     (first, second, firstConstraint, secondConstraint),
                     (second, first, secondConstraint, firstConstraint),
                 })
        {
            string message = BuildMessage(earlier, later);

            Assert.Contains(MayOverlap, message, StringComparison.Ordinal);
            Assert.DoesNotContain("the path", message, StringComparison.Ordinal);
            Assert.DoesNotContain("'/p'", message, StringComparison.Ordinal);
            Assert.Contains($"the constraints '{x}' and '{y}'", message, StringComparison.Ordinal);
            Assert.Contains(TreatedAsOverlapping, message, StringComparison.Ordinal);
            Assert.Contains(GeneralAdvice, message, StringComparison.Ordinal);
            Assert.DoesNotContain(ConstraintRemedy, message, StringComparison.Ordinal);
            Assert.DoesNotContain(OptionalAdvice, message, StringComparison.Ordinal);
            AssertInOrder(message, $"'{earlier}'", $"'{later}'");
        }
    }

    // An added constraint overlaps itself. Beside a position where the constraints differ, the first required position
    // where no text in common is known is the one named, whether the constraint has itself there or another.
    [Theory]
    [InlineData("/p/{a:upper}/{b:upper}", "/p/{c:upper}/{d:int}", "both have the constraint 'upper' at segment 2")]
    [InlineData("/p/{a:int}/{b:upper}", "/p/{c:long}/{d:upper}", "both have the constraint 'upper' at segment 3")]
    [InlineData("/p/{a:upper}/{b:shout}", "/p/{c:shout}/{d:shout}", "the constraints 'upper' and 'shout' at segment 2")]
    [InlineData("/p/{a:slug}/{b:upper}", "/p/{c:slug}/{d:int}", "the constraints 'upper' and 'int' at segment 3")]
    public void AnAddedConstraintWithItselfAtOnePositionAndAnotherAtAnotherIsRejected(
        string first,
        string second,
        string named)
    {
        string message = BuildMessage(first, second);

        Assert.Contains(MayOverlap, message, StringComparison.Ordinal);
        Assert.Contains(named, message, StringComparison.Ordinal);
        Assert.DoesNotContain("the path", message, StringComparison.Ordinal);
    }

    // The same constraint with itself and a common sample at one position, and two disjoint built-in constraints at
    // another, still keep the templates apart, as does a different literal.
    [Theory]
    [InlineData("/p/{a:slug}/{b:int}", "/p/{c:slug}/{d:alpha}")]
    [InlineData("/p/{a:upper}/{b:guid}", "/p/{c:shout}/{d:int}")]
    [InlineData("/p/{a:upper}/{b:bool}", "/p/{c:upper}/{d:long}")]
    [InlineData("/p/{a:upper}", "/q/{b:upper}")]
    [InlineData("/p/{a:any}/x", "/p/{b:int}/y")]
    public void ADisjointPairOrALiteralElsewhereStillKeepsTemplatesApart(string first, string second)
    {
        Assert.Equal(2, BuildTable(first, second).Definitions.Count);
        Assert.Equal(2, BuildTable(second, first).Definitions.Count);
    }

    // Optional parameters never keep templates apart, so when the required positions are the same, the advice for
    // optional parameters is given, with a path when the constraint there accepts a sample and without one otherwise.
    // It offers constraints for the alternatives only when their constraints are built in.
    [Fact]
    public void TheAdviceForOptionalParametersHoldsWithAddedConstraints()
    {
        string withPath = BuildMessage("/p/{a:slug}/{o:int?}", "/p/{b:SLUG}/{q:alpha?}");
        Assert.Contains(Ambiguous, withPath, StringComparison.Ordinal);
        Assert.Contains("'/p/0'", withPath, StringComparison.Ordinal);
        Assert.Contains(OptionalAdvice, withPath, StringComparison.Ordinal);
        Assert.Contains(AlternativesConstraintRemedy, withPath, StringComparison.Ordinal);
        Assert.DoesNotContain(GeneralAdvice, withPath, StringComparison.Ordinal);
        AssertMatchesAlone("/p/{a:slug}/{o:int?}", "/p/0");
        AssertMatchesAlone("/p/{b:SLUG}/{q:alpha?}", "/p/0");

        string withoutPath = BuildMessage("/p/{a:upper}/{o:int?}", "/p/{b:upper}/{q:alpha?}");
        Assert.Contains(MayOverlap, withoutPath, StringComparison.Ordinal);
        Assert.Contains("both have the constraint 'upper' at segment 2", withoutPath, StringComparison.Ordinal);
        Assert.Contains(OptionalAdvice, withoutPath, StringComparison.Ordinal);
        Assert.Contains(TreatedAsOverlapping, withoutPath, StringComparison.Ordinal);
        Assert.Contains(AlternativesConstraintRemedy, withoutPath, StringComparison.Ordinal);
        Assert.DoesNotContain(GeneralAdvice, withoutPath, StringComparison.Ordinal);
    }

    // Two added constraints on the alternatives, with a path made of the required segments: the advice keeps the
    // alternatives apart with a literal, not with constraints, and says why.
    [Theory]
    [InlineData("/p/{o:upper?}", "/p/{q:shout?}", "/p")]
    [InlineData("/p/{a:int}/{o:slug?}", "/p/{b:INT}/{q:any?}", "/p/0")]
    [InlineData("/{o:any?}", "/{q:upper?}", "/")]
    public void TheAdviceForOptionalAlternativesWithAddedConstraintsOffersALiteral(
        string first,
        string second,
        string path)
    {
        foreach ((string earlier, string later) in new[] { (first, second), (second, first) })
        {
            string message = BuildMessage(earlier, later);

            Assert.Contains(Ambiguous, message, StringComparison.Ordinal);
            Assert.Contains($"'{path}'", message, StringComparison.Ordinal);
            Assert.Contains(OptionalAdvice, message, StringComparison.Ordinal);
            Assert.Contains(LiteralForAlternatives, message, StringComparison.Ordinal);
            Assert.Contains(TreatedAsOverlapping, message, StringComparison.Ordinal);
            Assert.DoesNotContain(AlternativesConstraintRemedy, message, StringComparison.Ordinal);
            Assert.DoesNotContain(GeneralAdvice, message, StringComparison.Ordinal);
            AssertMatchesAlone(earlier, path);
            AssertMatchesAlone(later, path);
        }
    }

    // An added constraint overlaps both members of a group that the built-in constraints keep apart, so two earlier
    // templates conflict with it, and the earliest is the one reported, not the latest. Mapped in the order int, alpha,
    // added, with each of the earlier two in turn as the one mapped first.
    [Theory]
    [InlineData("int", "alpha", "/p/0")]
    [InlineData("alpha", "int", "/p/a")]
    public void TheEarliestTemplateAnAddedConstraintConflictsWithIsReported(
        string earliest,
        string between,
        string path)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new RouteTableBuilder()
            .AddConstraint("any", _ => true)
            .Map($"/p/{{x:{earliest}}}", _ => new SettingsRoute())
            .Map($"/p/{{x:{between}}}", _ => new ProductRoute("p"))
            .Map("/p/{x:any}", _ => new FileRoute(null))
            .Build());

        Assert.Contains(Ambiguous, exception.Message, StringComparison.Ordinal);
        AssertInOrder(exception.Message, $"'/p/{{x:{earliest}}}'", "'/p/{x:any}'");
        AssertInOrder(exception.Message, $"'{typeof(SettingsRoute).FullName}'", $"'{typeof(FileRoute).FullName}'");
        Assert.DoesNotContain($"'/p/{{x:{between}}}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(ProductRoute).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{path}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEarliestTemplateIsReportedWhenNoPathIsKnown()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new RouteTableBuilder()
            .AddConstraint("upper", Added[2].IsSatisfiedBy)
            .Map("/p/{x:int}", _ => new SettingsRoute())
            .Map("/p/{x:alpha}", _ => new ProductRoute("p"))
            .Map("/p/{x:upper}", _ => new FileRoute(null))
            .Build());

        Assert.Contains(MayOverlap, exception.Message, StringComparison.Ordinal);
        AssertInOrder(exception.Message, "'/p/{x:int}'", "'/p/{x:upper}'");
        Assert.DoesNotContain("'/p/{x:alpha}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("the constraints 'int' and 'upper'", exception.Message, StringComparison.Ordinal);
    }

    // Build asks the function about the samples the built-in constraints are compared with, and about nothing else.
    [Fact]
    public void BuildAsksTheFunctionOnlyAboutSamples()
    {
        var received = new HashSet<string>(StringComparer.Ordinal);
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("seen", value =>
            {
                received.Add(value);
                return false;
            })
            .Map("/p/{x:seen}", _ => new SettingsRoute())
            .Map("/p/{y:int}", _ => new SettingsRoute());

        Assert.Throws<InvalidOperationException>(builder.Build);
        Assert.Subset(
            new HashSet<string>(["0", "a", "true", "00000000-0000-0000-0000-000000000000"], StringComparer.Ordinal),
            received);
    }

    private static bool IsSlug(string value)
    {
        return value.Length > 0 && Regex.IsMatch(
            value,
            "^[a-z0-9-]+$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private static RouteTableBuilder Builder()
    {
        var builder = new RouteTableBuilder();
        foreach ((string name, Func<string, bool> isSatisfiedBy) in Added)
            builder.AddConstraint(name, isSatisfiedBy);

        return builder;
    }

    private static RouteTable BuildTable(params string[] templates)
    {
        RouteTableBuilder builder = Builder();
        foreach (string template in templates)
            builder.Map(template, _ => new SettingsRoute());

        return builder.Build();
    }

    private static string BuildMessage(string first, string second)
    {
        RouteTableBuilder builder = Builder()
            .Map(first, _ => new SettingsRoute())
            .Map(second, _ => new ProductRoute("p"));

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
        AssertInOrder(message, $"'{first}'", $"'{second}'");
        return message;
    }

    // The path the message gives matches the template in a table that holds only it, with the same constraints.
    private static void AssertMatchesAlone(string template, string path)
    {
        RouteTable alone = BuildTable(template);

        Assert.True(alone.Match(new Uri(path, UriKind.Relative)).IsSuccess, $"{template} against {path}");
        Assert.True(alone.Match(new Uri("https://example.com" + path)).IsSuccess, $"{template} against {path}");
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
