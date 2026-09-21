using System.ComponentModel.DataAnnotations;
using InstructorSharp.Modes;
using InstructorSharp.Parsing;
using InstructorSharp.Tests.Fakes;
using InstructorSharp.Validation;
using Microsoft.Extensions.AI;
using Xunit;

namespace InstructorSharp.Tests.Unit;

/// <summary>
/// Defects found in the pre-release review round that examined the public API, concurrency, and
/// the documentation against the implementation. One test per finding.
/// </summary>
public class CouncilRegressionTests
{
    private sealed class Basket
    {
        [Required]
        public string Owner { get; set; } = string.Empty;

        public Dictionary<string, InvoiceLine> Items { get; set; } = [];
    }

    [Fact]
    public void Dictionary_values_are_validated_and_keyed_by_their_json_path()
    {
        // Three documents advertise dictionary support and nothing covered it.
        var basket = new Basket
        {
            Owner = "Ali",
            Items =
            {
                ["good"] = new InvoiceLine { Description = "Fine", Amount = 10 },
                ["bad"] = new InvoiceLine { Description = "x", Amount = -1 },
            },
        };

        IReadOnlyList<ValidationFailure> failures =
            new DataAnnotationsValidator(InstructorJson.Default, maxDepth: 32).Validate(basket);

        Assert.Contains(failures, f => f.Path == "$.items.bad.amount");
        Assert.DoesNotContain(failures, f => f.Path.StartsWith("$.items.good", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("openai", ExtractionMode.JsonSchema)]
    [InlineData("azureopenai", ExtractionMode.JsonSchema)]
    [InlineData("gemini", ExtractionMode.JsonSchema)]
    [InlineData("mistral", ExtractionMode.JsonSchema)]
    [InlineData("groq", ExtractionMode.JsonSchema)]
    [InlineData("anthropic", ExtractionMode.ToolCall)]
    [InlineData("bedrock", ExtractionMode.ToolCall)]
    [InlineData("cohere", ExtractionMode.ToolCall)]
    [InlineData("ollama", ExtractionMode.Json)]
    [InlineData("vllm", ExtractionMode.Json)]
    [InlineData("deepseek", ExtractionMode.Json)]
    [InlineData("LM Studio", ExtractionMode.Json)]
    [InlineData("something-nobody-has-shipped-yet", ExtractionMode.MarkdownJson)]
    public void Every_provider_in_the_readme_table_resolves_to_the_documented_mode(
        string provider,
        ExtractionMode expected)
    {
        var metadata = new ChatClientMetadata(provider, new Uri("https://example.test"), "m");

        ExtractionStrategy? best = null;
        foreach (ExtractionStrategy strategy in Instructor.DefaultStrategies)
        {
            if (strategy.CanHandle(metadata) && (best is null || strategy.Priority > best.Priority))
            {
                best = strategy;
            }
        }

        Assert.NotNull(best);
        Assert.Equal(expected, best!.Mode);
    }

    [Fact]
    public void A_brace_inside_a_line_comment_does_not_unbalance_the_scan()
    {
        // The deserializer is configured to skip comments, so the scanner has to understand them
        // too or it rejects a response the parser would have accepted.
        const string Input = "{\n  // use {braces} sparingly\n  \"a\": 1\n}";

        Assert.True(JsonExtractor.TryExtract(Input, out string json));
        Assert.Contains("\"a\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_brace_inside_a_block_comment_does_not_unbalance_the_scan()
    {
        const string Input = "{ /* } not a real close */ \"a\": 1 }";

        Assert.True(JsonExtractor.TryExtract(Input, out string json));
        Assert.Contains("\"a\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streaming_refuses_tool_call_mode_even_when_it_is_requested_explicitly()
    {
        // Forcing ToolCall and then streaming used to set a forced tool and read only update.Text,
        // which is empty for tool-call updates, so the stream collected nothing and threw.
        var client = new FakeChatClient("anthropic").RespondWith("""{"name":"Ali","age":29}""");

        var options = new InstructorOptions { Mode = ExtractionMode.ToolCall };

        var seen = new List<UserInfo>();
        await foreach (UserInfo snapshot in client.AsInstructor().StreamAsync<UserInfo>("who is Ali", options))
        {
            seen.Add(snapshot);
        }

        Assert.Null(client.Calls[0].Options!.Tools);
        Assert.Equal("Ali", seen[seen.Count - 1].Name);
    }

    [Fact]
    public async Task An_explicit_mode_prefers_a_strategy_that_can_serve_the_provider()
    {
        // A custom strategy registered for a mode must not intercept a provider it cannot serve.
        var client = new FakeChatClient("openai").RespondWith("""{"name":"Ali","age":29}""");

        IInstructor instructor = client.AsInstructorBuilder()
            .AddStrategy(new NeverHandles())
            .WithMode(ExtractionMode.JsonSchema)
            .Build();

        await instructor.ExtractAsync<UserInfo>("who is Ali");

        // The built-in JsonSchemaStrategy ran, so a response format is set and the marker is not.
        Assert.NotNull(client.Calls[0].Options!.ResponseFormat);
        Assert.True(client.Calls[0].Options!.AdditionalProperties is null
                    || !client.Calls[0].Options!.AdditionalProperties!.ContainsKey("never"));
    }

    private sealed class NeverHandles : ExtractionStrategy
    {
        public override ExtractionMode Mode => ExtractionMode.JsonSchema;

        public override int Priority => 9999;

        public override bool CanHandle(ChatClientMetadata? metadata) => false;

        public override void Apply(StrategyContext context)
        {
            context.ChatOptions.AdditionalProperties ??= [];
            context.ChatOptions.AdditionalProperties["never"] = true;
        }
    }

    [Fact]
    public void A_runaway_stream_is_abandoned_rather_than_growing_without_bound()
    {
        var buffer = new StreamingJsonBuffer(maxBytes: 4096);

        InstructorException ex = Assert.Throws<InstructorException>(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                buffer.Append(new string('x', 64));
            }
        });

        Assert.Contains("exceeded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Options_handed_to_an_instructor_are_insulated_from_later_mutation()
    {
        // The instructor is documented as a thread-safe singleton, so a caller mutating the
        // options object afterwards must not change the behaviour of work already in flight.
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":500}""");

        var options = new InstructorOptions { MaxAttempts = 1 };
        IInstructor instructor = client.AsInstructor(options);

        options.MaxAttempts = 3;

        await Assert.ThrowsAsync<ExtractionFailedException>(
            () => instructor.ExtractAsync<UserInfo>("how old is Ali"));

        // One call, not three: the instructor is still using the value it was built with.
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public void A_validation_depth_below_one_is_rejected_instead_of_silently_disabling_validation()
    {
        var options = new InstructorOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxValidationDepth = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxValidationDepth = -1);
    }

    [Fact]
    public void A_result_can_be_constructed_outside_the_assembly()
    {
        // IInstructor is unimplementable if its return types cannot be built by a caller.
        var attempt = new ExtractionAttempt(
            1, ExtractionMode.JsonSchema, "{}", [], null, 10, 20);

        var result = new ExtractionResult<UserInfo>(new UserInfo { Name = "Ali" }, true, [attempt]);

        Assert.True(result.Succeeded);
        Assert.Equal(10, result.TotalInputTokens);
        Assert.Equal("Ali", result.ValueOrThrow().Name);
    }
}
