using AdamE.AppNav.State;
using AdamE.AppNav.Tests.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.State;

public sealed class RouteEntryTests
{
    private static readonly RouteMetadataKey<string> Campaign = new("campaign");
    private static readonly RouteMetadataKey<int> Step = new("step");

    // Texts that are empty or only white space: a space, a tab, and a mix of a space, a tab, a line feed, a carriage
    // return, a no-break space and an em space.
    public static TheoryData<string> BlankIds => new()
    {
        "",
        " ",
        "\t",
        " \t\n\r\u00A0\u2003",
    };

    [Fact]
    public void TheTwoArgumentConstructorGivesEmptyMetadata()
    {
        ProductRoute route = new("42");

        RouteEntry entry = new("entry-1", route);

        Assert.Equal("entry-1", entry.Id);
        Assert.Same(route, entry.Route);
        Assert.Equal(RouteMetadata.Empty, entry.Metadata);
        Assert.Equal(0, entry.Metadata.Count);
    }

    [Fact]
    public void TheThreeArgumentConstructorKeepsTheIdRouteAndMetadata()
    {
        ProductRoute route = new("42");
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring");

        RouteEntry entry = new("entry-1", route, metadata);

        Assert.Equal("entry-1", entry.Id);
        Assert.Same(route, entry.Route);
        Assert.Same(metadata, entry.Metadata);
    }

    // The id is not trimmed or folded: it is kept exactly as given.
    [Theory]
    [InlineData(" entry-1")]
    [InlineData("entry-1 ")]
    [InlineData("\tEntry-1\n")]
    public void TheIdIsKeptAsGiven(string id)
    {
        RouteEntry entry = new(id, new ProductRoute("42"));

        Assert.Same(id, entry.Id);
    }

    [Fact]
    public void ANullIdThrows()
    {
        ArgumentNullException two = Assert.Throws<ArgumentNullException>(
            () => new RouteEntry(null!, new ProductRoute("route-value-42")));
        ArgumentNullException three = Assert.Throws<ArgumentNullException>(
            () => new RouteEntry(null!, new ProductRoute("route-value-42"), RouteMetadata.Empty));

        Assert.Equal("id", two.ParamName);
        Assert.Equal("id", three.ParamName);
        Assert.DoesNotContain("route-value-42", two.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("route-value-42", three.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(BlankIds))]
    public void ABlankIdThrows(string id)
    {
        ArgumentException two = Assert.Throws<ArgumentException>(
            () => new RouteEntry(id, new ProductRoute("route-value-42")));
        ArgumentException three = Assert.Throws<ArgumentException>(
            () => new RouteEntry(id, new ProductRoute("route-value-42"), RouteMetadata.Empty));

        Assert.Equal("id", two.ParamName);
        Assert.Equal("id", three.ParamName);
        Assert.DoesNotContain("route-value-42", two.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("route-value-42", three.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullRouteThrows()
    {
        ArgumentNullException two = Assert.Throws<ArgumentNullException>(() => new RouteEntry("entry-secret", null!));
        ArgumentNullException three = Assert.Throws<ArgumentNullException>(
            () => new RouteEntry("entry-secret", null!, RouteMetadata.Empty));

        Assert.Equal("route", two.ParamName);
        Assert.Equal("route", three.ParamName);
        Assert.DoesNotContain("entry-secret", two.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-secret", three.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullMetadataThrows()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new RouteEntry("entry-secret", new ProductRoute("route-value-42"), null!));

        Assert.Equal("metadata", exception.ParamName);
        Assert.DoesNotContain("entry-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("route-value-42", exception.Message, StringComparison.Ordinal);
    }

    // The id is checked first, then the route, then the metadata.
    [Fact]
    public void TheIdIsCheckedBeforeTheRouteAndTheRouteBeforeTheMetadata()
    {
        ArgumentNullException nullId = Assert.Throws<ArgumentNullException>(
            () => new RouteEntry(null!, null!, null!));
        ArgumentException blankId = Assert.Throws<ArgumentException>(() => new RouteEntry(" ", null!, null!));
        ArgumentNullException nullRoute = Assert.Throws<ArgumentNullException>(
            () => new RouteEntry("entry-1", null!, null!));
        ArgumentNullException twoArguments = Assert.Throws<ArgumentNullException>(
            () => new RouteEntry(null!, null!));

        Assert.Equal("id", nullId.ParamName);
        Assert.Equal("id", blankId.ParamName);
        Assert.Equal("route", nullRoute.ParamName);
        Assert.Equal("id", twoArguments.ParamName);
    }

    // The metadata are built in different orders and hold the same entries, so they are equal.
    [Fact]
    public void EqualIdsRoutesAndMetadataAreEqual()
    {
        RouteEntry first = new(
            "entry-1",
            new ProductRoute("42"),
            RouteMetadata.Empty.With(Campaign, "spring").With(Step, 2));
        RouteEntry second = new(
            "entry-1",
            new ProductRoute("42"),
            RouteMetadata.Empty.With(Step, 2).With(Campaign, "spring"));

        Assert.Equal(first, second);
        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void AnEntryEqualsItself()
    {
        RouteEntry entry = new("entry-1", new ProductRoute("42"));
        RouteEntry same = entry;

        Assert.True(entry.Equals(same));
        Assert.True(entry == same);
        Assert.False(entry != same);
    }

    // The same route shown twice is two entries, told apart by their ids.
    [Fact]
    public void EntriesWithDifferentIdsAreNotEqual()
    {
        ProductRoute route = new("42");
        RouteEntry first = new("entry-1", route);
        RouteEntry second = new("entry-2", route);

        Assert.NotEqual(first, second);
        Assert.False(first == second);
        Assert.True(first != second);
    }

    [Fact]
    public void IdsAreComparedCaseSensitively()
    {
        RouteEntry lower = new("entry-a", new ProductRoute("42"));
        RouteEntry upper = new("entry-A", new ProductRoute("42"));

        Assert.NotEqual(lower, upper);
        Assert.False(lower == upper);
    }

    [Fact]
    public void IdsThatDifferOnlyInWhiteSpaceAreNotEqual()
    {
        RouteEntry plain = new("entry-1", new ProductRoute("42"));
        RouteEntry padded = new("entry-1 ", new ProductRoute("42"));

        Assert.NotEqual(plain, padded);
    }

    [Fact]
    public void EntriesWithDifferentRouteValuesAreNotEqual()
    {
        RouteEntry first = new("entry-1", new ProductRoute("42"));
        RouteEntry second = new("entry-1", new ProductRoute("43"));

        Assert.NotEqual(first, second);
        Assert.False(first == second);
        Assert.True(first != second);
    }

    [Fact]
    public void EntriesWithRoutesOfDifferentTypesAreNotEqual()
    {
        RouteEntry first = new("entry-1", new ProductRoute("42"));
        RouteEntry second = new("entry-1", new ProductByNameRoute("42"));

        Assert.NotEqual(first, second);
        Assert.False(first == second);
    }

    [Fact]
    public void EntriesWithDifferentMetadataValuesAreNotEqual()
    {
        RouteEntry first = new("entry-1", new ProductRoute("42"), RouteMetadata.Empty.With(Campaign, "spring"));
        RouteEntry second = new("entry-1", new ProductRoute("42"), RouteMetadata.Empty.With(Campaign, "autumn"));

        Assert.NotEqual(first, second);
        Assert.False(first == second);
    }

    [Fact]
    public void AnEntryWithMetadataDoesNotEqualOneWithout()
    {
        RouteEntry without = new("entry-1", new ProductRoute("42"));
        RouteEntry with = new("entry-1", new ProductRoute("42"), RouteMetadata.Empty.With(Campaign, "spring"));

        Assert.NotEqual(without, with);
        Assert.NotEqual(with, without);
        Assert.False(without == with);
    }

    [Fact]
    public void AnEntryDoesNotEqualNullOrAnotherType()
    {
        ProductRoute route = new("42");
        RouteEntry entry = new("entry-1", route);

        Assert.False(entry.Equals(null));
        Assert.False(entry.Equals((object?)null));
        Assert.False(entry.Equals((object)"entry-1"));
        Assert.False(entry.Equals((object)route));
        Assert.False(entry.Equals((object)new AppRouteRequest(route)));
    }

    [Fact]
    public void TheOperatorsHandleNullOnEitherSide()
    {
        RouteEntry entry = new("entry-1", new ProductRoute("42"));
        RouteEntry? none = null;

        Assert.False(entry == null);
        Assert.False(null == entry);
        Assert.True(entry != null);
        Assert.True(null != entry);
        Assert.True(none == null);
        Assert.False(none != null);
    }

    // The hash code is made from the id, the route and the metadata, so entries that differ in one of them spread
    // over the buckets of a set or a dictionary: ten of each give more than one hash code.
    [Fact]
    public void TheHashCodeDependsOnTheIdTheRouteAndTheMetadata()
    {
        ProductRoute route = new("42");
        var byId = new HashSet<int>();
        var byRoute = new HashSet<int>();
        var byMetadata = new HashSet<int>();
        for (var i = 0; i < 10; i++)
        {
            string text = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            byId.Add(new RouteEntry("entry-" + text, route).GetHashCode());
            byRoute.Add(new RouteEntry("entry", new ProductRoute(text)).GetHashCode());
            byMetadata.Add(new RouteEntry("entry", route, RouteMetadata.Empty.With(Campaign, text)).GetHashCode());
        }

        Assert.True(byId.Count > 1);
        Assert.True(byRoute.Count > 1);
        Assert.True(byMetadata.Count > 1);
    }

    // The default ToString prints only the type name.
    [Fact]
    public void ToStringDoesNotShowTheIdRouteOrMetadataValues()
    {
        RouteEntry entry = new(
            "entry-secret",
            new ProductRoute("route-value-42"),
            RouteMetadata.Empty.With(Campaign, "metadata-value-spring"));

        string? text = entry.ToString();

        Assert.Equal(typeof(RouteEntry).FullName, text);
        Assert.DoesNotContain("entry-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("route-value-42", text, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata-value-spring", text, StringComparison.Ordinal);
    }
}
