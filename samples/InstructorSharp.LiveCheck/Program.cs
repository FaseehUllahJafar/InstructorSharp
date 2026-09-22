using System.ClientModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using InstructorSharp;
using InstructorSharp.Validation;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

// Proves the library against a real model rather than a scripted fake. Both providers below are
// free and neither asks for a payment card.
//
//   Gemini    set GEMINI_API_KEY   (key from https://aistudio.google.com/apikey)
//   Ollama    set OLLAMA_MODEL     (after "ollama pull llama3.1:8b" - nothing else, no account)
//
// Run:  dotnet run --project samples/InstructorSharp.LiveCheck

IChatClient client;
string description;

try
{
    client = CreateClient(out description);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine(ex.Message);
    return 2;
}

Console.WriteLine($"Provider: {description}");
Console.WriteLine();

IInstructor instructor = client.AsInstructor();
int failures = 0;

// 1. An ordinary object.
await Check("object", async () =>
{
    UserInfo user = await instructor.ExtractAsync<UserInfo>(
        "Faseeh is a 29 year old engineer living in Lahore.");

    Require(user.Age == 29, $"age was {user.Age}, expected 29");
    Require(!string.IsNullOrWhiteSpace(user.Name), "name was empty");
    return $"{user.Name}, {user.Age}, {user.City}";
});

// 2. A list as the root type. Microsoft.Extensions.AI cannot request this at all, so it is the
//    headline claim in the README and the one that most needs proving against a real provider.
await Check("List<T> as the root type", async () =>
{
    List<UserInfo> people = await instructor.ExtractAsync<List<UserInfo>>(
        "Ali is 29 and Sara is 31. Both live in Karachi.");

    Require(people.Count == 2, $"expected 2 people, got {people.Count}");
    return string.Join("; ", people.Select(p => $"{p.Name} ({p.Age})"));
});

// 3. A primitive as the root type. Same story.
await Check("int as the root type", async () =>
{
    int days = await instructor.ExtractAsync<int>("How many days are in a leap year? Answer with the number.");
    Require(days == 366, $"expected 366, got {days}");
    return days.ToString();
});

// 4. An enum, which is also a non-object root.
await Check("enum as the root type", async () =>
{
    Priority priority = await instructor.ExtractAsync<Priority>(
        "A customer reports the checkout page is completely down. How urgent is this ticket?");

    return priority.ToString();
});

// 5. The repair loop, forced. The rule is invisible to the schema, so the first answer should
//    fail validation and the model should correct it from the feedback.
await Check("repair loop", async () =>
{
    var strictCity = new InstructorOptions { MaxAttempts = 3 };
    strictCity.Validators.Add(new CityMustBeUppercase());

    ExtractionResult<UserInfo> result = await instructor.TryExtractAsync<UserInfo>(
        "Bilal is 41 and lives in Islamabad.", strictCity);

    Require(result.Succeeded, "the repair loop never produced a valid object");
    Require(result.Value!.City == result.Value.City!.ToUpperInvariant(), "city was not corrected");
    return $"{result.Value.City} after {result.Attempts.Count} attempt(s)";
});

// 6. Streaming, which exercises the partial-JSON completer against a real token stream.
await Check("streaming", async () =>
{
    var snapshots = new List<string?>();
    await foreach (UserInfo snapshot in instructor.StreamAsync<UserInfo>(
        "Invent a software engineer living in Karachi and describe them."))
    {
        snapshots.Add(snapshot.Name);
    }

    Require(snapshots.Count > 0, "no snapshots were produced");
    return $"{snapshots.Count} snapshot(s), final name: {snapshots[snapshots.Count - 1]}";
});

Console.WriteLine();
Console.WriteLine(failures == 0
    ? "All checks passed. The library works against a real model."
    : $"{failures} check(s) failed. Do not publish until these pass.");

return failures == 0 ? 0 : 1;

async Task Check(string name, Func<Task<string>> run)
{
    Console.Write($"  {name,-28} ");
    try
    {
        string detail = await run();
        Console.WriteLine($"ok    {detail}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL  {ex.GetType().Name}: {ex.Message}");

        if (ex is ExtractionFailedException failed)
        {
            foreach (ExtractionAttempt attempt in failed.Attempts)
            {
                Console.WriteLine($"          attempt {attempt.AttemptNumber} via {attempt.Mode}");
                Console.WriteLine($"          model said: {Trim(attempt.RawResponse)}");
                foreach (ValidationFailure failure in attempt.Failures)
                {
                    Console.WriteLine($"          rejected:   {failure}");
                }
            }
        }
    }
}

static string Trim(string? text) =>
    text is null ? "(nothing)" : text.Length <= 160 ? text.Replace("\n", " ") : text.Substring(0, 160).Replace("\n", " ") + "...";

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static IChatClient CreateClient(out string description)
{
    string? geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    if (!string.IsNullOrWhiteSpace(geminiKey))
    {
        // Gemini exposes an OpenAI-compatible endpoint, so the OpenAI client drives it with only
        // a changed base address. The free tier serves the Flash models and needs no payment card.
        string model = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-2.5-flash";

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"),
        };

        description = $"Google AI Studio (Gemini), model {model}, free tier";
        return new OpenAIClient(new ApiKeyCredential(geminiKey!), options)
            .GetChatClient(model)
            .AsIChatClient();
    }

    string? ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
    if (!string.IsNullOrWhiteSpace(ollamaModel))
    {
        string host = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";
        description = $"Ollama at {host}, model {ollamaModel}, local and free";
        return new OllamaApiClient(new Uri(host), ollamaModel!);
    }

    throw new InvalidOperationException(
        """
        No provider configured. Both options below are free and neither asks for a payment card.

          Gemini, no install:
            1. Get a key at https://aistudio.google.com/apikey  (Google account, no card)
            2. PowerShell:  $env:GEMINI_API_KEY = "your-key"
            3. dotnet run --project samples/InstructorSharp.LiveCheck

          Ollama, no account at all, runs on your machine:
            1. Install from https://ollama.com/download
            2. ollama pull llama3.1:8b
            3. PowerShell:  $env:OLLAMA_MODEL = "llama3.1:8b"
            4. dotnet run --project samples/InstructorSharp.LiveCheck
        """);
}

/// <summary>A rule no schema can express, used to force the repair loop to run.</summary>
internal sealed class CityMustBeUppercase : IInstructorValidator<UserInfo>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        UserInfo value,
        CancellationToken cancellationToken = default)
    {
        bool ok = !string.IsNullOrEmpty(value.City) && value.City == value.City.ToUpperInvariant();

        return new(ok
            ? Array.Empty<ValidationFailure>()
            : [new ValidationFailure("$.city", "city must be written in ALL CAPITAL LETTERS")]);
    }
}

internal sealed class UserInfo
{
    [Required]
    [Description("The person's full name")]
    public string Name { get; set; } = string.Empty;

    [Range(0, 130)]
    public int Age { get; set; }

    [Description("The city they live in")]
    public string? City { get; set; }
}

internal enum Priority
{
    Low,
    Medium,
    High,
    Critical,
}
