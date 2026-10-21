namespace FluxEncoder.Core.Encoding;

/// <summary>
/// A contact sheet: <see cref="Columns"/> × <see cref="Rows"/> frames taken at even intervals, tiled into one image.
/// </summary>
/// <remarks>
/// Frames are taken at <c>duration / (count + 1)</c> intervals, so the first and last are inside the video rather
/// than on black opening or closing frames. The output format follows the extension: <c>.jpg</c>/<c>.jpeg</c>,
/// <c>.png</c>, or <c>.webp</c>.
/// </remarks>
public sealed record ThumbnailJob
{
    public const int MaxGrid = 20;

    public required string InputPath
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    public required string OutputPath
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    public int Columns
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxGrid);
            field = value;
        }
    } = 4;

    public int Rows
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxGrid);
            field = value;
        }
    } = 4;

    /// <summary>
    /// Width of each tile in pixels; height follows the video's display aspect ratio.
    /// </summary>
    public int TileWidth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 64);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1920);
            field = value;
        }
    } = 320;

    public JobPolicy Policy
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = JobPolicy.Default;

    public int Count => Columns * Rows;
}
