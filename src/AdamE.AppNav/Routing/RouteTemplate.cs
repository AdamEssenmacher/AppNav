using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace AdamE.AppNav.Routing;

/// <summary>
/// A parsed route template: the path pattern that a route answers to, such as <c>/products/{id}</c>.
/// </summary>
/// <remarks>
/// <para>
/// A template is a path that starts with <c>/</c> and is divided into segments by <c>/</c>. A segment written
/// <c>{name}</c> is a parameter: it matches any one path segment and captures that segment's decoded text under its
/// name. Every other segment is a literal that a path must contain exactly, in the same position.
/// </para>
/// <para>
/// A parameter name starts with an ASCII letter or an underscore, followed by ASCII letters, digits or underscores.
/// Names are compared without regard to case, so a template cannot use two names that differ only by case, and
/// captured values are looked up the same way.
/// </para>
/// <para>
/// A parameter written <c>{name:constraint}</c> matches a segment only when the segment's decoded text satisfies the
/// constraint, one of the built-in constraints <c>int</c>, <c>long</c>, <c>decimal</c>, <c>bool</c>, <c>guid</c>,
/// <c>alpha</c>, <c>sbyte</c>, <c>byte</c>, <c>short</c>, <c>ushort</c>, <c>uint</c>, <c>ulong</c>, <c>float</c> and
/// <c>double</c>, whose names are compared without regard to case. Each checks the text exactly as decoded, without
/// trimming, and independently of culture: the integer types accept an optional leading <c>+</c> or <c>-</c> and
/// ASCII digits within the type's range; <c>decimal</c>, <c>float</c> and <c>double</c> also accept one <c>.</c>,
/// with no exponent, within the type's range; <c>bool</c> accepts <c>true</c> or <c>false</c> in any ASCII case;
/// <c>guid</c> accepts only 32 hex digits, in either case, with hyphens in the 8-4-4-4-12 form; and <c>alpha</c>
/// accepts one or more Unicode letters. The captured value is the decoded text, unchanged. A template mapped on a
/// <see cref="RouteTableBuilder"/> can also name a constraint added to that builder with
/// <see cref="RouteTableBuilder.AddConstraint(string, Func{string, bool})"/>; <see cref="Parse(string)"/> knows only
/// the built-in ones, so the <see cref="Value"/> of such a template, read from <see cref="RouteTable.Definitions"/>,
/// cannot be given to it.
/// </para>
/// <para>
/// A parameter written <c>{name?}</c> or <c>{name:constraint?}</c> is optional: it matches one segment or none. The
/// <c>?</c> must be the last character inside the braces. Optional parameters must be trailing: every segment after
/// an optional parameter must also be an optional parameter, so <c>/products/{id?}</c> and <c>/a/{b?}/{c?}</c> are
/// valid and <c>/{id?}/details</c> is not. A path may leave out optional parameters from the end only, so the
/// present segments fill the template's positions from left to right.
/// </para>
/// <para>
/// A parameter written <c>{*name}</c> is a catch-all: it matches the rest of the path, zero or more segments, and
/// captures their decoded text joined with <c>/</c>. The <c>*</c> must be the first character inside the braces,
/// and a catch-all cannot have a constraint or be optional. It must be the last segment, and cannot follow an
/// optional parameter, so that the segments it captures always start at the same position, whatever the path.
/// </para>
/// <para>
/// Templates and paths are divided the same way. Leading, trailing and repeated slashes do not create segments, so
/// <c>/a//b/</c> has the same segments as <c>/a/b</c>, and <c>/</c> is the root template, which has no segments.
/// Each segment is then percent-decoded once, so <c>%2F</c> is part of its segment and does not divide it, except
/// in the segments a catch-all parameter captures, where it acts as <c>/</c>. A percent sequence that is not valid,
/// or does not decode to valid UTF-8, is kept as written.
/// </para>
/// <para>
/// A literal is compared with a decoded path segment ordinally and case-sensitively, code unit by code unit, in
/// every culture alike. No Unicode normalization is applied to templates, paths or captured values, so the same
/// visible text in two normalization forms does not match: <c>/caf%C3%A9</c>, with the single character U+00E9, does
/// not match <c>/cafe%CC%81</c>, with <c>e</c> followed by the combining accent U+0301. A captured value keeps the
/// form the path had. To accept both forms, write templates in NFC and, before matching, replace each path segment
/// with its decoded text normalized to NFC and percent-encoded again; normalizing the path as written changes
/// nothing that is percent-encoded, because percent-encoded text is ASCII.
/// </para>
/// <para>
/// A dot segment, one whose decoded text is exactly <c>.</c> or <c>..</c>, is never part of a route, because a URI
/// parser may remove it when it resolves a path. <see cref="Uri"/> removes it, written or percent-encoded, from a URI
/// with an authority, such as <c>https://host/a/../b</c>, and keeps it, decoding <c>%2E</c>, in one without, such as
/// <c>app:/a/../b</c>, as relative URIs and plain strings do, so the rule is enforced here rather than left to the
/// parser: a literal cannot be one, a path that has one in any position does not match, and
/// <see cref="Format(IReadOnlyDictionary{string, string})"/> does not write one. Other text that contains dots, such as
/// <c>...</c> or <c>.well-known</c>, is ordinary.
/// </para>
/// <para>
/// A segment cannot contain a control character (one for which <see cref="char.IsControl(char)"/> is
/// <see langword="true"/>, such as a tab or line feed). A literal segment also cannot start or end with white space,
/// or contain <c>{</c>, <c>}</c>, <c>?</c> or <c>#</c>. To match those characters, percent-encode them in the
/// template: for example <c>%09</c> for a tab, <c>%0A</c> for a line feed, <c>%00</c> for a null character,
/// <c>%20</c>, <c>%7B</c>, <c>%7D</c>, <c>%3F</c> and <c>%23</c>. Encoding does not make a dot segment a literal,
/// because that rule applies to the decoded text.
/// </para>
/// <para>
/// A segment also cannot contain a lone surrogate, a <see langword="char"/> that is half of a surrogate pair without
/// its other half, because such text is not valid UTF-16 and cannot be percent-encoded. A surrogate has no UTF-8
/// bytes of its own, and the three bytes it would encode to, such as <c>%ED%A0%80</c>, are not valid UTF-8, so that
/// text decodes to itself and the literal is that percent text.
/// </para>
/// </remarks>
public sealed class RouteTemplate
{
    // The ranks of a template's positions, in the order a route table tries them, lowest first: what a position must
    // match widens from fixed text, through a constrained value, to any value, then to any number of segments.
    // EndedRank, for a position past a template's last segment, must sit above every optional kind and the catch-all.
    // How far above is arbitrary, because a template that has ended and one that requires another segment never
    // match the same path; it must still be one fixed rank, so that the comparison stays consistent.
    private const int LiteralRank = 0;
    private const int ConstrainedParameterRank = 1;
    private const int EndedRank = 2;
    private const int ConstrainedOptionalParameterRank = 3;
    private const int ParameterRank = 4;
    private const int OptionalParameterRank = 5;
    private const int CatchAllParameterRank = 6;

    private readonly RouteSegment[] _segments;
    private readonly int _parameterCount;

    // The number of segments before the first optional or catch-all parameter, which is the fewest segments a matching
    // path has.
    private readonly int _requiredCount;

    // Whether the last segment is a catch-all parameter, which takes every path segment after the required ones.
    private readonly bool _endsInCatchAll;

    private RouteTemplate(string value, RouteSegment[] segments)
    {
        Value = value;
        _segments = segments;

        // The parser accepts segments that can be left out only after every other segment, and a catch-all parameter
        // only as the last one, so the first segment that can be left out is where the required segments end.
        var parameterCount = 0;
        int requiredCount = segments.Length;
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].IsParameter)
                parameterCount++;

            if (segments[i].CanBeLeftOut && requiredCount == segments.Length)
                requiredCount = i;
        }

        _parameterCount = parameterCount;
        _requiredCount = requiredCount;
        _endsInCatchAll = segments.Length > 0 && segments[^1].IsCatchAll;
    }

    /// <summary>
    /// Gets the template text exactly as it was passed to <see cref="Parse(string)"/>.
    /// </summary>
    public string Value { get; }

    // The parsed segments, for comparing templates with each other; a span, so that callers cannot change them.
    internal ReadOnlySpan<RouteSegment> Segments => _segments;

    /// <summary>
    /// Parses a route template.
    /// </summary>
    /// <param name="value">The template text. It must start with <c>/</c>.</param>
    /// <returns>The parsed template.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <para>
    /// <paramref name="value"/> is empty or consists only of white space or does not start with <c>/</c>; a segment
    /// contains a control character or a lone surrogate; a parameter contains <c>*</c> other than as its first
    /// character, contains <c>?</c> other than as its last character, has an invalid name, repeats the name of an
    /// earlier parameter, ignoring case, contains more than one <c>:</c>, or has an empty constraint or one that is not
    /// built in, since a constraint added with
    /// <see cref="RouteTableBuilder.AddConstraint(string, Func{string, bool})"/> is known only to the templates mapped
    /// on that builder; a catch-all parameter is optional or has a constraint; a literal segment starts or ends with
    /// white space, or contains <c>{</c>, <c>}</c>, <c>?</c> or <c>#</c>; a literal segment is a dot segment, <c>.</c>
    /// or <c>..</c> once decoded; a literal or a parameter that is not optional, including a catch-all parameter,
    /// follows an optional parameter; or any segment follows a catch-all parameter. Apart from the dot segment, which
    /// no encoding avoids, these checks apply to the text as written, before percent-decoding.
    /// </para>
    /// <para>
    /// The rules for names and constraints apply to a parameter with a first <c>*</c> and a final <c>?</c> removed, so
    /// <c>{?}</c> and <c>{*}</c> have an invalid name and <c>{id:?}</c> an empty constraint.
    /// </para>
    /// <para>
    /// The first invalid segment, reading left to right, is the one reported. A rule that involves two segments, such
    /// as a repeated name or a segment after a catch-all parameter, is broken by the later one. When a segment has
    /// more than one problem, which one is reported is not specified. The message shows each control character and
    /// lone surrogate as a <c>\uXXXX</c> escape.
    /// </para>
    /// </exception>
    public static RouteTemplate Parse(string value)
    {
        return Parse(value, nameof(value), resolveAdded: null);
    }

    // Parses a template that a public method received under another name, so that its exceptions name that argument
    // with the same messages, rather than value. A builder passes resolveAdded, as RouteTemplateParser.Parse describes,
    // so that its templates can name the constraints it adds.
    internal static RouteTemplate Parse(string value, string paramName, Func<string, RouteConstraint>? resolveAdded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        if (!value.StartsWith('/'))
            throw new ArgumentException("Route templates must start with '/'.", paramName);

        return new RouteTemplate(value, RouteTemplateParser.Parse(value, paramName, resolveAdded));
    }

    /// <summary>
    /// Matches a path against this template.
    /// </summary>
    /// <param name="path">
    /// The path to match, such as <c>/products/42</c>. A leading <c>/</c> is optional. The whole value is the path: it
    /// is not trimmed, and a query string or fragment is not removed.
    /// </param>
    /// <returns>
    /// The values captured by the template's parameters, keyed by parameter name as written in the template and
    /// looked up without regard to case, or <see langword="null"/> when the path does not match. An optional
    /// parameter that the path leaves out has no entry, and neither has a catch-all parameter when nothing is left of
    /// the path. Each match that captures a value returns its own dictionary; treat it as read-only. When nothing is
    /// captured, the dictionary is empty.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A path matches when it has at least as many segments as the template has before its first optional or
    /// catch-all parameter and, unless the template ends in a catch-all parameter, no more than the template has in
    /// all, and each segment in the position of a literal or an ordinary parameter is accepted by it: a decoded
    /// segment in a literal's position equals that literal, compared ordinally and case-sensitively without Unicode
    /// normalization, and a decoded segment in a parameter's position satisfies the parameter's constraint, if it has
    /// one. The optional parameters after the path's last segment are left out.
    /// </para>
    /// <para>
    /// A catch-all parameter captures every remaining segment, each decoded, joined with a single <c>/</c>. Repeated,
    /// leading and trailing slashes are therefore not reproduced. A <c>%2F</c> in a remaining segment behaves exactly
    /// like <c>/</c>, including that it does not create an empty part, so <c>/files/a%2F</c> captures <c>a</c> and a
    /// catch-all whose remaining segments decode to nothing but slashes has no entry.
    /// </para>
    /// <para>
    /// A segment that does not satisfy a constraint is not an error: the path simply does not match, even when the
    /// parameter is optional, because a segment that is present is never treated as left out. The value captured by a
    /// parameter that is not a catch-all is the decoded path segment, unchanged even when a constraint checked it, so
    /// it contains <c>/</c> where the path has <c>%2F</c>. White space is significant and <c>+</c> is not a space.
    /// </para>
    /// <para>
    /// A path that has a dot segment, as described in the remarks on <see cref="RouteTemplate"/>, does not match,
    /// wherever it is and however it is encoded, including as a part of what a catch-all parameter would capture, as in
    /// <c>/files/a%2F..%2Fb</c>. The dots are not resolved.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exception">
    /// Any exception that the function of a constraint added with
    /// <see cref="RouteTableBuilder.AddConstraint(string, Func{string, bool})"/> throws, unchanged, for a template
    /// mapped on that builder, such as one read from <see cref="RouteTable.Definitions"/>.
    /// </exception>
    public IReadOnlyDictionary<string, string>? Match(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var decodedCount = 0;
        return Match(RouteText.SplitSegments(path), ref decodedCount);
    }

    // Matches a path already split into segments, decoding them in place. The segments before decodedCount are always
    // decoded and the rest never are, so a route table can split a path once and share it between templates: no
    // segment is decoded twice, however many templates are tried, and none is read undecoded.
    internal IReadOnlyDictionary<string, string>? Match(string[] pathSegments, ref int decodedCount)
    {
        if (!Matches(pathSegments, ref decodedCount, out bool containsSlash))
            return null;

        // The segments in the template's own positions; with a catch-all, those before it.
        int positionalCount = _endsInCatchAll ? _requiredCount : pathSegments.Length;

        // Null when nothing is left, or only slashes decoded from '%2F', so that the catch-all has no entry.
        string? rest = _endsInCatchAll ? CaptureRemaining(pathSegments, positionalCount, containsSlash) : null;

        // Without a catch-all, the segments the path leaves out are all optional parameters, because only optional
        // parameters are trailing. With one, the catch-all is the only parameter that can be left out.
        int capturedCount = _endsInCatchAll
            ? _parameterCount - (rest is null ? 1 : 0)
            : _parameterCount - (_segments.Length - pathSegments.Length);
        if (capturedCount == 0)
            return ReadOnlyDictionary<string, string>.Empty;

        var values = new Dictionary<string, string>(capturedCount, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < positionalCount; i++)
        {
            RouteSegment segment = _segments[i];
            if (segment.IsParameter)
                values.Add(segment.Text, pathSegments[i]);
        }

        if (rest is not null)
            values.Add(_segments[^1].Text, rest);

        return values;
    }

    // Whether the template matches a path already split into segments, decoding them in place as Match does, but
    // without capturing anything, so that a route table can find which template a path selects without allocating.
    // Match decides by this same method, so the two never disagree.
    internal bool Matches(string[] pathSegments, ref int decodedCount)
    {
        return Matches(pathSegments, ref decodedCount, out _);
    }

    // containsSlash says whether a segment a catch-all captures decoded to text with a '/', which only capturing
    // needs.
    private bool Matches(string[] pathSegments, ref int decodedCount, out bool containsSlash)
    {
        containsSlash = false;
        if (pathSegments.Length < _requiredCount || (!_endsInCatchAll && pathSegments.Length > _segments.Length))
            return false;

        // The segments in the template's own positions; with a catch-all, those before it.
        int positionalCount = _endsInCatchAll ? _requiredCount : pathSegments.Length;

        // Every positional segment is checked, against its literal or its parameter's constraint, before anything
        // else is allocated, so a path that does not match allocates no dictionary.
        for (var i = 0; i < positionalCount; i++)
        {
            if (!_segments[i].Accepts(Decode(pathSegments, ref decodedCount, i)))
                return false;
        }

        // The path does not match when a segment a catch-all captures, or any part a decoded '/' divides one into, is
        // a dot segment, which Format could not write back.
        for (int i = positionalCount; i < pathSegments.Length; i++)
        {
            string decoded = Decode(pathSegments, ref decodedCount, i);
            if (RouteText.HasDotSegmentPart(decoded))
                return false;

            containsSlash |= decoded.Contains('/', StringComparison.Ordinal);
        }

        return true;
    }

    // Compares which of two templates a route table tries first: negative when this one is, positive when the other
    // is, and zero when they rank equally at every position. The first position whose ranks differ decides. Every
    // position has exactly one rank, including Ended for one past a template's last segment, so the comparison is a
    // total preorder: it is transitive, so sorting by it gives the same order whatever order the routes were mapped
    // in. If a missing segment instead beat every optional parameter but lost to every required one, /a would beat
    // /a/{id:int?}, which beats /a/{name}, which would beat /a, and the winner would depend on registration order.
    internal int ComparePrecedence(RouteTemplate other)
    {
        int length = Math.Max(_segments.Length, other._segments.Length);
        for (var i = 0; i < length; i++)
        {
            int difference = Rank(_segments, i) - Rank(other._segments, i);
            if (difference != 0)
                return difference;
        }

        return 0;
    }

    // Says whether this template and another of the same shape can both match a path, from their required positions
    // alone, because a path that leaves out every optional parameter and the catch-all matches both whatever their
    // constraints. They are disjoint when the constraints at some required position accept no text in common, which
    // two built-in constraints can be known to do, and a pair in disjoint is declared to do. Otherwise, path is one
    // that both match, with only the required segments, when a common segment is known at every required position;
    // when one is not, which only an added constraint causes, the templates may overlap, path is null and position is
    // the first required position whose segment is not known. Every position is checked before the path is built,
    // because most templates of one shape in a table differ in a constraint that keeps them apart, and those
    // comparisons then allocate nothing.
    internal ConstraintOverlap FindCommonPath(
        RouteTemplate other,
        DisjointConstraints disjoint,
        out string? path,
        out int position)
    {
        Debug.Assert(SameTemplateComparer.SameShape.Equals(this, other), "Only templates of one shape are compared.");

        path = null;
        position = -1;
        for (var i = 0; i < _requiredCount; i++)
        {
            ConstraintOverlap overlap = CommonSegment(other, i, disjoint, out _);
            if (overlap == ConstraintOverlap.Disjoint)
            {
                position = -1;
                return ConstraintOverlap.Disjoint;
            }

            if (overlap == ConstraintOverlap.MayOverlap && position < 0)
                position = i;
        }

        if (position >= 0)
            return ConstraintOverlap.MayOverlap;

        if (_requiredCount == 0)
        {
            path = "/";
            return ConstraintOverlap.CommonValue;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < _requiredCount; i++)
        {
            CommonSegment(other, i, disjoint, out string? segment);
            builder.Append('/').Append(segment);
        }

        path = builder.ToString();
        return ConstraintOverlap.CommonValue;
    }

    // The positions that advice on keeping this template apart from another of its shape is about: the required ones,
    // or, when required is false, the first one after them, if there is one. Templates that only optional parameters
    // tell apart are kept apart by mapping each alternative as its own template, and the first optional position is
    // then required in every one of those, so it alone decides whether they are apart: when it keeps them apart no
    // later position matters, and when it does not, the two shortest of them conflict whatever a later one holds.
    // Every piece of that advice looks at this one range, so that none of it speaks of a position that cannot help.
    private (int Start, int End) AdvicePositions(bool required)
    {
        return required
            ? (0, _requiredCount)
            : (_requiredCount, Math.Min(_requiredCount + 1, _segments.Length));
    }

    // Whether this template or another of the same shape has an added constraint at one of the positions that
    // AdvicePositions gives, so that a message does not offer constraints as a way to keep them apart where an added
    // constraint is involved.
    internal bool HasAddedConstraint(RouteTemplate other, bool required)
    {
        Debug.Assert(SameTemplateComparer.SameShape.Equals(this, other), "Only templates of one shape are compared.");

        (int start, int end) = AdvicePositions(required);
        for (int i = start; i < end; i++)
        {
            if (_segments[i].Constraint is { IsBuiltIn: false } ||
                other._segments[i].Constraint is { IsBuiltIn: false })
            {
                return true;
            }
        }

        return false;
    }

    // Whether this template and another of the same shape have, at their first position after the required ones, two
    // constraints declared disjoint. Mapping each alternative as its own template makes that position required in
    // every one of them, so it alone keeps them apart, whatever follows it; and when it does not, the two shortest of
    // them still conflict there, whatever is declared for a later position.
    internal bool HasDeclaredFirstAlternative(RouteTemplate other, DisjointConstraints disjoint)
    {
        Debug.Assert(SameTemplateComparer.SameShape.Equals(this, other), "Only templates of one shape are compared.");

        if (_requiredCount == _segments.Length)
            return false;

        RouteConstraint? constraint = _segments[_requiredCount].Constraint;
        RouteConstraint? otherConstraint = other._segments[_requiredCount].Constraint;
        return constraint is not null && otherConstraint is not null && disjoint.Contains(constraint, otherConstraint);
    }

    // The first position where this template and another of the same shape have two different constraints, one of them
    // added, that are neither declared disjoint nor, at a required position, known to accept a text in common, so that
    // declaring them disjoint could keep the templates apart, or -1 when there is none. The positions are those that
    // AdvicePositions gives. A constraint with itself can never be declared disjoint, so a position where both
    // templates have the same constraint is never given.
    internal int FindUndeclaredPosition(RouteTemplate other, DisjointConstraints disjoint, bool required)
    {
        Debug.Assert(SameTemplateComparer.SameShape.Equals(this, other), "Only templates of one shape are compared.");

        (int start, int end) = AdvicePositions(required);
        for (int i = start; i < end; i++)
        {
            RouteConstraint? constraint = _segments[i].Constraint;
            RouteConstraint? otherConstraint = other._segments[i].Constraint;
            if (constraint is null || otherConstraint is null || ReferenceEquals(constraint, otherConstraint) ||
                (constraint.IsBuiltIn && otherConstraint.IsBuiltIn) || disjoint.Contains(constraint, otherConstraint))
            {
                continue;
            }

            // Finding the templates ambiguous has already asked the functions at the required positions about the
            // samples, so asking again there can throw nothing new. It never asks those at the optional positions,
            // which keep no templates apart, so they are not asked here either: an exception one of them threw would
            // replace the error being written. A pair there is offered without knowing whether a sample satisfies
            // both, and Build checks the declaration if it is then made.
            if (!required ||
                RouteConstraint.FindCommonValue(constraint, otherConstraint, out _) == ConstraintOverlap.MayOverlap)
            {
                return i;
            }
        }

        return -1;
    }

    // The constraints of this template and another of the same shape at a position, for a message that names them.
    internal (RouteConstraint Constraint, RouteConstraint OtherConstraint) ConstraintsAt(
        RouteTemplate other,
        int position)
    {
        return (_segments[position].Constraint!, other._segments[position].Constraint!);
    }

    // Whether two templates of the same shape have the same required segments, so that only their optional
    // parameters, which never keep templates apart, distinguish them, and a message must not advise changing their
    // constraints. Literals are already the same within a shape, so only the constraints are compared, by reference,
    // which holds for added constraints too, since a builder has one instance for each name.
    internal bool HasSameRequiredSegments(RouteTemplate other)
    {
        Debug.Assert(SameTemplateComparer.SameShape.Equals(this, other), "Only templates of one shape are compared.");

        for (var i = 0; i < _requiredCount; i++)
        {
            if (!ReferenceEquals(_segments[i].Constraint, other._segments[i].Constraint))
                return false;
        }

        return true;
    }

    // The text of a path segment that both templates accept at a required position: a literal as Format writes it,
    // which is the same for both, a letter for parameters without a constraint, which take any text, or a value both
    // constraints accept, if one is known. Within a shape, one parameter has a constraint exactly when the other does.
    // A pair declared disjoint is taken at its word, without calling a function: Build has already checked it against
    // the samples, so none would be found in common.
    private ConstraintOverlap CommonSegment(
        RouteTemplate other,
        int position,
        DisjointConstraints disjoint,
        out string? text)
    {
        RouteSegment segment = _segments[position];
        if (!segment.IsParameter)
        {
            text = segment.EncodedText;
            return ConstraintOverlap.CommonValue;
        }

        RouteConstraint? constraint = segment.Constraint;
        RouteConstraint? otherConstraint = other._segments[position].Constraint;
        if (constraint is null || otherConstraint is null)
        {
            text = "x";
            return ConstraintOverlap.CommonValue;
        }

        if (disjoint.Contains(constraint, otherConstraint))
        {
            text = null;
            return ConstraintOverlap.Disjoint;
        }

        return RouteConstraint.FindCommonValue(constraint, otherConstraint, out text);
    }

    // The rank of a position, from the kind and the constraint of its segment, or Ended past the last one. It is
    // written out rather than taken from RouteSegmentKind, whose order says nothing about precedence.
    private static int Rank(RouteSegment[] segments, int position)
    {
        if (position >= segments.Length)
            return EndedRank;

        RouteSegment segment = segments[position];
        return segment.Kind switch
        {
            RouteSegmentKind.Literal => LiteralRank,
            RouteSegmentKind.Parameter => segment.Constraint is null ? ParameterRank : ConstrainedParameterRank,
            RouteSegmentKind.OptionalParameter =>
                segment.Constraint is null ? OptionalParameterRank : ConstrainedOptionalParameterRank,
            RouteSegmentKind.CatchAllParameter => CatchAllParameterRank,
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Builds the path that this template answers to, using the given parameter values.
    /// </summary>
    /// <param name="pathValues">
    /// The value of each of the template's parameters, keyed by parameter name without regard to case, as everywhere
    /// else in a template. Each name is looked up as written in the template first; only if that finds nothing is the
    /// dictionary searched for a key that equals the name ignoring case. Entries for names the template does not use
    /// are ignored. An optional or catch-all parameter is left out when it has no entry, neither under its name as
    /// written nor under a key that equals it ignoring case.
    /// </param>
    /// <returns>
    /// The path: <c>/</c> followed by the segments separated by <c>/</c>, with no trailing slash; <c>/</c> for the root
    /// template, and when the template has only optional parameters, or only a catch-all parameter, and none is given.
    /// Each literal is written as its decoded text and each parameter as its value, both percent-encoded with
    /// <see cref="Uri.EscapeDataString(string)"/>, so a <c>/</c> in a value is written as <c>%2F</c>. A catch-all
    /// parameter's value is split at each <c>/</c> instead, and each part is encoded and written as its own segment, so
    /// <c>a/b c</c> is written as <c>a/b%20c</c>. An optional or catch-all parameter that is left out is not written,
    /// and neither is anything after it, so <c>/products/{id?}</c> formats as <c>/products</c> without an <c>id</c>,
    /// and <c>/{*path}</c> formats as <c>/</c> without a <c>path</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <see cref="Match(string)"/> reads back what this method writes: for every template and every dictionary this
    /// method accepts, matching the formatted path returns exactly one entry for each required parameter, each optional
    /// parameter that was given and the catch-all parameter if it was given, with the values that were formatted. In
    /// the other direction, formatting the values that a match returned produces a path that matches again with the
    /// same values, unless the matched path itself contained a lone surrogate in a parameter's position:
    /// <see cref="Match(string)"/> does not check paths for one, and this method rejects such a value. The path never
    /// contains a dot segment, so a URI parser removes none of its segments as dot segments.
    /// </para>
    /// <para>
    /// That path need not have the same text as the path that was matched. The output is canonical: the same values
    /// always format to the same path, whatever path they were matched from. Every segment is encoded the same way,
    /// whether or not the original text was encoded, so <c>/my files</c> and <c>/my%20files</c> both format as
    /// <c>/my%20files</c>, and repeated slashes are not reproduced.
    /// </para>
    /// <para>
    /// A value for a parameter with a constraint must satisfy it, so that the path matches. A value for a catch-all
    /// parameter cannot have an empty part: it cannot start or end with <c>/</c> or contain <c>//</c>, because the
    /// empty segment would be dropped when the path is matched. A value cannot be a dot segment, as described in the
    /// remarks on <see cref="RouteTemplate"/>, and neither can a part of a catch-all value, because a URI parser may
    /// remove it and the path would not match. Values are not otherwise checked: white space, control
    /// characters, <c>%</c>, <c>/</c> and any other text, including <c>...</c>, are encoded and read back unchanged,
    /// except that in a catch-all value each <c>/</c> separates two segments.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pathValues"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A parameter that is not optional or catch-all has no entry in <paramref name="pathValues"/>; a parameter has no
    /// entry under its name as written and more than one whose key equals it ignoring case; a parameter's value is
    /// <see langword="null"/>, empty, not valid UTF-16 (it contains a lone surrogate, which cannot be percent-encoded
    /// and read back unchanged), is <c>.</c> or <c>..</c>, or does not satisfy the parameter's constraint; a catch-all
    /// parameter's value starts or ends with <c>/</c>, contains <c>//</c>, or has <c>.</c> or <c>..</c> as a part
    /// between slashes; or an optional parameter has a value although an earlier optional parameter was left out, so
    /// its segment cannot be written. These rules apply to optional and catch-all parameters that have an entry
    /// exactly as to other parameters: a <see langword="null"/> or empty value does not leave a parameter out. The
    /// first parameter in template order that cannot be formatted is the one reported. When a parameter has more than
    /// one problem, which one is reported is not specified.
    /// </exception>
    /// <exception cref="Exception">
    /// Any exception that the function of a constraint added with
    /// <see cref="RouteTableBuilder.AddConstraint(string, Func{string, bool})"/> throws, unchanged, for a template
    /// mapped on that builder, such as one read from <see cref="RouteTable.Definitions"/>.
    /// </exception>
    public string Format(IReadOnlyDictionary<string, string> pathValues)
    {
        ArgumentNullException.ThrowIfNull(pathValues);

        return TryFormat(pathValues, out string? path, out PathFormatFailure failure)
            ? path
            : throw FormatFailed(failure);
    }

    // Writes the path, or reports the first parameter in template order that cannot be written and why, so that
    // RouteTable.Format applies exactly these checks and words its own message. Throws only for a dictionary with more
    // than one key that equals a name ignoring case, which a dictionary that compares keys that way cannot have.
    internal bool TryFormat(
        IReadOnlyDictionary<string, string> pathValues,
        [NotNullWhen(true)] out string? path,
        out PathFormatFailure failure)
    {
        failure = default;
        path = null;
        if (_segments.Length == 0)
        {
            path = "/";
            return true;
        }

        var builder = new StringBuilder(Value.Length + (16 * _parameterCount));
        string? leftOut = null;
        foreach (RouteSegment segment in _segments)
        {
            if (!segment.IsParameter)
            {
                builder.Append('/').Append(segment.EncodedText);
                continue;
            }

            // Every later parameter is still looked up and checked after one is left out, so that a value given for
            // one of them is reported rather than silently dropped.
            if (!TryGetPathValue(pathValues, segment.Text, out string? value))
            {
                if (!segment.CanBeLeftOut)
                {
                    failure = new PathFormatFailure(PathValueProblem.Missing, segment, null, "");
                    return false;
                }

                leftOut ??= segment.Text;
                continue;
            }

            PathValueProblem? problem = FindProblem(segment, value);
            if (problem is not null)
            {
                string hint = problem == PathValueProblem.ConstraintNotSatisfied
                    ? segment.Constraint!.HintFor(value!)
                    : "";
                failure = new PathFormatFailure(problem.Value, segment, null, hint);
                return false;
            }

            if (leftOut is not null)
            {
                failure = new PathFormatFailure(PathValueProblem.AfterLeftOut, segment, leftOut, "");
                return false;
            }

            if (segment.IsCatchAll)
                AppendCatchAll(builder, value!);
            else
                builder.Append('/').Append(Uri.EscapeDataString(value!));
        }

        path = builder.Length == 0 ? "/" : builder.ToString();
        return true;
    }

    private static ArgumentException FormatFailed(PathFormatFailure failure)
    {
        RouteSegment parameter = failure.Parameter;
        string name = parameter.Text;
        string message = failure.Problem switch
        {
            PathValueProblem.Missing => $"Path parameter '{name}' has no value.",
            PathValueProblem.Null => $"Path parameter '{name}' has a null value{HowToLeaveOut(parameter)}.",
            PathValueProblem.Empty =>
                $"Path parameter '{name}' has an empty value" +
                (parameter.CanBeLeftOut
                    ? HowToLeaveOut(parameter)
                    : "; a parameter always matches exactly one path segment") + ".",
            PathValueProblem.LoneSurrogate =>
                $"Path parameter '{name}' has a value that is not valid UTF-16: it contains a lone surrogate, " +
                "which cannot be percent-encoded and read back unchanged.",
            PathValueProblem.EmptyPart =>
                $"Path parameter '{name}' has a value with an empty part; a catch-all value cannot start or end " +
                "with '/' or contain '//', because a match drops empty segments and would not return it.",
            PathValueProblem.ConstraintNotSatisfied =>
                $"Path parameter '{name}' has a value that does not satisfy its constraint " +
                $"'{parameter.Constraint!.Name}'." + failure.Hint,
            PathValueProblem.DotSegment =>
                $"Path parameter '{name}' has a value " +
                (parameter.IsCatchAll
                    ? "with a dot segment part ('.' or '..' between slashes), "
                    : "that is a dot segment ('.' or '..'), ") +
                RouteText.DotSegmentReason + ".",
            PathValueProblem.AfterLeftOut =>
                $"Path parameter '{name}' has a value, but it cannot be written because the earlier optional " +
                $"parameter '{failure.LeftOut}' is left out.",
            _ => throw new UnreachableException(),
        };

        return new ArgumentException(message, "pathValues");
    }

    // Writes each part of a catch-all value between slashes as its own segment, so that Match reads the slashes back
    // as separators. FindProblem has already rejected a value with an empty part.
    private static void AppendCatchAll(StringBuilder builder, string value)
    {
        ReadOnlySpan<char> rest = value;
        while (true)
        {
            int separator = rest.IndexOf('/');
            ReadOnlySpan<char> part = separator < 0 ? rest : rest[..separator];
            builder.Append('/').Append(Uri.EscapeDataString(part));
            if (separator < 0)
                return;

            rest = rest[(separator + 1)..];
        }
    }

    // Returns false only when the dictionary has no entry for the name, under the name as written or a key that
    // equals it ignoring case. An entry's value can still be null, which FindProblem reports.
    private static bool TryGetPathValue(IReadOnlyDictionary<string, string> pathValues, string name, out string? value)
    {
        return pathValues.TryGetValue(name, out value) || TryGetValueIgnoringCase(pathValues, name, out value);
    }

    // The first problem with a parameter's value, in the order the documentation of Format gives, or null when it can
    // be written.
    private static PathValueProblem? FindProblem(RouteSegment parameter, string? value)
    {
        if (value is null)
            return PathValueProblem.Null;
        if (value.Length == 0)
            return PathValueProblem.Empty;
        if (RouteText.ContainsLoneSurrogate(value))
            return PathValueProblem.LoneSurrogate;
        if (parameter.IsCatchAll &&
            (value[0] == '/' || value[^1] == '/' || value.Contains("//", StringComparison.Ordinal)))
        {
            return PathValueProblem.EmptyPart;
        }

        // Checked before the constraint, as Match checks a segment, so that a constraint is never asked about a dot
        // segment, which an added constraint's function need not expect, and a dot segment is reported as one even for
        // a parameter whose constraint would reject it too.
        if (parameter.IsCatchAll ? RouteText.HasDotSegmentPart(value) : RouteText.IsDotSegment(value))
            return PathValueProblem.DotSegment;

        // Checked last, so that every other problem is reported first.
        if (parameter.Constraint is { } constraint && !constraint.IsSatisfiedBy(value))
            return PathValueProblem.ConstraintNotSatisfied;

        return null;
    }

    // A null or empty value does not leave a parameter out, because Match never returns one; saying how to leave it out
    // keeps that from looking like a bug.
    private static string HowToLeaveOut(RouteSegment parameter)
    {
        if (!parameter.CanBeLeftOut)
            return "";

        return parameter.IsCatchAll
            ? "; to leave out a catch-all parameter, leave it out of the dictionary"
            : "; to leave out an optional parameter, leave it out of the dictionary";
    }

    // Only reached when the name as written is not a key, which is also how an optional parameter is left out. Reads
    // every entry, except that it reads none when nothing can be found: in an empty dictionary, or in a Dictionary
    // that compares keys without regard to case, such as the one Match returns, whose own lookup has already tried
    // every spelling of the name.
    private static bool TryGetValueIgnoringCase(
        IReadOnlyDictionary<string, string> pathValues,
        string name,
        out string? value)
    {
        value = null;
        if (pathValues.Count == 0 ||
            (pathValues is Dictionary<string, string> dictionary &&
             dictionary.Comparer.Equals(StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        string? foundKey = null;
        foreach (KeyValuePair<string, string> entry in pathValues)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(entry.Key, name))
                continue;

            if (foundKey is not null)
            {
                throw new ArgumentException(
                    $"Path parameter '{name}' is ambiguous: keys '{RouteText.EscapeForMessage(foundKey)}' and " +
                    $"'{RouteText.EscapeForMessage(entry.Key)}' both equal it ignoring case.",
                    nameof(pathValues));
            }

            foundKey = entry.Key;
            value = entry.Value;
        }

        return foundKey is not null;
    }

    // Returns the decoded segment at index, first decoding in place every segment from decodedCount up to it, so that
    // the segments before decodedCount are always decoded and the rest never are, in whatever order segments are
    // asked for.
    private static string Decode(string[] pathSegments, ref int decodedCount, int index)
    {
        while (decodedCount <= index)
        {
            pathSegments[decodedCount] = Uri.UnescapeDataString(pathSegments[decodedCount]);
            decodedCount++;
        }

        return pathSegments[index];
    }

    // Joins the decoded segments from start on with '/', or returns null when there is nothing to join. A single
    // segment is returned as it is, and string.Join allocates the joined value once, at its final length; only a
    // decoded '/', which the caller has looked for while decoding, needs the slower path.
    private static string? CaptureRemaining(string[] pathSegments, int start, bool containsSlash)
    {
        int count = pathSegments.Length - start;
        if (!containsSlash)
            return count == 0 ? null : count == 1 ? pathSegments[start] : string.Join('/', pathSegments, start, count);

        // A decoded '/' separates parts like any other, so the empty parts it leaves are dropped as empty path segments
        // are, and Format can write back every value this returns.
        // The joined value is never longer than the decoded segments and the slashes between them.
        int capacity = count - 1;
        for (int i = start; i < pathSegments.Length; i++)
            capacity += pathSegments[i].Length;

        var builder = new StringBuilder(capacity);
        for (int i = start; i < pathSegments.Length; i++)
        {
            ReadOnlySpan<char> segment = pathSegments[i];
            foreach (Range range in segment.Split('/'))
            {
                ReadOnlySpan<char> part = segment[range];
                if (part.IsEmpty)
                    continue;

                if (builder.Length > 0)
                    builder.Append('/');

                builder.Append(part);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
