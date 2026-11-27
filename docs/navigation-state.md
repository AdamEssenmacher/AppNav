# Navigation state

[Navigation state](architecture.md#the-central-idea) is where the app is, in application terms, held as immutable values rather than as pages, controls or view models. For now this page describes [route entries](#route-entries), the leaves of the state; the rest of the state arrives with the code that builds it.

## Route entries

A `RouteEntry` is one occurrence of a route in the navigation state: a [route](architecture.md#the-central-idea), the [metadata](routing.md#route-metadata) that travels with it, and an id that names this occurrence. The id tells one occurrence from another of the same route, so the same route shown twice is two entries; the app chooses it.

```csharp
using AdamE.AppNav;
using AdamE.AppNav.State;

RouteMetadataKey<string> campaign = new("campaign");

ItemRoute route = new(42);
RouteEntry first = new("item-1", route);
RouteEntry second = new("item-2", route, RouteMetadata.Empty.With(campaign, "spring"));

Console.WriteLine(first.Id);                                             // item-1
Console.WriteLine(first.Route);                                          // ItemRoute { Id = 42 }
Console.WriteLine(first.Metadata.Count);                                 // 0
Console.WriteLine(second.Metadata.TryGet(campaign, out string? code));   // True
Console.WriteLine(code);                                                 // spring
Console.WriteLine(first == second);                                      // False: the same route, two entries
Console.WriteLine(first == new RouteEntry("item-1", new ItemRoute(42))); // True
Console.WriteLine(first);                                                // AdamE.AppNav.State.RouteEntry

public sealed record ItemRoute(int Id) : AppRoute;
```

- `new RouteEntry(id, route)` has no metadata, so its `Metadata` has no entries, and `new RouteEntry(id, route, metadata)` has the metadata given.
- An id is any text that is not empty and not only white space. A `null` id throws `ArgumentNullException`, and an empty or blank one throws `ArgumentException`, both with the `ParamName` `id`. White space is what `char.IsWhiteSpace` says it is, so an id of other characters that cannot be seen, such as a zero-width space, is accepted. The id is kept exactly as given: it is not trimmed, so leading and trailing white space are part of it, and its case is not changed.
- A `null` route or metadata throws `ArgumentNullException` whose `ParamName` is `route` or `metadata`. The arguments are checked in order: id, route, metadata.
- The messages of these exceptions never contain the id or anything from the route, since an id is often built from route values, which can come from a link.
- `Id`, `Route` and `Metadata` are get-only and never hold `null`. A route entry is a class, not a record, so there is no `with` expression to copy it.
- Two entries are equal when their ids are equal, compared ordinally and case-sensitively, their routes are equal and their metadata are equal, as [route metadata](routing.md#route-metadata) compares, so two entries can be equal although their metadata hold values that differ in a way their type's equality ignores, such as the scale of a `decimal`. Equal entries have equal hash codes; `==` and `!=` agree with `Equals`, and two `null` entries are equal.
- An entry's `ToString` does not print its id, its route or its metadata: it prints only its full type name.
- An entry is immutable and safe to share across threads, provided its route is immutable too, as a route whose members cannot be set is. An entry compares and hashes by its route, so changing a member of a route that allows it changes what the entry equals.
