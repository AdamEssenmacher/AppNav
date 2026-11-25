namespace AdamE.AppNav.Routing;

/// <summary>
/// Thrown by a route's create function to say that a value the URI carries is not valid for the route, so that
/// <see cref="RouteTable.Match(Uri)"/> fails for the URI instead of throwing.
/// </summary>
/// <remarks>
/// <para>
/// A constraint in the template makes a path that does not satisfy it not match, so that another template can. This
/// exception is for a value that the template accepted but the create function cannot use, such as a number out of
/// the range the route allows, or a query value of the wrong shape. When the create function of the definition that
/// matched throws it, <see cref="RouteTable.Match(Uri)"/> returns a failed result with one
/// <see cref="RouteDiagnosticCodes.ValueInvalid"/> diagnostic, and no later template is tried.
/// </para>
/// <para>
/// Only an exception thrown while the table is calling that create function is turned into a result. Thrown anywhere
/// else, it is an ordinary exception. The table reads only <see cref="ValueName"/> and
/// <see cref="Exception.Message"/>; the message goes into the diagnostic's <see cref="RouteDiagnostic.Data"/>, so
/// what it says is the application's choice, and a message that quotes the value can carry a secret from the URI.
/// </para>
/// </remarks>
public sealed class RouteValueException : Exception
{
    /// <summary>
    /// Initializes a new instance with a message that names the value.
    /// </summary>
    /// <param name="valueName">
    /// The name of the value that is not valid: a path parameter name, or a query name. It can be empty, since a query
    /// can have an empty name.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="valueName"/> is <see langword="null"/>.</exception>
    public RouteValueException(string valueName)
        : this(valueName, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance with a message that says why the value is not valid.
    /// </summary>
    /// <param name="valueName">
    /// The name of the value that is not valid: a path parameter name, or a query name. It can be empty.
    /// </param>
    /// <param name="message">
    /// Why the value is not valid, or <see langword="null"/> for a message that names the value. The table reports
    /// this text as it is given, even when it is empty. Do not pass another exception's message: those of many
    /// .NET parse methods quote their input, which here is text from the URI.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="valueName"/> is <see langword="null"/>.</exception>
    public RouteValueException(string valueName, string? message)
        : this(valueName, message, null)
    {
    }

    /// <summary>
    /// Initializes a new instance with a message that says why the value is not valid, and the exception that caused
    /// this one.
    /// </summary>
    /// <param name="valueName">
    /// The name of the value that is not valid: a path parameter name, or a query name. It can be empty.
    /// </param>
    /// <param name="message">
    /// Why the value is not valid, or <see langword="null"/> for a message that names the value. The table reports
    /// this text as it is given, even when it is empty. Do not pass another exception's message: those of many
    /// .NET parse methods quote their input, which here is text from the URI.
    /// </param>
    /// <param name="innerException">
    /// The exception that caused this one, or <see langword="null"/>. The table neither keeps nor exposes it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="valueName"/> is <see langword="null"/>.</exception>
    public RouteValueException(string valueName, string? message, Exception? innerException)
        : base(MessageFor(valueName, message), innerException)
    {
        ValueName = valueName;
    }

    /// <summary>
    /// Gets the name of the value that is not valid: a path parameter name or a query name, as the create function
    /// gave it. Never <see langword="null"/>; it can be empty.
    /// </summary>
    public string ValueName { get; }

    // The base constructor runs before the body, so the name is checked here. The default message names the value but
    // never quotes it, since the value comes from the URI.
    private static string MessageFor(string valueName, string? message)
    {
        ArgumentNullException.ThrowIfNull(valueName);

        return message ?? $"The value of '{RouteText.EscapeForMessage(valueName)}' is not valid for the route.";
    }
}
