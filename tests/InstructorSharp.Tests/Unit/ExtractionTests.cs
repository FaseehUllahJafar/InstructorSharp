using System.Net.Http;
using InstructorSharp.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;

namespace InstructorSharp.Tests.Unit;

public class ExtractionTests
{
    [Fact]
    public async Task Extracts_on_the_first_attempt()
    {
        var client = new FakeChatClient().RespondWith("""{"name":"Ali","age":29,"city":"Lahore"}""");

        UserInfo user = await client.AsInstructor().ExtractAsync<UserInfo>("Ali is 29, from Lahore");

        Assert.Equal("Ali", user.Name);
        Assert.Equal(29, user.Age);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Retries_when_validation_fails_and_succeeds_on_the_second_attempt()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":29}""");

        UserInfo user = await client.AsInstructor().ExtractAsync<UserInfo>("how old is Ali");

        Assert.Equal(29, user.Age);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task The_retry_carries_the_specific_validation_error_back_to_the_model()
    {
        // This is the whole thesis of the library. A retry that does not say what was wrong is
        // just a re-roll, and re-rolls fail at the same rate every time.
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":29}""");

        await client.AsInstructor().ExtractAsync<UserInfo>("how old is Ali");

        string repair = client.Calls[1].LastMessageText;
        Assert.Contains("$.age", repair, StringComparison.Ordinal);
        Assert.Contains("rejected", repair, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_retry_includes_the_models_own_bad_answer_as_an_assistant_turn()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":29}""");

        await client.AsInstructor().ExtractAsync<UserInfo>("how old is Ali");

        List<ChatMessage> second = client.Calls[1].Messages;
        Assert.Contains(second, m => m.Role == ChatRole.Assistant && m.Text.Contains("500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retries_when_the_response_is_not_json_at_all()
    {
        var client = new FakeChatClient()
            .RespondWith("I'm sorry, I can't help with that.")
            .RespondWith("""{"name":"Ali","age":29}""");

        UserInfo user = await client.AsInstructor().ExtractAsync<UserInfo>("who is Ali");

        Assert.Equal("Ali", user.Name);
        Assert.Contains("JSON", client.Calls[1].LastMessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Throws_with_the_full_attempt_history_when_the_budget_runs_out()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":600}""")
            .RespondWith("""{"name":"Ali","age":700}""");

        ExtractionFailedException ex = await Assert.ThrowsAsync<ExtractionFailedException>(
            () => client.AsInstructor().ExtractAsync<UserInfo>("how old is Ali"));

        Assert.Equal(typeof(UserInfo), ex.TargetType);
        Assert.Equal(3, ex.Attempts.Count);
        Assert.All(ex.Attempts, a => Assert.False(a.Succeeded));
        Assert.Contains("500", ex.Attempts[0].RawResponse!, StringComparison.Ordinal);
        Assert.Contains("$.age", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Never_returns_an_invalid_object()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":500}""");

        // The contract is that a caller either gets a valid object or an exception. Silently
        // handing back an out-of-range value is the failure mode this library exists to prevent.
        await Assert.ThrowsAsync<ExtractionFailedException>(
            () => client.AsInstructor().ExtractAsync<UserInfo>("how old is Ali"));
    }

    [Fact]
    public async Task TryExtract_reports_failure_without_throwing()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":500}""")
            .RespondWith("""{"name":"Ali","age":500}""");

        ExtractionResult<UserInfo> result =
            await client.AsInstructor().TryExtractAsync<UserInfo>("how old is Ali");

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(3, result.Attempts.Count);
        Assert.Contains(result.Failures, f => f.Path == "$.age");
    }

    [Fact]
    public async Task MaxAttempts_of_one_disables_the_repair_loop()
    {
        var client = new FakeChatClient().RespondWith("""{"name":"Ali","age":500}""");

        var options = new InstructorOptions { MaxAttempts = 1 };
        await Assert.ThrowsAsync<ExtractionFailedException>(
            () => client.AsInstructor(options).ExtractAsync<UserInfo>("how old is Ali"));

        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task A_transport_failure_propagates_rather_than_looking_like_a_bad_answer()
    {
        // Reporting a 401 or a DNS failure as "the model produced no valid object" buries the
        // real cause. TryExtractAsync's non-throwing promise covers what the model said, not
        // whether the endpoint was reachable.
        var client = new FakeChatClient()
            .Throws(new HttpRequestException("503 from upstream"))
            .RespondWith("""{"name":"Ali","age":29}""");

        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.AsInstructor().TryExtractAsync<UserInfo>("who is Ali"));

        Assert.Contains("503", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Token_usage_is_accumulated_across_attempts()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":500}""", inputTokens: 100, outputTokens: 20)
            .RespondWith("""{"name":"Ali","age":29}""", inputTokens: 150, outputTokens: 20);

        ExtractionResult<UserInfo> result =
            await client.AsInstructor().TryExtractAsync<UserInfo>("how old is Ali");

        Assert.True(result.Succeeded);
        Assert.Equal(250, result.TotalInputTokens);
        Assert.Equal(40, result.TotalOutputTokens);
    }

    [Fact]
    public async Task The_token_budget_stops_a_runaway_repair_loop()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":500}""", inputTokens: 900, outputTokens: 200)
            .RespondWith("""{"name":"Ali","age":500}""", inputTokens: 900, outputTokens: 200);

        var options = new InstructorOptions { MaxAttempts = 5, TokenBudget = 1_000 };

        TokenBudgetExceededException ex = await Assert.ThrowsAsync<TokenBudgetExceededException>(
            () => client.AsInstructor(options).ExtractAsync<UserInfo>("how old is Ali"));

        Assert.Equal(1_000, ex.Budget);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task TryExtract_reports_budget_exhaustion_without_throwing()
    {
        // The whole point of the Try variant is that the caller need not wrap it in try/catch.
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":500}""", inputTokens: 900, outputTokens: 200)
            .RespondWith("""{"name":"Ali","age":500}""", inputTokens: 900, outputTokens: 200);

        var options = new InstructorOptions { MaxAttempts = 5, TokenBudget = 1_000 };

        ExtractionResult<UserInfo> result =
            await client.AsInstructor(options).TryExtractAsync<UserInfo>("how old is Ali");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures, f => f.Message.Contains("budget", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_per_call_validator_is_applied()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":29}""")
            .RespondWith("""{"name":"Ali","age":29}""");

        var options = new InstructorOptions { MaxAttempts = 2 };
        options.Validators.Add(new AlwaysRejects());

        await Assert.ThrowsAsync<ExtractionFailedException>(
            () => client.AsInstructor().ExtractAsync<UserInfo>("who is Ali", options));

        Assert.Equal(2, client.CallCount);
    }

    private sealed class AlwaysRejects : Validation.IInstructorValidator<UserInfo>
    {
        public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
            UserInfo value,
            CancellationToken cancellationToken = default) =>
            new([new ValidationFailure("$.name", "never acceptable")]);
    }

    [Fact]
    public async Task Extracts_a_primitive_through_the_envelope()
    {
        var client = new FakeChatClient().RespondWith("""{"value":42}""");

        int answer = await client.AsInstructor().ExtractAsync<int>("what is six times seven");

        Assert.Equal(42, answer);
    }

    [Fact]
    public async Task Extracts_a_bare_primitive_when_the_model_ignores_the_envelope()
    {
        var client = new FakeChatClient().RespondWith("""[1,2,3]""");

        List<int> numbers = await client.AsInstructor().ExtractAsync<List<int>>("first three numbers");

        Assert.Equal([1, 2, 3], numbers);
    }

    [Fact]
    public async Task Extracts_a_list_through_the_envelope()
    {
        var client = new FakeChatClient().RespondWith("""{"value":[{"name":"Ali","age":29}]}""");

        List<UserInfo> users = await client.AsInstructor().ExtractAsync<List<UserInfo>>("list the people");

        UserInfo user = Assert.Single(users);
        Assert.Equal("Ali", user.Name);
    }

    [Fact]
    public async Task Extracts_an_enum_by_name()
    {
        var client = new FakeChatClient().RespondWith("""{"title":"Ship it","priority":"High"}""");

        Task_ task = await client.AsInstructor().ExtractAsync<Task_>("what is the task");

        Assert.Equal(Priority.High, task.Priority);
    }

    [Fact]
    public async Task A_custom_validator_failure_is_fed_back_to_the_model()
    {
        // Cross-field arithmetic is exactly the kind of rule a JSON schema cannot express.
        var client = new FakeChatClient()
            .RespondWith("""{"number":"INV-1","total":999,"lines":[{"description":"Consulting","amount":100}]}""")
            .RespondWith("""{"number":"INV-1","total":100,"lines":[{"description":"Consulting","amount":100}]}""");

        IInstructor instructor = client.AsInstructorBuilder()
            .AddValidator<Invoice>(invoice =>
            {
                decimal sum = invoice.Lines.Sum(l => l.Amount);
                return sum == invoice.Total
                    ? []
                    : [new ValidationFailure("$.total", $"total must equal the sum of lines, which is {sum}")];
            })
            .Build();

        Invoice result = await instructor.ExtractAsync<Invoice>("read this invoice");

        Assert.Equal(100, result.Total);
        Assert.Contains("sum of lines", client.Calls[1].LastMessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parses_a_response_wrapped_in_a_markdown_fence()
    {
        var client = new FakeChatClient().RespondWith(
            """
            Here you go:

            ```json
            {"name":"Ali","age":29}
            ```
            """);

        UserInfo user = await client.AsInstructor().ExtractAsync<UserInfo>("who is Ali");

        Assert.Equal("Ali", user.Name);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Reads_an_answer_delivered_as_a_tool_call()
    {
        var client = new FakeChatClient("anthropic")
            .RespondWithToolCall("UserInfo", new Dictionary<string, object?>
            {
                ["name"] = "Ali",
                ["age"] = 29,
            });

        UserInfo user = await client.AsInstructor().ExtractAsync<UserInfo>("who is Ali");

        Assert.Equal("Ali", user.Name);
        Assert.Equal(29, user.Age);
    }

    [Fact]
    public async Task Per_call_options_do_not_mutate_the_shared_defaults()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":29}""")
            .RespondWith("""{"name":"Ali","age":29}""");

        var shared = new InstructorOptions { MaxAttempts = 3 };
        IInstructor instructor = client.AsInstructor(shared);

        await instructor.ExtractAsync<UserInfo>("one", new InstructorOptions { MaxAttempts = 1 });
        await instructor.ExtractAsync<UserInfo>("two");

        Assert.Equal(3, shared.MaxAttempts);
    }

    [Fact]
    public async Task The_configured_chat_options_reach_the_client()
    {
        var client = new FakeChatClient().RespondWith("""{"name":"Ali","age":29}""");

        var options = new InstructorOptions
        {
            ChatOptions = new ChatOptions { ModelId = "gpt-test", Temperature = 0.1f },
        };

        await client.AsInstructor(options).ExtractAsync<UserInfo>("who is Ali");

        Assert.Equal("gpt-test", client.Calls[0].Options!.ModelId);
        Assert.Equal(0.1f, client.Calls[0].Options!.Temperature);
    }

    [Fact]
    public async Task A_forced_tool_from_one_call_does_not_leak_into_the_next()
    {
        var client = new FakeChatClient("anthropic")
            .RespondWithToolCall("UserInfo", new Dictionary<string, object?> { ["name"] = "Ali", ["age"] = 29 })
            .RespondWithToolCall("UserInfo", new Dictionary<string, object?> { ["name"] = "Sara", ["age"] = 31 });

        var options = new InstructorOptions { ChatOptions = new ChatOptions { ModelId = "claude-test" } };
        IInstructor instructor = client.AsInstructor(options);

        await instructor.ExtractAsync<UserInfo>("one");
        await instructor.ExtractAsync<UserInfo>("two");

        Assert.Single(client.Calls[0].Options!.Tools!);
        Assert.Single(client.Calls[1].Options!.Tools!);
    }

    [Fact]
    public async Task Cancellation_is_observed()
    {
        var client = new FakeChatClient().RespondWith("""{"name":"Ali","age":29}""");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.AsInstructor().ExtractAsync<UserInfo>("who is Ali", cancellationToken: cts.Token));
    }
}
