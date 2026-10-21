using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Execution;

namespace FluxEncoder.Infrastructure.Tests.Execution;

public sealed class FFmpegFailureClassifierTests
{
    [Theory]
    [InlineData("[out#0/matroska @ 0x1] Error writing trailer: No space left on device", FailureCategory.InsufficientSpace)]
    [InlineData("File 'out.mkv' already exists. Exiting.", FailureCategory.OutputExists)]
    [InlineData("[h264_nvenc @ 0x1] Cannot load libcuda.so.1\nError while opening encoder", FailureCategory.HardwareUnavailable)]
    [InlineData("[hevc_nvenc @ 0x1] OpenEncodeSessionEx failed: unsupported device (2): (no details)", FailureCategory.HardwareUnavailable)]
    [InlineData("[AVHWDeviceContext @ 0x1] Failed to initialise VAAPI connection: -1 (unknown libva error).", FailureCategory.HardwareUnavailable)]
    [InlineData("[av1_amf @ 0] CreateComponent(AMFVideoEncoderHW_AV1) failed with error 11\n[vost#0:0/av1_amf @ 0] Error while opening encoder\nError sending frames to consumers: Encoder not found", FailureCategory.HardwareUnavailable)]
    [InlineData("[hevc_amf @ 0] 10-bit encoder is not supported by AMD GPU drivers versions lower than 23.30.", FailureCategory.HardwareUnavailable)]
    [InlineData("[h264_qsv @ 0] Error creating a MFX session: -9.", FailureCategory.HardwareUnavailable)]
    [InlineData("[h264_nvenc @ 0] Cannot load nvcuda.dll", FailureCategory.HardwareUnavailable)]
    [InlineData("[h264_nvenc @ 0] OpenEncodeSessionEx failed: out of memory (10): (no details)", FailureCategory.HardwareUnavailable)]
    [InlineData("Unknown encoder 'libfoo'", FailureCategory.Unsupported)]
    [InlineData("Unrecognized option 'bogus'.\nError splitting the argument list: Option not found", FailureCategory.Unsupported)]
    [InlineData("[mp4 @ 0x1] Could not find tag for codec pcm_s16le in stream #1, codec not currently supported in container", FailureCategory.Unsupported)]
    [InlineData("[tcp @ 0x1] Connection to tcp://127.0.0.1:9 failed: Connection refused", FailureCategory.NetworkError)]
    [InlineData("[out#0 @ 0x1] Error opening output out/x.mkv: No such file or directory\nError opening output files: No such file or directory", FailureCategory.OutputNotWritable)]
    [InlineData("[in#0 @ 0x1] Error opening input: Invalid data found when processing input", FailureCategory.InvalidInput)]
    [InlineData("[mov,mp4 @ 0x1] moov atom not found", FailureCategory.InvalidInput)]
    [InlineData("/data/out.mkv: Permission denied while opening output", FailureCategory.OutputNotWritable)]
    [InlineData("/data/in.mkv: Permission denied", FailureCategory.InvalidInput)]
    [InlineData("Conversion failed!", FailureCategory.EncodeFailed)]
    [InlineData("", FailureCategory.EncodeFailed)]
    public void Classify(string stderr, FailureCategory expected)
    {
        Assert.Equal(expected, FFmpegFailureClassifier.Classify(1, stderr).Category);
    }

    [Fact]
    public void Message_NamesTheLastErrorLineAndKeepsDetail()
    {
        const string Stderr = "Input #0, lavfi\n  Stream #0:0: Video\n[vost#0:0 @ 0x1] Unknown encoder 'libfoo'\nsome trailing info\n";

        var failure = FFmpegFailureClassifier.Classify(8, Stderr);

        Assert.Equal("FFmpeg exited with code 8: [vost#0:0 @ 0x1] Unknown encoder 'libfoo'", failure.Message);
        Assert.Equal(Stderr.Trim(), failure.Detail);
    }

    [Fact]
    public void Message_WithEmptyStderr()
    {
        Assert.Equal("FFmpeg exited with code 1.", FFmpegFailureClassifier.Classify(1, "").Message);
    }

    [Fact]
    public void HardwareUnavailable_ExitsAsUnsupported()
    {
        Assert.Equal(ExitCode.Unsupported, FailureCategory.HardwareUnavailable.ToExitCode());
    }

    [Theory]
    [InlineData("[hevc_amf @ 0] 10-bit encoder is not supported by AMD GPU drivers versions lower than 23.30.\n[vost#0:0/hevc_amf @ 0] Error while opening encoder - maybe incorrect parameters such as bit_rate, rate, width or height.", true)]
    [InlineData("File 'out.mkv' already exists. Exiting.", true)]
    [InlineData("[libx264 @ 0] using cpu capabilities: MMX2 SSE2Fast", false)]
    public void IndicatesFailure_CatchesErrorsReportedWithExitCodeZero(string stderr, bool expected)
    {
        // Observed: Windows FFmpeg 7.1.1 exits 0 after AMF refuses a 10-bit encode on a Radeon RX 580.
        Assert.Equal(expected, FFmpegFailureClassifier.IndicatesFailure(stderr));
    }
}
