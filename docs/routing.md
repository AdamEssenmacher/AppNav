# Routing

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

## Formatting a path

`Format` is the inverse of `Match`: it builds the path a template answers to from a value for each parameter.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate file = RouteTemplate.Parse("/my files/{name}");

string path = file.Format(new Dictionary<string, string> { ["name"] = "notes/2026.txt" });
Console.WriteLine(path);                      // /my%20files/notes%2F2026.txt
Console.WriteLine(file.Match(path)?["name"]); // notes/2026.txt
```

- The path starts with `/`, separates segments with a single `/` and has no trailing slash. The root template formats as `/`.
- Each literal is written as its decoded text and each value as given, both percent-encoded with `Uri.EscapeDataString`. A `/` in a value becomes `%2F`, so it stays inside its segment.
- `.` and `..` are written as they are, as values or literals, and a URI parser or browser may remove them and the segment before them when the path is resolved.
- Values are keyed by parameter name without regard to case, as everywhere else in a template. Each name is looked up as written in the template first, and only if that finds nothing is the dictionary searched for a key that equals the name ignoring case. Entries for names the template does not use are ignored.
- `Format` throws `ArgumentNullException` for a `null` dictionary. It throws `ArgumentException` when a parameter has no entry, when it has none under its name as written and more than one whose key equals it ignoring case, or when its value is `null`, empty or contains a lone surrogate, which cannot be encoded and read back. The first parameter in template order that cannot be formatted is the one reported. Any other value is allowed, including white space and control characters.

`Match` reads back what `Format` writes: for every template and every dictionary `Format` accepts, matching the formatted path returns exactly the template's parameters with the values that were formatted. In the other direction, formatting the values a match returned gives a path that matches again with the same values, unless the matched path contained a lone surrogate in a parameter's position, because `Match` does not check paths and `Format` rejects such a value. The new path need not have the same text as the one that was matched, because the output is canonical: the same values always format to the same path, whatever path they were matched from. Every segment is encoded the same way and repeated slashes are dropped. For example, `/my files/{name}` matches `//my files//%41/`, and formatting that match gives `/my%20files/A`.

## Invalid templates

`Parse` throws `ArgumentNullException` for `null`. It throws `ArgumentException` when the text is empty or only white space, or does not start with `/`. It also throws `ArgumentException` for:

- a segment that contains a control character (one for which `char.IsControl` is true), such as a tab, line feed or null character, anywhere in the segment, whether it is a parameter or a literal,
- a segment that contains a lone surrogate: a `char` that is half of a surrogate pair without its other half, which is not valid UTF-16 and cannot be percent-encoded,
- a parameter whose name is not valid, such as `{}`, `{ id }`, `{1a}` or `{a-b}`, including a name that contains `:`, `?` or `*`,
- a parameter that repeats the name of an earlier parameter, ignoring case, as in `/{id}/{ID}`,
- a literal segment that starts or ends with white space, as in `/ {id}` (spaces inside a segment, as in `/my files`, are allowed),
- `{` or `}` anywhere except around a whole-segment parameter, as in `item{id}` or `{a}{b}`, and
- a literal segment that contains `?` or `#`, which cannot appear in the path of a URI.

`Parse` reports the first invalid segment, reading left to right. Within a segment, a control character is reported first, then a lone surrogate, then any other problem. A control character is reported even when it is also white space, such as a tab at the start of a segment. The exception message shows a control character or a lone surrogate as a `\uXXXX` escape, such as `\u0009`, rather than printing it.

These checks apply to the text as written, before decoding. To match one of these characters literally, percent-encode it: `%09` for a tab, `%0A` for a line feed, `%00` for a null character (and the UTF-8 bytes of any other control character, such as `%C2%85` for U+0085), `%7B` for `{`, `%7D` for `}`, `%3F` for `?`, `%23` for `#` and `%20` for a space at the start or end of a segment. For example, `/what%3F` matches the path `/what%3F`, `/a%09b` matches a path whose segment is `a`, a tab and `b`, and `/%7Bid%7D` is a literal that matches the path `/{id}`. A lone surrogate has no encoded form: a surrogate has no UTF-8 bytes of its own, and the three bytes it would encode to, such as `%ED%A0%80`, are not valid UTF-8, so that text decodes to itself and the literal is that percent text.
