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
}
