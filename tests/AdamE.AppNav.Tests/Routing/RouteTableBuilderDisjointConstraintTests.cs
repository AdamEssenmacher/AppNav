using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// DeclareDisjointConstraints declares that no text satisfies two constraints together: how its names are checked, that
// the order of the calls does not matter, what Build checks about a declaration, and that a declaration belongs to one
// builder and reaches only the tables built after it.
public sealed class RouteTableBuilderDisjointConstraintTests
{
    private const string FewerThanTwo = "takes two or more constraint names";
    private const string NotValid = "is not valid";
    private const string Reserved = "is reserved";
    private const string GivenTwice = "is given twice";
    private const string NotAdded = "neither built in nor added on this builder";
    private const string NamedByDeclare = "named by DeclareDisjointConstraints";
    private const string DeclaredFalse = "so the declaration is false";
    private const string Ambiguous = "rank equally";
    private const string UnknownTemplateConstraint = "names the constraint 'nope'";

    private static readonly string[] Samples = ["0", "a", "true", "00000000-0000-0000-0000-000000000000"];

    // One or more ASCII lowercase letters and hyphens, starting with a letter, so that no number is a slug.
    private static bool IsSlug(string value)
    {
        if (value.Length == 0 || !char.IsAsciiLetterLower(value[0]))
            return false;

        foreach (char c in value)
        {
            if (!(char.IsAsciiLetterLower(c) || c == '-'))
                return false;
        }

        return true;
    }

    private static bool IsUpper(string value)
    {
        return value.Length > 0 && value.All(char.IsAsciiLetterUpper);
    }

    [Fact]
    public void DeclaringReturnsTheSameBuilder()
    {
        var builder = new RouteTableBuilder();

        Assert.Same(builder, builder.DeclareDisjointConstraints("slug", "int"));
    }

    // The example of the documentation: a slug and a number can share a position once declared disjoint, whichever
    // template is mapped first, and each path goes to its own route.
    [Fact]
    public void ADeclaredPairKeepsTemplatesApartInBothMappingOrders()
    {
        foreach (bool slugFirst in new[] { true, false })
        {
            var builder = new RouteTableBuilder()
                .AddConstraint("slug", IsSlug)
                .DeclareDisjointConstraints("slug", "int");
            if (slugFirst)
            {
                builder.Map("/a/{s:slug}", c => new ProductByNameRoute(c.Path("s")))
                    .Map("/a/{id:int}", c => new ProductRoute(c.Path("id")));
            }
            else
            {
                builder.Map("/a/{id:int}", c => new ProductRoute(c.Path("id")))
                    .Map("/a/{s:slug}", c => new ProductByNameRoute(c.Path("s")));
            }

            RouteTable table = builder.Build();

            Assert.Equal(new ProductByNameRoute("hello-world"), Route(table, "/a/hello-world"));
            Assert.Equal(new ProductRoute("42"), Route(table, "/a/42"));
            Assert.False(table.Match(new Uri("/a/Hello", UriKind.Relative)).IsSuccess);
        }
    }

    // The declaration can come before the constraint is added and before the templates are mapped, or after both.
    [Fact]
    public void TheOrderOfTheCallsDoesNotMatter()
    {
        RouteTable first = new RouteTableBuilder()
            .DeclareDisjointConstraints("slug", "int")
            .Map("/a/{s:slug}", _ => new SettingsRoute())
            .Map("/a/{id:int}", _ => new FileRoute(null))
            .AddConstraint("slug", IsSlug)
            .Build();
        RouteTable last = new RouteTableBuilder()
            .Map("/a/{s:slug}", _ => new SettingsRoute())
            .AddConstraint("slug", IsSlug)
            .Map("/a/{id:int}", _ => new FileRoute(null))
            .DeclareDisjointConstraints("slug", "int")
            .Build();

        Assert.Equal(2, first.Definitions.Count);
        Assert.Equal(2, last.Definitions.Count);
    }

    // Names are constraint names, compared without regard to ASCII case, in the declaration as in the templates.
    [Fact]
    public void NamesAreComparedWithoutRegardToCase()
    {
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .DeclareDisjointConstraints("SLUG", "Int")
            .Map("/a/{s:Slug}", c => new ProductByNameRoute(c.Path("s")))
            .Map("/a/{id:INT}", c => new ProductRoute(c.Path("id")))
            .Build();

        Assert.Equal(new ProductByNameRoute("x"), Route(table, "/a/x"));
        Assert.Equal(new ProductRoute("7"), Route(table, "/a/7"));
    }

    // Declaring a pair again, in the same call, in the other order or in a longer list, is allowed and changes nothing.
    [Fact]
    public void DeclaringAPairAgainChangesNothing()
    {
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .AddConstraint("upper", IsUpper)
            .DeclareDisjointConstraints("slug", "int")
            .DeclareDisjointConstraints("slug", "int")
            .DeclareDisjointConstraints("INT", "slug")
            .DeclareDisjointConstraints("upper", "slug", "int")
            .Map("/a/{s:slug}", _ => new SettingsRoute())
            .Map("/a/{id:int}", _ => new FileRoute(null))
            .Map("/a/{u:upper}", _ => new ProductRoute("p"))
            .Build();

        Assert.Equal(3, table.Definitions.Count);
    }

    // Every two of the names in a list are declared disjoint.
    [Fact]
    public void AListOfThreeDeclaresEveryPair()
    {
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .AddConstraint("upper", IsUpper)
            .DeclareDisjointConstraints("slug", "upper", "int")
            .Map("/a/{s:slug}", c => new ProductByNameRoute(c.Path("s")))
            .Map("/a/{u:upper}", c => new OrderRoute(c.Path("u"), null))
            .Map("/a/{id:int}", c => new ProductRoute(c.Path("id")))
            .Build();

        Assert.Equal(new ProductByNameRoute("abc"), Route(table, "/a/abc"));
        Assert.Equal(new OrderRoute("ABC", null), Route(table, "/a/ABC"));
        Assert.Equal(new ProductRoute("12"), Route(table, "/a/12"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FewerThanTwoNamesAreRejected(int count)
    {
        var builder = new RouteTableBuilder();
        string[] names = ["slug", "int"];

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.DeclareDisjointConstraints(names.AsSpan(0, count)));

        Assert.Equal("names", exception.ParamName);
        Assert.Contains(FewerThanTwo, exception.Message, StringComparison.Ordinal);
    }

    // An array that is null is an empty list of names.
    [Fact]
    public void ANullArrayIsRejectedAsNoNames()
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.DeclareDisjointConstraints((string[])null!));

        Assert.Equal("names", exception.ParamName);
        Assert.Contains(FewerThanTwo, exception.Message, StringComparison.Ordinal);
    }

    // A null name is reported before anything else: before the number of names, and before a name earlier in the
    // list that is not valid.
    public static TheoryData<string?[]> ListsWithANullName { get; } = new()
    {
        new[] { "slug", null },
        new string?[] { null },
        new string?[] { null, null },
        new[] { "a b", null },
        new[] { "string", "int", null },
        new[] { "int", "INT", null },
    };

    [Theory]
    [MemberData(nameof(ListsWithANullName))]
    public void ANullNameIsRejectedBeforeAnythingElse(string?[] names)
    {
        var builder = new RouteTableBuilder();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => builder.DeclareDisjointConstraints(names!));

        Assert.Equal("names", exception.ParamName);
    }

    [Fact]
    public void ASingleNullNameIsANullName()
    {
        var builder = new RouteTableBuilder();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => builder.DeclareDisjointConstraints((string)null!));

        Assert.Equal("names", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" int")]
    [InlineData("1a")]
    [InlineData("a b")]
    [InlineData("int?")]
    [InlineData("range(1,5)")]
    [InlineData("\u00E9t\u00E9")]
    public void ANameNotOfTheFormOfAConstraintNameIsRejected(string name)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.DeclareDisjointConstraints("int", name));

        Assert.Equal("names", exception.ParamName);
        Assert.Contains(NotValid, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("Double")]
    [InlineData("ULONG")]
    public void AReservedNameIsRejected(string name)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.DeclareDisjointConstraints("int", name));

        Assert.Equal("names", exception.ParamName);
        Assert.Contains(Reserved, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{name.ToLowerInvariant()}'", exception.Message, StringComparison.Ordinal);
    }

    // After the number of names, each name is checked in turn, its form, then whether it is reserved, then whether it
    // repeats an earlier one, and the first failure is reported.
    [Theory]
    [InlineData(new[] { "a b", "string" }, "is not valid")]
    [InlineData(new[] { "string", "a b" }, "'string' is reserved")]
    [InlineData(new[] { "int", "int", "a b" }, "'int' is given twice")]
    [InlineData(new[] { "int", "a b", "int" }, "is not valid")]
    [InlineData(new[] { "int", "slug", "SLUG" }, "'slug' is given twice")]
    public void NamesAreCheckedInOrder(string[] names, string expected)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.DeclareDisjointConstraints(names));

        Assert.Equal("names", exception.ParamName);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("slug", "slug")]
    [InlineData("slug", "SLUG")]
    [InlineData("int", "Int")]
    public void TheSameNameTwiceIsRejected(string first, string second)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.DeclareDisjointConstraints(first, "guid", second));

        Assert.Equal("names", exception.ParamName);
        Assert.Contains(GivenTwice, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{first.ToLowerInvariant()}'", exception.Message, StringComparison.Ordinal);
    }

    // A call that fails keeps none of its pairs, not even those of the names before the one it fails on: neither the
    // pair of slug and upper, which would let Build succeed, nor a name that is never added, which would fail it.
    [Theory]
    [InlineData("string")]
    [InlineData("SLUG")]
    [InlineData("a b")]
    [InlineData(null)]
    public void AFailedCallChangesNothing(string? bad)
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .Map("/a/{s:slug}", _ => new SettingsRoute())
            .Map("/a/{u:upper}", _ => new FileRoute(null))
            .AddConstraint("upper", IsUpper);

        ArgumentException exception = Assert.ThrowsAny<ArgumentException>(
            () => builder.DeclareDisjointConstraints("slug", "upper", "never", bad!));

        Assert.Equal("names", exception.ParamName);
        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
        Assert.Contains(Ambiguous, message, StringComparison.Ordinal);
        Assert.DoesNotContain(NamedByDeclare, message, StringComparison.Ordinal);
    }

    // A declared name that is neither built in nor added by the time of Build is reported by Build, as a template's is,
    // and a misspelled built-in name is named.
    [Theory]
    [InlineData("nope", null)]
    [InlineData("itn", "int")]
    [InlineData("UUID", "guid")]
    public void BuildReportsADeclaredNameThatIsNotAdded(string name, string? similar)
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .DeclareDisjointConstraints("int", name)
            .Map("/a/{id:int}", _ => new SettingsRoute());

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;

        Assert.Contains($"'{name.ToLowerInvariant()}'", message, StringComparison.Ordinal);
        Assert.Contains(NamedByDeclare, message, StringComparison.Ordinal);
        Assert.Contains(NotAdded, message, StringComparison.Ordinal);
        if (similar is not null)
            Assert.Contains($"If '{similar}' was meant", message, StringComparison.Ordinal);
        else
            Assert.Contains("If one of them was meant", message, StringComparison.Ordinal);

        builder.AddConstraint(name, _ => false);
        Assert.Single(builder.Build().Definitions);
    }

    // A template that failed to map can leave its constraint's name known to the builder but not added, which is still
    // not a constraint a declaration can name.
    [Fact]
    public void ANameLeftByATemplateThatFailedToMapIsNotAdded()
    {
        var builder = new RouteTableBuilder();
        Assert.Throws<ArgumentNullException>(
            () => builder.Map<SettingsRoute>("/a/{x:left}", null!));
        builder.DeclareDisjointConstraints("left", "int");

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;

        Assert.Contains("'left'", message, StringComparison.Ordinal);
        Assert.Contains(NamedByDeclare, message, StringComparison.Ordinal);
    }

    // Build checks the names the templates give first, then the declared names, in the order they were first declared,
    // then the declared pairs against the samples, and only then the templates against each other.
    [Fact]
    public void BuildReportsProblemsInADeterministicOrder()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .DeclareDisjointConstraints("int", "long")
            .DeclareDisjointConstraints("first", "second")
            .Map("/a/{x:int}", _ => new SettingsRoute())
            .Map("/a/{x:long}", _ => new FileRoute(null))
            .Map("/b/{x:nope}", _ => new SettingsRoute());

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
        Assert.Contains(UnknownTemplateConstraint, message, StringComparison.Ordinal);

        builder.AddConstraint("nope", _ => false);
        message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
        Assert.Contains("'first', named by DeclareDisjointConstraints", message, StringComparison.Ordinal);

        builder.AddConstraint("first", _ => false);
        message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
        Assert.Contains("'second', named by DeclareDisjointConstraints", message, StringComparison.Ordinal);

        builder.AddConstraint("second", _ => false);
        message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
        Assert.Contains("'int' and 'long'", message, StringComparison.Ordinal);
        Assert.Contains(DeclaredFalse, message, StringComparison.Ordinal);
    }

    // A declaration that a sample proves false is reported, naming both constraints and the sample, whether the pair
    // is of two added constraints, an added and a built-in one, or two built-in ones.
    [Theory]
    [InlineData("lower", "word", "a")]
    [InlineData("word", "lower", "a")]
    [InlineData("digits", "int", "0")]
    [InlineData("alpha", "lower", "a")]
    [InlineData("int", "long", "0")]
    [InlineData("decimal", "long", "0")]
    [InlineData("bool", "alpha", "true")]
    [InlineData("guid", "guid-like", "00000000-0000-0000-0000-000000000000")]
    public void BuildReportsADeclarationASampleProvesFalse(string first, string second, string sample)
    {
        RouteTableBuilder builder = BuilderWithSampleConstraints().DeclareDisjointConstraints(first, second);

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;

        Assert.Contains($"The constraints '{first}' and '{second}'", message, StringComparison.Ordinal);
        Assert.Contains($"accept the text '{sample}'", message, StringComparison.Ordinal);
        Assert.Contains(DeclaredFalse, message, StringComparison.Ordinal);
    }

    // Of several false declarations, the first pair declared is reported, with the first sample both accept.
    [Fact]
    public void TheFirstFalsePairDeclaredIsReported()
    {
        RouteTableBuilder builder = BuilderWithSampleConstraints()
            .DeclareDisjointConstraints("int", "guid")
            .DeclareDisjointConstraints("guid", "bool", "alpha")
            .DeclareDisjointConstraints("int", "long");

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;

        Assert.Contains("'bool' and 'alpha'", message, StringComparison.Ordinal);
        Assert.Contains("'true'", message, StringComparison.Ordinal);
        Assert.DoesNotContain("'long'", message, StringComparison.Ordinal);
    }

    // A true declaration about two built-in constraints is harmless: they are disjoint anyway.
    [Fact]
    public void ATrueDeclarationOfBuiltInConstraintsChangesNothing()
    {
        RouteTable table = new RouteTableBuilder()
            .DeclareDisjointConstraints("int", "guid", "alpha")
            .Map("/a/{id:int}", c => new ProductRoute(c.Path("id")))
            .Map("/a/{key:guid}", c => new ProductByNameRoute(c.Path("key")))
            .Build();

        Assert.Equal(new ProductRoute("5"), Route(table, "/a/5"));
    }

    // Build passes the samples, and nothing else, to the function of a declared constraint, whether or not a template
    // names it, and an exception the function throws propagates unchanged.
    [Fact]
    public void BuildAsksADeclaredFunctionOnlyAboutSamples()
    {
        var received = new HashSet<string>(StringComparer.Ordinal);
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("seen", value =>
            {
                received.Add(value);
                return false;
            })
            .DeclareDisjointConstraints("seen", "int");

        Assert.Empty(builder.Build().Definitions);
        Assert.NotEmpty(received);
        Assert.Subset(new HashSet<string>(Samples, StringComparer.Ordinal), received);
    }

    [Fact]
    public void AnExceptionTheFunctionThrowsForASamplePropagates()
    {
        var thrown = new FormatException("from the constraint");
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("throws", _ => throw thrown)
            .DeclareDisjointConstraints("int", "throws");

        Assert.Same(thrown, Assert.Throws<FormatException>(builder.Build));
    }

    // Which samples a function receives does not depend on the order of the names: a function that throws for the
    // third sample throws from Build whether it is named first or second, beside a constraint that rejects every
    // sample before it.
    [Theory]
    [InlineData("int", "t")]
    [InlineData("t", "int")]
    [InlineData("alpha", "t")]
    [InlineData("t", "alpha")]
    public void AnExceptionForAnySamplePropagatesWhateverTheOrderOfTheNames(string first, string second)
    {
        var thrown = new FormatException("from the constraint");
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("t", value => value == "true" ? throw thrown : false)
            .DeclareDisjointConstraints(first, second);

        Assert.Same(thrown, Assert.Throws<FormatException>(builder.Build));
    }

    // Nor on the order of the declarations: a pair declared before it that a sample proves false is not reported
    // before the function has been asked about every sample.
    [Fact]
    public void AnExceptionForASamplePropagatesBeforeAnEarlierFalseDeclarationIsReported()
    {
        var thrown = new FormatException("from the constraint");
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("t", value => value == "true" ? throw thrown : false)
            .DeclareDisjointConstraints("int", "long")
            .DeclareDisjointConstraints("t", "guid");

        Assert.Same(thrown, Assert.Throws<FormatException>(builder.Build));
    }

    // Every name is resolved before any function is called, so a name that is not added is reported even when the
    // function of a constraint declared before it throws for every text.
    [Fact]
    public void ANameNotAddedIsReportedBeforeAnyFunctionIsCalled()
    {
        var calls = 0;
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("thrower", _ =>
            {
                calls++;
                throw new FormatException("from the constraint");
            })
            .DeclareDisjointConstraints("thrower", "int")
            .DeclareDisjointConstraints("missing", "int");

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;

        Assert.Contains("'missing', named by DeclareDisjointConstraints", message, StringComparison.Ordinal);
        Assert.Equal(0, calls);
    }

    // The check of the declarations asks each function about each sample exactly once per build, however many pairs
    // name it and however often a pair is declared, and comparing templates at a position where they have a declared
    // pair asks nothing more.
    [Fact]
    public void EachDeclaredFunctionIsAskedAboutEachSampleOncePerBuild()
    {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        Func<string, bool> Counting(string name, Func<string, bool> isSatisfiedBy)
        {
            return value =>
            {
                string key = name + ":" + value;
                calls[key] = calls.GetValueOrDefault(key) + 1;
                return isSatisfiedBy(value);
            };
        }

        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("lower", Counting("lower", value => value.Length > 0 && value.All(char.IsAsciiLetterLower)))
            .AddConstraint("upper", Counting("upper", IsUpper))
            .DeclareDisjointConstraints("lower", "upper")
            .DeclareDisjointConstraints("upper", "lower")
            .DeclareDisjointConstraints("lower", "upper", "int")
            .Map("/a/{x:lower}", _ => new SettingsRoute())
            .Map("/a/{y:upper}", _ => new FileRoute(null))
            .Map("/a/{z:int}", _ => new ProductRoute("p"));

        for (var build = 1; build <= 2; build++)
        {
            Assert.Equal(3, builder.Build().Definitions.Count);

            Assert.Equal(2 * Samples.Length, calls.Count);
            foreach (string name in new[] { "lower", "upper" })
            {
                foreach (string sample in Samples)
                    Assert.Equal(build, calls[name + ":" + sample]);
            }
        }
    }

    // A declaration belongs to its builder: one about a built-in constraint, whose instance every builder shares, does
    // not keep apart the same templates on another builder, built before or after.
    [Fact]
    public void ADeclarationDoesNotReachAnotherBuilder()
    {
        foreach (bool declaringFirst in new[] { true, false })
        {
            RouteTableBuilder declaring = new RouteTableBuilder()
                .AddConstraint("upper", IsUpper)
                .DeclareDisjointConstraints("upper", "int")
                .Map("/a/{u:upper}", _ => new SettingsRoute())
                .Map("/a/{id:int}", _ => new FileRoute(null));
            RouteTableBuilder other = new RouteTableBuilder()
                .AddConstraint("upper", IsUpper)
                .Map("/a/{u:upper}", _ => new SettingsRoute())
                .Map("/a/{id:int}", _ => new FileRoute(null));

            if (declaringFirst)
            {
                Assert.Equal(2, declaring.Build().Definitions.Count);
                Assert.Contains(Ambiguous, Assert.Throws<InvalidOperationException>(other.Build).Message);
            }
            else
            {
                Assert.Contains(Ambiguous, Assert.Throws<InvalidOperationException>(other.Build).Message);
                Assert.Equal(2, declaring.Build().Definitions.Count);
                Assert.Contains(Ambiguous, Assert.Throws<InvalidOperationException>(other.Build).Message);
            }
        }
    }

    // A table already built never changes: a declaration made after a failed Build lets the next one succeed, and one
    // made after a table is built reaches only the tables built after it.
    [Fact]
    public void ADeclarationReachesOnlyTablesBuiltAfterIt()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .AddConstraint("upper", IsUpper)
            .Map("/a/{s:slug}", c => new ProductByNameRoute(c.Path("s")))
            .Map("/a/{id:int}", c => new ProductRoute(c.Path("id")));

        Assert.Contains(Ambiguous, Assert.Throws<InvalidOperationException>(builder.Build).Message);

        RouteTable first = builder.DeclareDisjointConstraints("slug", "int").Build();

        builder.Map("/a/{u:upper}", c => new OrderRoute(c.Path("u"), null));
        Assert.Contains(Ambiguous, Assert.Throws<InvalidOperationException>(builder.Build).Message);

        RouteTable second = builder.DeclareDisjointConstraints("upper", "slug", "int").Build();

        Assert.Equal(2, first.Definitions.Count);
        Assert.False(first.Match(new Uri("/a/ABC", UriKind.Relative)).IsSuccess);
        Assert.Equal(new ProductByNameRoute("abc"), Route(first, "/a/abc"));
        Assert.Equal(3, second.Definitions.Count);
        Assert.Equal(new OrderRoute("ABC", null), Route(second, "/a/ABC"));
    }

    // lower and word accept ASCII letters, lower only lowercase ones, so both accept a and true; digits accepts ASCII
    // digits, as int does 0; guid-like accepts any text of 36 characters.
    private static RouteTableBuilder BuilderWithSampleConstraints()
    {
        return new RouteTableBuilder()
            .AddConstraint("lower", value => value.Length > 0 && value.All(char.IsAsciiLetterLower))
            .AddConstraint("word", value => value.Length > 0 && value.All(char.IsAsciiLetter))
            .AddConstraint("digits", value => value.Length > 0 && value.All(char.IsAsciiDigit))
            .AddConstraint("guid-like", value => value.Length == 36);
    }

    private static AppRoute? Route(RouteTable table, string path)
    {
        RouteMatchResult result = table.Match(new Uri(path, UriKind.Relative));
        Assert.True(result.IsSuccess, path);
        return result.Route;
    }
}
