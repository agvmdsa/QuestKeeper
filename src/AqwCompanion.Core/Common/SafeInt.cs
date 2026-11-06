namespace AqwCompanion.Core.Common;

/// <summary>Conversions to int that cannot overflow.</summary>
public static class SafeInt
{
    /// <summary>Saturates at <see cref="int.MaxValue"/>: a larger value becomes the largest int. Negative values are not changed.</summary>
    public static int Clamp(long value) => (int)Math.Min(value, int.MaxValue);
}
