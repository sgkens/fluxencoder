using System.Collections.Immutable;
using FluxEncoder.Core.Hardware;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// What the user asked for: one input, one output, and how to treat its video and audio.
/// The planner turns a job into an <see cref="OutputPlan"/>; nothing here is FFmpeg-specific.
/// </summary>
/// <remarks>
/// This replaces the legacy <c>Hashtable</c> of options. Paths are stored as given; existence, input/output
/// identity, and overwrite checks belong to the planner (TSKE005.4) and the policies of TSKE002.5.
/// </remarks>
public sealed record EncodingJob
{
    /// <summary>
    /// A file path or URL.
    /// </summary>
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

    public required VideoTarget Video
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public required AudioTarget Audio
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// The vendor the user forced, or <see langword="null"/> for automatic selection (TSKE006.3).
    /// </summary>
    public HardwareVendor? Hardware
    {
        get;
        init
        {
            if (value is { } vendor && !Enum.IsDefined(vendor))
            {
                throw new ArgumentOutOfRangeException(nameof(value), vendor, null);
            }

            field = value;
        }
    }

    /// <summary>
    /// With a forced <see cref="Hardware"/> vendor, encode on the CPU if that vendor is unusable instead of failing.
    /// Automatic selection always falls back.
    /// </summary>
    public bool HardwareFallback { get; init; }

    /// <summary>
    /// The part of the input to encode, or <see langword="null"/> for all of it.
    /// </summary>
    public TrimRange? Trim { get; init; }

    public StreamSelection Streams
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = StreamSelection.Default;

    /// <summary>
    /// Raw FFmpeg arguments from <c>--ffmpeg-arg</c>, placed among the output options. They bypass every check
    /// FluxEncoder makes and are allowed only when <c>SourcePolicy.AllowExtraArguments</c> is set (TSKE010.1).
    /// </summary>
    public ImmutableArray<string> ExtraArguments
    {
        get;
        init
        {
            if (value.IsDefault || value.Any(a => a is null))
            {
                throw new ArgumentException("Extra arguments cannot be null.", nameof(value));
            }

            field = value;
        }
    } = [];

    public JobPolicy Policy
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = JobPolicy.Default;
}
