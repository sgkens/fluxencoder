namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Which container tags reveal where or on what a recording was made (TSKE011.6). Phones and cameras write them
/// unasked, and FFmpeg copies every container tag by default, so without this a converted video that is shared
/// carries the recording's GPS position and the device's make, model, serial number, or owner.
/// </summary>
/// <remarks>
/// <para>Removed by default: tags whose name contains a location word (<see cref="LocationWords"/>) anywhere, or one
/// of the device parts in <see cref="PrivateParts"/> as a whole part between <c>.</c>, <c>-</c>, <c>_</c>, and
/// <c>:</c> (short words such as <c>make</c> would match too much as substrings). That covers <c>location</c>, <c>location-eng</c>,
/// <c>com.apple.quicktime.location.ISO6709</c>, <c>com.apple.quicktime.make</c>/<c>model</c>/<c>software</c>,
/// <c>com.android.manufacturer</c>/<c>model</c>/<c>version</c>, and camera serial or owner tags.</para>
/// <para>Kept: descriptive tags a person chose, such as title, artist, album, comment, description, copyright, and
/// date, and per-stream language and title. Data streams (which can hold GPS telemetry, such as GoPro GPMF) are
/// already dropped unless <c>--keep-data</c> is given.</para>
/// </remarks>
public static class MetadataPrivacy
{
    public static readonly IReadOnlyList<string> LocationWords = ["location", "gps", "latitude", "longitude", "iso6709"];

    public static readonly IReadOnlySet<string> PrivateParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "xyz", "make", "model", "manufacturer", "serial", "serialnumber", "owner", "ownername",
    };

    /// <summary>
    /// Software tags only count as device information under a phone vendor's namespace, where they name the OS build.
    /// </summary>
    private static readonly string[] DeviceSoftwareTags = ["com.apple.quicktime.software", "com.android.version"];

    public static bool IsPrivate(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return LocationWords.Any(word => key.Contains(word, StringComparison.OrdinalIgnoreCase))
            || DeviceSoftwareTags.Contains(key, StringComparer.OrdinalIgnoreCase)
            || key.Split(['.', '-', '_', ':'], StringSplitOptions.RemoveEmptyEntries).Any(PrivateParts.Contains);
    }

    /// <summary>
    /// The private tags present, in a stable order.
    /// </summary>
    public static IReadOnlyList<string> PrivateKeys(IReadOnlyDictionary<string, string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return [.. tags.Keys.Where(IsPrivate).Order(StringComparer.OrdinalIgnoreCase)];
    }
}
