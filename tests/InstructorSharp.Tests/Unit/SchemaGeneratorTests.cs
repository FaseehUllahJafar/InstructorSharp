using System.Text.Json;
using InstructorSharp.Schema;
using InstructorSharp.Tests.Fakes;
using Xunit;

namespace InstructorSharp.Tests.Unit;

public class SchemaGeneratorTests
{
    private static readonly InstructorOptions Strict = new() { UseStrictSchema = true };
    private static readonly InstructorOptions Loose = new() { UseStrictSchema = false };

    [Fact]
    public void Object_types_are_not_enveloped()
    {
        SchemaDescriptor descriptor = SchemaGenerator.For(typeof(UserInfo), Strict);
        Assert.False(descriptor.IsEnveloped);
        Assert.Equal("object", descriptor.Schema.GetProperty("type").GetString());
    }

    [Fact]
    public void Strict_mode_requires_every_property()
    {
        SchemaDescriptor descriptor = SchemaGenerator.For(typeof(UserInfo), Strict);

        string[] required = descriptor.Schema.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        Assert.Contains("name", required);
        Assert.Contains("age", required);

        // Nullable in C# does not mean optional to a strict provider: the property must still
        // be listed, it just gets a nullable type.
        Assert.Contains("city", required);
    }

    [Fact]
    public void Strict_mode_closes_objects_to_additional_properties()
    {
        SchemaDescriptor descriptor = SchemaGenerator.For(typeof(UserInfo), Strict);
        Assert.False(descriptor.Schema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Strict_mode_moves_unsupported_keywords_into_the_description()
    {
        // Range(0,130) becomes minimum/maximum, which strict providers reject outright. The
        // constraint has to survive somewhere the model can still read it.
        SchemaDescriptor descriptor = SchemaGenerator.For(typeof(UserInfo), Strict);
        JsonElement age = descriptor.Schema.GetProperty("properties").GetProperty("age");

        Assert.False(age.TryGetProperty("minimum", out _));
        Assert.False(age.TryGetProperty("maximum", out _));

        string description = age.GetProperty("description").GetString()!;
        Assert.Contains("minimum", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("130", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Loose_mode_leaves_the_keywords_alone()
    {
        SchemaDescriptor descriptor = SchemaGenerator.For(typeof(UserInfo), Loose);
        JsonElement age = descriptor.Schema.GetProperty("properties").GetProperty("age");
        Assert.True(age.TryGetProperty("minimum", out _));
    }

    [Fact]
    public void Existing_descriptions_are_preserved_when_keywords_are_demoted()
    {
        SchemaDescriptor descriptor = SchemaGenerator.For(typeof(UserInfo), Strict);
        string description = descriptor.Schema
            .GetProperty("properties").GetProperty("name").GetProperty("description").GetString()!;

        Assert.Contains("full name", description, StringComparison.OrdinalIgnoreCase);
    }

    // The envelope cases are the documented gap in the built-in Microsoft.Extensions.AI path:
    // a strict schema root has to be an object, so these types cannot be requested directly.
    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(string))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(Priority))]
    [InlineData(typeof(List<string>))]
    [InlineData(typeof(UserInfo[]))]
    [InlineData(typeof(Dictionary<string, int>))]
    public void Non_object_roots_are_enveloped(Type type)
    {
        SchemaDescriptor descriptor = SchemaGenerator.For(type, Strict);

        Assert.True(descriptor.IsEnveloped);
        Assert.Equal("object", descriptor.Schema.GetProperty("type").GetString());
        Assert.True(descriptor.Schema.GetProperty("properties").TryGetProperty("value", out _));
        Assert.False(descriptor.Schema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Enveloping_a_list_of_objects_keeps_the_element_schema_reachable()
    {
        SchemaDescriptor descriptor = SchemaGenerator.For(typeof(List<Invoice>), Strict);

        Assert.True(descriptor.IsEnveloped);

        JsonElement value = descriptor.Schema.GetProperty("properties").GetProperty("value");
        Assert.Equal("array", value.GetProperty("type").GetString());

        // Element schemas may be inlined or referenced; either way the document must not have
        // lost the definitions, or the provider rejects it.
        string text = descriptor.SchemaText;
        bool describesElements = text.Contains("\"number\"", StringComparison.Ordinal)
                                 || text.Contains("$defs", StringComparison.Ordinal)
                                 || text.Contains("$ref", StringComparison.Ordinal);
        Assert.True(describesElements, $"element schema was lost: {text}");
    }

    [Fact]
    public void Nested_object_graphs_are_shaped_strictly_all_the_way_down()
    {
        SchemaDescriptor descriptor = SchemaGenerator.For(typeof(Invoice), Strict);
        string text = descriptor.SchemaText;

        // Every object in the document, not just the root, must be closed.
        Assert.DoesNotContain("\"additionalProperties\": true", text, StringComparison.Ordinal);
        Assert.Contains("lines", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Repeated_requests_reuse_the_cached_descriptor()
    {
        SchemaDescriptor first = SchemaGenerator.For(typeof(Invoice), Strict);
        SchemaDescriptor second = SchemaGenerator.For(typeof(Invoice), Strict);
        Assert.Same(first, second);
    }

    [Fact]
    public void Strict_and_loose_schemas_are_cached_separately()
    {
        SchemaDescriptor strict = SchemaGenerator.For(typeof(Invoice), Strict);
        SchemaDescriptor loose = SchemaGenerator.For(typeof(Invoice), Loose);
        Assert.NotSame(strict, loose);
    }
}
