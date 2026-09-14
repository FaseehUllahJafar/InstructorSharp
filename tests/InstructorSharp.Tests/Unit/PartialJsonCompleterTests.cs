using System.Text.Json;
using InstructorSharp.Parsing;
using Xunit;

namespace InstructorSharp.Tests.Unit;

public class PartialJsonCompleterTests
{
    private static string Complete(string partial)
    {
        Assert.True(PartialJsonCompleter.TryComplete(partial, out string json), $"could not complete: {partial}");

        // Everything this produces must be parseable; that is the whole contract.
        using var _ = JsonDocument.Parse(json);
        return json;
    }

    [Fact]
    public void Closes_an_open_object()
    {
        string json = Complete("""{"a":"x" """);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("x", document.RootElement.GetProperty("a").GetString());
    }

    [Fact]
    public void Closes_nested_containers_in_the_right_order()
    {
        // The trailing 2 is held back (see the number tests below), so only the delimited
        // element is settled -- but the containers around it must still close correctly.
        string json = Complete("""{"a":{"b":[1,2""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("a").GetProperty("b").GetArrayLength());
        Assert.Equal(1, document.RootElement.GetProperty("a").GetProperty("b")[0].GetInt32());
    }

    [Fact]
    public void Closes_three_levels_of_nesting()
    {
        string json = Complete("""{"a":{"b":{"c":"deep" """);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            "deep",
            document.RootElement.GetProperty("a").GetProperty("b").GetProperty("c").GetString());
    }

    [Fact]
    public void Drops_a_property_name_that_has_no_value_yet()
    {
        string json = Complete("""{"name":"Ali","age""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Ali", document.RootElement.GetProperty("name").GetString());
        Assert.False(document.RootElement.TryGetProperty("age", out _));
    }

    [Fact]
    public void Drops_a_trailing_comma()
    {
        string json = Complete("""{"a":1,""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt32());
    }

    [Fact]
    public void Salvages_a_partially_written_string_value()
    {
        // The point of the feature: text fields visibly fill in rather than snapping into
        // existence only once the closing quote arrives.
        string json = Complete("""{"name":"Fase""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Fase", document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void Salvages_a_partial_string_after_a_completed_property()
    {
        string json = Complete("""{"a":1,"name":"Lah""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt32());
        Assert.Equal("Lah", document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void Salvages_a_partial_string_inside_an_array()
    {
        string json = Complete("""["alpha","bet""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal("bet", document.RootElement[1].GetString());
    }

    [Fact]
    public void Drops_a_dangling_backslash_that_would_break_the_document()
    {
        string json = Complete("""{"path":"C:\\temp\""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(@"C:\temp", document.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void Drops_a_truncated_unicode_escape()
    {
        string json = Complete("""{"s":"ab\u00""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal("ab", document.RootElement.GetProperty("s").GetString());
    }

    [Fact]
    public void Keeps_a_complete_escape_sequence()
    {
        string json = Complete("""{"s":"a\u0041b""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal("aAb", document.RootElement.GetProperty("s").GetString());
    }

    [Fact]
    public void Holds_back_a_trailing_number_until_it_is_delimited()
    {
        // "12" might still become "123". A partial string is self-evidently partial to a
        // reader; a partial number is a plausible wrong answer, so it is withheld instead.
        string json = Complete("""{"a":1,"b":12""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("b", out _));
    }

    [Fact]
    public void Emits_a_number_once_a_delimiter_proves_it_complete()
    {
        string json = Complete("""{"b":123,""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(123, document.RootElement.GetProperty("b").GetInt32());
    }

    [Fact]
    public void Holds_back_a_trailing_boolean_until_it_is_delimited()
    {
        string json = Complete("""{"a":"x","ok":tru""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal("x", document.RootElement.GetProperty("a").GetString());
        Assert.False(document.RootElement.TryGetProperty("ok", out _));
    }

    [Fact]
    public void Handles_an_already_complete_document()
    {
        Assert.Equal("""{"a":1}""", Complete("""{"a":1}"""));
    }

    [Fact]
    public void Ignores_prose_before_the_document()
    {
        string json = Complete("""Here you go: {"a":"a value""");
        using var document = JsonDocument.Parse(json);
        Assert.Equal("a value", document.RootElement.GetProperty("a").GetString());
    }

    [Fact]
    public void A_bare_opening_brace_completes_to_an_empty_object()
    {
        // The first chunk off the wire is often just "{". An empty snapshot is the honest
        // representation of "nothing has arrived yet" and keeps consumers from special-casing.
        string json = Complete("{");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateObject().ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("no json here")]
    public void Returns_false_when_nothing_can_be_salvaged(string? partial)
    {
        Assert.False(PartialJsonCompleter.TryComplete(partial, out _));
    }
}
