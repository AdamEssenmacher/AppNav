namespace AdamE.AppNav;

/// <summary>
/// The base type for routes. A route is a typed value that names a destination in application terms, such as an
/// inventory item by its ID. It carries durable application identity only: not pages, view models, services,
/// callbacks or anything about how a request arrived.
/// </summary>
/// <remarks>
/// <para>
/// Derive a sealed record for each destination, with one property for each value that identifies it, and map it to a
/// template with <see cref="Routing.RouteTableBuilder.Map{TRoute}(string, Func{Routing.RouteMatchContext, TRoute})"/>,
/// or with the overload that also takes a format function, which makes the route writable as a link by
/// <see cref="Routing.RouteTable.Format(AppRoute)"/>.
/// </para>
/// <para>
/// Two routes of the same type with equal members are the same destination, so give a route only members with value
/// equality: strings, numbers, enums and other records, not arrays or lists, which records compare by reference.
/// A record's <see cref="object.ToString"/> prints every member, so a route that is logged shows all of its values.
/// </para>
/// </remarks>
public abstract record AppRoute;
