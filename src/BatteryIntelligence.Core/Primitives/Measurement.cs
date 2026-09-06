using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Primitives;

/// <summary>
/// A value that knows whether it was measured, calculated, estimated — or is
/// simply not available.
/// </summary>
/// <typeparam name="T">The underlying value type.</typeparam>
/// <remarks>
/// <para>
/// This type is the code-level expression of the core engineering principle in
/// the specification (section 3): every number carries its provenance, and an
/// unavailable reading is represented by the absence of a value rather than by a
/// zero or a plausible-looking substitute.
/// </para>
/// <para>
/// There is no public constructor taking a raw value and an arbitrary grade.
/// Values are created through the named factories, which makes the grade an
/// explicit decision at every call site instead of a parameter that can be
/// filled in carelessly.
/// </para>
/// </remarks>
public readonly record struct Measurement<T>
    where T : struct
{
    private Measurement(T? value, DataQuality quality, MeasurementSource source)
    {
        Value = value;
        Quality = quality;
        Source = source;
    }

    /// <summary>The value, or <see langword="null"/> when unavailable.</summary>
    public T? Value { get; }

    /// <summary>How the value came to be known.</summary>
    public DataQuality Quality { get; }

    /// <summary>Which API or subsystem supplied it.</summary>
    public MeasurementSource Source { get; }

    /// <summary>Whether a usable value is present.</summary>
    public bool HasValue => Value.HasValue;

    /// <summary>
    /// Whether the value may contribute to aggregates, rates and insights.
    /// Suspect and unknown-provenance values may not.
    /// </summary>
    public bool IsUsable => Value.HasValue && Quality.IsTrustworthy();

    /// <summary>
    /// Whether the UI must show a grade badge alongside this value.
    /// </summary>
    public bool RequiresBadge => Value.HasValue && !Quality.IsPresentedPlainly();

    /// <summary>A value read directly from hardware or a Windows API.</summary>
    public static Measurement<T> Measured(T value, MeasurementSource source) =>
        new(value, DataQuality.Measured, source);

    /// <summary>A value obtained by exact arithmetic over measured inputs.</summary>
    public static Measurement<T> Calculated(T value, MeasurementSource source = MeasurementSource.Derived) =>
        new(value, DataQuality.Calculated, source);

    /// <summary>A value produced by an estimation model.</summary>
    public static Measurement<T> Estimated(T value, MeasurementSource source = MeasurementSource.Model) =>
        new(value, DataQuality.Estimated, source);

    /// <summary>
    /// A value that is structurally valid but physically implausible. Retained
    /// for diagnosis and excluded from every computation.
    /// </summary>
    public static Measurement<T> Suspect(T value, MeasurementSource source) =>
        new(value, DataQuality.Suspect, source);

    /// <summary>
    /// No value. This is a legitimate, complete answer — not an error and not a
    /// placeholder for a number that should have been found.
    /// </summary>
    public static Measurement<T> Unavailable(MeasurementSource source = MeasurementSource.Unknown) =>
        new(null, DataQuality.Unknown, source);

    /// <summary>
    /// Returns this measurement re-graded to the worse of its own grade and
    /// <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// Used when a value is combined with, or derived from, another whose grade
    /// may be worse. A grade can only ever degrade through this operation.
    /// </remarks>
    public Measurement<T> DegradedBy(DataQuality other) =>
        Value is null ? this : new Measurement<T>(Value, Quality.Worst(other), Source);

    /// <summary>
    /// Projects the value through <paramref name="selector"/>, preserving grade
    /// and source. An unavailable measurement stays unavailable.
    /// </summary>
    public Measurement<TResult> Map<TResult>(Func<T, TResult> selector)
        where TResult : struct
    {
        ArgumentNullException.ThrowIfNull(selector);

        return Value is null
            ? Measurement<TResult>.Unavailable(Source)
            : new Measurement<TResult>(selector(Value.Value), Quality, Source);
    }

    /// <summary>Returns the value, or <paramref name="fallback"/> when unavailable.</summary>
    /// <remarks>
    /// Intended for computation only. Never use the fallback as a displayed
    /// value — an unavailable reading must be shown as unavailable, not as a
    /// default.
    /// </remarks>
    public T ValueOr(T fallback) => Value ?? fallback;

    /// <inheritdoc/>
    public override string ToString() =>
        Value is null ? "unavailable" : $"{Value.Value} ({Quality})";
}

/// <summary>
/// Combining helpers for <see cref="Measurement{T}"/>.
/// </summary>
public static class Measurement
{
    /// <summary>
    /// Combines two measurements through <paramref name="combine"/>, taking the
    /// worse of the two grades. If either input is unavailable, the result is
    /// unavailable.
    /// </summary>
    /// <remarks>
    /// This is how derived quantities such as electric current are produced.
    /// Because the result is graded by
    /// <see cref="DataQualityExtensions.Worst(DataQuality, DataQuality)"/> and the
    /// caller supplies the grade explicitly, a derived value can never silently
    /// present itself as measured.
    /// </remarks>
    public static Measurement<TResult> Combine<TLeft, TRight, TResult>(
        Measurement<TLeft> left,
        Measurement<TRight> right,
        Func<TLeft, TRight, TResult> combine,
        DataQuality resultQuality = DataQuality.Calculated,
        MeasurementSource source = MeasurementSource.Derived)
        where TLeft : struct
        where TRight : struct
        where TResult : struct
    {
        ArgumentNullException.ThrowIfNull(combine);

        if (left.Value is null || right.Value is null)
        {
            return Measurement<TResult>.Unavailable(source);
        }

        TResult value = combine(left.Value.Value, right.Value.Value);
        DataQuality quality = resultQuality
            .Worst(left.Quality)
            .Worst(right.Quality);

        return quality switch
        {
            DataQuality.Measured => Measurement<TResult>.Measured(value, source),
            DataQuality.Calculated => Measurement<TResult>.Calculated(value, source),
            DataQuality.Estimated => Measurement<TResult>.Estimated(value, source),
            _ => Measurement<TResult>.Suspect(value, source),
        };
    }
}
