namespace FluxEncoder.Infrastructure.Tests.Support;

/// <summary>
/// Reads captured tool output from the <c>Fixtures</c> folder copied next to the test assembly.
/// </summary>
internal static class Fixture
{
    public static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "Fixtures", .. parts]));
}
