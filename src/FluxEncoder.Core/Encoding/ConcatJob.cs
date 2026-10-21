using System.Collections.Immutable;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Joins several inputs end to end into one output.
/// </summary>
/// <remarks>
/// <see cref="ConcatMode.Auto"/> copies streams when every input matches and the container accepts them, and
/// re-encodes through a normalizing filter otherwise. Only the first video and first audio stream of each input
/// are joined; per-input subtitles and extra tracks rarely line up across files.
/// </remarks>
public sealed record ConcatJob
{
    public ConcatJob(IEnumerable<string> inputs, string outputPath, VideoTarget video, AudioTarget audio)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audio);

        Inputs = [.. inputs];
        if (Inputs.Length < 2)
        {
            throw new ArgumentException("Concatenation needs at least two inputs.", nameof(inputs));
        }

        if (Inputs.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Input paths cannot be blank.", nameof(inputs));
        }

        OutputPath = outputPath;
        Video = video;
        Audio = audio;
    }

    public ImmutableArray<string> Inputs { get; }

    public string OutputPath { get; }

    /// <summary>
    /// The target when re-encoding. With <see cref="VideoTarget.StreamCopy"/>, an incompatible set of inputs is
    /// re-encoded to H.264 with a warning (Auto) or rejected (Copy).
    /// </summary>
    public VideoTarget Video { get; }

    public AudioTarget Audio { get; }

    public ConcatMode Mode
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null);
    } = ConcatMode.Auto;

    public JobPolicy Policy
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = JobPolicy.Default;

    public bool Equals(ConcatJob? other) =>
        other is not null
        && Inputs.SequenceEqual(other.Inputs)
        && OutputPath == other.OutputPath
        && Video == other.Video
        && Audio == other.Audio
        && Mode == other.Mode
        && Policy == other.Policy;

    public override int GetHashCode() => HashCode.Combine(Inputs.Length, OutputPath, Video, Audio, Mode);

    /// <summary>
    /// The equivalent single-output job, which carries the output path and policies through execution.
    /// </summary>
    public EncodingJob ToEncodingJob() => new()
    {
        InputPath = Inputs[0],
        OutputPath = OutputPath,
        Video = Video,
        Audio = Audio,
        Policy = Policy,
    };
}
