using Xunit;

namespace AdamE.AppNav.Tests;

public sealed class RouteMetadataKeyTests
{
    [Fact]
    public void AKeyHasTheNameItWasGiven()
    {
        var key = new RouteMetadataKey<string>("campaign");

        Assert.Equal("campaign", key.Name);
        Assert.Equal("campaign", key.ToString());
    }

    [Fact]
    public void ANullNameIsRejected()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new RouteMetadataKey<string>(null!));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void AnEmptyNameIsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new RouteMetadataKey<string>(""));

        Assert.Equal("name", exception.ParamName);
    }

    // Any text that is not empty is a name: white space, punctuation and non-ASCII characters are allowed.
    [Theory]
    [InlineData(" ")]
    [InlineData("utm_source")]
    [InlineData("a b/c?d=e&f")]
    [InlineData("caf\u00e9")]
    public void AnyNonEmptyTextIsAName(string name)
    {
        Assert.Equal(name, new RouteMetadataKey<int>(name).Name);
    }

    // An absent entry is how metadata says that there is no value, so the nullable form of a value type is rejected.
    [Fact]
    public void TheNullableFormOfAValueTypeIsRejected()
    {
#pragma warning disable CS8714 // The constraint gives only a warning, so the run-time check is what is under test.
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new RouteMetadataKey<int?>("n"));
        ArgumentException other = Assert.Throws<ArgumentException>(() => new RouteMetadataKey<DayOfWeek?>("d"));
#pragma warning restore CS8714

        Assert.Equal("TValue", exception.ParamName);
        Assert.Contains("'Int32?'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("is the nullable form of 'Int32'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Use 'Int32' instead", exception.Message, StringComparison.Ordinal);
        Assert.Equal("TValue", other.ParamName);
        Assert.Contains("'DayOfWeek?'", other.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeysWithTheSameNameAndTypeAreEqual()
    {
        var first = new RouteMetadataKey<int>("n");
        var second = new RouteMetadataKey<int>("n");

        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void KeysWithDifferentNamesAreNotEqual()
    {
        var first = new RouteMetadataKey<int>("n");

        Assert.False(first.Equals(new RouteMetadataKey<int>("m")));
        Assert.False(first.Equals((object)new RouteMetadataKey<int>("m")));
    }

    // Names are compared ordinally and case-sensitively.
    [Fact]
    public void KeysWhoseNamesDifferOnlyByCaseAreNotEqual()
    {
        Assert.False(new RouteMetadataKey<int>("n").Equals(new RouteMetadataKey<int>("N")));
    }

    [Fact]
    public void KeysWithTheSameNameAndDifferentTypesAreNotEqual()
    {
        object number = new RouteMetadataKey<int>("n");
        object text = new RouteMetadataKey<string>("n");

        Assert.False(number.Equals(text));
        Assert.False(text.Equals(number));
    }

    [Fact]
    public void AKeyIsNotEqualToNullOrToAnotherObject()
    {
        var key = new RouteMetadataKey<string>("n");

        Assert.False(key.Equals(null));
        Assert.False(key.Equals((object?)null));
        Assert.False(key.Equals("n"));
    }

    // The operators agree with Equals, and two nulls are equal.
    [Fact]
    public void TheOperatorsAgreeWithEquals()
    {
        var first = new RouteMetadataKey<int>("n");
        var same = new RouteMetadataKey<int>("n");
        var other = new RouteMetadataKey<int>("m");
        RouteMetadataKey<int>? none = null;
        RouteMetadataKey<int>? alsoNone = null;

        Assert.True(first == same);
        Assert.False(first != same);
        Assert.False(first == other);
        Assert.True(first != other);
        Assert.False(first == none);
        Assert.True(none != first);
        Assert.True(none == alsoNone);
        Assert.False(none != alsoNone);
    }
}
