using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// AddConstraint adds a constraint of the application's own to the templates mapped on one builder: how its name is
// checked, that the order of the calls does not matter, that Build reports a name that was never added, and that
// Match and Format ask the function exactly as they ask a built-in constraint.
public sealed class RouteTableBuilderConstraintTests
{
    private const string NotValid = "is not valid";
    private const string BuiltInName = "is the name of a built-in constraint";
    private const string Reserved = "is reserved";
    private const string AlreadyAdded = "is already added on this builder";
    private const string NotAdded = "neither built in nor added on this builder";
    private const string UnknownConstraint = "unknown constraint";
    private const string SameTemplate = "are the same template";
    private const string Ambiguous = "rank equally and both match the path";
    private const string NotSatisfied = "does not satisfy its constraint 'slug'";
    private const string MatchedFirst = "is matched first by the template";
    private const string DotSegmentValue = "has a value that is a dot segment";

    // A value that stands for a secret, distinctive enough that finding it in a message is not a coincidence.
    private const string Secret = "secret-7qx9zr2kp4";

    // One or more ASCII lowercase letters, digits and hyphens.
    private static bool IsSlug(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (char c in value)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
                return false;
        }

        return true;
    }

    [Fact]
    public void AddingReturnsTheSameBuilder()
    {
        var builder = new RouteTableBuilder();

        Assert.Same(builder, builder.AddConstraint("slug", IsSlug));
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        var builder = new RouteTableBuilder();

        ArgumentNullException name = Assert.Throws<ArgumentNullException>(() => builder.AddConstraint(null!, IsSlug));
        ArgumentNullException function = Assert.Throws<ArgumentNullException>(
            () => builder.AddConstraint("slug", null!));

        Assert.Equal("name", name.ParamName);
        Assert.Equal("isSatisfiedBy", function.ParamName);
        AssertSlugCanStillBeAdded(builder);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" slug")]
    [InlineData("slug ")]
    [InlineData("1a")]
    [InlineData("-a")]
    [InlineData("_a")]
    [InlineData("a b")]
    [InlineData("a.b")]
    [InlineData("a:b")]
    [InlineData("a?")]
    [InlineData("a*")]
    [InlineData("a/b")]
    [InlineData("{a}")]
    [InlineData("range(1,5)")]
    [InlineData("a\u0009")]
    [InlineData("\u00E9t\u00E9")]
    [InlineData("\u212Aey")]
    [InlineData("\uFF49nt")]
    public void ANameNotOfTheFormIsRejected(string name)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => builder.AddConstraint(name, IsSlug));

        Assert.Equal("name", exception.ParamName);
        Assert.Contains(NotValid, exception.Message, StringComparison.Ordinal);
        AssertSlugCanStillBeAdded(builder);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("INT")]
    [InlineData("long")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("Guid")]
    [InlineData("alpha")]
    public void ABuiltInNameIsRejected(string name)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => builder.AddConstraint(name, IsSlug));

        Assert.Equal("name", exception.ParamName);
        Assert.Contains(BuiltInName, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{name.ToLowerInvariant()}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("char")]
    [InlineData("sbyte")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("ulong")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("Double")]
    [InlineData("STRING")]
    public void AReservedNameIsRejected(string name)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => builder.AddConstraint(name, IsSlug));

        Assert.Equal("name", exception.ParamName);
        Assert.Contains(Reserved, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{name.ToLowerInvariant()}'", exception.Message, StringComparison.Ordinal);
        AssertSlugCanStillBeAdded(builder);
    }

    // A second function for a name is rejected, whatever case it is written in, and the first one stays.
    [Theory]
    [InlineData("slug")]
    [InlineData("SLUG")]
    [InlineData("Slug")]
    public void ANameAlreadyAddedIsRejectedAndKeepsItsFunction(string again)
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")));

        ArgumentException exception = Assert.Throws<ArgumentException>(() => builder.AddConstraint(again, _ => true));

        Assert.Equal("name", exception.ParamName);
        Assert.Contains(AlreadyAdded, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'slug'", exception.Message, StringComparison.Ordinal);
        RouteTable table = builder.Build();
        Assert.True(Matches(table, "/p/ok"));
        Assert.False(Matches(table, "/p/NotASlug"));
    }

    // Every name of the form is constraint text that the template parser accepts, as written and in any case, required
    // and optional.
    [Theory]
    [InlineData("a")]
    [InlineData("Z")]
    [InlineData("slug")]
    [InlineData("order-code")]
    [InlineData("a_1")]
    [InlineData("x--")]
    [InlineData("y__")]
    [InlineData("Z9")]
    [InlineData("integer")]
    [InlineData("int32")]
    [InlineData("enum")]
    public void EveryNameOfTheFormCanBeAddedAndNamedByATemplate(string name)
    {
        foreach (string written in new[] { name, name.ToUpperInvariant(), name.ToLowerInvariant() })
        {
            RouteTable table = new RouteTableBuilder()
                .AddConstraint(name, value => value == "yes")
                .Map($"/p/{{v:{written}}}", _ => new SettingsRoute())
                .Map($"/q/{{v:{written}?}}", _ => new SettingsRoute())
                .Build();

            Assert.True(Matches(table, "/p/yes"));
            Assert.False(Matches(table, "/p/no"));
            Assert.True(Matches(table, "/q"));
            Assert.True(Matches(table, "/q/yes"));
            Assert.False(Matches(table, "/q/no"));
        }
    }

    // Constraint text that no added constraint can be named is still a template error at Map, so it is never left for
    // Build to report. That includes a constraint written with arguments.
    [Theory]
    [InlineData("/p/{n:range(1,5)}", "range(1,5)")]
    [InlineData("/p/{n:min(1)}", "min(1)")]
    [InlineData("/p/{n:1a}", "1a")]
    [InlineData("/p/{n:-a}", "-a")]
    [InlineData("/p/{n:_a}", "_a")]
    [InlineData("/p/{n:a.b}", "a.b")]
    [InlineData("/p/{n: slug}", " slug")]
    [InlineData("/p/{n:slug }", "slug ")]
    [InlineData("/p/{n:%73lug}", "%73lug")]
    [InlineData("/p/{n:\u00E9t\u00E9}", "\u00E9t\u00E9")]
    [InlineData("/p/{n:\u212Aey?}", "\u212Aey")]
    public void ConstraintTextThatCannotBeANameIsStillRejectedByMap(string template, string constraint)
    {
        RouteTableBuilder builder = new RouteTableBuilder().AddConstraint("slug", IsSlug);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.Map(template, _ => new SettingsRoute()));

        Assert.Equal("template", exception.ParamName);
        Assert.Contains($"{UnknownConstraint} '{constraint}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AddConstraint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARangeWithMoreThanOneColonIsStillRejectedAsSuch()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new RouteTableBuilder().Map("/p/{n:range(1:5)}", _ => new SettingsRoute()));

        Assert.Equal("template", exception.ParamName);
        Assert.Contains("more than one ':'", exception.Message, StringComparison.Ordinal);
    }

    // RouteTemplate.Parse knows only the built-in constraints, so it rejects a name at once, even one that a builder
    // has added, and says where an added constraint is known.
    [Fact]
    public void ParseRejectsANameThatOnlyABuilderKnows()
    {
        new RouteTableBuilder().AddConstraint("slug", IsSlug);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse("/p/{s:slug}"));

        Assert.Equal("value", exception.ParamName);
        Assert.Contains($"{UnknownConstraint} 'slug'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "known only to the templates mapped on the builder it was added to",
            exception.Message,
            StringComparison.Ordinal);
    }

    // A reserved name can never be added, so a template that names one is rejected by Map, in any case, rather than
    // left for Build; RouteTemplate.Parse says the same.
    [Theory]
    [InlineData("/p/{x:string}", "string")]
    [InlineData("/p/{x:Double}", "double")]
    [InlineData("/p/{x:CHAR?}", "char")]
    [InlineData("/p/{a:int}/{x:ulong}", "ulong")]
    public void AReservedNameIsRejectedByMapAndByParse(string template, string name)
    {
        ArgumentException map = Assert.Throws<ArgumentException>(
            () => new RouteTableBuilder().Map(template, _ => new SettingsRoute()));
        ArgumentException parse = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(template));

        Assert.Equal("template", map.ParamName);
        Assert.Equal("value", parse.ParamName);
        foreach (ArgumentException exception in new[] { map, parse })
        {
            Assert.Contains(Reserved, exception.Message, StringComparison.Ordinal);
            Assert.Contains($"'{name}'", exception.Message, StringComparison.Ordinal);
        }
    }

    // A Map that fails after its template is parsed adds no mapping, so the names its template mentioned are no
    // error for Build, and can still be added later.
    [Fact]
    public void NamesFromAMapThatFailedAreNoErrorAndCanStillBeAdded()
    {
        var builder = new RouteTableBuilder();

        Assert.Throws<ArgumentException>(() => builder.Map("/p/{a:first}/{b:second}/{A}", _ => new SettingsRoute()));
        Assert.Throws<ArgumentNullException>(
            () => builder.Map<SettingsRoute>("/q/{c:third}", createRoute: null!));

        Assert.Empty(builder.Build().Definitions);

        RouteTable table = builder
            .AddConstraint("first", value => value == "1")
            .AddConstraint("third", value => value == "3")
            .Map("/p/{a:first}", _ => new SettingsRoute())
            .Map("/q/{c:THIRD}", _ => new SettingsRoute())
            .Build();

        Assert.True(Matches(table, "/p/1"));
        Assert.False(Matches(table, "/p/2"));
        Assert.True(Matches(table, "/q/3"));
    }

    [Fact]
    public void TheConstraintCanBeAddedBeforeOrAfterTheMapThatNamesIt()
    {
        RouteTable before = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")))
            .Build();
        RouteTable after = new RouteTableBuilder()
            .Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")))
            .AddConstraint("slug", IsSlug)
            .Build();

        foreach (RouteTable table in new[] { before, after })
        {
            Assert.Equal(new ProductRoute("a-1"), table.Match(new Uri("/p/a-1", UriKind.Relative)).Route);
            Assert.False(Matches(table, "/p/A"));
        }
    }

    [Fact]
    public void BuildReportsANameThatWasNeverAdded()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .Map("/a/{x:int}", _ => new SettingsRoute())
            .Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(NotAdded, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/p/{s:slug}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{nameof(ProductRoute)}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'slug'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "'int', 'long', 'decimal', 'bool', 'guid' and 'alpha'",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("AddConstraint", exception.Message, StringComparison.Ordinal);
    }

    // A misspelled built-in name has the form of an added one, so Build reports it, and says which built-in
    // constraint it resembles: one character added, left out or changed, two adjacent characters swapped, or the
    // name another system gives the type.
    [Theory]
    [InlineData("itn", "int")]
    [InlineData("nit", "int")]
    [InlineData("ints", "int")]
    [InlineData("in", "int")]
    [InlineData("imt", "int")]
    [InlineData("Int32", "int")]
    [InlineData("integer", "int")]
    [InlineData("lnog", "long")]
    [InlineData("lung", "long")]
    [InlineData("int-", "int")]
    [InlineData("Int64", "long")]
    [InlineData("deciml", "decimal")]
    [InlineData("boolean", "bool")]
    [InlineData("boool", "bool")]
    [InlineData("giud", "guid")]
    [InlineData("UUID", "guid")]
    [InlineData("alpah", "alpha")]
    [InlineData("alfha", "alpha")]
    public void BuildNamesTheBuiltInConstraintThatAMisspelledNameResembles(string written, string meant)
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .Map("/p/{s:" + written + "}", c => new ProductRoute(c.Path("s")));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(NotAdded, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"If '{meant}' was meant", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AddConstraint", exception.Message, StringComparison.Ordinal);
    }

    // A name two or more edits from every built-in name gets no such hint, only the general one.
    [Theory]
    [InlineData("slug")]
    [InlineData("tin")]
    [InlineData("i")]
    [InlineData("integers")]
    [InlineData("alphabet")]
    [InlineData("lungs")]
    [InlineData("int-id")]
    public void BuildNamesNoBuiltInConstraintForANameThatResemblesNone(string written)
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .Map("/p/{s:" + written + "}", c => new ProductRoute(c.Path("s")));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(NotAdded, exception.Message, StringComparison.Ordinal);
        Assert.Contains("If one of them was meant", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("' was meant", exception.Message, StringComparison.Ordinal);
    }

    // The hint is advice only: a name that resembles a built-in one can still be added and used.
    [Fact]
    public void ANameThatResemblesABuiltInConstraintCanBeAdded()
    {
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("ints", text => text == "1,2")
            .Map("/p/{s:ints}", c => new ProductRoute(c.Path("s")))
            .Build();

        Assert.True(Matches(table, "/p/1,2"));
        Assert.False(Matches(table, "/p/1"));
    }

    // The first name not added, in the order the routes were mapped and then in template order, is reported in the
    // case messages write names in, before any other problem with the templates.
    [Fact]
    public void BuildReportsTheFirstNameNotAddedBeforeAnyOtherProblem()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("known", _ => true)
            .Map("/a/{x:known}/{y:First}/{z:second}", _ => new SettingsRoute())
            .Map("/a/{x:known}/{y:First}/{z:second}", _ => new SettingsRoute())
            .Map("/b/{x:third}", _ => new SettingsRoute());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(NotAdded, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'first'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'second'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'third'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SameTemplate, exception.Message, StringComparison.Ordinal);
    }

    // A constraint belongs to the builder it was added to.
    [Fact]
    public void AConstraintAddedOnAnotherBuilderIsNotKnown()
    {
        new RouteTableBuilder().AddConstraint("slug", IsSlug).Map("/p/{s:slug}", _ => new SettingsRoute()).Build();
        RouteTableBuilder other = new RouteTableBuilder().Map("/p/{s:slug}", _ => new SettingsRoute());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(other.Build);

        Assert.Contains(NotAdded, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedBuildCanBeRepeatedAndSucceedsOnceTheConstraintIsAdded()
    {
        RouteTableBuilder builder = new RouteTableBuilder().Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")));

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(builder.Build).Message);

        builder.AddConstraint("slug", IsSlug);
        RouteTable first = builder.Build();
        RouteTable second = builder.Build();

        Assert.True(Matches(first, "/p/ok"));
        Assert.True(Matches(second, "/p/ok"));
        Assert.False(Matches(second, "/p/NO"));
    }

    // Adding and mapping after a build leaves the table already built as it was.
    [Fact]
    public void ATableAlreadyBuiltDoesNotChangeWhenTheBuilderGainsConstraints()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")));
        RouteTable earlier = builder.Build();

        builder.Map("/q/{s:upper}", c => new ProductByNameRoute(c.Path("s")));
        Assert.Throws<InvalidOperationException>(builder.Build);
        builder.AddConstraint("upper", value => value.Length > 0 && value.All(char.IsAsciiLetterUpper));
        RouteTable later = builder.Build();

        Assert.Single(earlier.Definitions);
        Assert.False(Matches(earlier, "/q/AB"));
        Assert.True(Matches(earlier, "/p/ok"));
        Assert.Equal(2, later.Definitions.Count);
        Assert.True(Matches(later, "/q/AB"));
        Assert.False(Matches(later, "/q/ab"));
    }

    // The function receives the segment decoded, exactly as a built-in constraint does, and never trimmed; the value
    // captured is that text, unchanged.
    [Fact]
    public void MatchAsksTheFunctionWithTheDecodedSegment()
    {
        var received = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("spaced", value =>
            {
                received.Add(value);
                return value.Contains(' ', StringComparison.Ordinal);
            })
            .Map("/p/{s:spaced}", c => new ProductRoute(c.Path("s")))
            .Build();

        Assert.Equal(new ProductRoute("a b"), table.Match(new Uri("/p/a%20b", UriKind.Relative)).Route);
        Assert.Equal(new ProductRoute(" x "), table.Match(new Uri("/p/%20x%20", UriKind.Relative)).Route);
        Assert.Equal(
            new ProductRoute("caf\u00E9 1"),
            table.Match(new Uri("/p/caf%C3%A9%201", UriKind.Relative)).Route);
        Assert.Equal(new ProductRoute("a/ b"), table.Match(new Uri("/p/a%2F%20b", UriKind.Relative)).Route);
        Assert.False(Matches(table, "/p/ab"));
        Assert.Equal(["a b", " x ", "caf\u00E9 1", "a/ b", "ab"], received);
    }

    [Fact]
    public void ASegmentTheFunctionRefusesDoesNotMatchAndAnotherTemplateCanTakeIt()
    {
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")))
            .Map("/p/{name}", c => new ProductByNameRoute(c.Path("name")))
            .Build();

        Assert.Equal(new ProductRoute("ok"), table.Match(new Uri("/p/ok", UriKind.Relative)).Route);
        Assert.Equal(new ProductByNameRoute("Not OK"), table.Match(new Uri("/p/Not%20OK", UriKind.Relative)).Route);
    }

    [Fact]
    public void AnOptionalParameterWithAnAddedConstraintMatchesOneSegmentOrNone()
    {
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .Map("/p/{s:slug?}", c => new OrderRoute("p", c.TryGetPath("s", out string? s) ? s : null))
            .Build();

        Assert.Equal(new OrderRoute("p", null), table.Match(new Uri("/p", UriKind.Relative)).Route);
        Assert.Equal(new OrderRoute("p", "ok"), table.Match(new Uri("/p/ok", UriKind.Relative)).Route);
        Assert.False(Matches(table, "/p/NO"));
    }

    [Fact]
    public void TheNameIsComparedIgnoringCase()
    {
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("Slug", IsSlug)
            .Map("/p/{s:SLUG}", _ => new SettingsRoute())
            .Map("/q/{s:sLuG}", _ => new SettingsRoute())
            .Build();

        Assert.True(Matches(table, "/p/ok"));
        Assert.True(Matches(table, "/q/ok"));
        Assert.False(Matches(table, "/q/OK"));
    }

    // One constraint for each name on a builder, so the same name in another case, under another parameter name, makes
    // the same template, whether it is added before or after.
    [Fact]
    public void TheSameNameInAnotherCaseMakesTheSameTemplate()
    {
        foreach (bool addFirst in new[] { true, false })
        {
            var builder = new RouteTableBuilder();
            if (addFirst)
                builder.AddConstraint("slug", IsSlug);

            builder.Map("/p/{x:slug}", _ => new SettingsRoute()).Map("/p/{y:SLUG}", _ => new ProductRoute("p"));
            if (!addFirst)
                builder.AddConstraint("slug", IsSlug);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

            Assert.Contains(SameTemplate, exception.Message, StringComparison.Ordinal);
            Assert.Contains("'/p/{x:slug}'", exception.Message, StringComparison.Ordinal);
            Assert.Contains("'/p/{y:SLUG}'", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnExceptionTheFunctionThrowsPropagatesFromMatch()
    {
        var thrown = new InvalidTimeZoneException("from the constraint");
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("odd", value => value == "boom" ? throw thrown : true)
            .Map("/p/{s:odd}", c => new ProductRoute(c.Path("s")))
            .Build();

        Assert.Same(thrown, Assert.Throws<InvalidTimeZoneException>(
            () => table.Match(new Uri("/p/boom", UriKind.Relative))));
        Assert.True(Matches(table, "/p/fine"));
    }

    // A RouteValueException is what a create function throws to fail a match, but one from a constraint is not caught
    // as that: the constraint runs while the table selects a template, before any create function is called.
    [Fact]
    public void ARouteValueExceptionTheFunctionThrowsAlsoPropagatesFromMatch()
    {
        var thrown = new RouteValueException("s", "from the constraint");
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("odd", value => value == "boom" ? throw thrown : true)
            .Map("/p/{s:odd}", c => new ProductRoute(c.Path("s")))
            .Build();

        Assert.Same(thrown, Assert.Throws<RouteValueException>(
            () => table.Match(new Uri("/p/boom", UriKind.Relative))));
    }

    [Fact]
    public void AnExceptionTheFunctionThrowsPropagatesFromFormatAndFormatUri()
    {
        var thrown = new InvalidTimeZoneException("from the constraint");
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("odd", value => value == "boom" ? throw thrown : true)
            .Map("/p/{s:odd}", c => new ProductRoute(c.Path("s")), f => f.Path("s", r => r.Id))
            .Build();

        Assert.Same(thrown, Assert.Throws<InvalidTimeZoneException>(() => table.Format(new ProductRoute("boom"))));
        Assert.Same(thrown, Assert.Throws<InvalidTimeZoneException>(
            () => table.FormatUri(new ProductRoute("boom"), new Uri("https://example.com"))));
        Assert.Equal("/p/fine", table.Format(new ProductRoute("fine")));
    }

    // Build asks the function about sample texts only when it compares two templates of one shape, and an exception
    // the function throws there propagates.
    [Fact]
    public void AnExceptionTheFunctionThrowsPropagatesFromBuild()
    {
        var thrown = new InvalidTimeZoneException("from the constraint");
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("odd", _ => throw thrown)
            .Map("/p/{s:odd}", c => new ProductRoute(c.Path("s")));

        Assert.Single(builder.Build().Definitions);

        builder.Map("/p/{n:int}", _ => new SettingsRoute());
        Assert.Same(thrown, Assert.Throws<InvalidTimeZoneException>(builder.Build));
    }

    // A dot segment never matches, so the function is not asked about one, however it is encoded.
    [Theory]
    [InlineData("/p/.")]
    [InlineData("/p/..")]
    [InlineData("/p/%2E")]
    [InlineData("/p/%2e%2E")]
    public void MatchDoesNotAskTheFunctionAboutADotSegment(string path)
    {
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("nodots", value => value is "." or ".." ? throw new InvalidOperationException() : true)
            .Map("/p/{s:nodots}", c => new ProductRoute(c.Path("s")))
            .Build();

        Assert.False(Matches(table, path));
        Assert.Null(table.Definitions[0].Template.Match(path));
    }

    // Format rejects a dot segment before it asks the constraint, as Match does, even for a function that accepts it.
    [Fact]
    public void FormatRejectsADotSegmentWithoutAskingTheFunction()
    {
        var received = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("any", value =>
            {
                received.Add(value);
                return true;
            })
            .Map("/p/{s:any}", c => new ProductRoute(c.Path("s")), f => f.Path("s", r => r.Id))
            .Build();

        foreach (string dotSegment in new[] { ".", ".." })
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => table.Format(new ProductRoute(dotSegment)));
            ArgumentException template = Assert.Throws<ArgumentException>(
                () => table.Definitions[0].Template.Format(new Dictionary<string, string> { ["s"] = dotSegment }));

            Assert.Equal("route", exception.ParamName);
            Assert.Contains(DotSegmentValue, exception.Message, StringComparison.Ordinal);
            Assert.Contains("is a dot segment", template.Message, StringComparison.Ordinal);
        }

        Assert.Empty(received);
        Assert.Equal("/p/a.b", table.Format(new ProductRoute("a.b")));
        Assert.Equal(["a.b"], received);
    }

    // A template read from Definitions keeps the builder's constraint, so its own Match and Format call the function,
    // and an exception it throws propagates from them too.
    [Fact]
    public void AnExceptionTheFunctionThrowsPropagatesFromTheTemplateOfADefinition()
    {
        var thrown = new InvalidTimeZoneException("from the constraint");
        RouteTemplate template = new RouteTableBuilder()
            .AddConstraint("odd", value => value == "boom" ? throw thrown : value == "ok")
            .Map("/p/{s:odd}", c => new ProductRoute(c.Path("s")))
            .Build()
            .Definitions[0]
            .Template;

        Assert.Same(thrown, Assert.Throws<InvalidTimeZoneException>(() => template.Match("/p/boom")));
        Assert.Same(thrown, Assert.Throws<InvalidTimeZoneException>(
            () => template.Format(new Dictionary<string, string> { ["s"] = "boom" })));
        Assert.Equal("ok", template.Match("/p/ok")?["s"]);
        Assert.Null(template.Match("/p/no"));
        Assert.Equal("/p/ok", template.Format(new Dictionary<string, string> { ["s"] = "ok" }));
        Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(template.Value));
    }

    [Fact]
    public void FormatChecksTheValueWrittenWithTheFunction()
    {
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")), f => f.Path("s", r => r.Id))
            .Build();

        Assert.Equal("/p/" + Secret, table.Format(new ProductRoute(Secret)));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.Format(new ProductRoute(Secret.ToUpperInvariant())));

        Assert.Equal("route", exception.ParamName);
        Assert.Contains(NotSatisfied, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Format refuses a path that a template ranking higher would match first, and asks an added constraint about the
    // written value to find out, as it asks a built-in one.
    [Fact]
    public void FormatAsksTheFunctionWhetherATemplateThatRanksHigherWouldMatchThePath()
    {
        RouteTable table = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")))
            .Map("/p/{name}", c => new ProductByNameRoute(c.Path("name")), f => f.Path("name", r => r.Name))
            .Build();

        Assert.Equal("/p/Not%20" + Secret, table.Format(new ProductByNameRoute("Not " + Secret)));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.Format(new ProductByNameRoute(Secret)));

        Assert.Equal("route", exception.ParamName);
        Assert.Contains(MatchedFirst, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/p/{s:slug}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    // A parameter with an added constraint ranks as one with a built-in constraint: above one without, whatever the
    // order of the mappings.
    [Fact]
    public void AnAddedConstraintOutranksNoConstraint()
    {
        foreach (bool constrainedFirst in new[] { true, false })
        {
            var builder = new RouteTableBuilder().AddConstraint("slug", IsSlug);
            if (constrainedFirst)
                builder.Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")));

            builder.Map("/p/{name}", c => new ProductByNameRoute(c.Path("name")));
            if (!constrainedFirst)
                builder.Map("/p/{s:slug}", c => new ProductRoute(c.Path("s")));

            RouteTable table = builder.Build();

            Assert.Equal(new ProductRoute("ok"), table.Match(new Uri("/p/ok", UriKind.Relative)).Route);
            Assert.Equal(new ProductByNameRoute("OK"), table.Match(new Uri("/p/OK", UriKind.Relative)).Route);
        }
    }

    // It ranks equally with a built-in constraint, and cannot be known to be apart from it, so the two are rejected.
    [Fact]
    public void AnAddedConstraintTiesWithABuiltInOneAndTheyAreRejected()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddConstraint("slug", IsSlug)
            .Map("/p/{s:slug}", _ => new SettingsRoute())
            .Map("/p/{n:int}", _ => new SettingsRoute());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(Ambiguous, exception.Message, StringComparison.Ordinal);
    }

    private static bool Matches(RouteTable table, string path)
    {
        return table.Match(new Uri(path, UriKind.Relative)).IsSuccess;
    }

    // A rejected call leaves the builder as it was, so the name can still be added and used.
    private static void AssertSlugCanStillBeAdded(RouteTableBuilder builder)
    {
        RouteTable table = builder.AddConstraint("slug", IsSlug).Map("/p/{s:slug}", _ => new SettingsRoute()).Build();

        Assert.True(Matches(table, "/p/ok"));
    }
}
