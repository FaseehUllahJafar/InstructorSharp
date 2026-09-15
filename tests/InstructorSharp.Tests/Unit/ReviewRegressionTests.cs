using System.Diagnostics;
using System.Text;
using System.Text.Json;
using InstructorSharp.Parsing;
using InstructorSharp.Schema;
using InstructorSharp.Tests.Fakes;
using InstructorSharp.Validation;
using Microsoft.Extensions.AI;
using Xunit;

namespace InstructorSharp.Tests.Unit;

/// <summary>
/// One test per defect found during the pre-release review. Each of these failed before the
/// corresponding fix, so they are the thing that stops the bug coming back.
/// </summary>
public class ReviewRegressionTests
{
    [Fact]
    public void The_schema_cache_does_not_grow_without_bound()
    {
        // Callers who build a fresh JsonSerializerOptions per request would otherwise never hit
        // the cache and would grow it for the lifetime of the process.
        for (int i = 0; i < 700; i++)
        {
            var options = new InstructorOptions
            {
                SerializerOptions = new JsonSerializerOptions(InstructorJson.Default),
            };

            SchemaDescriptor descriptor = SchemaGenerator.For(typeof(UserInfo), options);

            // Correctness must survive the cap: every call still returns a usable schema.
            Assert.Equal("object", descriptor.Schema.GetProperty("type").GetString());
        }
    }

    [Fact]
    public void A_diamond_graph_does_not_explode_the_validator()
    {
        // Each level's two children point at the same node below it, so the graph has 2^40
        // distinct paths through only 40 distinct objects. A visited-set that forgets a node
        // once it has been left re-walks every path and never finishes; one that remembers
        // visits 40 objects and returns immediately.
        const int Depth = 40;

        var current = new DiamondNode { Name = "leaf" };
        for (int i = 0; i < Depth; i++)
        {
            current = new DiamondNode { Name = $"d{i}", Left = current, Right = current };
        }

        var validator = new DataAnnotationsValidator(InstructorJson.Default, maxDepth: Depth + 8);

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ValidationFailure> failures = validator.Validate(current);
        stopwatch.Stop();

        Assert.Empty(failures);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 2_000,
            $"validation took {stopwatch.ElapsedMilliseconds}ms, so the graph is being re-walked per path");
    }

    [Fact]
    public void A_shared_invalid_node_is_reported_once_not_once_per_path()
    {
        var shared = new DiamondNode { Name = string.Empty };   // fails [Required]
        var root = new DiamondNode { Name = "root", Left = shared, Right = shared };

        var validator = new DataAnnotationsValidator(InstructorJson.Default, maxDepth: 16);

        ValidationFailure failure = Assert.Single(validator.Validate(root));
        Assert.Equal("$.left.name", failure.Path);
    }

    [Fact]
    public void A_key_containing_an_escaped_quote_survives_partial_completion()
    {
        // The salvage path used to re-escape a key that was already escaped, turning \" into \\"
        // and producing a document that would not parse.
        const string Partial = """{"say \"hi\"":"there""";

        Assert.True(PartialJsonCompleter.TryComplete(Partial, out string json));

        using var document = JsonDocument.Parse(json);
        Assert.Equal("there", document.RootElement.GetProperty("say \"hi\"").GetString());
    }

    [Fact]
    public void A_surrogate_pair_split_across_chunks_is_not_corrupted()
    {
        // Astral characters are two UTF-16 chars, and a provider may end a chunk between them.
        // This is the hazard that incremental encoding introduces and that the stateful Encoder
        // is there to remove: encoding each chunk on its own would replace the orphaned half
        // with U+FFFD and corrupt the text permanently.
        const string Emoji = "\U0001F680";   // rocket
        string full = $$"""{"name":"go {{Emoji}} now"}""";

        int split = full.IndexOf(Emoji, StringComparison.Ordinal) + 1;   // between the surrogates

        var buffer = new StreamingJsonBuffer();
        buffer.Append(full.Substring(0, split));
        buffer.Append(full.Substring(split));

        Assert.Equal(full, buffer.GetText());

        Assert.True(buffer.TryComplete(out string json));
        using var document = JsonDocument.Parse(json);
        Assert.Equal($"go {Emoji} now", document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void A_balanced_but_unparseable_preamble_is_skipped()
    {
        // "{ the object }" is balanced but is not JSON. Returning it would burn a repair attempt.
        const string Input = """Here is the { object } you wanted: {"name":"Ali","age":29}""";

        Assert.True(JsonExtractor.TryExtract(Input, out string json));
        Assert.Equal("""{"name":"Ali","age":29}""", json);
    }

    [Fact]
    public void Prose_that_merely_looks_balanced_is_not_mistaken_for_json()
    {
        Assert.False(JsonExtractor.TryExtract("I set the value to { whatever you like }.", out _));
    }

    [Fact]
    public void A_deeply_nested_document_is_not_rejected_by_the_parse_check()
    {
        // The parse check exists to tell JSON from prose. Its depth ceiling must sit far above
        // anything a model produces, or a valid deep document would be discarded here and the
        // caller's serializer -- which may well allow that depth -- would never see it.
        var deep = new StringBuilder();
        const int Depth = 400;

        for (int i = 0; i < Depth; i++)
        {
            deep.Append("""{"a":""");
        }

        deep.Append("1");
        deep.Append('}', Depth);

        string text = deep.ToString();

        Assert.True(JsonExtractor.TryExtract(text, out string json));
        Assert.Equal(text, json);
    }

    [Fact]
    public async Task Streaming_does_not_use_tool_calling()
    {
        // Providers stream tool arguments inconsistently, so streaming drops to a text mode.
        var client = new FakeChatClient("anthropic").RespondWith("""{"name":"Ali","age":29}""");

        var seen = new List<UserInfo>();
        await foreach (UserInfo snapshot in client.AsInstructor().StreamAsync<UserInfo>("who is Ali"))
        {
            seen.Add(snapshot);
        }

        Assert.Null(client.Calls[0].Options!.Tools);
        Assert.Equal("Ali", seen[^1].Name);
    }

    [Fact]
    public async Task Non_streaming_still_uses_tool_calling_on_anthropic()
    {
        // The streaming carve-out must not weaken the normal path.
        var client = new FakeChatClient("anthropic")
            .RespondWithToolCall("UserInfo", new Dictionary<string, object?> { ["name"] = "Ali", ["age"] = 29 });

        await client.AsInstructor().ExtractAsync<UserInfo>("who is Ali");

        Assert.Single(client.Calls[0].Options!.Tools!);
    }

    [Fact]
    public void The_streaming_buffer_matches_a_single_shot_encode()
    {
        // Chunk boundaries must not change the bytes, whatever they fall between.
        const string Text = """{"a":"café ☕ 日本語 A","b":[1,2,3]}""";

        for (int split = 1; split < Text.Length; split++)
        {
            var buffer = new StreamingJsonBuffer();
            buffer.Append(Text.Substring(0, split));
            buffer.Append(Text.Substring(split));

            Assert.Equal(Text, buffer.GetText());
            Assert.Equal(Encoding.UTF8.GetByteCount(Text), buffer.Length);
        }
    }
}
