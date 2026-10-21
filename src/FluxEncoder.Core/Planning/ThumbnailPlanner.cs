using System.Globalization;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// Plans a thumbnail contact sheet (TSKE005.8).
/// </summary>
/// <remarks>
/// <para>
/// Validation: the input must be finite and have motion video; it must contain at least as many frames as
/// thumbnails requested; the sheet must fit the output format's dimension limit (WebP 16383 px, JPEG and PNG
/// 65500 px here); and the encoder for the format must exist.
/// </para>
/// <para>
/// The filter chain seeks to the first interval, keeps one frame per interval with <c>fps</c>, scales to the tile
/// width at the display aspect ratio, and tiles. HDR sources are tone-mapped to BT.709 when the build has
/// <c>zscale</c> and <c>tonemap</c>, otherwise they are warned about.
/// </para>
/// </remarks>
public static class ThumbnailPlanner
{
    private sealed record ImageFormat(string Encoder, int MaxDimension, string[] QualityOptions);

    private static readonly Dictionary<string, ImageFormat> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = new("mjpeg", 65500, ["-q:v", "3"]),
        [".jpeg"] = new("mjpeg", 65500, ["-q:v", "3"]),
        [".png"] = new("png", 65500, []),
        [".webp"] = new("libwebp", 16383, ["-quality", "85"]),
    };

    public static Result<PlannedJob> Plan(ThumbnailJob job, PlanningInput input, PlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        var warnings = new List<JobWarning>();
        var extension = Path.GetExtension(job.OutputPath);

        if (!Formats.TryGetValue(extension, out var format))
        {
            return Fail(FailureCategory.InvalidUsage, $"Thumbnail sheets are written as .jpg, .png, or .webp, not '{extension}'.");
        }

        if (!context.Capabilities.HasEncoder(format.Encoder) || !context.Capabilities.Muxers.Contains("image2"))
        {
            return Fail(FailureCategory.Unsupported, $"This FFmpeg build cannot write {extension} images ({format.Encoder}).");
        }

        if (!input.IsProbed || input.Media.Duration is not { } duration || duration <= TimeSpan.Zero)
        {
            return Fail(FailureCategory.InvalidInput, $"'{input.Source.Value}' has no known length, so thumbnails cannot be spaced.");
        }

        var video = input.Media.MotionVideoStreams.FirstOrDefault();
        if (video is null)
        {
            return Fail(FailureCategory.InvalidInput, $"'{input.Source.Value}' has no video to take thumbnails from.");
        }

        var interval = duration / (job.Count + 1);
        var frameRate = video.AverageFrameRate?.Value ?? 25;
        var frames = duration.TotalSeconds * frameRate;
        if (frames < job.Count)
        {
            return Fail(FailureCategory.InvalidInput, $"The video has about {(int)frames} frames, fewer than the {job.Count} thumbnails requested.");
        }

        var (tileWidth, tileHeight) = TileSize(video, job.TileWidth);
        var sheetWidth = (long)tileWidth * job.Columns;
        var sheetHeight = (long)tileHeight * job.Rows;
        if (sheetWidth > format.MaxDimension || sheetHeight > format.MaxDimension)
        {
            return Fail(FailureCategory.InvalidUsage,
                $"A {job.Columns}x{job.Rows} sheet of {tileWidth}x{tileHeight} tiles is {sheetWidth}x{sheetHeight} px, over the {extension} limit of {format.MaxDimension} px. Use fewer or smaller tiles.");
        }

        var filters = new List<string>();
        if (video.HdrFormats != HdrFormat.None)
        {
            if (context.Capabilities.HasFilter("zscale") && context.Capabilities.HasFilter("tonemap"))
            {
                filters.Add("zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv");
                warnings.Add(new JobWarning(WarningCodes.ThumbnailsToneMapped, "HDR video was tone-mapped to SDR for the thumbnails."));
            }
            else
            {
                warnings.Add(new JobWarning(WarningCodes.ThumbnailsHdrWashedOut, "This FFmpeg build lacks zscale/tonemap, so HDR thumbnails will look washed out."));
            }
        }

        var seconds = EncodePlanner.FormatSeconds(interval);
        filters.Add($"fps=1/{seconds}");
        filters.Add(string.Create(CultureInfo.InvariantCulture, $"scale={tileWidth}:{tileHeight}"));
        filters.Add("setsar=1");
        filters.Add(string.Create(CultureInfo.InvariantCulture, $"tile={job.Columns}x{job.Rows}"));
        if (format.Encoder == "mjpeg")
        {
            filters.Add("format=yuvj420p");
        }

        var encodingJob = new EncodingJob
        {
            InputPath = job.InputPath,
            OutputPath = job.OutputPath,
            Video = new VideoTarget.StreamCopy(),
            Audio = new AudioTarget.StreamCopy(),
            Policy = job.Policy,
        };

        // Progress reaches the end of the input when the last tile is taken.
        var timeline = new InputTimeline.Finite(duration);

        var path = OutputPathPlanner.Resolve(job.OutputPath, job.Policy, [input.Source], 0, context);
        warnings.AddRange(path.Warnings);
        if (path.Failure is not null)
        {
            return path.Failure;
        }

        if (path.SkipReason is not null)
        {
            return Result<PlannedJob>.Success(PlannedJob.Skip(path.FinalPath!, path.SkipReason, timeline, warnings));
        }

        var final = path.FinalPath!;
        var temporary = OutputPaths.Temporary(final, context.NewToken());

        List<string> arguments =
        [
            "-ss", seconds, "-i", input.Source.Value,
            "-map", "0:" + video.Index.ToString(CultureInfo.InvariantCulture),
            "-vf", string.Join(',', filters),
            "-frames:v", "1",
            "-c:v", format.Encoder,
            .. format.QualityOptions,
            "-update", "1",
            "-n", "-f", "image2", temporary,
        ];

        var plan = new OutputPlan(encodingJob, [new FFmpegInvocation(arguments)])
        {
            FinalOutputPath = final,
            TemporaryOutputPath = temporary,
        };

        return Result<PlannedJob>.Success(PlannedJob.Run(plan, timeline, warnings));

        Result<PlannedJob> Fail(FailureCategory category, string message) => new JobFailure(category, message);
    }

    /// <summary>
    /// Tile dimensions at the display aspect ratio (storage size × sample aspect ratio), even-sized for chroma
    /// subsampling, with 90° rotations swapping width and height.
    /// </summary>
    public static (int Width, int Height) TileSize(VideoStream video, int tileWidth)
    {
        ArgumentNullException.ThrowIfNull(video);

        double width = video.Width ?? 16;
        double height = video.Height ?? 9;
        width *= video.SampleAspectRatio?.Value ?? 1.0;

        if (video.Rotation is { } rotation && Math.Abs(rotation) % 180 == 90)
        {
            (width, height) = (height, width);
        }

        var tileHeight = (int)Math.Round(tileWidth * height / width / 2) * 2;
        return (tileWidth - tileWidth % 2, Math.Max(2, tileHeight));
    }
}
