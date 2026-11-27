namespace AdamE.AppNav.Routing;

/// <summary>
/// The codes of the diagnostics that routing reports in <see cref="RouteDiagnostic.Code"/>.
/// </summary>
/// <remarks>
/// A code is lower-case words joined by <c>_</c>, grouped by <c>.</c> from the general to the specific, as in
/// <c>route.not_matched</c>. A code, once published, does not change meaning: compare it, not the message, to tell
/// diagnostics apart.
/// </remarks>
public static class RouteDiagnosticCodes
{
    /// <summary>
    /// No route's template matches the path of the URI. The diagnostic's <see cref="RouteDiagnostic.Data"/> holds the
    /// path that was matched under the key <c>path</c>, as a <see langword="string"/>; its message does not.
    /// </summary>
    public const string NotMatched = "route.not_matched";

    /// <summary>
    /// A template matched the path of the URI, but a value the URI carries is not valid: its create function threw a
    /// <see cref="RouteValueException"/> because the value is not valid for the route, or the table, reading the
    /// route metadata it declares after the create function returned, found a query value that is not valid for its
    /// key. The result's <see cref="RouteMatchResult.Definition"/> is the definition whose template matched. The
    /// diagnostic's <see cref="RouteDiagnostic.Data"/> holds, each as a <see langword="string"/>, the name of the
    /// value, which is the exception's <see cref="RouteValueException.ValueName"/> or the name of the key, under the
    /// key <c>name</c>, the template as written under <c>template</c>, what is wrong with the value, which is the
    /// exception's message, under <c>reason</c>, and the path that was matched under <c>path</c>. Its message holds
    /// none of them.
    /// </summary>
    public const string ValueInvalid = "route.value.invalid";
}
