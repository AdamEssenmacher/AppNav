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
| `guid` | 32 hex digits, in either case, written either without separators, as in `6f9619ff8b86d011b42d00cf4fc964ff`, or with hyphens in groups of 8, 4, 4, 4 and 12, as in `6f9619ff-8b86-d011-b42d-00cf4fc964ff`. |
| `alpha` | One or more Unicode letters, including letters outside the Basic Multilingual Plane, such as `𝒜`. |

- Constraint names are compared without regard to case: `{id:INT}` is `{id:int}`.
- Every check is the same in every culture and applies to the decoded text exactly as it is captured. Nothing is trimmed, so a value with white space, such as `%2042`, never satisfies a number, `bool` or `guid`. Only the characters in the table are allowed: no thousands separators, exponents, currency symbols, trailing signs or digits other than ASCII `0` to `9`, such as `٣`, and no braces or parentheses around a guid.
- `alpha` is letters only. Digits, spaces, punctuation and combining marks are not letters, so decomposed text such as `e` followed by a combining acute accent (`e%CC%81`) does not satisfy it, though the single character `é` does. Some text keeps combining marks even in NFC, such as most Devanagari words, and never satisfies it.
- The captured value is the decoded text, unchanged: `+007` is captured as `+007`, not `7`.
- Several texts can denote one value, such as `7`, `007` and `+7`, `true` and `TRUE`, or a guid in upper and lower case. Each satisfies the constraint and is captured as written, and `Format` writes whatever text it is given, so producing one canonical link for each value is the job of the code that turns typed values into text.

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
- `Format` throws `ArgumentNullException` for a `null` dictionary. It throws `ArgumentException` when a parameter that is not optional and not a catch-all has no entry, when a parameter has no entry under its name as written and more than one whose key equals it ignoring case, or when its value is `null`, empty, contains a lone surrogate, which cannot be encoded and read back, does not satisfy the parameter's [constraint](#constraints), because the path would not match, or is `.` or `..`. For a parameter without a constraint, any other value is allowed, including white space and control characters.
- A catch-all value also cannot have an empty part: `Format` throws `ArgumentException` for one that starts or ends with `/` or contains `//`, because the empty segment would be dropped when the path is matched, and the match would not return the same value. It throws the same for a catch-all value with `.` or `..` as a part.
- An optional or catch-all parameter that has an entry is checked the same way. A `null` or empty value does not leave it out; leave its entry out of the dictionary instead.
- `Format` also throws `ArgumentException` when an optional parameter has a value but an earlier optional parameter was left out, because its segment cannot be written without the earlier one. For `/a/{b?}/{c?}`, a value for `c` without one for `b` is rejected.
- The first parameter in template order that cannot be formatted is the one reported. A value that is not valid is reported before the fact that an earlier optional parameter was left out, a lone surrogate in a catch-all value is reported before an empty part, and a dot segment is reported after every other problem with the value.

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
- a parameter whose constraint is not one of the [built-in constraints](#constraints), such as `{id:slug}` or `{id: int}`; the message lists the supported names,
- a [catch-all parameter](#catch-all-parameters) that is optional, as in `{*rest?}`, or has a constraint, as in `{*rest:int}`,
- a literal segment that starts or ends with white space, as in `/ {id}` (spaces inside a segment, as in `/my files`, are allowed),
- `{` or `}` anywhere except around a whole-segment parameter, as in `item{id}` or `{a}{b}`,
- a literal segment that contains `?` or `#`, which cannot appear in the path of a URI,
- a literal segment that is a [dot segment](#dot-segments), as written or encoded, as in `/a/../b`, `/a/./b` or `/a/%2E%2E/b`,
- a literal or a parameter that is not optional, including a catch-all parameter, after an optional parameter, as in `/{id?}/details` or `/{id?}/{*rest}`; the message names the optional parameter, and
- any segment after a catch-all parameter, as in `/files/{*path}/details`; the message names the catch-all parameter.

The rules for names and constraints apply to a parameter with a first `*` and a final `?` removed, so `{?}` and `{*}` have an invalid name and `{id:?}` has no constraint after `:`.

`Parse` reports the first invalid segment, reading left to right. Within a segment, a control character is reported first, then a lone surrogate, then any other problem with the segment's own text, of which a dot segment is the last, then a segment that follows a catch-all parameter or follows an optional parameter without being one, then a repeated name. Within a parameter, a `*` that is not first is reported first, then a `?` that is not last, then an invalid name, then a catch-all parameter that is optional, then a catch-all parameter with a constraint, then more than one `:`, then an empty constraint, then an unknown one. A control character is reported even when it is also white space, such as a tab at the start of a segment. The exception message shows a control character or a lone surrogate as a `\uXXXX` escape, such as `\u0009`, rather than printing it.

Apart from the dot segment rule, these checks apply to the text as written, before decoding. To match one of these characters literally, percent-encode it: `%09` for a tab, `%0A` for a line feed, `%00` for a null character (and the UTF-8 bytes of any other control character, such as `%C2%85` for U+0085), `%7B` for `{`, `%7D` for `}`, `%3F` for `?`, `%23` for `#` and `%20` for a space at the start or end of a segment. For example, `/what%3F` matches the path `/what%3F`, `/a%09b` matches a path whose segment is `a`, a tab and `b`, and `/%7Bid%7D` is a literal that matches the path `/{id}`. A lone surrogate has no encoded form: a surrogate has no UTF-8 bytes of its own, and the three bytes it would encode to, such as `%ED%A0%80`, are not valid UTF-8, so that text decodes to itself and the literal is that percent text. Encoding does not help with a dot segment either: `%2E%2E` is `..` once decoded, and the rule applies to the decoded text.
