namespace AdamE.AppNav.Routing;

/// <summary>
/// Reads a path or query value as a value of the application's own type, for a codec registered with
/// <see cref="RouteTableBuilder.AddValueCodec{TValue}"/>.
/// </summary>
/// <typeparam name="TValue">The type the text is read as.</typeparam>
/// <param name="text">
/// The value to read, decoded: a captured path value, or a query value. It is never <see langword="null"/>, and it can
/// be empty, as for a query value written as <c>?name=</c>.
/// </param>
/// <param name="value">
/// When this method returns <see langword="true"/>, the value the text gives, which must not be
/// <see langword="null"/>. When it returns <see langword="false"/>, the value is ignored, and may be
/// <see langword="null"/> or the default value of <typeparamref name="TValue"/>.
/// </param>
/// <returns>
/// <see langword="true"/> when the text is a valid value of <typeparamref name="TValue"/>; <see langword="false"/>
/// when it is not.
/// </returns>
/// <remarks>
/// Return <see langword="false"/> for a text that is not a valid value, rather than throw: the typed read then rejects
/// the value as it rejects one of a built-in type. A parser that returns <see langword="true"/> must give a value:
/// <see langword="true"/> with <see langword="null"/> is refused with <see cref="InvalidOperationException"/>. The
/// value is declared nullable so that a lambda, or a <c>TryParse</c> method whose parameter is <c>out T?</c>,
/// converts for a reference type without a warning; for a struct, <typeparamref name="TValue"/> is the struct
/// itself. An exception it throws during a typed read propagates unchanged out
/// of the read and out of <see cref="RouteTable.Match(Uri)"/>, except a <see cref="RouteValueException"/>, which
/// <see cref="RouteTable.Match(Uri)"/> takes as a value that is not valid, as it takes one thrown by a create
/// function. One it throws while <see cref="RouteTable.Format(AppRoute)"/> reads back a written text propagates
/// unchanged, a <see cref="RouteValueException"/> included.
/// </remarks>
public delegate bool RouteValueParser<TValue>(string text, out TValue? value);
