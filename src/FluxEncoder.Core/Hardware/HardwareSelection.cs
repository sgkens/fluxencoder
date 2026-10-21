using System.Collections.Immutable;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Hardware;

/// <param name="Strategy">The strategy to plan with.</param>
/// <param name="FallbackAllowed">
/// Whether a hardware failure during the encode may be retried on the CPU: always for automatic selection, only
/// with <c>--hardware-fallback</c> when the user forced a vendor.
/// </param>
/// <param name="Warnings">Notes about the choice, such as falling back to the CPU before starting.</param>
public sealed record HardwareSelection(IHardwareStrategy Strategy, bool FallbackAllowed, ImmutableArray<JobWarning> Warnings)
{
    public bool Equals(HardwareSelection? other) =>
        other is not null && ReferenceEquals(Strategy, other.Strategy) && FallbackAllowed == other.FallbackAllowed && Warnings.SequenceEqual(other.Warnings);

    public override int GetHashCode() => HashCode.Combine(Strategy, FallbackAllowed);
}
