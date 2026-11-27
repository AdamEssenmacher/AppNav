using AdamE.AppNav.Routing;

namespace AdamE.AppNav;

/// <summary>
/// Names one entry of <see cref="RouteMetadata"/> and fixes the type of its value. Route metadata is the values that
/// travel with a route but are not part of its identity, such as a campaign code on a link.
/// </summary>
/// <typeparam name="TValue">
/// The type of the value. It cannot be the nullable form of a value type: an absent entry is how metadata says that
/// there is no value.
/// </typeparam>
/// <remarks>
/// <para>
/// Two keys are equal when their names are equal, compared ordinally and case-sensitively, and they have the same
/// <typeparamref name="TValue"/>. So <c>new RouteMetadataKey&lt;int&gt;("n")</c> and
/// <c>new RouteMetadataKey&lt;string&gt;("n")</c> are different keys, which name different entries.
/// </para>
/// <para>
/// A key is immutable and safe to share across threads. Declare each key once, for example as a static field, and
/// use it wherever the entry is written or read.
/// </para>
/// </remarks>
public sealed class RouteMetadataKey<TValue> : IEquatable<RouteMetadataKey<TValue>>
    where TValue : notnull
{
    /// <summary>
    /// Initializes a key with the given name.
    /// </summary>
    /// <param name="name">The name of the key. Any text that is not empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, and the exception's <see cref="ArgumentException.ParamName"/> is
    /// <c>name</c>; or <typeparamref name="TValue"/> is the nullable form of a value type, and the exception's
    /// <see cref="ArgumentException.ParamName"/> is <c>TValue</c>. The message names the type.
    /// </exception>
    public RouteMetadataKey(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Type? underlying = Nullable.GetUnderlyingType(typeof(TValue));
        if (underlying is not null)
        {
            string type = RouteValueTypes.DisplayName(typeof(TValue));
            string inner = RouteValueTypes.DisplayName(underlying);
            throw new ArgumentException(
                $"The type '{type}' cannot be the value type of a route metadata key, because it is the nullable " +
                $"form of '{inner}'. Use '{inner}' instead; an entry that is absent says that there is no value.",
                nameof(TValue));
        }

        Name = name;
    }

    /// <summary>
    /// Gets the name of the key.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Determines whether this key equals another: whether their names are equal, compared ordinally and
    /// case-sensitively. Both have the same <typeparamref name="TValue"/>.
    /// </summary>
    /// <param name="other">The key to compare with.</param>
    /// <returns><see langword="true"/> when the keys are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(RouteMetadataKey<TValue>? other)
    {
        return other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether this key equals an object: whether it is a key with the same
    /// <typeparamref name="TValue"/> and an equal name.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal key; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as RouteMetadataKey<TValue>);
    }

    /// <summary>
    /// Returns a hash code that agrees with <see cref="Equals(RouteMetadataKey{TValue})"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(StringComparer.Ordinal.GetHashCode(Name), typeof(TValue));
    }

    /// <summary>
    /// Determines whether two keys are equal, as <see cref="Equals(RouteMetadataKey{TValue})"/> does; two that are both
    /// <see langword="null"/> are equal.
    /// </summary>
    /// <param name="left">The first to compare.</param>
    /// <param name="right">The second to compare.</param>
    /// <returns><see langword="true"/> when they are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(RouteMetadataKey<TValue>? left, RouteMetadataKey<TValue>? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>
    /// Determines whether two keys are not equal: the opposite of the equality operator.
    /// </summary>
    /// <param name="left">The first to compare.</param>
    /// <param name="right">The second to compare.</param>
    /// <returns><see langword="true"/> when they are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(RouteMetadataKey<TValue>? left, RouteMetadataKey<TValue>? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Returns the name of the key.
    /// </summary>
    /// <returns>The name of the key.</returns>
    public override string ToString()
    {
        return Name;
    }
}
