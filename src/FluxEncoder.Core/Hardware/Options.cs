namespace FluxEncoder.Core.Hardware;

internal static class Options
{
    public static KeyValuePair<string, string>[] Of(params (string Key, string Value)[] options) =>
        [.. options.Select(o => KeyValuePair.Create(o.Key, o.Value))];
}
