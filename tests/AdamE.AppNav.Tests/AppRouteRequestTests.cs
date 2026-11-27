using AdamE.AppNav.Tests.Routing;
using Xunit;

namespace AdamE.AppNav.Tests;

public sealed class AppRouteRequestTests
{
    private static readonly RouteMetadataKey<string> Campaign = new("campaign");
    private static readonly RouteMetadataKey<int> Step = new("step");

    [Fact]
    public void TheOneArgumentConstructorGivesEmptyMetadata()
    {
        ProductRoute route = new("42");

        AppRouteRequest request = new(route);

        Assert.Same(route, request.Route);
        Assert.Equal(RouteMetadata.Empty, request.Metadata);
        Assert.Equal(0, request.Metadata.Count);
    }

    [Fact]
    public void TheTwoArgumentConstructorKeepsTheRouteAndMetadata()
    {
        ProductRoute route = new("42");
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring");

        AppRouteRequest request = new(route, metadata);

        Assert.Same(route, request.Route);
        Assert.Same(metadata, request.Metadata);
    }

    [Fact]
    public void ANullRouteThrows()
    {
        ArgumentNullException one = Assert.Throws<ArgumentNullException>(() => new AppRouteRequest(null!));
        ArgumentNullException two = Assert.Throws<ArgumentNullException>(
            () => new AppRouteRequest(null!, RouteMetadata.Empty));

        Assert.Equal("route", one.ParamName);
        Assert.Equal("route", two.ParamName);
    }

    [Fact]
    public void NullMetadataThrows()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new AppRouteRequest(new ProductRoute("42"), null!));

        Assert.Equal("metadata", exception.ParamName);
    }

    [Fact]
    public void WithMetadataSetsTheEntryAndKeepsTheRoute()
    {
        ProductRoute route = new("42");

        AppRouteRequest request = new AppRouteRequest(route).WithMetadata(Campaign, "spring");

        Assert.Same(route, request.Route);
        Assert.True(request.Metadata.TryGet(Campaign, out string? value));
        Assert.Equal("spring", value);
    }

    [Fact]
    public void WithMetadataReplacesTheEntryForAnEqualKey()
    {
        AppRouteRequest request = new AppRouteRequest(new ProductRoute("42"))
            .WithMetadata(Campaign, "spring")
            .WithMetadata(new RouteMetadataKey<string>("campaign"), "autumn");

        Assert.Equal(1, request.Metadata.Count);
        Assert.True(request.Metadata.TryGet(Campaign, out string? value));
        Assert.Equal("autumn", value);
    }

    [Fact]
    public void WithMetadataChains()
    {
        AppRouteRequest request = new AppRouteRequest(new ProductRoute("42"))
            .WithMetadata(Campaign, "spring")
            .WithMetadata(Step, 2);

        Assert.Equal(RouteMetadata.Empty.With(Campaign, "spring").With(Step, 2), request.Metadata);
    }

    [Fact]
    public void WithMetadataLeavesTheRequestUnchanged()
    {
        ProductRoute route = new("42");
        AppRouteRequest original = new(route);

        AppRouteRequest changed = original.WithMetadata(Campaign, "spring");

        Assert.NotSame(original, changed);
        Assert.Same(route, original.Route);
        Assert.Equal(RouteMetadata.Empty, original.Metadata);
        Assert.False(original.Metadata.TryGet(Campaign, out _));
    }

    // WithMetadata delegates to RouteMetadata.With, so the parameter names are the same.
    [Fact]
    public void WithMetadataThrowsForANullKeyOrValue()
    {
        AppRouteRequest request = new(new ProductRoute("42"));

        ArgumentNullException key = Assert.Throws<ArgumentNullException>(
            () => request.WithMetadata(null!, "spring"));
        ArgumentNullException value = Assert.Throws<ArgumentNullException>(
            () => request.WithMetadata(Campaign, null!));

        Assert.Equal("key", key.ParamName);
        Assert.Equal("value", value.ParamName);
    }

    // The metadata are built in different orders and hold the same entries, so they are equal.
    [Fact]
    public void EqualRoutesAndEqualMetadataAreEqual()
    {
        AppRouteRequest first = new AppRouteRequest(new ProductRoute("42"))
            .WithMetadata(Campaign, "spring")
            .WithMetadata(Step, 2);
        AppRouteRequest second = new AppRouteRequest(new ProductRoute("42"))
            .WithMetadata(Step, 2)
            .WithMetadata(Campaign, "spring");

        Assert.Equal(first, second);
        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void RequestsWithDifferentRouteValuesAreNotEqual()
    {
        AppRouteRequest first = new(new ProductRoute("42"));
        AppRouteRequest second = new(new ProductRoute("43"));

        Assert.NotEqual(first, second);
        Assert.False(first == second);
        Assert.True(first != second);
    }

    [Fact]
    public void RequestsWithRoutesOfDifferentTypesAreNotEqual()
    {
        AppRouteRequest first = new(new ProductRoute("42"));
        AppRouteRequest second = new(new ProductByNameRoute("42"));

        Assert.NotEqual(first, second);
        Assert.False(first == second);
    }

    [Fact]
    public void RequestsWithDifferentMetadataValuesAreNotEqual()
    {
        AppRouteRequest first = new AppRouteRequest(new ProductRoute("42")).WithMetadata(Campaign, "spring");
        AppRouteRequest second = new AppRouteRequest(new ProductRoute("42")).WithMetadata(Campaign, "autumn");

        Assert.NotEqual(first, second);
        Assert.False(first == second);
    }

    [Fact]
    public void ARequestWithMetadataDoesNotEqualOneWithout()
    {
        AppRouteRequest without = new(new ProductRoute("42"));
        AppRouteRequest with = without.WithMetadata(Campaign, "spring");

        Assert.NotEqual(without, with);
        Assert.NotEqual(with, without);
        Assert.False(without == with);
    }

    [Fact]
    public void ARequestDoesNotEqualNull()
    {
        AppRouteRequest request = new(new ProductRoute("42"));

        Assert.False(request.Equals(null));
        Assert.False(request.Equals((object?)null));
        Assert.False(request == null);
        Assert.True(request != null);
    }

    // A with expression cannot set the get-only properties, so it can only copy a request.
    [Fact]
    public void AWithCopyEqualsTheOriginal()
    {
        AppRouteRequest original = new AppRouteRequest(new ProductRoute("42")).WithMetadata(Campaign, "spring");

        AppRouteRequest copy = original with { };

        Assert.NotSame(original, copy);
        Assert.Equal(original, copy);
        Assert.Same(original.Route, copy.Route);
        Assert.Same(original.Metadata, copy.Metadata);
    }

    // The route prints its values; the metadata prints only its type name, not its entries.
    [Fact]
    public void ToStringShowsTheRouteValuesButNotTheMetadataValues()
    {
        AppRouteRequest request = new AppRouteRequest(new ProductRoute("route-value-42"))
            .WithMetadata(Campaign, "metadata-value-spring");

        string text = request.ToString();

        Assert.Contains("route-value-42", text, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata-value-spring", text, StringComparison.Ordinal);
    }
}
