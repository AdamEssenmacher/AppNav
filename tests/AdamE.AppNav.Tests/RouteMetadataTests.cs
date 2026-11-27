using System.Globalization;
using Xunit;

namespace AdamE.AppNav.Tests;

public sealed class RouteMetadataTests
{
    private static readonly RouteMetadataKey<string> Campaign = new("campaign");
    private static readonly RouteMetadataKey<int> Count = new("count");
    private static readonly RouteMetadataKey<int> Number = new("n");
    private static readonly RouteMetadataKey<string> Text = new("n");

    [Fact]
    public void EmptyHasNoEntries()
    {
        Assert.Equal(0, RouteMetadata.Empty.Count);
        Assert.False(RouteMetadata.Empty.TryGet(Campaign, out string? value));
        Assert.Null(value);
    }

    [Fact]
    public void AValueTypeEntryIsStoredAndRead()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Count, 3);

        Assert.Equal(1, metadata.Count);
        Assert.True(metadata.TryGet(Count, out int value));
        Assert.Equal(3, value);
    }

    [Fact]
    public void AReferenceTypeEntryIsStoredAndRead()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring");

        Assert.Equal(1, metadata.Count);
        Assert.True(metadata.TryGet(Campaign, out string? value));
        Assert.Equal("spring", value);
    }

    // A key equal to the one an entry was written with reads it: keys are compared by name and type.
    [Fact]
    public void AnEqualKeyReadsTheEntry()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring");

        Assert.True(metadata.TryGet(new RouteMetadataKey<string>("campaign"), out string? value));
        Assert.Equal("spring", value);
    }

    [Fact]
    public void AnAbsentKeyGivesFalseAndTheDefaultValue()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring");

        Assert.False(metadata.TryGet(Count, out int number));
        Assert.Equal(0, number);
        Assert.False(metadata.TryGet(new RouteMetadataKey<string>("Campaign"), out string? text));
        Assert.Null(text);
    }

    [Fact]
    public void WithReplacesTheEntryForAnEqualKey()
    {
        RouteMetadata metadata = RouteMetadata.Empty
            .With(Campaign, "spring")
            .With(Count, 1)
            .With(new RouteMetadataKey<string>("campaign"), "autumn");

        Assert.Equal(2, metadata.Count);
        Assert.True(metadata.TryGet(Campaign, out string? value));
        Assert.Equal("autumn", value);
        Assert.Equal(RouteMetadata.Empty.With(Count, 1).With(Campaign, "autumn"), metadata);
    }

    [Fact]
    public void WithTheSameValueGivesAnEqualCollection()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring");

        RouteMetadata again = metadata.With(Campaign, "spring");

        Assert.Equal(metadata, again);
        Assert.Equal(1, again.Count);
    }

    [Fact]
    public void WithLeavesTheReceiverUnchanged()
    {
        RouteMetadata first = RouteMetadata.Empty.With(Campaign, "spring");

        RouteMetadata second = first.With(Campaign, "autumn").With(Count, 2);

        Assert.Equal(1, first.Count);
        Assert.True(first.TryGet(Campaign, out string? value));
        Assert.Equal("spring", value);
        Assert.False(first.TryGet(Count, out _));
        Assert.Equal(2, second.Count);
        Assert.Equal(0, RouteMetadata.Empty.Count);
    }

    [Fact]
    public void WithoutRemovesTheEntry()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring").With(Count, 1);

        RouteMetadata removed = metadata.Without(Campaign);

        Assert.Equal(1, removed.Count);
        Assert.False(removed.TryGet(Campaign, out _));
        Assert.True(removed.TryGet(Count, out int value));
        Assert.Equal(1, value);
        Assert.Equal(RouteMetadata.Empty.With(Count, 1), removed);
    }

    [Fact]
    public void WithoutAValueTypeEntryRemovesIt()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Count, 1).With(Campaign, "spring");

        RouteMetadata removed = metadata.Without(Count);

        Assert.Equal(1, removed.Count);
        Assert.False(removed.TryGet(Count, out _));
        Assert.Equal(RouteMetadata.Empty.With(Campaign, "spring"), removed);
    }

    [Fact]
    public void WithoutTheLastEntryGivesACollectionEqualToEmpty()
    {
        RouteMetadata removed = RouteMetadata.Empty.With(Campaign, "spring").Without(Campaign);

        Assert.Equal(0, removed.Count);
        Assert.Equal(RouteMetadata.Empty, removed);
        Assert.Equal(RouteMetadata.Empty.GetHashCode(), removed.GetHashCode());
    }

    [Fact]
    public void WithoutAnAbsentKeyGivesAnEqualCollection()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring");

        Assert.Equal(metadata, metadata.Without(Count));
        Assert.Equal(metadata, metadata.Without(new RouteMetadataKey<string>("Campaign")));
        Assert.Equal(RouteMetadata.Empty, RouteMetadata.Empty.Without(Campaign));
    }

    [Fact]
    public void WithoutLeavesTheReceiverUnchanged()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring").With(Count, 1).With(Number, 2);
        RouteMetadata copy = RouteMetadata.Empty.With(Campaign, "spring").With(Count, 1).With(Number, 2);

        RouteMetadata middle = metadata.Without(Count);
        RouteMetadata first = metadata.Without(Campaign);
        RouteMetadata last = metadata.Without(Number);

        Assert.Equal(3, metadata.Count);
        Assert.Equal(copy, metadata);
        Assert.True(metadata.TryGet(Campaign, out string? campaign));
        Assert.Equal("spring", campaign);
        Assert.True(metadata.TryGet(Count, out int count));
        Assert.Equal(1, count);
        Assert.True(metadata.TryGet(Number, out int number));
        Assert.Equal(2, number);
        Assert.Equal(RouteMetadata.Empty.With(Campaign, "spring").With(Number, 2), middle);
        Assert.Equal(RouteMetadata.Empty.With(Count, 1).With(Number, 2), first);
        Assert.Equal(RouteMetadata.Empty.With(Campaign, "spring").With(Count, 1), last);
    }

    // An entry is identified by the key's name and value type, so keys that share a name but not a type name two
    // entries, and neither reads the other's value.
    [Fact]
    public void KeysWithTheSameNameAndDifferentTypesNameDifferentEntries()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Number, 7).With(Text, "seven");

        Assert.Equal(2, metadata.Count);
        Assert.True(metadata.TryGet(Number, out int number));
        Assert.Equal(7, number);
        Assert.True(metadata.TryGet(Text, out string? text));
        Assert.Equal("seven", text);

        RouteMetadata numberOnly = metadata.Without(Text);
        Assert.Equal(1, numberOnly.Count);
        Assert.False(numberOnly.TryGet(Text, out _));
        Assert.True(numberOnly.TryGet(Number, out _));
    }

    [Fact]
    public void AKeyDoesNotReadAnEntryOfAnotherTypeWithTheSameName()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Number, 7);

        Assert.False(metadata.TryGet(Text, out string? text));
        Assert.Null(text);
        Assert.False(metadata.TryGet(new RouteMetadataKey<long>("n"), out long wider));
        Assert.Equal(0L, wider);
        Assert.False(metadata.TryGet(new RouteMetadataKey<object>("n"), out object? boxed));
        Assert.Null(boxed);
    }

    // A key of a base type names a different entry from a key of a derived type with the same name.
    [Fact]
    public void KeysOfABaseAndADerivedTypeNameDifferentEntries()
    {
        var anything = new RouteMetadataKey<object>("n");
        RouteMetadata metadata = RouteMetadata.Empty.With(Text, "text").With(anything, "object");

        Assert.Equal(2, metadata.Count);
        Assert.True(metadata.TryGet(Text, out string? text));
        Assert.Equal("text", text);
        Assert.True(metadata.TryGet(anything, out object? value));
        Assert.Equal("object", value);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring");

        ArgumentNullException get = Assert.Throws<ArgumentNullException>(
            () => metadata.TryGet<string>(null!, out _));
        ArgumentNullException withKey = Assert.Throws<ArgumentNullException>(
            () => metadata.With<string>(null!, "x"));
        ArgumentNullException withValue = Assert.Throws<ArgumentNullException>(
            () => metadata.With(Campaign, null!));
        ArgumentNullException without = Assert.Throws<ArgumentNullException>(
            () => metadata.Without<string>(null!));

        Assert.Equal("key", get.ParamName);
        Assert.Equal("key", withKey.ParamName);
        Assert.Equal("value", withValue.ParamName);
        Assert.Equal("key", without.ParamName);
    }

    // A null value is never stored: the collection the call was made on is unchanged.
    [Fact]
    public void ANullValueIsNotStored()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring");

        Assert.Throws<ArgumentNullException>(() => metadata.With(Campaign, null!));

        Assert.True(metadata.TryGet(Campaign, out string? value));
        Assert.Equal("spring", value);
    }

    [Fact]
    public void CollectionsWithTheSameEntriesInAnotherOrderAreEqual()
    {
        RouteMetadata first = RouteMetadata.Empty.With(Campaign, "spring").With(Count, 1).With(Number, 2);
        RouteMetadata second = RouteMetadata.Empty.With(Number, 2).With(Campaign, "spring").With(Count, 1);

        Assert.True(first.Equals(second));
        Assert.True(second.Equals(first));
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void CollectionsWithADifferentValueAreNotEqual()
    {
        RouteMetadata first = RouteMetadata.Empty.With(Campaign, "spring").With(Count, 1);
        RouteMetadata second = RouteMetadata.Empty.With(Campaign, "spring").With(Count, 2);

        Assert.False(first.Equals(second));
        Assert.False(first.Equals((object)second));
    }

    [Fact]
    public void CollectionsWithADifferentKeyAreNotEqual()
    {
        RouteMetadata number = RouteMetadata.Empty.With(Number, 1);

        Assert.NotEqual(number, RouteMetadata.Empty.With(Count, 1));
        Assert.NotEqual(number, RouteMetadata.Empty.With(new RouteMetadataKey<long>("n"), 1L));
        Assert.NotEqual(
            RouteMetadata.Empty.With(Text, "a"),
            RouteMetadata.Empty.With(new RouteMetadataKey<string>("N"), "a"));
    }

    [Fact]
    public void CollectionsWithADifferentCountAreNotEqual()
    {
        RouteMetadata one = RouteMetadata.Empty.With(Campaign, "spring");
        RouteMetadata two = one.With(Count, 1);

        Assert.False(one.Equals(two));
        Assert.False(two.Equals(one));
        Assert.False(RouteMetadata.Empty.Equals(one));
    }

    [Fact]
    public void ACollectionIsNotEqualToNullOrToAnotherObject()
    {
        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring");

        Assert.False(metadata.Equals(null));
        Assert.False(metadata.Equals((object?)null));
        Assert.False(metadata.Equals("spring"));
        Assert.False(RouteMetadata.Empty.Equals(null));
    }

    [Fact]
    public void StructValuesCompareByValue()
    {
        var key = new RouteMetadataKey<DateTimeOffset>("at");
        var at = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        RouteMetadata first = RouteMetadata.Empty.With(key, at);
        RouteMetadata second = RouteMetadata.Empty.With(key, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, RouteMetadata.Empty.With(key, at.AddSeconds(1)));
    }

    [Fact]
    public void RecordValuesCompareByValue()
    {
        var key = new RouteMetadataKey<Referral>("referral");

        RouteMetadata first = RouteMetadata.Empty.With(key, new Referral("mail", 3));
        RouteMetadata second = RouteMetadata.Empty.With(key, new Referral("mail", 3));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, RouteMetadata.Empty.With(key, new Referral("mail", 4)));
        Assert.True(first.TryGet(key, out Referral? value));
        Assert.Equal(new Referral("mail", 3), value);
    }

    // A value of a type that compares by reference makes collections equal only when they hold the same instance.
    [Fact]
    public void ArrayValuesCompareByReference()
    {
        var key = new RouteMetadataKey<int[]>("ids");
        int[] ids = [1, 2];

        Assert.Equal(RouteMetadata.Empty.With(key, ids), RouteMetadata.Empty.With(key, ids));
        Assert.NotEqual(RouteMetadata.Empty.With(key, ids), RouteMetadata.Empty.With(key, [1, 2]));
    }

    [Fact]
    public void ALargerCollectionStillBehaves()
    {
        var keys = new RouteMetadataKey<int>[20];
        RouteMetadata forward = RouteMetadata.Empty;
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = new RouteMetadataKey<int>("k" + i);
            forward = forward.With(keys[i], i);
        }

        RouteMetadata backward = RouteMetadata.Empty;
        for (int i = keys.Length - 1; i >= 0; i--)
            backward = backward.With(keys[i], i);

        Assert.Equal(20, forward.Count);
        for (int i = 0; i < keys.Length; i++)
        {
            Assert.True(forward.TryGet(keys[i], out int value));
            Assert.Equal(i, value);
        }

        Assert.Equal(forward, backward);
        Assert.Equal(forward.GetHashCode(), backward.GetHashCode());

        RouteMetadata replaced = forward.With(keys[10], 100);
        Assert.Equal(20, replaced.Count);
        Assert.True(replaced.TryGet(keys[10], out int changed));
        Assert.Equal(100, changed);
        Assert.NotEqual(forward, replaced);

        RouteMetadata removed = forward.Without(keys[0]).Without(keys[19]).Without(keys[10]);
        Assert.Equal(17, removed.Count);
        Assert.False(removed.TryGet(keys[10], out _));
        Assert.True(removed.TryGet(keys[11], out int next));
        Assert.Equal(11, next);
        Assert.NotEqual(forward, removed);
        Assert.Equal(removed.With(keys[10], 10).With(keys[0], 0).With(keys[19], 19), forward);
    }

    // With always stores the value it is given, even over an equal one, so detail that the type's equality ignores
    // is kept: the scale of a decimal.
    [Fact]
    public void WithStoresTheGivenDecimalEvenWhenAnEqualOneIsStored()
    {
        var key = new RouteMetadataKey<decimal>("amount");

        RouteMetadata metadata = RouteMetadata.Empty.With(key, 1.0m).With(key, 1.00m);

        Assert.True(metadata.TryGet(key, out decimal value));
        Assert.Equal("1.00", value.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(RouteMetadata.Empty.With(key, 1.0m), metadata);
    }

    // DateTimeOffset values are equal when they are the same instant, whatever their offsets; the offset given is
    // kept.
    [Fact]
    public void WithStoresTheGivenOffsetEvenWhenAnEqualInstantIsStored()
    {
        var key = new RouteMetadataKey<DateTimeOffset>("at");
        var utc = new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 1, 2, 5, 0, 0, TimeSpan.FromHours(2));

        RouteMetadata metadata = RouteMetadata.Empty.With(key, utc).With(key, later);

        Assert.True(metadata.TryGet(key, out DateTimeOffset value));
        Assert.Equal(TimeSpan.FromHours(2), value.Offset);
        Assert.Equal(RouteMetadata.Empty.With(key, utc), metadata);
    }

    // Setting, reading and removing an entry never call the value's Equals or GetHashCode.
    [Fact]
    public void AValueWhoseEqualityThrowsCanBeSetReadReplacedAndRemoved()
    {
        var key = new RouteMetadataKey<Unequatable>("u");

        RouteMetadata metadata = RouteMetadata.Empty.With(Campaign, "spring").With(key, new Unequatable(1));
        RouteMetadata replaced = metadata.With(key, new Unequatable(2));
        RouteMetadata removed = replaced.Without(key);

        Assert.True(metadata.TryGet(key, out Unequatable first));
        Assert.Equal(1, first.Id);
        Assert.True(replaced.TryGet(key, out Unequatable second));
        Assert.Equal(2, second.Id);
        Assert.Equal(1, removed.Count);
        Assert.False(removed.TryGet(key, out _));
        Assert.True(removed.TryGet(Campaign, out string? campaign));
        Assert.Equal("spring", campaign);
    }

    // The operators agree with Equals, and two nulls are equal.
    [Fact]
    public void TheOperatorsAgreeWithEquals()
    {
        var key = new RouteMetadataKey<string>("campaign");
        RouteMetadata first = RouteMetadata.Empty.With(key, "spring");
        RouteMetadata same = RouteMetadata.Empty.With(key, "spring");
        RouteMetadata other = RouteMetadata.Empty.With(key, "autumn");
        RouteMetadata? none = null;
        RouteMetadata? alsoNone = null;

        Assert.True(first == same);
        Assert.False(first != same);
        Assert.False(first == other);
        Assert.True(first != other);
        Assert.False(first == none);
        Assert.True(none != first);
        Assert.True(none == alsoNone);
        Assert.False(none != alsoNone);
    }

    private sealed record Referral(string Channel, int Step);

    private readonly struct Unequatable(int id) : IEquatable<Unequatable>
    {
        public int Id { get; } = id;

        public bool Equals(Unequatable other)
        {
            throw new InvalidOperationException("Equals was called.");
        }

        public override bool Equals(object? obj)
        {
            throw new InvalidOperationException("Equals was called.");
        }

        public override int GetHashCode()
        {
            throw new InvalidOperationException("GetHashCode was called.");
        }
    }
}
