using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav;

/// <summary>
/// An immutable collection of route metadata: values that travel with a route but are not part of its identity, such
/// as a campaign code on a link. Each entry is named by a <see cref="RouteMetadataKey{TValue}"/>, which fixes the
/// type of its value.
/// </summary>
/// <remarks>
/// <para>
/// Start from <see cref="Empty"/> and add entries with <see cref="With{TValue}(RouteMetadataKey{TValue}, TValue)"/>.
/// Each method that changes the collection returns a new one and leaves the collection it is called on unchanged, so
/// a collection is safe to share across threads.
/// </para>
/// <para>
/// An entry is identified by its key: the key's name and its value type. A key named <c>n</c> of type
/// <see cref="int"/> and one named <c>n</c> of type <see cref="string"/> name two entries, which can both be present,
/// and neither reads the other's value. A value is only ever stored under a key of its own type, and an entry never
/// holds <see langword="null"/>; an absent entry is how the collection says that there is no value.
/// </para>
/// <para>
/// Two collections are equal when they hold the same entries, with equal keys and values equal by
/// <see cref="EqualityComparer{T}.Default"/>, whatever order the entries were added in. A value of a type that
/// compares by reference, such as an array or a list, makes two collections equal only when that entry holds the
/// same instance, so prefer values with value equality: strings, numbers, enums and records. Equal collections can
/// also hold values that differ in ways their type's equality ignores, such as the scale of a <see cref="decimal"/>.
/// <see cref="Equals(RouteMetadata)"/> and <see cref="GetHashCode"/> call the values' equality; the methods that set,
/// read and remove an entry do not.
/// </para>
/// </remarks>
public sealed class RouteMetadata : IEquatable<RouteMetadata>
{
    private readonly RouteMetadataEntry[] _entries;

    // Takes ownership of entries: the caller made the array for this collection alone, with at most one entry for
    // each key, and keeps no reference to it.
    private RouteMetadata(RouteMetadataEntry[] entries)
    {
        _entries = entries;
    }

    /// <summary>
    /// Gets a collection with no entries.
    /// </summary>
    public static RouteMetadata Empty { get; } = new([]);

    /// <summary>
    /// Gets the number of entries in the collection.
    /// </summary>
    public int Count => _entries.Length;

    /// <summary>
    /// Gets the value of the entry for a key.
    /// </summary>
    /// <remarks>
    /// The entry is found by its key alone, so this method does not call the value's
    /// <see cref="object.Equals(object)"/> or <see cref="object.GetHashCode"/>.
    /// </remarks>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The key of the entry.</param>
    /// <param name="value">
    /// The value of the entry when there is one; otherwise the default value of <typeparamref name="TValue"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the collection has an entry for <paramref name="key"/>; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryGet<TValue>(RouteMetadataKey<TValue> key, [MaybeNullWhen(false)] out TValue value)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(key);

        TypedRouteMetadataEntry<TValue>? entry = Find(key, out _);
        if (entry is null)
        {
            value = default;
            return false;
        }

        value = entry.Value;
        return true;
    }

    /// <summary>
    /// Returns a collection with the entry for a key set to a value, replacing an entry for an equal key.
    /// </summary>
    /// <remarks>
    /// The value given is always stored, even when the entry already holds an equal value, so it is the value that
    /// <see cref="TryGet{TValue}(RouteMetadataKey{TValue}, out TValue)"/> reads, with any detail that the type's
    /// equality ignores, such as the scale of a <see cref="decimal"/>. This method does not call the value's
    /// <see cref="object.Equals(object)"/> or <see cref="object.GetHashCode"/>.
    /// </remarks>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The key of the entry.</param>
    /// <param name="value">The value of the entry.</param>
    /// <returns>
    /// A collection with the other entries of this one and the entry for <paramref name="key"/>. This collection is
    /// unchanged.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="key"/> or <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    public RouteMetadata With<TValue>(RouteMetadataKey<TValue> key, TValue value)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        TypedRouteMetadataEntry<TValue> entry = new(key, value);
        // The value given is always stored, even over an equal one, since equal values can differ in ways their
        // type's equality ignores, such as the scale of a decimal. So the value's Equals is not called.
        if (Find(key, out int index) is not null)
        {
            RouteMetadataEntry[] replaced = [.. _entries];
            replaced[index] = entry;
            return new RouteMetadata(replaced);
        }

        RouteMetadataEntry[] added = new RouteMetadataEntry[_entries.Length + 1];
        Array.Copy(_entries, added, _entries.Length);
        added[^1] = entry;
        return new RouteMetadata(added);
    }

    /// <summary>
    /// Returns a collection without the entry for a key.
    /// </summary>
    /// <remarks>
    /// The entry is found by its key alone, so this method does not call the value's
    /// <see cref="object.Equals(object)"/> or <see cref="object.GetHashCode"/>.
    /// </remarks>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The key of the entry.</param>
    /// <returns>
    /// A collection with the other entries of this one, which equals this one when it has no entry for
    /// <paramref name="key"/>. This collection is unchanged.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public RouteMetadata Without<TValue>(RouteMetadataKey<TValue> key)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(key);

        if (Find(key, out int index) is null)
            return this;
        if (_entries.Length == 1)
            return Empty;

        RouteMetadataEntry[] removed = new RouteMetadataEntry[_entries.Length - 1];
        Array.Copy(_entries, removed, index);
        Array.Copy(_entries, index + 1, removed, index, _entries.Length - index - 1);
        return new RouteMetadata(removed);
    }

    /// <summary>
    /// Determines whether this collection equals another: whether they hold the same entries, with equal keys and
    /// values equal by <see cref="EqualityComparer{T}.Default"/>, whatever order the entries were added in.
    /// </summary>
    /// <param name="other">The collection to compare with.</param>
    /// <returns><see langword="true"/> when the collections are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(RouteMetadata? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (_entries.Length != other._entries.Length)
            return false;

        // Neither collection holds two entries for one key, so with equal counts every entry of this one having an
        // equal entry in the other means that they hold the same entries.
        foreach (RouteMetadataEntry entry in _entries)
        {
            if (!other.HasEqualEntry(entry))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether this collection equals an object: whether it is a collection that holds the same entries.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when the object is an equal collection; otherwise <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as RouteMetadata);
    }

    /// <summary>
    /// Returns a hash code that agrees with <see cref="Equals(RouteMetadata)"/> and does not depend on the order the
    /// entries were added in.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        // A sum does not depend on the order of the entries.
        int hash = 0;
        foreach (RouteMetadataEntry entry in _entries)
            hash = unchecked(hash + entry.GetEntryHashCode());
        return hash;
    }

    // The entry for key and its position, found by a type test, which cannot fail, so no cast is made; null and -1
    // when there is none.
    private TypedRouteMetadataEntry<TValue>? Find<TValue>(RouteMetadataKey<TValue> key, out int index)
        where TValue : notnull
    {
        for (index = 0; index < _entries.Length; index++)
        {
            if (_entries[index] is TypedRouteMetadataEntry<TValue> typed && typed.Key.Equals(key))
                return typed;
        }

        index = -1;
        return null;
    }

    private bool HasEqualEntry(RouteMetadataEntry entry)
    {
        foreach (RouteMetadataEntry candidate in _entries)
        {
            if (candidate.EntryEquals(entry))
                return true;
        }

        return false;
    }
}
