using System.Globalization;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTableBuilderEnumValueCodecTests
{
    private const string DifferOnlyByCase = "differ only by case";
    private const string NotRegistered = "AddEnumValueCodec";

    [Fact]
    public void RegisteringReturnsTheSameBuilder()
    {
        var builder = new RouteTableBuilder();

        Assert.Same(builder, builder.AddEnumValueCodec<Status>());
        Assert.Same(builder, builder.AddEnumValueCodec<Status>());
    }

    [Fact]
    public void AnEnumWhoseNamesDifferOnlyByCaseIsRejected()
    {
        var builder = new RouteTableBuilder();

        ArgumentException clash = Assert.Throws<ArgumentException>(() => builder.AddEnumValueCodec<Clash>());
        ArgumentException later = Assert.Throws<ArgumentException>(() => builder.AddEnumValueCodec<LaterClash>());

        foreach (ArgumentException exception in new[] { clash, later })
        {
            Assert.Equal("TEnum", exception.ParamName);
            Assert.Contains(DifferOnlyByCase, exception.Message, StringComparison.Ordinal);
        }

        Assert.Contains(nameof(Clash), clash.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(LaterClash), later.Message, StringComparison.Ordinal);
    }

    // A rejected enum is not registered, in either form, and the builder goes on working.
    [Fact]
    public void ARejectedEnumLeavesTheBuilderAsItWas()
    {
        var builder = new RouteTableBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddEnumValueCodec<Clash>());
        RouteTable table = builder
            .AddEnumValueCodec<Status>()
            .Map("/a/{v}", c => new ProductRoute(c.Path<Status>("v").ToString()))
            .Map("/b/{v}", c => new ProductRoute(c.Path<Clash>("v").ToString()))
            .Map("/c/{v}", c => new ProductRoute(c.Path<Clash?>("v").ToString()!))
            .Build();

        Assert.Equal(new ProductRoute("Shipped"), Match(table, "/a/shipped").Route);
        AssertNotRegistered(table, "/b/A");
        AssertNotRegistered(table, "/c/A");
    }

    // Ordinal comparison without regard to case does not fold Turkish dotless i into I, so those names do not clash
    // and each names only its own member.
    [Fact]
    public void NamesThatDifferByADotlessIDoNotClash()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Dotless>()
            .Map("/{v}", c => new ProductRoute(((int)c.Path<Dotless>("v")).ToString(CultureInfo.InvariantCulture)))
            .Build();

        Assert.Equal(new ProductRoute("0"), Match(table, "/I").Route);
        Assert.Equal(new ProductRoute("0"), Match(table, "/i").Route);
        Assert.Equal(new ProductRoute("1"), Match(table, "/" + Uri.EscapeDataString("\u0131")).Route);
        Assert.False(Match(table, "/" + Uri.EscapeDataString("\u0130")).IsSuccess);
    }

    // That no text then converts is checked with the typed path reads.
    [Fact]
    public void AnEnumWithNoMembersCanBeRegistered()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Nothing>()
            .Map("/settings", _ => new SettingsRoute())
            .Build();

        Assert.Single(table.Definitions);
        Assert.Equal(new SettingsRoute(), Match(table, "/settings").Route);
    }

    [Fact]
    public void RegisteringTheSameEnumTwiceDoesNothing()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .AddEnumValueCodec<Status>()
            .Map("/{v}", c => new ProductRoute(c.Path<Status>("v") + "/" + c.Path<Status?>("v")))
            .AddEnumValueCodec<Status>()
            .Build();

        Assert.Equal(new ProductRoute("Pending/Pending"), Match(table, "/PENDING").Route);
        Assert.False(Match(table, "/0").IsSuccess);
    }

    [Fact]
    public void ARegistrationAfterAMappingServesThatMapping()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/orders/{status}", c => new ProductRoute(c.Path<Status>("status").ToString()))
            .AddEnumValueCodec<Status>()
            .Build();

        Assert.Equal(new ProductRoute("Shipped"), Match(table, "/orders/shipped").Route);
    }

    [Fact]
    public void ARegistrationAfterBuildChangesOnlyTheTablesBuiltAfterIt()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .Map("/orders/{status}", c => new ProductRoute(c.Path<Status>("status").ToString()));
        RouteTable before = builder.Build();

        builder.AddEnumValueCodec<Status>();
        RouteTable after = builder.Build();

        AssertNotRegistered(before, "/orders/shipped");
        Assert.Equal(new ProductRoute("Shipped"), Match(after, "/orders/shipped").Route);
    }

    [Fact]
    public void TablesWithDifferentRegistrationsAreIndependent()
    {
        RouteTable statusTable = Reading(new RouteTableBuilder().AddEnumValueCodec<Status>());
        RouteTable accessTable = Reading(new RouteTableBuilder().AddEnumValueCodec<Access>());

        Assert.Equal(new ProductRoute("Pending"), Match(statusTable, "/s/pending").Route);
        AssertNotRegistered(statusTable, "/a/read");
        Assert.Equal(new ProductRoute("Read"), Match(accessTable, "/a/read").Route);
        AssertNotRegistered(accessTable, "/s/pending");
    }

    private static RouteTable Reading(RouteTableBuilder builder)
    {
        return builder
            .Map("/s/{v}", c => new ProductRoute(c.Path<Status>("v").ToString()))
            .Map("/a/{v}", c => new ProductRoute(c.Path<Access>("v").ToString()))
            .Build();
    }

    private static RouteMatchResult Match(RouteTable table, string uri)
    {
        return table.Match(new Uri(uri, UriKind.Relative));
    }

    private static void AssertNotRegistered(RouteTable table, string uri)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Match(table, uri));

        Assert.Contains(NotRegistered, exception.Message, StringComparison.Ordinal);
    }

    private enum Status
    {
        Pending,
        Shipped,
    }

    [Flags]
    private enum Access
    {
        Read = 1,
        Write = 2,
    }

    private enum Clash
    {
        A,
        a,
    }

    // The clash is between the first and the last name, with another in between.
    private enum LaterClash
    {
        Value,
        Other,
        VALUE,
    }

    private enum Dotless
    {
        I,
        \u0131,
    }

    private enum Nothing;
}
