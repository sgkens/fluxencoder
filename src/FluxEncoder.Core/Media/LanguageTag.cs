using System.Diagnostics.CodeAnalysis;

namespace FluxEncoder.Core.Media;

/// <summary>
/// A stream language as carried in container metadata: usually an ISO 639-2 code such as <c>eng</c>,
/// occasionally an ISO 639-1 code or a BCP 47 tag such as <c>pt-BR</c>.
/// </summary>
/// <remarks>
/// The primary subtag is stored in lower case and any region or script subtags are kept as written.
/// The undetermined code <c>und</c> is not a language; callers model it as <see langword="null"/>.
/// No registry lookup is performed, so a well-formed but unassigned code is accepted.
/// </remarks>
public readonly record struct LanguageTag
{
    private const string Undetermined = "und";

    private LanguageTag(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// The primary language subtag, for example <c>pt</c> for <c>pt-BR</c>.
    /// </summary>
    public string PrimarySubtag => Value.Split('-', 2)[0];

    /// <summary>
    /// The primary subtag in ISO 639-2/T form, so <c>en</c>, <c>eng</c> give <c>eng</c> and <c>fre</c>,
    /// <c>fra</c>, <c>fr</c> give <c>fra</c>.
    /// </summary>
    public string CanonicalPrimarySubtag => IsoLanguages.Canonicalize(PrimarySubtag);

    /// <summary>
    /// Whether a stream tagged <paramref name="streamLanguage"/> satisfies this requested language.
    /// A bare request (<c>pt</c>) matches any variant (<c>por</c>, <c>pt-BR</c>); a request with subtags
    /// (<c>pt-BR</c>) also requires the same subtags.
    /// </summary>
    public bool Matches(LanguageTag streamLanguage)
    {
        if (Value is null || streamLanguage.Value is null
            || !string.Equals(CanonicalPrimarySubtag, streamLanguage.CanonicalPrimarySubtag, StringComparison.Ordinal))
        {
            return false;
        }

        var requestedRest = Value.AsSpan(PrimarySubtag.Length);
        return requestedRest.IsEmpty
            || requestedRest.Equals(streamLanguage.Value.AsSpan(streamLanguage.PrimarySubtag.Length), StringComparison.OrdinalIgnoreCase);
    }

    /// <returns>
    /// <see langword="false"/> for empty or malformed text and for <c>und</c>.
    /// </returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out LanguageTag tag)
    {
        tag = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var subtags = text.Trim().Split('-');
        var primary = subtags[0];

        if (primary.Length is < 2 or > 3 || !primary.All(char.IsAsciiLetter))
        {
            return false;
        }

        if (subtags.Skip(1).Any(s => s.Length is < 1 or > 8 || !s.All(char.IsAsciiLetterOrDigit)))
        {
            return false;
        }

        primary = primary.ToLowerInvariant();
        if (primary == Undetermined)
        {
            return false;
        }

        subtags[0] = primary;
        tag = new LanguageTag(string.Join('-', subtags));
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}
