using InstructorSharp.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;

namespace InstructorSharp.Tests.Integration;

/// <summary>
/// Tests that talk to a real model. They are skipped unless the relevant API key is present, so
/// the default <c>dotnet test</c> stays fast, free and offline.
/// </summary>
/// <remarks>
/// <para>
/// These exist because the unit suite proves the library does what it intends, and only a live
/// call proves the intent matches what providers actually accept. A strict schema that OpenAI
/// rejects would pass every test above.
/// </para>
/// <para>
/// To run them, set <c>OPENAI_API_KEY</c> (and optionally <c>ANTHROPIC_API_KEY</c>), add the
/// matching provider package to this project, and uncomment the client factory below. They are
/// left commented rather than referenced so that the default build has no provider dependency.
/// </para>
/// </remarks>
public class LiveProviderTests
{
    private const string OpenAiKeyVariable = "OPENAI_API_KEY";

    private static string? OpenAiKey => Environment.GetEnvironmentVariable(OpenAiKeyVariable);

    private static bool OpenAiConfigured => !string.IsNullOrWhiteSpace(OpenAiKey);

    /// <summary>
    /// Builds a live client. Left unimplemented deliberately: wiring it requires a provider
    /// package, and which one is the reader's choice.
    /// </summary>
    private static IChatClient CreateOpenAiClient()
    {
        // Step 1: dotnet add package Microsoft.Extensions.AI.OpenAI
        // Step 2: replace the body below with:
        //
        //   return new OpenAI.Chat.ChatClient("gpt-4o-mini", OpenAiKey).AsIChatClient();
        //
        throw new SkipException(
            "Add a provider package and implement CreateOpenAiClient to run the live suite. " +
            "See the remarks on LiveProviderTests.");
    }

    [Fact]
    public async Task Extracts_a_person_from_prose()
    {
        Assert.SkipUnless(OpenAiConfigured, $"{OpenAiKeyVariable} is not set.");

        IInstructor instructor = CreateOpenAiClient().AsInstructor();

        UserInfo user = await instructor.ExtractAsync<UserInfo>(
            "Faseeh is a 29 year old engineer living in Lahore.");

        Assert.Equal(29, user.Age);
        Assert.Contains("Lahore", user.City ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Extracts_a_list_which_the_built_in_path_cannot_request()
    {
        Assert.SkipUnless(OpenAiConfigured, $"{OpenAiKeyVariable} is not set.");

        IInstructor instructor = CreateOpenAiClient().AsInstructor();

        List<UserInfo> people = await instructor.ExtractAsync<List<UserInfo>>(
            "Ali is 29 and Sara is 31. Both live in Karachi.");

        Assert.Equal(2, people.Count);
    }

    [Fact]
    public async Task The_repair_loop_recovers_from_a_deliberately_hard_constraint()
    {
        Assert.SkipUnless(OpenAiConfigured, $"{OpenAiKeyVariable} is not set.");

        // The model is given data that violates a rule it cannot see in the schema, so the
        // first answer should fail validation and the second should be corrected.
        IInstructor instructor = CreateOpenAiClient().AsInstructorBuilder()
            .AddValidator<UserInfo>(u => u.City is "Lahore"
                ? []
                : [new ValidationFailure("$.city", "city must be exactly \"Lahore\"")])
            .Build();

        UserInfo user = await instructor.ExtractAsync<UserInfo>("Ali, 29, from the city of Lahore, Pakistan.");

        Assert.Equal("Lahore", user.City);
    }

    private sealed class SkipException(string message) : Exception(message);
}
