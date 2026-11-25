using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteValueExceptionTests
{
    [Fact]
    public void TheNameOnlyConstructorKeepsTheNameAndGivesAMessageThatNamesIt()
    {
        var exception = new RouteValueException("orderId");

        Assert.Equal("orderId", exception.ValueName);
        Assert.Contains("'orderId'", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void TheMessageConstructorKeepsTheNameAndTheMessage()
    {
        var exception = new RouteValueException("id", "An order id is positive.");

        Assert.Equal("id", exception.ValueName);
        Assert.Equal("An order id is positive.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void TheInnerExceptionConstructorKeepsAllThree()
    {
        var inner = new FormatException();

        var exception = new RouteValueException("id", "Not a number.", inner);

        Assert.Equal("id", exception.ValueName);
        Assert.Equal("Not a number.", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void ANullMessageGivesTheMessageThatNamesTheValue()
    {
        var exception = new RouteValueException("orderId", null, null);

        Assert.Contains("'orderId'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyNameIsAllowedForTheEmptyQueryName()
    {
        var exception = new RouteValueException("");

        Assert.Equal("", exception.ValueName);
        Assert.Contains("''", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullNameIsRejectedByEveryConstructor()
    {
        Assert.Equal(
            "valueName",
            Assert.Throws<ArgumentNullException>(() => new RouteValueException(null!)).ParamName);
        Assert.Equal(
            "valueName",
            Assert.Throws<ArgumentNullException>(() => new RouteValueException(null!, "x")).ParamName);
        Assert.Equal(
            "valueName",
            Assert.Throws<ArgumentNullException>(() => new RouteValueException(null!, "x", null)).ParamName);
    }

    [Fact]
    public void TheDefaultMessageEscapesControlCharactersInTheName()
    {
        var exception = new RouteValueException("a\u000Ab");

        Assert.DoesNotContain("\u000A", exception.Message, StringComparison.Ordinal);
        Assert.Equal("a\u000Ab", exception.ValueName);
    }
}
