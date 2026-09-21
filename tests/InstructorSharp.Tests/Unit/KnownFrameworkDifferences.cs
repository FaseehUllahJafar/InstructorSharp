using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using InstructorSharp.Schema;
using Xunit;

namespace InstructorSharp.Tests.Unit;

/// <summary>
/// Behaviour that genuinely differs between .NET Framework and modern .NET, pinned here so the
/// difference is tracked rather than rediscovered, and so it fails loudly if upstream fixes it
/// and the documentation goes stale.
/// </summary>
public class KnownFrameworkDifferences
{
    private sealed class Bounds
    {
        [Range(0, 130)]
        public int FromZero { get; set; }

        [Range(1, 130)]
        public int FromOne { get; set; }

        [Range(-5, 130)]
        public int FromNegative { get; set; }
    }

    private static JsonElement PropertyOf(string name) =>
        SchemaGenerator.For(typeof(Bounds), new InstructorOptions { UseStrictSchema = false })
            .Schema.GetProperty("properties").GetProperty(name);

    [Fact]
    public void The_upper_bound_of_a_range_is_emitted_on_every_target()
    {
        foreach (string property in new[] { "fromZero", "fromOne", "fromNegative" })
        {
            Assert.True(
                PropertyOf(property).TryGetProperty("maximum", out _),
                $"{property} lost its maximum");
        }
    }

    [Fact]
    public void A_positive_lower_bound_is_emitted_on_every_target()
    {
        Assert.True(PropertyOf("fromOne").TryGetProperty("minimum", out _));
    }

    /// <summary>
    /// The netstandard2.0 schema exporter drops <c>minimum</c> whenever the lower bound is zero
    /// or negative, while modern .NET emits it. Nothing in this library can reach the attribute
    /// from the schema-transform hook, which only exposes the property name and path.
    /// </summary>
    /// <remarks>
    /// The practical effect is limited: the model is simply not told about that lower bound, so
    /// it may guess a value below it. Validation still rejects such a value and the repair loop
    /// still corrects it, so an out-of-range number is never returned to the caller. It costs an
    /// extra round trip, not correctness. If this test starts failing on .NET Framework, the
    /// upstream bug has been fixed and the note in docs/architecture.md should be removed.
    /// </remarks>
    [Fact]
    public void A_zero_or_negative_lower_bound_is_dropped_only_on_dotnet_framework()
    {
        bool zeroHasMinimum = PropertyOf("fromZero").TryGetProperty("minimum", out _);
        bool negativeHasMinimum = PropertyOf("fromNegative").TryGetProperty("minimum", out _);

#if NETFRAMEWORK
        Assert.False(zeroHasMinimum, "upstream appears fixed: update docs/architecture.md");
        Assert.False(negativeHasMinimum, "upstream appears fixed: update docs/architecture.md");
#else
        Assert.True(zeroHasMinimum);
        Assert.True(negativeHasMinimum);
#endif
    }
}
