using InstructorSharp.Modes;
using InstructorSharp.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;

namespace InstructorSharp.Tests.Unit;

public class StrategySelectionTests
{
    [Theory]
    [InlineData("openai")]
    [InlineData("azureopenai")]
    [InlineData("gemini")]
    public async Task Providers_with_native_schema_support_get_a_response_format(string provider)
    {
        var client = new FakeChatClient(provider).RespondWith("""{"name":"Ali","age":29}""");

        await client.AsInstructor().ExtractAsync<UserInfo>("who is Ali");

        ChatOptions options = client.Calls[0].Options!;
        Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
        Assert.NotNull(((ChatResponseFormatJson)options.ResponseFormat!).Schema);
        Assert.Null(options.Tools);
    }

    [Fact]
    public async Task Anthropic_gets_a_forced_tool_call()
    {
        var client = new FakeChatClient("anthropic")
            .RespondWithToolCall("UserInfo", new Dictionary<string, object?> { ["name"] = "Ali", ["age"] = 29 });

        await client.AsInstructor().ExtractAsync<UserInfo>("who is Ali");

        ChatOptions options = client.Calls[0].Options!;
        Assert.Single(options.Tools!);
        Assert.IsType<RequiredChatToolMode>(options.ToolMode);
    }

    [Fact]
    public async Task Ollama_gets_json_mode_plus_the_schema_in_the_prompt()
    {
        var client = new FakeChatClient("ollama").RespondWith("""{"name":"Ali","age":29}""");

        await client.AsInstructor().ExtractAsync<UserInfo>("who is Ali");

        Assert.Equal(ChatResponseFormat.Json, client.Calls[0].Options!.ResponseFormat);
        Assert.Contains("JSON Schema", client.Calls[0].AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_provider_falls_back_to_prompting()
    {
        // The fallback is what makes this work against a model nobody has heard of yet.
        const string Fenced = "```json\n{\"name\":\"Ali\",\"age\":29}\n```";
        var client = new FakeChatClient("some-new-startup-2027").RespondWith(Fenced);

        UserInfo user = await client.AsInstructor().ExtractAsync<UserInfo>("who is Ali");

        Assert.Equal("Ali", user.Name);
        Assert.Null(client.Calls[0].Options!.ResponseFormat);
        Assert.Contains("fenced code block", client.Calls[0].AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_explicit_mode_overrides_detection()
    {
        var client = new FakeChatClient("openai").RespondWith("""{"name":"Ali","age":29}""");

        var options = new InstructorOptions { Mode = ExtractionMode.MarkdownJson };
        await client.AsInstructor(options).ExtractAsync<UserInfo>("who is Ali");

        Assert.Null(client.Calls[0].Options!.ResponseFormat);
    }

    [Fact]
    public async Task The_schema_instruction_is_sent_once_not_once_per_attempt()
    {
        // Regression test. The strategy used to append to the durable conversation, so a
        // three-attempt extraction paid for the schema three times and buried the repair
        // request under a schema dump.
        var client = new FakeChatClient("ollama")
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":29}""");

        await client.AsInstructor().ExtractAsync<UserInfo>("how old is Ali");

        int schemaMentions = client.Calls[1].Messages
            .Count(m => m.Text.Contains("JSON Schema", StringComparison.Ordinal));

        Assert.Equal(1, schemaMentions);
    }

    [Fact]
    public async Task The_repair_request_is_the_last_thing_the_model_reads()
    {
        var client = new FakeChatClient("ollama")
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":29}""");

        await client.AsInstructor().ExtractAsync<UserInfo>("how old is Ali");

        ChatMessage last = client.Calls[1].Messages[client.Calls[1].Messages.Count - 1];
        Assert.Equal(ChatRole.User, last.Role);
        Assert.Contains("$.age", last.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_schema_instruction_sits_with_the_system_messages()
    {
        var client = new FakeChatClient("ollama").RespondWith("""{"name":"Ali","age":29}""");

        await client.AsInstructor().ExtractAsync<UserInfo>("You extract people.", "Ali is 29");

        List<ChatMessage> sent = client.Calls[0].Messages;
        int firstNonSystem = sent.FindIndex(m => m.Role != ChatRole.System);
        int schemaIndex = sent.FindIndex(m => m.Text.Contains("JSON Schema", StringComparison.Ordinal));

        Assert.True(schemaIndex >= 0);
        Assert.True(schemaIndex < firstNonSystem, "the schema instruction must precede the user turn");
    }

    [Fact]
    public async Task A_custom_strategy_takes_precedence()
    {
        var client = new FakeChatClient("openai").RespondWith("""{"name":"Ali","age":29}""");

        IInstructor instructor = client.AsInstructorBuilder()
            .AddStrategy(new StampingStrategy())
            .Build();

        await instructor.ExtractAsync<UserInfo>("who is Ali");

        Assert.Equal("yes", client.Calls[0].Options!.AdditionalProperties!["stamped"]);
    }

    /// <summary>A strategy proving the public seam works end to end.</summary>
    private sealed class StampingStrategy : ExtractionStrategy
    {
        public override ExtractionMode Mode => ExtractionMode.JsonSchema;

        public override int Priority => 1000;

        public override bool CanHandle(ChatClientMetadata? metadata) => true;

        public override void Apply(StrategyContext context)
        {
            context.ChatOptions.AdditionalProperties ??= [];
            context.ChatOptions.AdditionalProperties["stamped"] = "yes";
        }
    }
}
