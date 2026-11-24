# Routing

## Route templates

A route template is the path pattern that a route answers to, such as `/settings/profile`. `RouteTemplate.Parse` turns the template text into a `RouteTemplate`, and `Match` tells you whether a path fits it and, when the template has [parameters](#path-parameters), what they captured.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate template = RouteTemplate.Parse("/settings/profile");

Console.WriteLine(template.Match("/settings/profile") is not null);  // True
Console.WriteLine(template.Match("settings//profile/") is not null); // True: slashes are normalized
Console.WriteLine(template.Match("/Settings/profile") is not null);  // False: segments are case-sensitive
Console.WriteLine(template.Match("/settings") is not null);          // False: a segment is missing
```

A template must start with `/`. It is divided into segments at each `/`. A segment is either literal text or a [parameter](#path-parameters). A path matches when it has the same number of segments as the template and each literal equals the path segment in the same position. `Match` returns `null` when the path does not match, and a dictionary of captured values when it does; for a template without parameters, the dictionary is empty.

Templates and paths are divided and decoded the same way:

- Leading, trailing and repeated slashes do not create segments. `/a//b/` has the same segments as `/a/b`, and `/` is the root template, which has no segments.
- Each segment is percent-decoded once, after the text is divided. `/files/a%20b` matches `/files/a b`, and `%2F` stays inside its segment, so `/files/a%2Fb` does not match `/files/a/b`. A percent sequence that is not valid, or does not decode to valid UTF-8, such as `%zz` or `%FF`, is kept as written.
- Literals are compared with path segments ordinally and case-sensitively.

A path is otherwise taken as it is:

- The leading `/` is optional.
- White space is significant: `/settings ` does not match `/settings`.
- A query string or fragment is not removed, so remove it before calling `Match`.
- `+` is not a space, and `.` and `..` are ordinary segments.

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
- A parameter always matches exactly one segment, never zero. Its value is the segment after decoding, exactly as a literal would be compared: `%2F` becomes `/` inside the value, white space is kept, and an invalid percent sequence is kept as written. Values are not checked further, so a value can be any text, including a decoded control character.
- Each match returns its own dictionary. Treat it as read-only.

## Invalid templates

`Parse` throws `ArgumentNullException` for `null`. It throws `ArgumentException` when the text is empty or only white space, or does not start with `/`. It also throws `ArgumentException` for:

- a segment that contains a control character (one for which `char.IsControl` is true), such as a tab, line feed or null character, anywhere in the segment, whether it is a parameter or a literal,
- a parameter whose name is not valid, such as `{}`, `{ id }`, `{1a}` or `{a-b}`, including a name that contains `:`, `?` or `*`,
- a parameter that repeats the name of an earlier parameter, ignoring case, as in `/{id}/{ID}`,
- a literal segment that starts or ends with white space, as in `/ {id}` (spaces inside a segment, as in `/my files`, are allowed),
- `{` or `}` anywhere except around a whole-segment parameter, as in `item{id}` or `{a}{b}`, and
- a literal segment that contains `?` or `#`, which cannot appear in the path of a URI.

`Parse` reports the first invalid segment, reading left to right. Within a segment, a control character is reported before any other problem, even when it is also white space, as a tab or line feed at the start of a segment is. The exception message shows a control character as a `\uXXXX` escape, such as `\u0009`, rather than printing it.

These checks apply to the text as written, before decoding. To match one of these characters literally, percent-encode it: `%09` for a tab, `%0A` for a line feed, `%00` for a null character (and the UTF-8 bytes of any other control character, such as `%C2%85` for U+0085), `%7B` for `{`, `%7D` for `}`, `%3F` for `?`, `%23` for `#` and `%20` for a space at the start or end of a segment. For example, `/what%3F` matches the path `/what%3F`, `/a%09b` matches a path whose segment is `a`, a tab and `b`, and `/%7Bid%7D` is a literal that matches the path `/{id}`.
