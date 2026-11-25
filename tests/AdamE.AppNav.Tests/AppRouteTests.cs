using AdamE.AppNav.Tests.Routing;
using Xunit;

namespace AdamE.AppNav.Tests;

public sealed class AppRouteTests
{
    [Fact]
    public void RoutesOfTheSameTypeWithEqualMembersAreEqual()
    {
        AppRoute first = new ProductRoute("42");
        AppRoute second = new ProductRoute("42");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new ProductRoute("43"));
    }

    [Fact]
    public void RoutesOfDifferentTypesAreNotEqualEvenWithEqualMembers()
    {
        AppRoute product = new ProductRoute("42");
        AppRoute byName = new ProductByNameRoute("42");

        Assert.NotEqual(product, byName);
        Assert.False(product == byName);
    }
}
