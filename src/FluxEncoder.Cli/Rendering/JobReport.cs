using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Cli.Rendering;

/// <summary>
/// One processed (or, for <c>--dry-run</c>, planned) input, as reported to the user.
/// </summary>
internal sealed record JobReport(string Input, EncodingJob Job, JobResult Result, PlannedJob? Planned, IHardwareStrategy? Strategy)
{
    /// <summary>
    /// The video encoder used, <c>copy</c> for stream copy, or <see langword="null"/> when nothing was planned.
    /// </summary>
    public string? VideoEncoder =>
        Job.Video is VideoTarget.Encode encode
            ? Strategy is not null && Strategy.TryGetVideoEncoder(encode.Codec, out var encoder) ? encoder : null
            : "copy";
}
