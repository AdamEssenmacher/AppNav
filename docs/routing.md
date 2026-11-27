# Routing

A [route](architecture.md#the-central-idea) is a typed value that names a destination in application terms. A [route template](#route-templates) is the path pattern a route answers to, and the [route table](#the-route-table) maps templates to route types, so that matching a URI gives you a typed route.

## Route templates

A route template is the path pattern that a route answers to, such as `/settings/profile`. `RouteTemplate.Parse` turns the template text into a `RouteTemplate`, and `Match` tells you whether a path fits it and, when the template has [parameters](#path-parameters), what they captured. [`Format`](#formatting-a-path) builds a path from parameter values.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate template = RouteTemplate.Parse("/settings/profile");

Console.WriteLine(template.Match("/settings/profile") is not null);  // True
Console.WriteLine(template.Match("settings//profile/") is not null); // True: slashes are normalized
Console.WriteLine(template.Match("/Settings/profile") is not null);  // False: segments are case-sensitive
Console.WriteLine(template.Match("/settings") is not null);          // False: a segment is missing
```

A template must start with `/`. It is divided into segments at each `/`. A segment is either literal text or a [parameter](#path-parameters), which can carry a [constraint](#constraints), can be [optional](#optional-parameters) or can be a [catch-all](#catch-all-parameters). A path matches when it has the same number of segments as the template and each literal equals the path segment in the same position, except that a path can leave out [optional parameters](#optional-parameters), only from the end, and a [catch-all parameter](#catch-all-parameters) at the end takes any number of segments, including none. `Match` returns `null` when the path does not match, and a dictionary of captured values when it does; when nothing is captured, the dictionary is empty.

Templates and paths are divided and decoded the same way:

- Leading, trailing and repeated slashes do not create segments. `/a//b/` has the same segments as `/a/b`, and `/` is the root template, which has no segments.
- Each segment is percent-decoded once, after the text is divided. `/files/a%20b` matches `/files/a b`, and `%2F` stays inside its segment, so `/files/a%2Fb` does not match `/files/a/b`. The exception is the segments a [catch-all parameter](#catch-all-parameters) captures, where `%2F` acts as `/`. A percent sequence that is not valid, or does not decode to valid UTF-8, such as `%zz` or `%FF`, is kept as written.
- Literals are compared with decoded path segments ordinally and case-sensitively, as described in [How text is compared](#how-text-is-compared).

A path is otherwise taken as it is:

- The leading `/` is optional.
- White space is significant: `/settings ` does not match `/settings`.
- A query string or fragment is not removed, so remove it before calling `Match`.
- `+` is not a space.

### How text is compared

A literal equals a decoded path segment when the two are the same sequence of UTF-16 code units: the comparison is ordinal and case-sensitive. No culture's rules are applied, so `/i` matches neither `/I` nor `/İ`, whatever the current culture. No Unicode normalization is applied either, so the same visible text in two normalization forms is two different literals. `/caf%C3%A9` ends in the single character `é` (U+00E9) and `/cafe%CC%81` ends in `e` followed by a combining acute accent (U+0301). They render the same, but neither matches the other.

- A captured value keeps the form the path had. Nothing is normalized when a value is captured or when `Format` writes it, so the value captured from `/cafe%CC%81` is `e` followed by U+0301, and `Format` writes it back as `cafe%CC%81`.
- Parameter names and constraint names are ASCII and are compared ignoring case, so normalization cannot affect them.
- The [`alpha` constraint](#constraints) rejects combining marks, so decomposed text does not satisfy it.

Write templates in NFC, the form in which `é` is one character and the one most keyboards and tools produce. If paths can arrive in another form, normalize them before calling `Match`: remove any query string or fragment, divide the path at `/`, and replace each segment with `Uri.EscapeDataString(Uri.UnescapeDataString(segment).Normalize(NormalizationForm.FormC))`. Encoding the text again keeps `%2F` inside its segment and keeps `%25` and invalid percent sequences meaning what they did. `path.Normalize(NormalizationForm.FormC)` alone changes nothing that is percent-encoded, because percent-encoded text is ASCII. `Normalize` throws `ArgumentException` for a segment that contains a lone surrogate, which `Match` accepts. NFC also replaces a few characters with their canonical equivalents, such as the Kelvin sign with `K`. To compare captured values in one form instead, normalize the values `Match` returns.

Ordinal comparison is fast, unambiguous and the same on every platform and in every culture. Normalization would make two different paths the same route without the app asking for it.

### Dot segments

A dot segment, a segment whose decoded text is exactly `.` or `..`, is never part of a route, because a URI parser may remove it when it resolves a path. `System.Uri` removes it, written or percent-encoded, from a URI with an authority, such as `https://host/a/../b` or `app://host/a/../b`, and keeps it, decoding `%2E`, in one without, such as `app:/a/../b`, as relative URIs and plain strings do. So the rule is enforced here rather than left to the parser: a template cannot have one as a literal, a path that has one in any position does not match, and `Format` never writes one, so a URI parser removes none of the segments of a formatted path as dot segments. Other text that contains dots, such as `...`, `a..b` or `.well-known`, is ordinary.

This is not path-traversal protection for a parameter that is not a catch-all, because an encoded slash stays inside its segment: `/files/{name}` matches `/files/..%2F..%2Fsecret` with `name` set to `../../secret`, so code that uses a value as a file path must validate it.

## Path parameters

A segment written `{name}` is a parameter. It matches any one path segment and captures that segment's decoded text under its name.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate product = RouteTemplate.Parse("/stores/{storeId}/products/{productId}");

IReadOnlyDictionary<string, string>? values = product.Match("/stores/north%20wind/products/42");
Console.WriteLine(values?["storeId"]);   // north wind
Console.WriteLine(values?["PRODUCTID"]); // 42: names are not case-sensitive

Console.WriteLine(product.Match("/stores/northwind/products") is null); // True: a segment is missing
```

- A parameter is the whole segment. Its name starts with an ASCII letter or `_`, followed by ASCII letters, digits or `_`.
- Names are compared without regard to case. A template cannot use two names that differ only by case. The dictionary `Match` returns is keyed by the names as written in the template and looks them up without regard to case.
- A parameter matches exactly one segment, unless it is [optional](#optional-parameters) or a [catch-all](#catch-all-parameters). Its value is the segment after decoding, exactly as a literal would be compared: `%2F` becomes `/` inside the value, white space is kept, an invalid percent sequence is kept as written, and the text is not [normalized](#how-text-is-compared). Unless the parameter has a [constraint](#constraints), values are not checked further, so a value can be any text, including a decoded control character.
- Each match that captures a value returns its own dictionary. Treat it as read-only.

## Constraints

A parameter written `{name:constraint}` matches a segment only when the segment's decoded text satisfies the constraint. A segment that does not satisfy it is not an error: the path does not match this template, and may match another.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate order = RouteTemplate.Parse("/stores/{store:alpha}/orders/{id:int}");

Console.WriteLine(order.Match("/stores/northwind/orders/42")?["id"]);     // 42
Console.WriteLine(order.Match("/stores/northwind/orders/+007")?["id"]);   // +007: the value is not normalized
Console.WriteLine(order.Match("/stores/northwind/orders/4.2") is null);   // True: 4.2 is not an int
Console.WriteLine(order.Match("/stores/northwind/orders/%2042") is null); // True: white space is not trimmed
Console.WriteLine(order.Match("/stores/north%20wind/orders/42") is null); // True: a space is not a letter
```

The built-in constraints are:

| Constraint | Satisfied by |
| --- | --- |
| `int` | An optional leading `+` or `-` followed by one or more ASCII digits, within the range of `int`, from -2147483648 to 2147483647. Leading zeros are allowed. |
| `long` | The same, within the range of `long`, from -9223372036854775808 to 9223372036854775807. |
| `decimal` | An optional leading `+` or `-` followed by ASCII digits with at most one `.`, and at least one digit, so `.5`, `5.` and `-.5` satisfy it. The value must lie within the range of `decimal`, ±79228162514264337593543950335, after rounding to its precision: digits beyond that are allowed, so `79228162514264337593543950335.4` satisfies it and `79228162514264337593543950335.5` does not. At the other end, a value too small to represent rounds to zero and still satisfies it, such as `0.` followed by 100 zeros and a `1`. |
| `bool` | `true` or `false`, in any ASCII case, such as `True` or `FALSE`. |
| `guid` | 32 hex digits, in either case, with hyphens in groups of 8, 4, 4, 4 and 12, as in `6f9619ff-8b86-d011-b42d-00cf4fc964ff`: 36 characters, the form `Guid.ToString()` writes. The form without hyphens, which `Guid.ToString("N")` writes, such as `6f9619ff8b86d011b42d00cf4fc964ff`, does not satisfy it. |
| `alpha` | One or more Unicode letters, including letters outside the Basic Multilingual Plane, such as `𝒜`. |
| `sbyte`, `byte`, `short`, `ushort`, `uint`, `ulong` | The rule of `int`, within the range of the type, as for the [typed read](#typed-values) of the same type: `300` is not a `byte` and `-1` is not a `uint`, but `-0` and `+5` satisfy each of them. |
| `float`, `double` | The shape of `decimal`, within the range of the type rather than that of `decimal`, as for the [typed read](#typed-values) of the same type: no exponent, `NaN` or `Infinity`, digits beyond the precision of the type allowed, and a magnitude beyond its largest finite value, after rounding, rejected. A whole number of 30 digits is a `float` and a `double` but not a `decimal`, and one of 40 digits is a `double` but not a `float`. |

- Constraint names are compared without regard to case: `{id:INT}` is `{id:int}`.
- Each constraint named after a type accepts exactly the text that the [typed read](#typed-values) of that type accepts, by the same rule, so `{n:byte}` guarantees that `Path<byte>("n")` succeeds.
- Every check is the same in every culture and applies to the decoded text exactly as it is captured. Nothing is trimmed, so a value with white space, such as `%2042`, never satisfies a number, `bool` or `guid`. Only the characters in the table are allowed: no thousands separators, exponents, currency symbols, trailing signs or digits other than ASCII `0` to `9`, such as `٣`, and no braces, parentheses or other separators in a guid.
- `alpha` is letters only. Digits, spaces, punctuation and combining marks are not letters, so decomposed text such as `e` followed by a combining acute accent (`e%CC%81`) does not satisfy it, though the single character `é` does. Some text keeps combining marks even in NFC, such as most Devanagari words, and never satisfies it.
- The captured value is the decoded text, unchanged: `+007` is captured as `+007`, not `7`.
- Some text satisfies more than one constraint, such as `0`, which is a value of every number type, or `true`, which is a `bool` and `alpha`. A route table cannot have two templates that differ only in such constraints, as described in [Templates that cannot be told apart](#templates-that-cannot-be-told-apart).
- Several texts can denote one value, such as `7`, `007` and `+7`, `true` and `TRUE`, or a guid in upper and lower case. Each satisfies the constraint and is captured as written, and `RouteTemplate.Format` writes whatever text it is given. `RouteTable.Format` writes one canonical text for each typed value, as described in [Formatting a route](#formatting-a-route).
- To accept a guid with or without hyphens, use a parameter without a constraint and parse the value in the create function, reading it as a string: `Path<Guid>` follows the rule of `guid` and accepts only the hyphenated form. Three things follow. A value that is not a guid still matches the template, so the create function must [reject it](#rejecting-a-value), which fails the match for that URI rather than letting another template try it. A position holds only one parameter without a constraint among templates of one shape, unless another required position keeps the templates apart. And beside `{id:int}` or `{x:alpha}`, a value without hyphens that is also a valid number, such as 32 zeros, or is made only of letters still goes to the constrained route, which ranks higher.

## Custom constraints

A route table can also use constraints of the application's own. Add one to the builder with `AddConstraint(name, isSatisfiedBy)`, and the templates mapped on that builder can name it as they name a built-in constraint, as in `{slug:slug}` or `{slug:slug?}`:

```csharp
using AdamE.AppNav.Routing;

RouteTable routes = new RouteTableBuilder()
    .AddConstraint("slug", IsSlug)
    .Map("/articles/{slug:slug}", c => new ArticleRoute(c.Path("slug")))
    .Map("/articles/{id:int}/edit", c => new EditArticleRoute(c.Path<int>("id")))
    .Build();

static bool IsSlug(string text)
{
    if (text.Length == 0)
        return false;

    foreach (char c in text)
    {
        if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            return false;
    }

    return true;
}

public sealed record ArticleRoute(string Slug) : AppRoute;

public sealed record EditArticleRoute(int Id) : AppRoute;
```

- A name is an ASCII letter followed by ASCII letters, digits, `-` or `_`, such as `slug` or `order-code`. Names are compared without regard to ASCII case, so `{x:Slug}` names `slug`, and messages write the name in lower case. A template that names the same constraint in another case is the same template, as `/a/{x:slug}` and `/a/{y:SLUG}` are.
- `AddConstraint` throws `ArgumentNullException` for a `null` name or function, and `ArgumentException`, whose `ParamName` is `name`, for a name not of that form, the name of a built-in constraint, a reserved name, or a name already added on the builder. The reserved names are `string` and `char`: a template that named one would look like a constraint of a type whose values a route reads or writes as text, so neither can be added. A `string` constraint would accept every text, which is no constraint at all: leave the parameter unconstrained. A `char` is deliberately not a value type of the library, with no typed read: read a single character as a string. When it throws, the builder is not changed. A reserved name can never be added, so `Map` rejects a template that names one, as in `{x:string}`, with `ArgumentException`, rather than leaving it for `Build`.
- Adding applies to the whole table, so `AddConstraint` can come before or after the `Map` calls that name the constraint. `Map` accepts a template that names a constraint that is not built in, provided the name has the form above and is not reserved; it is `Build` that throws `InvalidOperationException` when a template names one that has not been added by then. The message names the template, the route type and the constraint, and lists the built-in constraints. A misspelled built-in name has that form too, so `{id:itn}` is reported by `Build`, not by `Map`; when the name is one character or one swap of adjacent characters away from a built-in name, or is `int32`, `integer`, `int64`, `int8`, `uint8`, `ubyte`, `int16`, `uint16`, `uint32`, `uint64`, `single`, `boolean` or `uuid`, the message says which built-in constraint may have been meant. A name close to more than one built-in name is given the first in the order of the [table](#constraints), so `unt` is reported as `int`, not `uint`. Constraint text not of that form, such as `{n:range(1,5)}`, is still rejected by `Map`, as by `Parse`. A table that is already built does not change; call `Build` again for one that includes a later constraint.
- A constraint belongs to the builder it was added to. `RouteTemplate.Parse` knows only the built-in constraints, and rejects any other name at once, so the `Value` of a template that names an added constraint, read from `RouteTable.Definitions`, cannot be given to it. The template itself keeps the constraint: its own `Match` and `Format` use it.

The function receives the decoded segment text exactly as a built-in constraint does, without trimming, and returns whether the text satisfies the constraint. It must return the same answer for the same text, and must be safe to call from several threads at once, as a built table is. It is called:

- by `Match`, for the segment in the parameter's position, unless the segment is a [dot segment](#dot-segments), which never matches;
- by `Format` and `FormatUri`, to check the value written for the parameter, unless it is `.` or `..`, which is rejected first, and when they check whether another template would match the path written first, as [What a round trip promises](#what-a-round-trip-promises) describes;
- by `RouteTemplate.Match` and `RouteTemplate.Format` of a template mapped on the builder, such as one read from `RouteTable.Definitions`, as the table's own methods call it; and
- by `Build`, with sample texts, when it compares two templates of one shape, as described below, and when it checks a pair [declared disjoint](#declaring-constraints-disjoint).

An exception the function throws propagates unchanged from each of these, a `RouteValueException` included: the function runs while the table chooses a template, before any create function is called, so it cannot fail a match the way a create function [rejects a value](#rejecting-a-value). Return `false` instead.

Which text a function accepts cannot be known, so `Build` treats an added constraint as overlapping every constraint, itself included; only a pair [declared disjoint](#declaring-constraints-disjoint) is taken to accept no text in common, and a constraint can never be declared disjoint from itself. Without a declaration it cannot keep two templates apart that otherwise rank equally: `/articles/{slug:slug}` and `/articles/{id:int}` are rejected as [templates that cannot be told apart](#templates-that-cannot-be-told-apart), and would be even for a function that accepted no number. Keep such templates apart with a literal, as `/articles/{id:int}/edit` above does, or declare that the two constraints accept no text in common. A parameter with an added constraint ranks as one with a built-in constraint, so beside `/articles/{name}`, `/articles/{slug:slug}` is tried first.

A constraint only decides whether a segment matches. The captured value is the decoded text, unchanged, as for the built-in constraints, and no typed read is named after an added constraint. To read the value as a type of the application's own, pair the constraint with a [codec](#custom-value-types) for that type and read it with `Path<T>`, or parse the string in the create function; a value the codec's parser rejects then fails the match for that URI.

### Declaring constraints disjoint

When the application knows that two constraints accept no text in common, it can say so with `DeclareDisjointConstraints`, and `Build` then keeps apart templates that differ only in them:

```csharp
using AdamE.AppNav.Routing;

RouteTable routes = new RouteTableBuilder()
    .AddConstraint("slug", IsSlug)
    .DeclareDisjointConstraints("slug", "int")
    .Map("/articles/{slug:slug}", c => new ArticleRoute(c.Path("slug")))
    .Map("/articles/{id:int}", c => new ArticleByIdRoute(c.Path<int>("id")))
    .Build();

// A slug starts with a lowercase letter, so no slug is a number.
static bool IsSlug(string text)
{
    if (text.Length == 0 || !char.IsAsciiLetterLower(text[0]))
        return false;

    foreach (char c in text)
    {
        if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            return false;
    }

    return true;
}

public sealed record ArticleRoute(string Slug) : AppRoute;

public sealed record ArticleByIdRoute(int Id) : AppRoute;
```

- The names are constraint names, compared without regard to ASCII case: built-in names, such as `int`, and names added with `AddConstraint` can both be listed. Every two of the names are declared disjoint, so `DeclareDisjointConstraints("slug", "int", "guid")` declares `slug` and `int`, `slug` and `guid`, and `int` and `guid`, and nothing about any other pair. Declaring `a` and `b`, then `b` and `c`, does not declare `a` and `c`.
- Declaring applies to the whole table, so the call can come before or after the `AddConstraint` and `Map` calls concerned. Declaring a pair again, with an identical call, with the names in the other order, or as part of another list, is allowed and changes nothing. A table that is already built does not change; call `Build` again for one that includes a later declaration.
- The method checks its arguments in this order. A `null` name anywhere in the list throws `ArgumentNullException`, whose `ParamName` is `names`, before anything else, even when it is the only name. Then it throws `ArgumentException`, whose `ParamName` is `names` too, for fewer than two names (an array that is `null` counts as none), and then, for each name in turn, for a name not of the form of a constraint name, for a reserved name, `string` or `char`, and for a name that repeats an earlier one in the call, ignoring case, since a constraint always overlaps itself; the first failure is reported. When it throws, the builder is not changed.
- `Build` throws `InvalidOperationException` for a listed name that is neither built in nor added by then. The message names the constraint, says that `DeclareDisjointConstraints` named it, and, for a misspelled built-in name such as `itn`, names the built-in constraint it resembles, as for a template.
- `Build` checks every declared pair against the texts `0`, `a`, `true` and `00000000-0000-0000-0000-000000000000`, the samples it compares templates with. `Build` passes each sample text to the function of each constraint of a declared pair, once for that check however many pairs name the constraint, before it reports any pair, so which texts a function receives, and whether an exception it throws for one propagates, does not depend on the order of the names or of the declarations. When both constraints of a pair accept one of them, it throws `InvalidOperationException` naming both constraints and the text, and saying the declaration is false. The built-in constraints are known in full, so every false declaration about two of them is caught: any two number constraints, such as `byte` and `int`, both accept `0`, and `bool` and `alpha` both accept `true`. Declaring two built-in constraints that accept no text in common, such as `int` and `guid`, is true and changes nothing. Comparing the templates afterwards can pass a function the same texts again. An exception a function throws for a sample propagates unchanged from `Build`.

`Build` checks, in this order: the constraints the templates name, then the names declared disjoint, in the order they were first declared, then the declared pairs against the samples, in the order the pairs were first declared, reporting the first sample in the order above that both accept, then the templates against each other, then the format functions. So a template that names a constraint that was never added is reported before a declaration, and a declaration that a sample proves false is reported before any templates are compared.

Beyond that check, a declaration is trusted: a required position where two templates have the two constraints of a declared pair keeps them apart, and `Build` calls no function to compare them there. If the declaration is false, both templates match some path, and for such a path the order the routes were mapped in decides which one wins: the one mapped first. For example, with a constraint `lower` of five or more lowercase ASCII letters and `word` of five or more ASCII letters of either case declared disjoint, which no sample disproves, `/p/{a:lower}` and `/p/{b:word}` both build, and `/p/hello` opens the route of whichever was mapped first. `Format` and `FormatUri` then refuse to write the route of the template mapped later for a value that both constraints accept, such as `hello`, with the `ArgumentException` for a path [matched first by another template](#when-a-route-cannot-be-written), in either mapping order, whose message says that the other template ranks equally and was mapped first, which only a false declaration allows, and to correct or remove the declaration; the route of the template mapped first is written as usual, and so is a value of the later one that the earlier one's constraint rejects, such as `Hello` for `word` mapped after `lower`. Declare only pairs that truly accept no text in common.

## Optional parameters

A parameter written `{name?}` or `{name:constraint?}` is optional: a path can leave it out. The `?` must be the last character inside the braces.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate orders = RouteTemplate.Parse("/orders/{id:int?}/{tab?}");

Console.WriteLine(orders.Match("/orders/42/items")?["tab"]);        // items
Console.WriteLine(orders.Match("/orders/42")?.ContainsKey("tab"));  // False: tab is left out
Console.WriteLine(orders.Match("/orders/")?.Count);                 // 0
Console.WriteLine(orders.Match("/orders/latest") is null);          // True: latest is not an int
Console.WriteLine(orders.Format(new Dictionary<string, string>())); // /orders
```

- Optional parameters must be trailing: every segment after an optional parameter must also be an optional parameter. `/a/{b?}/{c?}` and `/{id?}` are valid; `/{id?}/details`, `/{a?}/{b}` and `/{a?}/{*rest}` are not.
- A path matches when it has at least the segments before the first optional parameter and no more segments than the template. The segments it has fill the template's positions from left to right, so a path can only leave out optional parameters from the end.
- A segment that is present must satisfy the parameter's constraint. If it does not, the path does not match: the segment is never treated as left out.
- An optional parameter that the path leaves out has no entry in the dictionary, not a `null` or empty value.

## Catch-all parameters

A parameter written `{*name}` is a catch-all: it matches the rest of the path, however many segments are left, including none. The `*` must be the first character inside the braces.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate docs = RouteTemplate.Parse("/docs/{version}/{*page}");

Console.WriteLine(docs.Match("/docs/v2/guides/getting%20started")?["page"]); // guides/getting started
Console.WriteLine(docs.Match("/docs/v2//guides//")?["page"]);                // guides: slashes are normalized
Console.WriteLine(docs.Match("/docs/v2")?.ContainsKey("page"));              // False: nothing is left
Console.WriteLine(docs.Match("/docs") is null);                              // True: version is missing

var values = new Dictionary<string, string> { ["version"] = "v2", ["page"] = "guides/a b" };
Console.WriteLine(docs.Format(values));                                      // /docs/v2/guides/a%20b
```

- A catch-all parameter must be the last segment, so `/files/{*path}/details` is not valid. It cannot follow an optional parameter, as in `/{a?}/{*rest}`, so that the segments it captures always start at the same position, whatever the path.
- A catch-all parameter cannot be optional, as in `{*rest?}`, because it already matches when nothing is left of the path. It cannot have a constraint either, as in `{*rest:int}`.
- A path matches when it has at least the segments before the catch-all parameter, and those segments match as they would without it. `/{*rest}` matches every path, including `/`.
- The captured value is the remaining segments, each decoded, joined with a single `/`. Leading, trailing and repeated slashes are not reproduced, so `/docs/v2/a//b/` captures `a/b`. A `%2F` in a remaining segment behaves exactly like `/`, including that it does not create an empty part: `/docs/v2/a%2Fb` and `/docs/v2/a/b` both capture `a/b`, `/docs/v2/a%2F` captures `a`, and `/docs/v2/%2F` captures nothing.
- When nothing is left of the path, the catch-all parameter has no entry in the dictionary, not an empty value. The path still matches.
- A path does not match when any remaining segment is a [dot segment](#dot-segments), or has one as a part that a `%2F` divides off, as in `/docs/v2/a%2F..%2Fb`.

## Formatting a path

`Format` is the inverse of `Match`: it builds the path a template answers to from a value for each required parameter and for any optional or catch-all parameter that is given.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate file = RouteTemplate.Parse("/my files/{name}");

string path = file.Format(new Dictionary<string, string> { ["name"] = "notes/2026.txt" });
Console.WriteLine(path);                      // /my%20files/notes%2F2026.txt
Console.WriteLine(file.Match(path)?["name"]); // notes/2026.txt
```

- The path starts with `/`, separates segments with a single `/` and has no trailing slash. The root template formats as `/`.
- An [optional parameter](#optional-parameters) is left out when the dictionary has no entry for it, under its name as written or under a key that equals it ignoring case. Its segment and everything after it are not written, so `/products/{id?}` formats as `/products` without an `id`, and `/{id?}` formats as `/`. A [catch-all parameter](#catch-all-parameters) is left out the same way, so `/files/{*path}` formats as `/files` without a `path`, and `/{*path}` formats as `/`.
- Each literal is written as its decoded text and each value as given, both percent-encoded with `Uri.EscapeDataString`. A `/` in a value becomes `%2F`, so it stays inside its segment.
- A catch-all value is split at each `/` instead, and each part is percent-encoded and written as its own segment, so `a/b c` is written as `a/b%20c`.
- The path never has a [dot segment](#dot-segments): no literal is one, and a value that is `.` or `..`, or a catch-all value with such a part, as in `a/../b`, is rejected. Any other value that contains dots, such as `...`, is written as it is.
- Values are keyed by parameter name without regard to case, as everywhere else in a template. Each name is looked up as written in the template first, and only if that finds nothing is the dictionary searched for a key that equals the name ignoring case. Entries for names the template does not use are ignored.
- `Format` throws `ArgumentNullException` for a `null` dictionary. It throws `ArgumentException` when a parameter that is not optional and not a catch-all has no entry, when a parameter has no entry under its name as written and more than one whose key equals it ignoring case, or when its value is `null`, empty, contains a lone surrogate, which cannot be encoded and read back, is `.` or `..`, or does not satisfy the parameter's [constraint](#constraints), because the path would not match. A value that is `.` or `..` is reported as such before the constraint is checked, so a constraint is never asked about one, as in `Match`. For a parameter without a constraint, any other value is allowed, including white space and control characters.
- A catch-all value also cannot have an empty part: `Format` throws `ArgumentException` for one that starts or ends with `/` or contains `//`, because the empty segment would be dropped when the path is matched, and the match would not return the same value. It throws the same for a catch-all value with `.` or `..` as a part.
- An optional or catch-all parameter that has an entry is checked the same way. A `null` or empty value does not leave it out; leave its entry out of the dictionary instead.
- `Format` also throws `ArgumentException` when an optional parameter has a value but an earlier optional parameter was left out, because its segment cannot be written without the earlier one. For `/a/{b?}/{c?}`, a value for `c` without one for `b` is rejected.
- The first parameter in template order that cannot be formatted is the one reported. When a parameter has more than one problem, which one is reported is not specified.

`Match` reads back what `Format` writes: for every template and every dictionary `Format` accepts, matching the formatted path returns exactly one entry for each required parameter, each optional parameter that was given and the catch-all parameter if it was given, with the values that were formatted. In the other direction, formatting the values a match returned gives a path that matches again with the same values, unless the matched path contained a lone surrogate in a parameter's position, because `Match` does not check paths for one and `Format` rejects such a value. A path with a [dot segment](#dot-segments) never matches, so no match returns a value that `Format` rejects for that reason. The new path need not have the same text as the one that was matched, because the output is canonical: the same values always format to the same path, whatever path they were matched from. Every segment is encoded the same way and repeated slashes are dropped. For example, `/my files/{name}` matches `//my files//%41/`, and formatting that match gives `/my%20files/A`.

## Invalid templates

`Parse` throws `ArgumentNullException` for `null`. It throws `ArgumentException` when the text is empty or only white space, or does not start with `/`. It also throws `ArgumentException` for:

- a segment that contains a control character (one for which `char.IsControl` is true), such as a tab, line feed or null character, anywhere in the segment, whether it is a parameter or a literal,
- a segment that contains a lone surrogate: a `char` that is half of a surrogate pair without its other half, which is not valid UTF-16 and cannot be percent-encoded,
- a parameter that contains `*` anywhere but as its first character, such as `{re*st}`, `{rest*}`, `{**rest}` or `{rest:*}`,
- a parameter that contains `?` anywhere but as its last character, such as `{id?:int}`, `{id??}` or `{id:in?t}`,
- a parameter whose name is not valid, such as `{}`, `{ id }`, `{1a}`, `{a-b}` or `{:int}`,
- a parameter that repeats the name of an earlier parameter, ignoring case, as in `/{id}/{ID}` or `/{id:int}/{ID}`,
- a parameter that contains more than one `:`, as in `{id:int:long}`, `{id::int}` or `{id:int:}`,
- a parameter with `:` but no constraint after it, as in `{id:}`,
- a parameter whose constraint is not one of the [built-in constraints](#constraints), such as `{id:slug}` or `{id: int}`; the message lists the supported names. A template mapped on a builder can also name a [custom constraint](#custom-constraints) added to that builder, and `Map` leaves a name of the form a custom constraint can have, such as `slug`, for `Build` to check, unless it is [reserved](#custom-constraints), `string` or `char`, which `Map` and `Parse` both reject with a message that says so,
- a [catch-all parameter](#catch-all-parameters) that is optional, as in `{*rest?}`, or has a constraint, as in `{*rest:int}`,
- a literal segment that starts or ends with white space, as in `/ {id}` (spaces inside a segment, as in `/my files`, are allowed),
- `{` or `}` anywhere except around a whole-segment parameter, as in `item{id}` or `{a}{b}`,
- a literal segment that contains `?` or `#`, which cannot appear in the path of a URI,
- a literal segment that is a [dot segment](#dot-segments), as written or encoded, as in `/a/../b`, `/a/./b` or `/a/%2E%2E/b`,
- a literal or a parameter that is not optional, including a catch-all parameter, after an optional parameter, as in `/{id?}/details` or `/{id?}/{*rest}`; the message names the optional parameter, and
- any segment after a catch-all parameter, as in `/files/{*path}/details`; the message names the catch-all parameter.

The rules for names and constraints apply to a parameter with a first `*` and a final `?` removed, so `{?}` and `{*}` have an invalid name and `{id:?}` has no constraint after `:`.

`Parse` reports the first invalid segment, reading left to right. A rule that involves two segments, such as a repeated name or a segment after a catch-all parameter, is broken by the later one. When a segment has more than one problem, which one is reported is not specified. The exception message shows a control character or a lone surrogate as a `\uXXXX` escape, such as `\u0009`, rather than printing it.

Apart from the dot segment rule, these checks apply to the text as written, before decoding. To match one of these characters literally, percent-encode it: `%09` for a tab, `%0A` for a line feed, `%00` for a null character (and the UTF-8 bytes of any other control character, such as `%C2%85` for U+0085), `%7B` for `{`, `%7D` for `}`, `%3F` for `?`, `%23` for `#` and `%20` for a space at the start or end of a segment. For example, `/what%3F` matches the path `/what%3F`, `/a%09b` matches a path whose segment is `a`, a tab and `b`, and `/%7Bid%7D` is a literal that matches the path `/{id}`. A lone surrogate has no encoded form: a surrogate has no UTF-8 bytes of its own, and the three bytes it would encode to, such as `%ED%A0%80`, are not valid UTF-8, so that text decodes to itself and the literal is that percent text. Encoding does not help with a dot segment either: `%2E%2E` is `..` once decoded, and the rule applies to the decoded text.

## The route table

A route is a sealed record that derives from `AppRoute`. A `RouteTableBuilder` maps a template to each route type, with a function that creates the route from the values the template captured and the query of the URI, and `Build` turns the mappings into a `RouteTable`. `Match` matches the path of a URI and returns a `RouteMatchResult`, and [`Format`](#formatting-a-route) writes a route back as a path and query.

```csharp
using AdamE.AppNav;
using AdamE.AppNav.Routing;

RouteTable table = new RouteTableBuilder()
    .Map("/settings", _ => new SettingsRoute())
    .Map("/products/{id:int}/{tab?}", c => new ProductRoute(
        c.Path<int>("id"),
        c.TryGetPath("tab", out string? tab) ? tab : null,
        c.TryGetQuery("sort", out string? sort) ? sort : null))
    .Build();

RouteMatchResult result = table.Match(new Uri("myapp:///products/42/reviews?sort=newest"));
if (result.IsSuccess)
    Console.WriteLine(result.Route); // ProductRoute { Id = 42, Tab = reviews, Sort = newest }

Console.WriteLine(table.Match(new Uri("/orders", UriKind.Relative)).IsSuccess); // False

public sealed record SettingsRoute : AppRoute;

public sealed record ProductRoute(int Id, string? Tab, string? Sort) : AppRoute;
```

> **Note: write custom-scheme links with three slashes.** In `myapp://products/42`, `System.Uri` reads `products` as the host, which the table ignores, so the path is `/42`. That path does not reach the products route, but a `/{slug}` or `/{*rest}` route matches it. Likewise `myapp://open?tab=1` is read as the path `/`, which matches a root route. Write such links as `myapp:///products/42`, which has no host and the path `/products/42`.

A route carries application identity only: the values that name its destination, not pages, view models, services, callbacks or anything about how a request arrived. Two routes of the same type with equal members are the same destination, so give a route only members with value equality, such as strings, numbers, enums and other records; arrays and lists compare by reference. A record's `ToString` prints every member.

### What is read from a URI

- Only the path decides which route matches. The scheme, user information, host, port and fragment are ignored, and the query is read only by a create function that asks for it, as described in [Query values](#query-values).
- The table checks neither the scheme nor the host, so `https://other.example/account` and `file:///account` match exactly as the app's own links do. Deciding which sources to accept belongs to the code that receives the link, before or after matching.
- For an absolute URI, the path is `Uri.AbsolutePath`, so `https://example.com/settings`, `myapp:///settings` and `myapp:/settings` all have the path `/settings`. A URI whose path is empty, such as `mailto:a@b.c`, is read as the root path, which the template `/` matches.
- The path of an absolute URI is the one `System.Uri` produces. It decodes percent-encoded unreserved characters (`%41` becomes `A`), percent-encodes spaces and non-ASCII characters, and keeps repeated slashes. In a URI with an authority, even an empty one as in `myapp:///a/../b`, it also removes [dot segments](#dot-segments), so that path is `/b`. In a URI with no authority at all, such as `app:/a/../b`, it keeps them, so that path does not match. `app:x/y` has the path `x/y`, which matches `/x/y`.
- For some schemes `System.Uri` has no query, so a `?` is part of the path: `ftp://host/a?x=1` has the path `/a%3Fx=1`, which a `/{slug}` route captures as `a?x=1`. The schemes are `ftp`, `news`, `nntp`, `gopher`, `telnet`, `uuid` and `vsmacros`, and a UNC path written with backslashes, such as `\\server\share\a?x=1`.
- For a relative URI, such as `new Uri("/settings?tab=1", UriKind.Relative)`, `System.Uri` parses nothing, so the path is its `OriginalString` up to the first `?` or `#`, whichever comes first. Nothing else is done to it: it is not trimmed, and the leading `/` is optional, as for `RouteTemplate.Match`. An empty relative URI, or one that is only a query or fragment, has an empty path, which matches the root template `/`.
- Build app-relative links with `UriKind.Relative` or `UriKind.RelativeOrAbsolute`. `new Uri("/settings?tab=1")`, with no `UriKind`, is a `file:` URI on Unix-like platforms and throws on Windows; the table reads such a URI as relative, from the text the caller wrote, so it matches `/settings`. This applies only when the text starts with `/`: `new Uri(" /a?x=1")`, with leading white space, is a file URI read as absolute, with the `?` in its path and no query values.

`System.Uri` drops `%20` sequences at the very end of a URI that contains, anywhere including the host, a non-ASCII character, a percent-encoded byte from `%80` to `%FF`, or a percent-encoded unreserved character such as `%41`: the path of `https://example.com/%C3%A9%20` is `/%C3%A9`, and the query of `https://example.com/%41?v=%20` is `?v=`. Any character after them, even a lone `#` or `&`, keeps them. The table does not work around this.

### Query values

A create function reads the query of the URI with `TryGetQuery(name, out value)` and `GetQueryValues(name)`. The query is never used to choose a route: `/settings?tab=/products/1` matches `/settings`, and a relative URI that is only a query, such as `?tab=1`, matches the root template `/` and keeps its query.

- For an absolute URI, the query is `Uri.Query`, which excludes the fragment except as noted below. For a relative URI, and for an implicit file URI whose text starts with `/`, which the table reads as relative, it is the `OriginalString` from the first `?` that comes before any `#`, up to the next `#`, so `/a?tab=1#x` has the query `?tab=1` and `/a#x?tab=1` has none.
- One leading `?` is removed. Pairs are separated by `&` only, and empty pairs are skipped. The first `=` separates the name from the value, so a second `?` or `=` is part of a name or value. A name without `=` has an empty value, and an empty name, as in `?=x`, is kept under the name `""`.
- Names and values are decoded after splitting, so `%26` and `%3D` are data. `+` is a space, as in form encoding, although in a path it is a literal `+`: `/files/a+b?v=a+b` captures `a+b` from the path and gives `a b` for `v`. Write `%2B` for a literal `+` in a query. Percent sequences are decoded once, so `%2541` is `%41`, and a sequence that is not valid, or not valid UTF-8, is kept as written.
- Names are compared ordinally without regard to case, as path parameter names are.
- `TryGetQuery` gives the first value when a name repeats, so `?tab=a&tab=b` gives `a`. That is what a browser's `URLSearchParams.get` and most web frameworks return, so the app and a web page handling the same link agree on which value they read; code that needs every value uses `GetQueryValues`. Names that differ only by case are one name here, unlike in a browser, so `?Tab=x&tab=y` gives `x`. `TryGetQuery` gives the empty string for a name with no value, as in `?flag` or `?flag=`. It returns `false` with `null` when the query does not have the name, and never throws for a missing value.
- `GetQueryValues` gives every value of a name in order, including repeated and empty values, and an empty list when there are none. The list cannot be changed.
- `TryGetQuery<T>` and `GetQueryValues<T>` read values as numbers, `bool`, `Guid`, a registered enum or a type with a codec, as described in [Typed query values](#typed-query-values).

The query is read and parsed only when a create function first asks for a value, at most once per match, so a URI that matches nothing, and a route that reads no query values, pay nothing for the query. Nothing limits the size of a query, and the cost of reading it grows linearly with its length. `System.Uri` itself accepts a query of a million characters, absolute or relative, so an app that receives links from outside should bound their length before calling `Match`; nothing else does. Like the path, the query can carry a secret, so it never appears in a diagnostic.

For an absolute URI the query is the one `System.Uri` reports, and the values are decoded from it, which is not always what the link's author wrote:

- `System.Uri` drops `%20` sequences at the very end of some URIs, [as described above](#what-is-read-from-a-uri), so the last value of a query can lose trailing spaces.
- `System.Uri` trims spaces, tabs and line breaks from the start and end of an absolute URI, so `https://example.com/a?v=a ` gives `a`, while the relative `/a?v=a ` keeps the space.
- For the schemes [listed above](#what-is-read-from-a-uri) that have no query, such as `ftp://host/a?x=1`, there are no query values; the `?` and what follows it are part of the path.
- A `Uri` created with `UriCreationOptions.DangerousDisablePathAndQueryCanonicalization` is read exactly as `System.Uri` exposes it, and the table does not support it specially: its `Query` can include the fragment, as `?x=1#f` for `https://example.com/a?x=1#f`, and its `AbsolutePath` can include a `#`.

A relative URI is read from its original text, so its values are decoded from exactly what was written.

### Which route wins

Templates are tried by precedence, and the first one that matches the path wins; later ones are not tried. Precedence comes from the templates alone, so the more specific template wins whatever order the routes were mapped in, and `Build` rejects templates that rank equally and could both match a path, as described [below](#templates-that-cannot-be-told-apart). Two templates are compared segment by segment from the left, and the first position where their ranks differ decides: the higher rank is tried first. A template that has no segment at a position, because it has ended, has the rank *ended* there. The ranks, highest first:

1. A literal, such as `settings`.
2. A required parameter with a constraint, such as `{id:int}`.
3. Ended.
4. An optional parameter with a constraint, such as `{id:int?}`.
5. A required parameter without a constraint, such as `{name}`.
6. An optional parameter without a constraint, such as `{name?}`.
7. A catch-all parameter, such as `{*rest}`.

Which constraint a parameter has, which text a literal has, and parameter names do not affect the rank. So:

- A literal beats any parameter: for `/products/new`, `/products/new` wins over `/products/{id}`.
- A constrained parameter beats an unconstrained one, even when it is optional and the other required: for `/products/42`, `/products/{id:int?}` wins over `/products/{name}`.
- Otherwise a required parameter beats an optional one: for `/products/42`, `/products/{id}` wins over `/products/{id?}`.
- Anything beats a catch-all: for `/files/a`, `/files/{name}` wins over `/files/{*path}`.
- An exact template beats one that continues with an optional or catch-all parameter: for `/a`, `/a` wins over `/a/{b?}` and `/a/{*rest}`. Exactly where *ended* sits among the ranks above the optional kinds makes no difference, because a template that has ended and one that needs another segment never match the same path.
- The leftmost difference in rank decides when templates cross: for `/a/b`, `/a/{y}` wins over `/{x}/b`.

The leftmost rule decides even when a later segment is more specific. For `/a/5/edit`, `/a/{id:int}/{*rest}` is tried before `/a/{name}/edit`, because their ranks first differ at the second segment, where a constrained parameter outranks an unconstrained one. If that is not what you want, give the edit route the same constraint, as in `/a/{id:int}/edit`, so that the two templates first differ at the literal, which wins; or do not let a catch-all share a prefix with more specific routes.

`Build` rejects two templates that rank equally at every position and can match the same path, whether they are [the same template](#the-same-template-twice) or [cannot otherwise be told apart](#templates-that-cannot-be-told-apart), so the order routes are mapped in never affects which route wins, unless a [declaration that two constraints are disjoint](#declaring-constraints-disjoint) is false. In this version, the same route type can be mapped to more than one template, though only one of those mappings can say how to [write the route](#formatting-a-route). `Definitions` lists the table's mappings in the order they were made, not the order they are tried in.

#### The same template twice

`Build` throws `InvalidOperationException` when two mappings have the same template, because the one mapped second could never be matched. The message names both templates and both route types, the earlier mapping first. When two different route types have the same name, the message writes both in full: with their namespace where they have one, or, when that still leaves them alike, as for generic types whose arguments share a name, as `Type.ToString()` writes them, and with the name of their assembly when even that leaves them alike, or, when that still leaves them alike, as their assembly-qualified names. When there are several such pairs, the first mapping, in the order the routes were mapped, whose template repeats an earlier one is reported, with the mapping it repeats.

Two templates are the same when, once parsed, they have the same number of segments and, at every position, the same kind of segment (a literal, a required, optional or catch-all parameter), the same decoded text for a literal, compared [ordinally and case-sensitively](#how-text-is-compared), and the same constraint, or none, for a parameter. Parameter names do not count. So:

- `/a/{x}` and `/a/{y}` are the same, and so are `/a/{id:int}` and `/a/{n:INT}`.
- `/caf%C3%A9` and `/café` are the same, and so are `/a//b/` and `/a/b`.
- `/Users` and `/users` are not the same, nor are `/a/{id}` and `/a/{id?}`.
- `/a/{id:int}` and `/a/{id:long}` are not the same, though `Build` rejects them for the reason in the next section.

This applies even when both mappings are for the same route type.

#### Templates that cannot be told apart

`Build` also throws `InvalidOperationException` for two templates that are not the same but rank equally at every position and both match some path, such as `/products/{id:int}` and `/products/{n:long}`, which both match `/products/42`. For such a path the one mapped first would win, so the order of the mappings would decide the route. Two templates rank equally when they have the same number of segments and the same [rank](#which-route-wins) at every position, and then some path matches both exactly when, at every required position:

- two literals have the same decoded text, compared [ordinally and case-sensitively](#how-text-is-compared);
- two parameters both have no constraint; or
- two parameters have constraints that some text satisfies both. A constraint overlaps itself. Of the [built-in constraints](#constraints), the eleven number constraints, `int`, `long`, `decimal`, `sbyte`, `byte`, `short`, `ushort`, `uint`, `ulong`, `float` and `double`, all accept `0`, so any two of them overlap, as `/p/{n:byte}` and `/p/{n:double}` do; and `bool` accepts `true`, which is also letters for `alpha`. No other pair overlaps: `alpha` accepts no number, `bool` accepts no number, and `guid` overlaps only itself. A [custom constraint](#custom-constraints) is taken to overlap every constraint, itself included, because which text it accepts cannot be known; only a pair [declared disjoint](#declaring-constraints-disjoint) is taken to accept no text in common, and a constraint can never be declared disjoint from itself.

Because a guid has hyphens between its hex digits, which no number or letters can have, `guid` overlaps no other built-in constraint, so `{id:int}` and `{key:guid}` can share a position: `/orders/{id:int}` and `/orders/{key:guid}` send `/orders/42` and `/orders/6f9619ff-8b86-d011-b42d-00cf4fc964ff` to different routes.

Optional parameters and a catch-all never keep two templates apart, whatever their constraints, because a path that leaves them out matches both: `/a/{x:int?}` and `/a/{y:alpha?}` both match `/a`. To have `/a`, a number and a word go to different routes, map `/a`, `/a/{x:int}` and `/a/{y:alpha}`. Keeping one of them optional also works, as in `/a/{x:int?}` and `/a/{y:alpha}`, because the two templates then rank differently and precedence decides.

The message names both templates and both route types, the earlier mapping first, and one path that both match, such as `/products/0` for the example above. When two different route types have the same name, the message writes both in full: with their namespace where they have one, or, when that still leaves them alike, as for generic types whose arguments share a name, as `Type.ToString()` writes them, and with the name of their assembly when even that leaves them alike, or, when that still leaves them alike, as their assembly-qualified names. The path has only the required segments, with each literal as `Format` writes it, `x` for a parameter without a constraint, and a value both constraints accept. For a custom constraint, that value is looked for among the texts `0`, `a`, `true` and `00000000-0000-0000-0000-000000000000`, which `Build` can pass to its function. When the constraints at some required position accept none of them together, no path is known, and the message names none: it says that the templates could both match a path, names the constraints at the first such position, as in "the constraints 'slug' and 'int' at segment 2 are not known to accept no text in common", and says that a custom constraint is treated as overlapping every constraint, and that only a pair declared with `DeclareDisjointConstraints` is taken to accept no text in common. The message then says what to do. In general: keep the templates apart at a required position, with a different literal or with constraints there that no text satisfies together, make them rank differently, or remove one. When a custom constraint is involved at the positions the advice is about, the required positions or, when only optional parameters tell the templates apart, the first position after them, the advice does not suggest constraints there, since a custom constraint keeps no templates apart unless declared disjoint, and the message says how a custom constraint is treated, whether or not a path is known. When, at one of those positions, the templates have two different constraints, one of them custom, that are not declared disjoint and, at a required position, accept no sample together, the advice also offers the declaration, naming the first such position and the call, as in `DeclareDisjointConstraints("slug", "int")`, for the case that no text satisfies both. When only optional parameters tell the templates apart, the advice is about the first position after the required ones alone: mapped as its own template, each alternative has that position required, so it decides whether they are kept apart, and a pair at a later position is never offered. `Build` asks no function about the samples there, so a pair at that position is offered without that check, and `Build` checks the declaration once it is made. It never offers it for a constraint with itself, or for a pair that a sample shows to overlap. When only optional parameters tell the templates apart and the two constraints at that first position are already declared disjoint, the message leaves out how custom constraints are treated, unless no path is known, and says to map the shorter path and each alternative as its own template with a required parameter, where those declarations keep the alternatives apart, or to keep them apart with a different literal, or to remove one. With built-in constraints alone, the message is as described here. When the templates have the same literals and constraints at every required position, so that only optional parameters tell them apart, it says instead to map the shorter path and each alternative as its own template with a required parameter, giving the alternatives constraints that no text satisfies together, or to remove one. When there are several such pairs, the first mapping, in the order the routes were mapped, that conflicts with an earlier one is reported, with the earliest mapping it conflicts with. The same template twice is looked for first, across the whole table, so it is reported even when two mappings that conflict in this way come earlier: with `/p/{a:int}`, `/p/{b:long}` and `/p/{c:int}` mapped in that order, `Build` reports the first and third as the same template.

Still allowed:

- the same route type on several templates;
- templates that rank differently and match the same path, because precedence decides between them: for `/a/5`, `/a/{id:int}` wins over `/a/{name}`;
- templates that rank equally but cannot match the same path, such as `/a/{id:int}` and `/a/{w:alpha}`, or `/a` and `/b`.

### Creating the route

When a template matches, its create function is called once with a `RouteMatchContext`, which is valid only during that call, so do not store it:

- `Path(name)` returns a captured value, decoded, and throws `KeyNotFoundException` when the template has no such parameter or it is an [optional](#optional-parameters) or [catch-all](#catch-all-parameters) parameter that the path left out. Use it for a required parameter.
- `TryGetPath(name, out value)` returns `true` with the value when the path has one, and `false` with `null` in the cases where `Path` throws. Use it for an optional or catch-all parameter, as in `c.TryGetPath("tab", out string? tab) ? tab : null`.
- `Path<T>(name)` and `TryGetPath<T>(name, out value)` read a value as a number, `bool`, `Guid`, a registered enum or a type with a codec, as described in [Typed values](#typed-values).
- Names are compared without regard to case.
- `PathValues` is the dictionary `RouteTemplate.Match` returned. Treat it as read-only.
- `TryGetQuery(name, out value)` and `GetQueryValues(name)` read the query, as described in [Query values](#query-values), and `TryGetQuery<T>(name, out value)` and `GetQueryValues<T>(name)` read query values as typed values, as described in [Typed query values](#typed-query-values).
- `SourceUri` is the URI that was matched.

An exception the create function throws propagates out of `Match` unchanged, and no later template is tried, except a `RouteValueException`, which rejects a value as described [below](#rejecting-a-value). A create function that returns `null` makes `Match` throw `InvalidOperationException`.

### Typed values

`Path<T>(name)` and `TryGetPath<T>(name, out value)` read a captured value as one of these types: `string`, `bool`, `Guid`, `decimal`, `double`, `float`, the integer types `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long` and `ulong`, any enum registered on the builder as described in [Enum values](#enum-values), any type of the app's own with a codec registered on the builder as described in [Custom value types](#custom-value-types), and the nullable form of each of them that is a value type, such as `int?`. A `string` is the captured text unchanged. The others, apart from enums and types with a codec, follow these rules, which are the same in every culture and apply to the text exactly as captured, without trimming:

- Every number type, `bool` and `Guid`: a value converts exactly when it satisfies the [built-in constraint](#constraints) of the same name, by the same rule, so `Path<int>` accepts exactly the text that `{id:int}` accepts, `Path<byte>` exactly the text that `{n:byte}` accepts, and `Path<Guid>` only the hyphenated form that `{id:guid}` accepts. The rules for the types other than `int`, `long`, `decimal`, `bool` and `Guid` follow.
- The other integer types follow the rule of `int` within their own range: an optional `+` or `-` followed by one or more ASCII digits, leading zeros allowed, whose value the type can hold. `300` is not a `byte` and `-1` is not a `uint`, but `-0` and `+5` are, as they are for `int`.
- `float` and `double` accept text of the same shape as `decimal`: an optional `+` or `-` followed by ASCII digits with at most one `.`, and at least one digit, so `.5`, `5.` and `-.5` are numbers. There is no exponent, `NaN` or `Infinity`, so `1e5` is not a `double`. They do not share the range of `decimal`: a whole number of 30 digits is a `float` and a `double` but not a `decimal`. The value is the nearest one the type can hold: digits beyond its precision round, at any length, so `0.1000000000000000055511151231257827021181583404541015625000001` is the `double` 0.1. A value that, after rounding to the nearest value of the type, is beyond its largest finite value is rejected, not read as infinity, so a whole number of 40 digits is a `double` but not a `float`, and one of 400 digits is neither. `-0` is a negative zero, equal to `0`, for which `double.IsNegative` is `true`, and so is a negative value too small to represent, such as `-0.` followed by 400 zeros and a `1`.

There is no typed read for `alpha`, which is a constraint on text, not a type. A catch-all value converts from its whole text, slashes included.

A `decimal` keeps what the text says about scale and sign: `5.00` and `5` are equal values that print as `5.00` and `5`, and `-0` is a negative zero, equal to `0` and printed as `0`, for which `decimal.IsNegative` is `true`. Digits beyond the precision of `decimal` round, so `1.00000000000000000000000000001` gives `1.0000000000000000000000000000`. Different texts can therefore give equal values, as they can for the other types: `+007` and `7` are both the `int` 7.

A nullable type reads by exactly the rule of its underlying type: the same text converts, to the same value, and the same text is rejected, with the same message. It lets an optional parameter be read into a nullable variable, which is `null` when the path leaves the parameter out:

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/list/{page?}", c =>
    {
        c.TryGetPath("page", out int? page);
        return new ListRoute(page);
    })
    .Build();

// /list gives ListRoute(null), /list/3 gives ListRoute(3), and /list/x fails the match.

public sealed record ListRoute(int? Page) : AppRoute;
```

- `Path<T>(name)` throws `KeyNotFoundException` when the path has no value for the name, as `Path(name)` does. Use it for a required parameter.
- `TryGetPath<T>(name, out value)` returns `false` with the default value of `T`, which is `null` for a nullable type, when the path has no value for the name. Use it for an optional or catch-all parameter.
- `Path<int?>(name)` behaves exactly as `Path<int>(name)`: it never returns `null` and throws `KeyNotFoundException` for an absent value, so read a value that may be absent with `TryGetPath`.

> **Note: a value that is present but not valid is not a missing value.** `TryGetPath<T>` does not return `false` for text that does not convert, as most `Try` methods would. Absent returns `false`; present but not valid rejects the match: both methods throw a `RouteValueException`, which fails the match as described in [Rejecting a value](#rejecting-a-value), so a bad link is never treated as a link without the value. For a lenient fallback, read the string and use `int.TryParse`, or the parse method of the type you need:
>
> ```csharp
> int page = c.TryGetPath("page", out string? text)
>     && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed) ? parsed : 1;
> ```

With the constraint of the same name in the template, such as `{id:int}` read with `Path<int>` or `{n:byte}` read with `Path<byte>`, the conversion cannot fail, because a value that does not satisfy the constraint never reaches the create function. So use the constraint named after the type you read. A different constraint does not give that guarantee, though it still works as follows. A narrower one only keeps values out: `{n:byte}` read with `Path<int>` and `{n:uint}` read with `Path<long>` always convert, and a value beyond the narrower range does not match the template at all rather than reach the read. A wider one, or one of another kind, lets through values that do not convert, and each then fails the match: `{id:long}` read with `Path<int>` lets `/3000000000` through, `{n:int}` read with `Path<byte>` lets `/300` through, `{n:decimal}` read with `Path<ulong>` lets `/1.5` and `/-1` through, and `{id:int}` read with `Path<bool>` lets `/1` through. A constraint can also be neither narrower nor wider, and then does both: `{n:long}` read with `Path<ulong>` makes a URI with a value above 9223372036854775807 fail to match the template at all, and lets `/-1` through to fail the read. Every value that `{n:decimal}` accepts converts to `float` and `double`, rounded, since their range is larger. Without a constraint, a value that does not convert fails the match for that URI, and no other template is tried, even one that would also match the path. The exception's `ValueName` is the name the create function passed, and its message names the parameter and the type, as in `The path value of 'id' is not a valid int.`, but never quotes the value, which can carry a secret from the URI.

Any other type, such as `Int128`, `Half` or `DateTime`, or its nullable form, is read only once it has a codec, as described in [Custom value types](#custom-value-types), and an enum only once it is registered. A `char` cannot have a codec: it is one character of a string, so read it as a string. Using a type that has none throws `InvalidOperationException` whether or not the path has the value, and it propagates out of `Match`, since it is a mistake in the app rather than in the URI. The message names the method that registers a codec for the type, `AddValueCodec`, or for an enum both `AddEnumValueCodec` and `AddValueCodec`. A collection, such as `List<int>`, and a `char` cannot have a codec, and their message says to read the value as a string instead.

#### Enum values

An enum is read once it is registered on the builder with `AddEnumValueCodec<TEnum>()`:

```csharp
RouteTable table = new RouteTableBuilder()
    .AddEnumValueCodec<OrderStatus>()
    .Map("/orders/{status}", c => new OrdersRoute(c.Path<OrderStatus>("status")))
    .Build();

Console.WriteLine(table.Match(new Uri("/orders/shipped", UriKind.Relative)).Route); // OrdersRoute { Status = Shipped }

RouteMatchResult result = table.Match(new Uri("/orders/2", UriKind.Relative));
Console.WriteLine(result.IsSuccess);                     // False
Console.WriteLine(result.Diagnostics[0].Data["reason"]); // The path value of 'status' is not a valid OrderStatus.

public enum OrderStatus { Pending, InProgress, Shipped }

public sealed record OrdersRoute(OrderStatus Status) : AppRoute;
```

Once registered, `Path<T>`, `TryGetPath<T>` and `TryGetQuery<T>` read the enum and its nullable form, and `GetQueryValues<T>` reads the enum. As for the other types, `GetQueryValues<T>` does not read the nullable form, registered or not. A value converts by these rules:

- The text must be the name of a member, and gives the value of that member. Case is ignored, ordinally, so `shipped` and `SHIPPED` are `Shipped` in every culture, and the Turkish dotted and dotless i match neither `i` nor `I`.
- Only names convert. A number, such as `2`, `-1` or `0x1`, does not: it would tie links to the order the members are declared in, so adding or moving a member would change what an old link means. A list of names, such as `Pending,Shipped`, does not convert either. Nothing is trimmed, so ` Shipped` is not valid, and the empty value of a query, as in `?status=`, is not valid.
- Two names for the same value both convert, to that value.
- An enum with `[Flags]` can be registered, but only the name of a single member converts, never a combination such as `Read,Write`.
- An enum with no members can be registered, and then no text converts.
- A text that does not convert rejects the match with a `RouteValueException`, as for the other types, and no later template is tried. The message names the enum by its simple name, even for an enum declared inside another type, and never quotes the value.

The names of a registered enum are part of the links of the app: renaming a member changes which links work, so rename one only as you would change a route template.

Registering applies to the whole table, so it can come before or after the `Map` calls whose create functions read the enum. Registering the same enum again does nothing. A table that is already built does not change; call `Build` again for a table that includes a later registration. `AddEnumValueCodec` throws `ArgumentException` for an enum with two members whose names differ only by case, such as `A` and `a`, since a text that named one would name both, and for an enum that already has a codec from `AddValueCodec`, since a type has one codec. Reading an enum that is not registered throws `InvalidOperationException` whose message names `AddEnumValueCodec`, whether or not the value is present, and it propagates out of `Match`.

There is no constraint named after enums, so a template with `{status:enum}` is not valid unless the builder adds a [custom constraint](#custom-constraints) of that name, and `Build` throws without one, and a template cannot keep a name that is not a member away from its create function; that name fails the match instead. For other spellings, such as `in-progress`, give the enum a codec of its own, as [Custom value types](#custom-value-types) shows.

#### Custom value types

A type of the app's own, such as an id, is read and written once it has a codec, registered on the builder with `AddValueCodec<TValue>(tryParse, format)`: a parser that reads a text as a value, and a writer that writes a value as its text.

```csharp
using System.Globalization;
using AdamE.AppNav;
using AdamE.AppNav.Routing;

RouteTable table = new RouteTableBuilder()
    .AddValueCodec<ProductId>(
        (string text, out ProductId id) => ProductId.TryParse(text, out id),
        id => id.ToString())
    .Map("/products/{id}",
        c => new ProductRoute(c.Path<ProductId>("id")),
        format => format.Path("id", r => r.Id))
    .Build();

Console.WriteLine(table.Format(new ProductRoute(new ProductId(42))));            // /products/p42
Console.WriteLine(table.Match(new Uri("/products/p42", UriKind.Relative)).Route); // ProductRoute { Id = p42 }
Console.WriteLine(table.Match(new Uri("/products/p042", UriKind.Relative)).Route); // ProductRoute { Id = p42 }

RouteMatchResult result = table.Match(new Uri("/products/42", UriKind.Relative));
Console.WriteLine(result.Diagnostics[0].Data["reason"]); // The path value of 'id' is not a valid ProductId.

public readonly record struct ProductId(int Value)
{
    public static bool TryParse(string text, out ProductId id)
    {
        int value = 0;
        bool parsed = text.StartsWith('p')
            && int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out value);
        id = new ProductId(value);
        return parsed;
    }

    public override string ToString() => "p" + Value.ToString(CultureInfo.InvariantCulture);
}

public sealed record ProductRoute(ProductId Id) : AppRoute;
```

Once registered, `Path<T>`, `TryGetPath<T>`, `TryGetQuery<T>` and `GetQueryValues<T>` read the type, and a format function writes it with `Path`, `Query` and `QueryValues`, as [Formatting a route](#formatting-a-route) describes. The two functions are a contract that the table relies on but can check only in part:

- `tryParse` returns `true` with the value for a text that is a valid value, and `false` for one that is not. It must accept every text that `format` writes. It may accept several spellings of one value, as the parser of `ProductId` accepts `p042` for `p42`: each spelling matches, and `Format` writes the one that `format` gives.
- `format` returns the text of a value, and should give equal values the same text, so that one value has one link. It returns `null` for a value that has no text, and `Format` then throws `ArgumentException`, as it does for a `double` that is NaN.
- Both must be safe to call from several threads at once, since a built table is, and should have no side effects. Either can be called more than once for one value: `Format` reads back each text that `format` writes with `tryParse`, as [What a round trip promises](#what-a-round-trip-promises) describes.
- That check only asks whether `tryParse` accepts the text. It does not catch a `format` that gives two different values the same text, nor a `tryParse` that reads a written text back as a different value: those round trips are the codec's responsibility.
- A path value must also satisfy the constraint of its parameter, if the template gives one, as for any type.
- Returning `false` and throwing are different. `false` means the text is not a valid value: the read throws `RouteValueException`, which fails the match as it does for a built-in type, and no later template is tried. The message names the type by its simple name, as in `The path value of 'id' is not a valid ProductId.`, and never quotes the value. An exception that either function throws is a mistake in the app, and propagates unchanged, out of `Match` or `Format`, with one exception: a `RouteValueException` that `tryParse` throws during a read fails the match as a value that is not valid, with that exception's own `ValueName`, as one thrown by a create function does; out of `Format` and `FormatUri`, it propagates like any other. Returning `true` with a `null` value is a mistake in the codec too: the read, or the check of a written text, throws `InvalidOperationException`, whose message names the type.
- The text given to `tryParse` is decoded and never `null`. It can be empty, for a query value written as `?id=` or `?id`, and the parser decides whether that is valid. A text that `format` writes can be empty too: as a query value it is written as `id=`, and as a path value it is rejected, as an empty path value always is. A text with a lone surrogate is rejected, as it is for a `string`.

The parser's value is declared `out TValue?`, which for a struct is the struct itself, so the usual shapes convert without a warning. A method with the usual `TryParse` shape can be passed as it is, as in `AddValueCodec<ProductId>(ProductId.TryParse, id => id.ToString())`, whether its parameter is `out T`, `out T?`, `[NotNullWhen(true)] out T?` or `[MaybeNullWhen(false)] out T`, and so can a method of the base class library such as `Version.TryParse`. The parameter types of a lambda can be left out, as in `(text, out id) => ProductId.TryParse(text, out id)`. If you give them for a reference type, write the value as nullable, as in `(string text, out Slug? slug) => Slug.TryParse(text, out slug)`: a lambda that declares `out Slug` instead makes the compiler warn that its nullability does not match the delegate, and that it may assign `null` to it when it calls a `TryParse` whose parameter is `out Slug?`. Write `out Slug?`, or leave the types out. A method with another argument, such as the `TryParse(string, IFormatProvider, out T)` of `IParsable<T>`, needs a lambda. Do not pass a method of the base class library that reads by the current culture, such as `DateOnly.TryParse(string, out DateOnly)` or `TimeSpan.TryParse(string, out TimeSpan)`, which would make a link mean different things on different devices: write a lambda that passes `CultureInfo.InvariantCulture` and an exact format, as in `(string text, out DateOnly date) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)`, and write the value by the same format, as in `date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)`. A reference type is read and written in the same way:

```csharp
using System.Diagnostics.CodeAnalysis;
using AdamE.AppNav;
using AdamE.AppNav.Routing;

RouteTable table = new RouteTableBuilder()
    .AddValueCodec<Slug>(Slug.TryParse, slug => slug.Value)
    .Map("/articles/{slug}",
        c => new ArticleRoute(c.Path<Slug>("slug")),
        format => format.Path("slug", r => r.Slug))
    .Build();

Console.WriteLine(table.Match(new Uri("/articles/hello-world", UriKind.Relative)).Route);
// ArticleRoute { Slug = Slug { Value = hello-world } }
Console.WriteLine(table.Match(new Uri("/articles/Hello", UriKind.Relative)).IsSuccess); // False

public sealed record Slug(string Value)
{
    public static bool TryParse(string text, [NotNullWhen(true)] out Slug? slug)
    {
        bool valid = text.Length > 0 && text.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
        slug = valid ? new Slug(text) : null;
        return valid;
    }
}

public sealed record ArticleRoute(Slug Slug) : AppRoute;
```

`AddValueCodec` throws `ArgumentNullException` for a `null` function, and `ArgumentException`, whose `ParamName` is `TValue`, for a type that cannot have a codec. In both cases the builder is not changed.

- The built-in types, `string`, `bool`, `Guid`, `decimal`, `double`, `float` and the eight integer types, and their nullable forms, cannot be given a codec. Their text is fixed by the library so that it always agrees with [constraints](#constraints) such as `{id:int}`; a codec that changed it would make `{id:int}` and `Path<int>` disagree. For another text, use a type of your own, as `ProductId` above does for an `int`.
- A nullable type, such as `ProductId?`, cannot be given a codec: register `ProductId`, and its nullable form is then read and written by the same codec.
- A collection, any type that implements `IEnumerable` other than `string`, cannot be given a codec, because a codec reads and writes one value, and neither can its nullable form. Write a list of values for one name with `QueryValues` and read it with `GetQueryValues<T>`, or wrap the collection in a type of your own.
- A `char`, or its nullable form, cannot be given a codec: it is one character of a string, and `QueryValues` rejects it, since a string given to it by mistake makes `char` the element type. Read and write it as a string, or use a type of your own.
- A type has one codec. Registering a second one for the same type throws, whether the first came from `AddValueCodec` or from `AddEnumValueCodec`, and so does `AddEnumValueCodec` for an enum that already has a codec from `AddValueCodec`. Registering the same enum twice with `AddEnumValueCodec` still does nothing.
- Any other type can have one: a class, a struct, a record, an interface, an abstract class or an enum.

A codec is found by exactly the type that is read or written, never by a base type or an interface. A codec for an interface `IShape` reads `Path<IShape>`, but not `Path<Circle>` for a class `Circle` that implements it, and a format function whose value function returns a `Circle` needs a codec for `Circle`, or a cast to `IShape`. No template constraint is named after a type with a codec, so a template with `{id:productid}` is not valid unless the builder adds a [custom constraint](#custom-constraints) of that name, and a text that the parser rejects fails the match instead. Registering applies to the whole table, before or after the `Map` calls, as for enums, and a table that is already built does not change.

A codec of its own is how an enum gets spellings that are not the names of its members:

```csharp
using AdamE.AppNav;
using AdamE.AppNav.Routing;

RouteTable table = new RouteTableBuilder()
    .AddValueCodec<OrderStatus>(
        (string text, out OrderStatus status) =>
        {
            (bool known, status) = text switch
            {
                "pending" => (true, OrderStatus.Pending),
                "in-progress" => (true, OrderStatus.InProgress),
                "shipped" => (true, OrderStatus.Shipped),
                _ => (false, default(OrderStatus)),
            };
            return known;
        },
        status => status switch
        {
            OrderStatus.Pending => "pending",
            OrderStatus.InProgress => "in-progress",
            OrderStatus.Shipped => "shipped",
            _ => null,
        })
    .Map("/orders/{status}",
        c => new OrdersRoute(c.Path<OrderStatus>("status")),
        format => format.Path("status", r => r.Status))
    .Build();

Console.WriteLine(table.Format(new OrdersRoute(OrderStatus.InProgress)));                 // /orders/in-progress
Console.WriteLine(table.Match(new Uri("/orders/in-progress", UriKind.Relative)).Route);   // OrdersRoute { Status = InProgress }
Console.WriteLine(table.Match(new Uri("/orders/InProgress", UriKind.Relative)).IsSuccess); // False

public enum OrderStatus { Pending, InProgress, Shipped }

public sealed record OrdersRoute(OrderStatus Status) : AppRoute;
```

The enum is then read and written by that codec alone: the names of its members are not read, a value it gives no text for cannot be written, and it cannot also be registered with `AddEnumValueCodec`.

A struct with a codec, such as `ProductId`, can also be read and written in its nullable form, `ProductId?`, wherever the nullable form of a built-in type can be: by `TryGetPath<T>`, `TryGetQuery<T>` and `Path<T>`, and by `Path` and `Query` in a format function, but not as the element of a list. `TryGetPath("id", out ProductId? id)` returns `false` with `null` when the path has no value. The nullable form is read and written by the codec of the struct, so the same text gives the same value. Each value read or written in that form costs one allocation, a box around the value, which the struct itself does not: a typed read or write of `ProductId` allocates nothing beyond what the codec's own functions allocate.

#### Typed query values

`TryGetQuery<T>(name, out value)` and `GetQueryValues<T>(name)` read query values as the same types, registered enums and types with a codec included, by the same rules, so `TryGetQuery<int>` accepts exactly the text that `Path<int>` accepts and gives the same value. The query is read, decoded and compared by name as described in [Query values](#query-values).

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/products", c =>
    {
        c.TryGetQuery("page", out int? page);
        IReadOnlyList<string> tags = c.GetQueryValues<string>("tag");
        return new ProductsRoute(page ?? 1, string.Join(",", tags));
    })
    .Build();

// /products?page=2&tag=a&tag=b gives ProductsRoute(2, "a,b"), /products gives ProductsRoute(1, ""), and
// /products?page=x and /products?page= fail the match.

public sealed record ProductsRoute(int Page, string Tags) : AppRoute;
```

- `TryGetQuery<T>` returns `false` with the default value of `T`, which is `null` for a nullable type, only when the query does not have the name or there is no query.
- A value that is present but does not convert rejects the match with a `RouteValueException`, as a path value does. An empty value is present: `?page=` and `?page` give the empty string, which is not a valid number, `bool`, `Guid` or enum, so `TryGetQuery<int>` rejects both. `TryGetQuery<string>` gives the empty string, as `TryGetQuery` does, and the parser of a codec receives it and decides.
- The first value decides, as for `TryGetQuery`: `?page=x&page=2` read as `int` fails the match, although the second value is valid, and `?page=2&page=x` gives 2.
- `GetQueryValues<T>` gives every value in order, converted, or fails: one value that does not convert, an empty one included, rejects the match, so `?id=3&id=5` read as `int` gives 3 and 5, and `?id=3&id=x` fails. A name the query does not have gives an empty list. The list cannot be changed, and `GetQueryValues<string>` gives the same values as `GetQueryValues`.
- `GetQueryValues<T>` does not support a nullable type, such as `int?` or `ProductId?`, since an element of the list is never absent; read the list as `int`. It throws `InvalidOperationException`, as an unsupported type does, whether or not the query has the name, and its message names the type to read the list as, and the registration it needs when it has none.
- `+` is a space in a query, so `?n=+5` gives a space followed by `5`, which is not a valid `int`, while `?n=%2B5` gives `+5`, which is 5.
- The message of the exception says that it is a query value, as in `The query value of 'page' is not a valid int.`, where a path value gives `The path value of 'page' is not a valid int.`, so a path parameter and a query value of the same name are told apart. Neither message quotes the value, which can carry a secret such as a sign-in token.

For a lenient read, read the string and use `int.TryParse`, as for a path value:

```csharp
int page = c.TryGetQuery("page", out string? text)
    && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed) ? parsed : 1;
```

Whether a query value is required is the app's decision. To require one, reject the match when it is absent, with a message that says what is missing, since the default message of `RouteValueException` does not say where the value was expected:

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/results", context =>
    {
        if (!context.TryGetQuery("page", out int page))
            throw new RouteValueException("page", "The query value 'page' is required.");

        return new ResultsRoute(page);
    })
    .Build();

// /results?page=2 gives ResultsRoute(2), and /results and /results?page=x fail the match.

public sealed record ResultsRoute(int Page) : AppRoute;
```

### Rejecting a value

A create function that finds a captured or query value unusable throws `RouteValueException` with the name of the value, and an optional message that says why. `Match` then returns a failed result instead of throwing:

```csharp
using AdamE.AppNav;
using AdamE.AppNav.Routing;

RouteTable table = new RouteTableBuilder()
    .Map("/orders/{id:int}", c =>
    {
        int id = c.Path<int>("id");
        return id > 0 ? new OrderRoute(id) : throw new RouteValueException("id", "An order id is positive.");
    })
    .Build();

RouteMatchResult result = table.Match(new Uri("/orders/0", UriKind.Relative));
Console.WriteLine(result.IsSuccess);                  // False
Console.WriteLine(result.Diagnostics[0].Code);        // route.value.invalid
Console.WriteLine(result.Diagnostics[0].Data["name"]); // id

public sealed record OrderRoute(int Id) : AppRoute;
```

- The result has one diagnostic whose `Code` is `RouteDiagnosticCodes.ValueInvalid` (`route.value.invalid`). Its `Data` holds the exception's `ValueName` under `name`, the template as written under `template`, the exception's message under `reason`, and the path as it was matched under `path`, all strings. Its `Message` holds none of them, and the text of the query itself is not included. The exception itself, with its inner exception, is not kept.
- `Route` is `null`, and `Definition` is the definition whose template matched.
- No later template is tried, even one that also matches the path: the template that matched owns the URI.
- Only a `RouteValueException` thrown by the create function of the definition that matched, while `Match` is calling it, is turned into a result. That includes one thrown by the parser of a [codec](#custom-value-types) that a typed read in the create function calls. Every other exception propagates unchanged, and a `RouteValueException` thrown anywhere else is an ordinary exception. A create function that calls `Match` itself gets a result from the inner call, not the exception.
- `reason` is whatever text the app gives as the message. The default message names the value but does not quote it; a message that quotes the value can carry a secret from the URI. Do not pass another exception's `Message`, as in `new RouteValueException("id", e.Message, e)`: many .NET parse methods quote their input, so `int.Parse` on `tok-123` says `The input string 'tok-123' was not in a correct format.` Pass the parse exception as the inner exception if you like; the table does not keep it.
- If the fallback needs the other captured values, such as the user in `/users/{u}/orders/{id}`, return a fallback route from the create function instead of rejecting, because a failed result carries only the definition and the name of the value.

A route says no in one of two ways. A [constraint](#constraints) makes its template not match, so another template can match the path; use one when a value of another shape belongs to another route. Rejecting a value in the create function fails the match for that URI; use it when the template is the right one but the value cannot be used, such as a number out of range or a query value of the wrong form.

### When a match fails

A URI that no template matches is not an error. The table itself does not throw for the content of a URI; an exception thrown by a create function, other than a `RouteValueException`, propagates to the caller.

When `IsSuccess` is true, `Route` and `Definition` are not `null`. When it is false, `Route` is `null`, `Diagnostics` has at least one entry, and `Definition` is the definition whose template matched when there was one, as for a [rejected value](#rejecting-a-value), or `null`. A URI that no template matches gives one diagnostic whose `Code` is `RouteDiagnosticCodes.NotMatched` (`route.not_matched`). Its `Message` never contains text from the URI, because a deep-link path can carry a secret, such as a sign-in or reset token, and messages tend to be logged. The path is in `Data` under the key `path`, exactly as it was read, and logging it is the caller's decision. `Data` cannot be changed, even through a cast.

The diagnostic codes in this version are `route.not_matched`, for a URI that no template matches, and `route.value.invalid`, for a [rejected value](#rejecting-a-value). Codes are lower-case words joined by `_`, grouped by `.` from the general to the specific. A code, once published, does not change meaning, so compare codes rather than messages.

### Formatting a route

A mapping can also say how to write its route back as a link. Give `Map` a format function as its third argument: it declares, for each parameter of the template, a function that reads the parameter's value from the route, and for each query value the route is written with, a function that reads that value. `Format` then writes a route as the path of its mapping's template, followed by its query.

```csharp
using AdamE.AppNav;
using AdamE.AppNav.Routing;

RouteTable table = new RouteTableBuilder()
    .Map("/orders/{id:int}",
        c => new OrderRoute(c.Path<int>("id")),
        format => format.Path("id", r => r.Id))
    .Build();

string path = table.Format(new OrderRoute(42));                        // /orders/42
Console.WriteLine(table.Match(new Uri(path, UriKind.Relative)).Route); // OrderRoute { Id = 42 }

public sealed record OrderRoute(int Id) : AppRoute;
```

`Format` returns the route as the text of a relative URI: its path and, when at least one query value is written, its query, as described in [Writing query values](#writing-query-values). It never writes a fragment. `FormatUri` writes the same text behind a scheme and a host, as an absolute URI, as described in [Writing a full URI](#writing-a-full-uri).

#### Declaring the values

- The format function runs once, while `Map` maps the route, not each time a route is written.
- It must declare every parameter of the template, required, optional and catch-all alike, with `Path(name, value)`. `Map` throws `ArgumentException` for one that is missing, naming the template and the first parameter in template order that is not declared, and for a name that is not a parameter of the template or is declared twice. Names are compared without regard to case, as the typed reads compare them.
- It declares each query value with `Query(name, value)`, and each list of values for one name with `QueryValues(name, values)`, before or after the path values, as [Writing query values](#writing-query-values) describes. A mapping may declare none.
- A template with no parameters is made writable with a format function that declares nothing, as in `.Map("/settings", _ => new SettingsRoute(), _ => { })`. A mapping made without a format function cannot write its route.
- The type of a value is one the [typed reads](#typed-values) support: `string`, `bool`, `Guid`, `decimal`, `double`, `float`, the eight integer types, a registered enum, a type with a codec, or the nullable form of any of them that is a value type. Any other type, such as `DateTime`, needs a codec, registered with `AddValueCodec` as [Custom value types](#custom-value-types) describes, and an enum needs one from `AddEnumValueCodec` or `AddValueCodec`. Registering can come before or after the `Map` call, so `Build`, not `Path`, throws `InvalidOperationException` for a type that has none, naming the route type, the template, the parameter or query value, the type and the method that registers it. Only what no codec can help is rejected by `Path` itself: a collection, such as `int[]`, for which it throws `InvalidOperationException`; convert such a value to a string and declare that instead.
- The type argument of `Map` must be a type that can be instantiated. A route is written by the mapping of its exact type, so `Map` throws `ArgumentException` for an abstract route type given a format function, which happens when the type is inferred from a create function whose result is a base type. Give the type argument explicitly, as in `Map<OrderRoute>(...)`, or map each concrete type.
- When `Map` throws, it adds no mapping.

Of the mappings of one route type, at most one can have a format function, so that writing a route never has to choose a template. `Build` throws `InvalidOperationException` naming the route type and both templates when two do. Mappings without one are not limited, so `/` and `/home` can both create a `HomeRoute` while only `/home` says how to write it:

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/", _ => new HomeRoute())
    .Map("/home", _ => new HomeRoute(), _ => { })
    .Build();

Console.WriteLine(table.Format(new HomeRoute())); // /home

public sealed record HomeRoute : AppRoute;
```

`Format` finds the mapping by the exact type of the route, as `GetType()` gives it. A mapping of a base type does not write a route of a type derived from it, and a base type and a derived type can each have their own format function. For a route type that no mapping with a format function has, `Format` throws `InvalidOperationException`, whose message says whether the type is not mapped at all or is mapped without a format function.

#### How values are written

Each value of a built-in type or a registered enum is written as one canonical text, which the typed read of the same type reads back to an equal value, so the text of a number, `bool` or `Guid` always satisfies the [constraint](#constraints) named after its type. Equal values always give the same path, and nothing depends on the culture. A value of a type with a codec is written as the text its codec writes, as its row below says.

| Type | Text |
| --- | --- |
| `string` | The value itself, percent-encoded as `RouteTemplate.Format` encodes it, so `a b` is `a%20b` and `a/b` is `a%2Fb`. In a catch-all value, `/` separates segments, so `a/b c` is `a/b%20c`. |
| `bool` | `true` or `false`, in lower case. |
| Integer types | Invariant digits, with `-` for a negative value and no `+` or leading zeros: `7` and `-7`, never `+7` or `007`. |
| `decimal` | Normalized, so that equal values give equal text: no trailing zeros after the point, no point without a fraction, and `0` for every zero, whatever its sign or scale. `1.50` is `1.5`, `2.0` is `2` and `-0.0` is `0`. Never an exponent. |
| `double`, `float` | The shortest digits that read back as the same value of the type, written without an exponent, which the read does not accept: `1E+21` is `1000000000000000000000` and `1E-07` is `0.0000001`. A `float` is written by its own digits, so `0.1f` is `0.1`. A negative zero is `0`. Because no exponent is used, an extreme value is written in full: the largest and the smallest positive `double` take more than 300 characters each. NaN and the infinities cannot be written, because no text reads as them. |
| `Guid` | The hyphenated form in lower case, as `Guid.ToString()` writes it: `6f9619ff-8b86-d011-b42d-00cf4fc964ff`. |
| Registered enum | The name of the member with that value, as declared, such as `Shipped`. When several members have the value, the name that sorts first ordinally is written, so for `enum Level { Primary = 1, Alpha = 1 }` it is `Alpha`. Ordinally, every upper-case ASCII letter sorts before every lower-case one, so of `Zulu` and `alpha` it is `Zulu`. A value that no member has, such as an undefined number or a combination of flags with no name of its own, cannot be written. |
| Type with a codec | The text that the codec's `format` returns, which `Format` then reads back with the codec's `tryParse`; a text that the parser does not accept is not written, as [When a route cannot be written](#when-a-route-cannot-be-written) says. What is canonical is the codec's choice. A value for which `format` returns `null` cannot be written. |
| Nullable form | The text of the underlying type. `null` leaves the parameter out. |

A `null` value, a null reference or a nullable value type without a value, leaves an optional or catch-all parameter out, with its segment, and is an error for a required one:

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/list/{page:int?}",
        c => new ListRoute(c.TryGetPath("page", out int? page) ? page : null),
        format => format.Path("page", r => r.Page))
    .Build();

Console.WriteLine(table.Format(new ListRoute(null))); // /list
Console.WriteLine(table.Format(new ListRoute(3)));    // /list/3

public sealed record ListRoute(int? Page) : AppRoute;
```

There is no way to write an empty path value. An empty string is not left out: a path segment cannot be empty, so it is rejected, as `RouteTemplate.Format` rejects it.

#### Writing query values

`Query(name, value)` declares a query value: a function that reads one value from the route, which `Format` writes after the path as `name=value`.

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/products",
        c => new ProductsRoute(
            c.TryGetQuery("page", out int page) ? page : null,
            c.TryGetQuery("sort", out string? sort) ? sort : null),
        format => format
            .Query("page", r => r.Page)
            .Query("sort", r => r.Sort))
    .Build();

Console.WriteLine(table.Format(new ProductsRoute(2, "name")));    // /products?page=2&sort=name
Console.WriteLine(table.Format(new ProductsRoute(null, "name"))); // /products?sort=name
Console.WriteLine(table.Format(new ProductsRoute(null, null)));   // /products
Console.WriteLine(table.Format(new ProductsRoute(null, "")));     // /products?sort=

public sealed record ProductsRoute(int? Page, string? Sort) : AppRoute;
```

- The values are written in the order they were declared, as `name=value` pairs joined by `&`, after a `?`. There is no `?` when no value is written.
- The type of a value is one that `Path` accepts, and the value is written as the same text, as [How values are written](#how-values-are-written) describes. `Query` throws `InvalidOperationException` for a collection, such as `string[]` or `List<int>`, since it writes one value for the name; the message says to declare several values for one name with `QueryValues`, described below. As for `Path`, `Build` reports a type that has no codec.
- `null`, a null reference or a nullable value type without a value, leaves the value out. An empty string is a value: it is written as `name=`, which reads back as the empty string. There is no way to write a name without `=`.
- A name is any text, the empty string included, as on the read side. `Query` throws `ArgumentException` for a name declared twice, with `Query` or `QueryValues`, compared as the reads compare names, ordinally without regard to case, because the reads take the first value of a name, so the second would never be read. It throws one too for a name that contains a lone surrogate. A query value may have the name of a path parameter: in `/items/{id}?id=x`, the two are read separately.
- Names and values are percent-encoded with `Uri.EscapeDataString`, which leaves only ASCII letters, digits, `-`, `.`, `_` and `~` as they are. A space is written as `%20`, never `+`, because the query reader takes `+` for a space, as form encoding does, so a literal `+` is written as `%2B`. `&`, `=`, `#`, `?` and `%` are encoded too, so that they are read back as data rather than splitting or ending the query: `a b+c&d` is written as `a%20b%2Bc%26d`, and the reader gives back exactly the text written.

`QueryValues(name, values)` declares a list of values for one name: a function that reads the list from the route, each element of which `Format` writes as its own `name=value` pair.

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/products",
        c => new ProductsRoute(c.TryGetQuery("page", out int page) ? page : null, c.GetQueryValues("tag")),
        format => format
            .Query("page", r => r.Page)
            .QueryValues("tag", r => r.Tags))
    .Build();

Console.WriteLine(table.Format(new ProductsRoute(2, ["a", "b"])));         // /products?page=2&tag=a&tag=b
Console.WriteLine(table.Format(new ProductsRoute(null, ["b", "a", "b"]))); // /products?tag=b&tag=a&tag=b
Console.WriteLine(table.Format(new ProductsRoute(2, [])));                 // /products?page=2
Console.WriteLine(table.Format(new ProductsRoute(null, [""])));            // /products?tag=

public sealed record ProductsRoute(int? Page, IReadOnlyList<string> Tags) : AppRoute
{
    // A record compares a list member by reference, so this route compares its tags by their elements.
    public bool Equals(ProductsRoute? other) =>
        other is not null && Page == other.Page && Tags.SequenceEqual(other.Tags);

    public override int GetHashCode() => HashCode.Combine(Page, Tags.Count);
}
```

The route defines its own equality because a record compares a list member by reference, and [The route table](#the-route-table) asks for routes whose members compare by value. With it, matching `/products?page=2&tag=a&tag=b` gives a route equal to `new ProductsRoute(2, ["a", "b"])`. Without it, the matched route would have the same tags and still not be equal to the route that was written, as [What a round trip promises](#what-a-round-trip-promises) explains.

- The element type is one that `GetQueryValues<T>` reads: `string`, `bool`, `Guid`, `decimal`, `double`, `float`, the eight integer types, a registered enum or a type with a codec, each written as the same text as a single value, and read back by the codec's parser in the same way. It is inferred from the function, so a function that returns `string[]`, `List<int>`, `IReadOnlyList<Guid>`, `HashSet<string>`, `ImmutableArray<int>` or `IEnumerable<long>` needs no type argument. A function that returns only `null` or `[]` has no type to infer from; give it, as in `QueryValues<string>("tag", _ => [])`. A default `ImmutableArray<T>`, which is what an unset member of that type holds, is neither `null` nor empty: enumerating it throws `InvalidOperationException`, which propagates unchanged, as any exception from the list does.
- A nullable element type, such as `int?` or `ProductId?`, is not supported, since an element of a list is never absent, and neither is a collection, so a list of lists cannot be written. `QueryValues` throws `InvalidOperationException` for either. For a nullable type, the message names the type to declare instead, and the registration that type needs when it is not built in. A string given to `QueryValues` is a list of `char`, and its message says to declare it with `Query`; a list of lists gets a message that says to declare each list under its own name. As for `Query`, `Build` reports a type that has no codec.
- A `null` list and an empty one both write nothing. There is no `?` when no value is written, by any declaration.
- An element that is `null`, which only a list of a reference type, such as strings, can have, cannot be written, and `Format` throws for it. An empty string is a value, written as `name=`.
- The elements are written in the order the list gives them, repeated and empty ones included, where the list was declared among the other query values: `.QueryValues("a", ...).Query("x", ...).QueryValues("b", ...)` writes `?a=1&a=2&x=m&b=3`.
- The function is called at most once each time a route is formatted, and the list it returns is enumerated once, so a list computed lazily is computed once. After an element that cannot be written, the elements after it are not read and the later functions are not called. An exception thrown while the list is enumerated, or by a codec while it writes an element, propagates unchanged.
- A name belongs to one declaration: `QueryValues` throws `ArgumentException` for a name already declared with `Query` or `QueryValues`, and `Query` for one already declared with either, compared ordinally without regard to case. Otherwise a name follows the rules of `Query`, and each element is encoded as a single value is.

`QueryValues` is a method of its own, rather than a collection given to `Query`, because `Query` writes one value for its name. A collection given to `Query` is rejected, and the message names `QueryValues`, so a list is never written by accident as one value or as several.

#### When a route cannot be written

`Format` throws `ArgumentNullException` for a `null` route. It throws `ArgumentException`, whose `ParamName` is `route`, when a value cannot be written. The checks are those of [`RouteTemplate.Format`](#formatting-a-path), applied to the text of each path value, one more for a path or query value that has no text, one for a text that a codec writes but its own parser does not accept, one for a query value that cannot be encoded, and one for a `null` element of a list of query values. An element of a list is checked as a single query value is:

- a required parameter's value is `null`;
- a value is empty, contains a lone surrogate, or is `.` or `..`;
- a value does not satisfy the parameter's [constraint](#constraints), which happens when the value's type is wider than the constraint, as for `{id:int}` written from a `long` above 2147483647 or `{n:byte}` written from an `int` of 256, or when a string does not fit it, as for `{n:alpha}` written from `ab1`;
- a catch-all value starts or ends with `/`, contains `//`, or has `.` or `..` as a part;
- an optional parameter has a value although an earlier optional parameter is left out, as for `/a/{b?}/{c?}` with a value for `c` and none for `b`;
- a value has no text: a `double` or `float` that is NaN or infinite, an enum value that no member has, or a value for which its codec's `format` returns `null`;
- a codec writes a text that its own `tryParse` does not accept, so that a link with it could not be read back;
- a query value contains a lone surrogate, which `Uri.EscapeDataString` would write as the encoding of U+FFFD, so that it would read back changed; or
- an element of a list of query values is `null`, which no read of the list could return.

`Format` throws `InvalidOperationException` when the parser of a codec, reading back a text its writer wrote, returns `true` with a `null` value, which is a mistake in the codec; the message names the type.

`Format` also throws `ArgumentException`, after all of these checks and once every value function has been called, when every value can be written but another template matches the path first, as [What a round trip promises](#what-a-round-trip-promises) describes.

The message names the route type, the template and the parameter, and says what is wrong in fixed words, as in `The route type 'ProductRoute' cannot be written by the template '/products/{name}': path parameter 'name' is required, but its value is null.` For a query value, it names the query value where a path failure names the path parameter, as in `The route type 'ProductsRoute' cannot be written by the template '/products': query value 'sort' has a value that is not valid UTF-16: it contains a lone surrogate, which cannot be percent-encoded and read back unchanged.` For an element of a list, it names the list's query value the same way, as in `The route type 'ProductsRoute' cannot be written by the template '/products': query value 'tag' has a null element, which cannot be written, because an element of a list is never absent; leave it out of the list instead.`, and says neither which element it is nor anything about the others. It never quotes the value, nor the text a codec wrote for it, because a route can carry a secret, such as a sign-in token, and messages tend to be logged, and the exception has no inner exception that could. When more than one value cannot be written, which one is reported is not specified, beyond the check of the path against other templates coming last. An exception thrown by a value function, or by either function of a codec, propagates unchanged.

Each value function is called at most once each time a route is formatted: the path functions in template order, then the query functions in the order they were declared. After a value that cannot be written, or an exception from a value function, the later functions are not called, so no query function is called for a route whose path cannot be written. A list of query values is enumerated once, and stops at its first element that cannot be written.

`Format` never changes the table.

#### What a round trip promises

When `Format` returns, `Match` of the text it returns selects the mapping that wrote it, and the typed reads give back the values the format function read, for a type with a codec as far as the codec keeps its contract, as described below: each single value, read with the typed read of the type its value function returns, is equal to the one written, and each list, read with `GetQueryValues<T>`, has equal elements in the same order. The route that matches is then equal to the one written, provided the create function reads each value that way and builds the route from the values as the format function reads them, which the table cannot check, and provided the route type compares its lists by their elements. A record does not do that by default: it compares a list member by reference, as [The route table](#the-route-table) notes, so a record with a list member reads back with equal values but is not equal to the route written unless it defines its own equality. In the other direction, text that a typed read accepts is written back as its canonical text, and writing the route again changes nothing: `/orders/+007` matches as `OrderRoute { Id = 7 }`, which is written as `/orders/7`. The mapping that writes a route is the one of its exact type, so a mapping whose create function returns routes of several types derived from its route type creates routes that it does not write.

For a type with a codec, the promise is the codec's. `Format` reads back each text the codec writes with the codec's own parser, and throws `ArgumentException` rather than write a text that the parser does not accept, so every link it returns can be read. It does not compare the value read back with the one written: equality is the type's own, and a type without value equality would fail any such comparison. A value reads back equal exactly when the codec makes it so, by giving back from `tryParse` a value equal to the one `format` was given, and the route then matches equal to the one written as for any other type. Text that the parser accepts is written back as the text `format` writes for the value it gives, so for `ProductId` above, `/products/p007` is written back as `/products/p7`.

The same holds for a query value read with the typed read of its type, `TryGetQuery<T>`, which reads the first value of a name. `Format` writes one value for each name declared with `Query`, so that first value is the one written; a link with the name repeated reads its first value, and is written back with that value alone. A list declared with `QueryValues` reads back through `GetQueryValues<T>`, or `GetQueryValues` for strings, as a list with the same elements in the same order. That is exact because those reads return every value of a name in the order the query gives them, repeated and empty values included, and `Format` writes the elements in the order of the list, under the one name that only that declaration writes. The one difference is `null`: a `null` list writes nothing, as an empty list does, and a name the query does not have reads back as an empty list, not as `null`. A query value never affects which template matches: `Match` chooses by the path alone, and so does the check below.

[Precedence](#which-route-wins) decides which template wins, and `Build` rejects only templates that rank equally, so a template that ranks higher can match a path that another template writes; so can one that ranks equally and was mapped first, when a [declaration that two constraints are disjoint](#declaring-constraints-disjoint) is false. `Format` does not return such a path. After writing it, `Format` finds the template that `Match` would select for it, by the templates and their constraints alone, without calling a create function, and throws `ArgumentException`, whose `ParamName` is `route`, when that is not the template of the mapping that wrote it:

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/products/new", _ => new NewProductRoute())
    .Map("/products/{name}",
        c => new ProductRoute(c.Path("name")),
        format => format.Path("name", r => r.Name))
    .Build();

Console.WriteLine(table.Format(new ProductRoute("widget"))); // /products/widget
Console.WriteLine(table.Format(new ProductRoute("NEW")));    // /products/NEW
table.Format(new ProductRoute("new"));                       // throws ArgumentException

public sealed record NewProductRoute : AppRoute;

public sealed record ProductRoute(string Name) : AppRoute;
```

The template `/products/new` matches the path `/products/new` first, so a link written from it would not lead to the product: it would open `NewProductRoute`, or fail, were that mapping's create function to reject the value. The message names the route type and both templates: `The route type 'ProductRoute' cannot be written by the template '/products/{name}': the path it writes is matched first by the template '/products/new', which ranks higher, so a link to it would be matched by that template instead. Change the templates so that they cannot match the same path, for example by constraining a parameter or by giving one of them a literal of its own.` When the template that matches first ranks equally and was mapped first, which only a false [declaration that two constraints are disjoint](#declaring-constraints-disjoint) allows, the message says that instead of "ranks higher", and says to correct or remove the declaration. Like every other `Format` failure, it never quotes the route's values or the path, and it has no inner exception. Literals are compared case-sensitively, so `NEW` is written. The same happens for a constraint the value satisfies, as for the name `42` beside `/products/{id:int}`, for an optional parameter left out, as for `/list/{page:int?}` with no page beside `/list`, and for a catch-all value that a longer template matches, as for `doc/edit` in `/files/{*path}` beside `/files/{name}/edit`. The template that matches first counts whether or not its mapping has a format function, and even when its mapping creates the same route type, because the table cannot know that the two create functions agree: with `/items/{id:int}`, mapped without a format function, beside `/items/{slug}`, which writes the same route type, the slug `42` cannot be written. This check reads the written path alone, comes after every other, once every value function has been called, and calls none.

To fix the table, change the templates so that they cannot match the same path, for example by constraining a parameter or by giving one of them a literal of its own. With `/products/{id:int}` in place of `/products/{name}`, no value it writes is `new`. With `/products/by-name/{name}` in place of it, the two templates never match the same path, and `new` is written as `/products/by-name/new`.

The promise is about `Match` of the returned text as a relative URI, `new Uri(text, UriKind.Relative)`, and of an absolute URI with that path and query, with one limit for the absolute URI. `System.Uri` changes the text of an absolute URI in one way: it drops `%20` sequences at the very end of an absolute URI that contains a non-ASCII character anywhere, including in the host the application puts in front of the text, as [What is read from a URI](#what-is-read-from-a-uri) describes. `Format` writes a non-ASCII character in a literal, a path value, a query name or a query value as percent-encoded bytes from `%80` to `%FF`, which count too. So the last value written, the last query value or the last path value when no query is written, loses its trailing spaces in such a link, and a path value that is only spaces then no longer matches. An absolute URI that is all ASCII, and any relative URI, keep them. Only the very end counts: a path value followed by a query keeps its spaces, and so does a query value followed by another. A path value that loses its space may also match another template:

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/é/new", _ => new NewProductRoute())
    .Map("/é/{name}",
        c => new ProductRoute(c.Path("name")),
        format => format.Path("name", r => r.Name))
    .Build();

string path = table.Format(new ProductRoute("new "));                  // /%C3%A9/new%20
Console.WriteLine(table.Match(new Uri(path, UriKind.Relative)).Route); // ProductRoute { Name = new  }
Console.WriteLine(table.Match(new Uri("https://app" + path)).Route);   // NewProductRoute { }
```

Without `/é/new` in the table, the absolute link matches `/é/{name}`, but reads the name back as `new`, without the space. With a query value written after it, the name keeps its space, and the query value written last loses its own:

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/é/{name}",
        c => new SearchRoute(c.Path("name"), c.TryGetQuery("q", out string? q) ? q : null),
        format => format.Path("name", r => r.Name).Query("q", r => r.Q))
    .Build();

string text = table.Format(new SearchRoute("new ", "a "));             // /%C3%A9/new%20?q=a%20
Console.WriteLine(table.Match(new Uri(text, UriKind.Relative)).Route); // SearchRoute { Name = new , Q = a  }
Console.WriteLine(table.Match(new Uri("https://app" + text)).Route);   // SearchRoute { Name = new , Q = a }

public sealed record SearchRoute(string Name, string? Q) : AppRoute;
```

The host counts as well: `https://app/a/new%20?q=a%20` keeps every `%20`, while `https://é.example/a/new%20?q=a%20` reads `q` as `a`, although nothing `Format` wrote is non-ASCII.

That limit is `Format`'s, for an absolute URI the application builds from its text. `FormatUri` has none, because it refuses the link: it builds the absolute URI itself, checks what `System.Uri` reads back from it, and throws rather than return one that would lose a space, as [Writing a full URI](#writing-a-full-uri) describes.

#### Writing a full URI

`FormatUri(route, baseUri)` writes a route as an absolute URI: the scheme and authority of `baseUri`, followed by exactly the text that `Format` returns for the route. It returns a `Uri`.

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/orders/{id:int}",
        c => new OrderRoute(c.Path<int>("id")),
        format => format.Path("id", r => r.Id))
    .Build();

Uri web = table.FormatUri(new OrderRoute(42), new Uri("https://example.com"));
Uri app = table.FormatUri(new OrderRoute(42), new Uri("myapp:///"));
Console.WriteLine(web.AbsoluteUri);        // https://example.com/orders/42
Console.WriteLine(app.AbsoluteUri);        // myapp:///orders/42
Console.WriteLine(table.Match(app).Route); // OrderRoute { Id = 42 }

public sealed record OrderRoute(int Id) : AppRoute;
```

The route is written once, as `Format` writes it, so each value function is called at most once and in the same order, and every failure of `Format` is thrown unchanged, with the same exception type, `ParamName` and message. `FormatUri` throws `ArgumentNullException` for a `null` route or base, then checks the base, before any value function is called, then writes the route, then checks the link.

Pass the `Uri` itself, or its `AbsoluteUri`, to the code that opens or shares the link. Never pass its `ToString()`, or put it in an interpolated string such as `$"{uri}"`, which calls `ToString()`: `ToString()` unescapes characters such as `%20`, and its text can change the link when it is parsed again, as the example under the trailing space below shows.

The base gives the scheme, user information, host and port of the link, and nothing else. They are written as `System.Uri` writes them in the base's `AbsoluteUri`: the scheme and a host name in lower case, a default port left out, and a non-ASCII host name in Unicode. So `https://user:pw@example.com:8443` gives `https://user:pw@example.com:8443/orders/42`, `https://example.com:443` gives `https://example.com/orders/42`, and `https://é.example` gives `https://é.example/orders/42`. `Match` reads none of them. User information in the base is copied into every link written from it, so a credential in the base travels with each link; no exception message includes it.

- The base must be absolute. `FormatUri` throws `ArgumentException`, whose `ParamName` is `baseUri`, for a relative URI, and for `new Uri("/")`, which is a file URI on Unix-like platforms that the table [reads as relative](#what-is-read-from-a-uri).
- Its path must be empty or `/`, and it must have no query and no fragment, not even an empty one, as in `https://example.com?` or `https://example.com#`. `Match` reads the whole path of a link, so a link written under a base path would not match: under `https://example.com/app`, `new OrderRoute(42)` would be `https://example.com/app/orders/42`, which `/orders/{id:int}` does not match. Rather than drop the path or write under it, `FormatUri` throws `ArgumentException`, whose `ParamName` is `baseUri`. Its message says whether the base has a path, a query or a fragment, and never quotes the base, which can carry user information. To give every link a prefix, put the prefix in the templates: map `/app/orders/{id:int}`, and `https://example.com` gives `https://example.com/app/orders/42`.
- For a custom scheme with no host, give the base an empty authority, `myapp:///`, so that the link has three slashes, as in `myapp:///orders/42`. A base with a host, `myapp://host`, keeps it: `myapp://host/orders/42` has the host `host` and the path `/orders/42`. That is the host trap of [What is read from a URI](#what-is-read-from-a-uri) seen from the other side: `FormatUri` never writes the first segment of the path where a host would be read, as the hand-written `myapp://orders/42` does. A base with no authority, `myapp:` or `myapp:/`, gives `myapp:/orders/42`, which `Match` reads as the same path.
- A base whose path is empty or `/` is accepted under any scheme whose links read back as written. `System.Uri` parses `mailto:`, `urn:`, `about:`, `data:` and `javascript:` as it parses a custom scheme, so `urn:` gives `urn:/orders/42`. `mailto:a@b.c` is accepted too, because `System.Uri` reads `a@b.c` as user information and a host, with an empty path, and it gives `mailto:a@b.c/orders/42`. `urn:isbn:1` and `about:blank` have a path and are refused. `FormatUri` promises that `Match` reads the link back, not that the link is useful under its scheme.
- A host that is an IPv6 address with a zone id, as in `http://[fe80::1%25en0]:5000`, is refused with `ArgumentException`, whose `ParamName` is `baseUri`: `System.Uri` leaves the zone id out of the text of a URI, so the link would name a different address.

When `FormatUri` returns, the path and the query that `Match` reads from the URI it returns are exactly the text that `Format` returns for the route. So everything that [What a round trip promises](#what-a-round-trip-promises) says of that text as a relative URI holds for the URI: `Match` of it selects the mapping that wrote it, and each value reads back equal through the typed read of its type. `FormatUri` checks this on the URI it built, reading its path and query as `Match` reads them, and throws `ArgumentException` rather than return a link that would read back differently. That happens in two ways.

The first is a trailing space. The last value written ends with a space, and the URI contains a non-ASCII character anywhere: in a value, in a literal of the template, or in the user information or host of the base. `System.Uri` drops `%20` from the end of such a URI, as [What is read from a URI](#what-is-read-from-a-uri) describes, so the link would lose the space. The value written last is the last query value, or the last path value when no query is written; the last element of a list counts too. A value followed by another keeps its spaces, and so does every value of an all-ASCII URI:

```csharp
RouteTable table = new RouteTableBuilder()
    .Map("/s/{name}",
        c => new SearchRoute(c.Path("name"), c.TryGetQuery("q", out string? q) ? q : null),
        format => format.Path("name", r => r.Name).Query("q", r => r.Q))
    .Build();

var idn = new Uri("https://é.example");
var ascii = new Uri("https://example.com");
Console.WriteLine(table.FormatUri(new SearchRoute("new ", "a"), idn).AbsoluteUri);    // https://é.example/s/new%20?q=a
Console.WriteLine(table.FormatUri(new SearchRoute("new ", null), ascii).AbsoluteUri); // https://example.com/s/new%20
table.FormatUri(new SearchRoute("new ", null), idn);                                  // throws ArgumentException

public sealed record SearchRoute(string Name, string? Q) : AppRoute;
```

The second link keeps its space only as long as it is passed on as a `Uri` or as its `AbsoluteUri`: its `ToString()` is `https://example.com/s/new ` with the space unescaped, and `new Uri` of that text trims the space, so `Match` reads the name `new`.

The exception's `ParamName` is `route`, and its message names the route type and the template, as in `The route type 'SearchRoute' cannot be written by the template '/s/{name}' as an absolute URI: the last value written ends with a space, which System.Uri drops from the end of an absolute URI that contains a non-ASCII character, so the link would be read back differently. Write a value that does not end with a space, or use the relative text that Format returns. A base whose host and user information are ASCII avoids this only when nothing else in the link is non-ASCII.` Like every `Format` failure, it never quotes the value or the text, and it does not quote the base either.

The other way is a scheme under which `System.Uri` changes the path or the query. The schemes that have no query, such as `ftp`, [listed above](#what-is-read-from-a-uri), refuse a link with a query, though they write one without: `ftp://example.com` gives `ftp://example.com/s/new` for `new SearchRoute("new", null)`. `file` and `vsmacros` refuse a value that contains `/` in a segment that is not a catch-all, which they decode from `%2F`; a catch-all value's `/` is written as it is and is unaffected. `net.tcp` and `net.pipe` refuse a path value that contains `\`, which they read as `/`. The behaviour of `file` was established on Unix-like platforms and can differ on Windows, where `System.Uri` treats file URIs differently; the check runs on every platform, so the promise holds everywhere, even where which links are refused differs. The exception's `ParamName` is `baseUri`, and its message names the scheme alone: `A URI of the scheme 'ftp' does not keep the link as written: System.Uri changes its path or query, so the link would be read back differently. Use a scheme whose URIs keep them, such as https or a custom scheme of the application.` Under `http`, `https`, `ws`, `wss` and a custom scheme, `System.Uri` changes nothing that `Format` writes: `Format` writes no dot segment, writes every percent sequence in upper case, and encodes only characters that are not unreserved, so under those schemes a link is refused only for a trailing space.

### Threads

A built table is immutable and safe to use from several threads at once, for `Match`, `Format` and `FormatUri` alike. Mapping more routes or registering more codecs on the builder does not change a table it has already built; call `Build` again for a table that includes them. A builder itself is not thread-safe, and a create function, a value function of a format function, or a function of a codec registered with `AddValueCodec`, that runs on several threads must be safe to call that way.
