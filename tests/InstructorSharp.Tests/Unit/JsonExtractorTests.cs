using InstructorSharp.Parsing;
using Xunit;

namespace InstructorSharp.Tests.Unit;

public class JsonExtractorTests
{
    [Fact]
    public void Extracts_bare_object()
    {
        Assert.True(JsonExtractor.TryExtract("""{"a":1}""", out string json));
        Assert.Equal("""{"a":1}""", json);
    }

    [Fact]
    public void Extracts_bare_array()
    {
        Assert.True(JsonExtractor.TryExtract("""[1,2,3]""", out string json));
        Assert.Equal("""[1,2,3]""", json);
    }

    [Fact]
    public void Strips_leading_prose()
    {
        Assert.True(JsonExtractor.TryExtract("""Here is the JSON you asked for: {"a":1}""", out string json));
        Assert.Equal("""{"a":1}""", json);
    }

    [Fact]
    public void Strips_trailing_prose()
    {
        Assert.True(JsonExtractor.TryExtract("""{"a":1} Let me know if you need anything else!""", out string json));
        Assert.Equal("""{"a":1}""", json);
    }

    [Fact]
    public void Unwraps_json_fence()
    {
        const string Input = """
            Sure thing.

            ```json
            {"name":"Ali","age":29}
            ```

            Hope that helps.
            """;

        Assert.True(JsonExtractor.TryExtract(Input, out string json));
        Assert.Equal("""{"name":"Ali","age":29}""", json);
    }

    [Fact]
    public void Unwraps_fence_without_language_tag()
    {
        const string Input = """
            ```
            {"a":1}
            ```
            """;

        Assert.True(JsonExtractor.TryExtract(Input, out string json));
        Assert.Equal("""{"a":1}""", json);
    }

    [Fact]
    public void Braces_inside_strings_do_not_unbalance_the_scan()
    {
        const string Input = """{"template":"hello {name}, welcome to {city}","ok":true}""";

        Assert.True(JsonExtractor.TryExtract(Input, out string json));
        Assert.Equal(Input, json);
    }

    [Fact]
    public void Escaped_quote_inside_string_is_handled()
    {
        const string Input = """{"quote":"she said \"hi\" loudly"}""";

        Assert.True(JsonExtractor.TryExtract(Input, out string json));
        Assert.Equal(Input, json);
    }

    [Fact]
    public void Escaped_backslash_before_quote_terminates_the_string()
    {
        // The value ends in a literal backslash, so the very next quote really does close it.
        const string Input = """{"path":"C:\\temp\\","ok":true}""";

        Assert.True(JsonExtractor.TryExtract(Input, out string json));
        Assert.Equal(Input, json);
    }

    [Fact]
    public void Picks_the_real_document_when_a_stray_brace_appears_first()
    {
        // The preamble contains an unbalanced brace; the scanner must skip it and find the object.
        const string Input = """I will use the { character here. {"a":1}""";

        Assert.True(JsonExtractor.TryExtract(Input, out string json));
        Assert.Equal("""{"a":1}""", json);
    }

    [Fact]
    public void Keeps_nested_structures_intact()
    {
        const string Input = """{"a":{"b":[1,{"c":2}]},"d":"e"}""";

        Assert.True(JsonExtractor.TryExtract(Input, out string json));
        Assert.Equal(Input, json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("I am sorry, I cannot help with that.")]
    [InlineData("""{"a":1""")]
    public void Returns_false_when_there_is_no_complete_document(string? input)
    {
        Assert.False(JsonExtractor.TryExtract(input, out string json));
        Assert.Equal(string.Empty, json);
    }
}
