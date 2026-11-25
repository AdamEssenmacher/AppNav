using System.Collections.ObjectModel;

namespace AdamE.AppNav.Routing;

/// <summary>
/// Describes why a URI did not produce a route.
/// </summary>
public sealed class RouteDiagnostic
{
    // Takes ownership of data: the caller passes a dictionary it made for this diagnostic alone and keeps no reference
    // to it, so that the read-only wrapper is the only way to reach it.
    internal RouteDiagnostic(string code, string message, Dictionary<string, object?> data)
    {
        Code = code;
        Message = message;
        Data = new ReadOnlyDictionary<string, object?>(data);
    }

    /// <summary>
    /// Gets the code that identifies the kind of diagnostic, one of the values in <see cref="RouteDiagnosticCodes"/>.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets a message that describes the diagnostic for a developer. It never contains text from the URI, which can
    /// carry secrets such as sign-in tokens, so it is safe to log. The wording may change; compare
    /// <see cref="Code"/> instead.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the values that describe the diagnostic, keyed as each code in <see cref="RouteDiagnosticCodes"/>
    /// documents. Never <see langword="null"/>, and read-only: it cannot be changed, even through a cast. Values from
    /// the URI are kept as they were, without escaping or truncation, and can carry secrets, so logging them is the
    /// caller's decision.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Data { get; }
}
