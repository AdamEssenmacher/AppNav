namespace AdamE.AppNav.Tests.Routing;

// Small routes for the route table tests, one for each shape of captured value.
internal sealed record SettingsRoute : AppRoute;

internal sealed record ProductRoute(string Id) : AppRoute;

internal sealed record ProductByNameRoute(string Name) : AppRoute;

internal sealed record OrderRoute(string Id, string? Tab) : AppRoute;

internal sealed record FileRoute(string? Path) : AppRoute;
