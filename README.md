# InstructorSharp

**Validated, strongly-typed objects out of any LLM — with automatic repair when the model gets it wrong.**

[![CI](https://github.com/FaseehUllahJafar/InstructorSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/FaseehUllahJafar/InstructorSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/InstructorSharp.svg)](https://www.nuget.org/packages/InstructorSharp)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

The single most common thing anyone needs from an LLM is a validated, typed object rather than a
hopeful string. Python solved this with [Instructor](https://github.com/567-labs/instructor).
So did TypeScript, Go, Ruby, Elixir, Rust and PHP.

This is the C# one.

```csharp
using InstructorSharp;

var instructor = chatClient.AsInstructor();

var user = await instructor.ExtractAsync<UserInfo>(
    "Faseeh is a 29 year old engineer living in Lahore.");

// user.Name == "Faseeh", user.Age == 29, user.City == "Lahore"
// ...and it is guaranteed to satisfy every validation rule on UserInfo, or it threw.
```

---

## Why not just use `Microsoft.Extensions.AI`?

`GetResponseAsync<T>` is a good starting point and this library builds on the same `IChatClient`
abstraction. It stops short in four places that matter in production:

| | `Microsoft.Extensions.AI` | InstructorSharp |
|---|---|---|
| Model returns an invalid object | you get it back, invalid | validation errors are sent back to the model and it tries again |
| `List<T>`, `int`, `string` as the root | rejected by strict providers | wrapped and unwrapped automatically |
| Model wraps JSON in prose or a fence | deserialization throws | parsed out defensively |
| Provider without native schema support | silently degrades | explicit per-provider strategies, with a prompted fallback |

The repair loop is the core of it. A plain retry re-rolls the same request and fails at the same
rate every time. This puts the model's own bad answer back in the conversation and tells it
exactly which field was wrong:

```
Your previous response was rejected because of these problems:
- $.lines[2].amount: The field Amount must be between 0.01 and 1000000.
- $.total: total must equal the sum of the line amounts, which is 450.

Return the corrected JSON value. Fix only the fields listed above and keep
every other field exactly as you already had it. Output JSON only.
```

Models are markedly better at correcting a specific mistake than at re-doing the whole task.

---

## Install

```bash
dotnet add package InstructorSharp
```

Then bring your own model. InstructorSharp depends on **no provider SDK** — it works with any
`IChatClient`, so whichever you already use is the one it talks to:

```bash
# pick whichever you already have
dotnet add package Microsoft.Extensions.AI.OpenAI     # OpenAI, Azure OpenAI, and OpenAI-compatible endpoints
dotnet add package OllamaSharp                        # Ollama
```

**Targets:** .NET 10, .NET 8, and **.NET Standard 2.0 — so it runs on .NET Framework 4.6.2+.**
If you are maintaining a Framework 4.8 service, this is the only structured-extraction library
for C# you can actually install.

---

## Defining what you want

Any POCO or record. Validation attributes are the contract, and they are enforced — not merely
suggested to the model:

```csharp
public sealed record Invoice
{
    [Required]
    public string Number { get; init; } = "";

    [Range(0, double.MaxValue)]
    public decimal Total { get; init; }

    public List<InvoiceLine> Lines { get; init; } = [];
}

public sealed record InvoiceLine
{
    [Required, MinLength(2)]
    public string Description { get; init; } = "";

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; init; }
}

var invoice = await instructor.ExtractAsync<Invoice>(
    "Extract the invoice from this email:\n" + emailBody);
```

Validation is **recursive**. `Validator.TryValidateObject` in the BCL checks the top-level object
and stops, so a bad amount on the third line sails through. This walks nested objects,
collections and dictionaries, and reports failures by JSON path (`$.lines[2].amount`) because
that is the document the model actually wrote.

### Rules a schema cannot express

Cross-field arithmetic, a value that must exist in your database, a total that must equal its
lines:

```csharp
var instructor = chatClient.AsInstructorBuilder()
    .AddValidator<Invoice>(inv =>
    {
        var sum = inv.Lines.Sum(l => l.Amount);
        return sum == inv.Total
            ? []
            : [new ValidationFailure("$.total", $"total must equal the sum of lines, which is {sum}")];
    })
    .Build();
```

Write the message as an instruction to the model, because that is where it goes.

### Already have FluentValidation rules?

```bash
dotnet add package InstructorSharp.FluentValidation
```

```csharp
var instructor = chatClient.AsInstructorBuilder()
    .AddFluentValidator(new InvoiceValidator())
    .Build();
```

Your existing `AbstractValidator<T>` becomes the repair feedback. CLR property paths
(`Lines[2].Total`) are rewritten to JSON paths (`$.lines[2].total`) automatically.

---

## Primitives and collections

These are the types the built-in path cannot request, because a strict schema root must be an
object. InstructorSharp wraps them for transport and unwraps them on the way back:

```csharp
int count      = await instructor.ExtractAsync<int>("How many people are mentioned?");
List<string> t = await instructor.ExtractAsync<List<string>>("List the topics.");
Priority p     = await instructor.ExtractAsync<Priority>("How urgent is this ticket?");
```

---

## Streaming

Snapshots of the object as the model writes it, so a UI fills in field by field:

```csharp
await foreach (var partial in instructor.StreamAsync<Invoice>(prompt))
{
    Render(partial);   // Number arrives, then Total, then lines appear one by one
}
```

Or stream the elements of a collection, processing each as soon as it is complete:

```csharp
await foreach (var item in instructor.StreamListAsync<ActionItem>(transcript))
{
    await queue.PublishAsync(item);   // item 1 goes out while the model is still writing item 5
}
```

Two deliberate details:

- A partially-received **string** is shown as it arrives, but a partially-received **number** is
  withheld until provably complete. A half-written string reads as obviously mid-word; a
  half-written `123` looks exactly like a confident `1`.
- Streaming never uses tool-call mode, even if you ask for it explicitly. Providers deliver
  partial tool arguments inconsistently, so on a tool-calling provider such as Anthropic,
  `StreamAsync` drops to the strongest text-producing mode instead of guessing. Non-streaming
  calls are unaffected.

There is no repair loop while streaming, by design: output a user has already seen cannot be
silently retried. The final object is validated, and an invalid one throws.

---

## Controlling cost

A repair loop can quietly cost several times what the first call did. It is capped:

```csharp
var instructor = chatClient.AsInstructorBuilder()
    .WithMaxAttempts(3)
    .WithTokenBudget(10_000)   // across all attempts of one extraction
    .Build();
```

Two honest limits on that budget. It stops the *next* attempt once the spend so far has passed
the ceiling; it cannot truncate a reply already in flight, so a single very long response can
overshoot. And it applies to `ExtractAsync`/`TryExtractAsync` only — streaming is bounded by
`MaxStreamBytes` (8 MiB by default) instead, because token usage is not known until the stream
ends.

Use `TryExtractAsync` when you would rather inspect the outcome than catch:

```csharp
var result = await instructor.TryExtractAsync<Invoice>(prompt);

if (!result.Succeeded)
{
    logger.LogWarning("Extraction failed after {Attempts} attempts, {Tokens} tokens. First error: {Error}",
        result.Attempts.Count,
        result.TotalInputTokens + result.TotalOutputTokens,
        result.Failures.FirstOrDefault());
}
```

`ExtractAsync` either returns a fully valid object or throws `ExtractionFailedException` carrying
every attempt and the model's raw text for each. It never hands back a partially valid value.

---

## Providers

The request is shaped per provider, chosen automatically from the client's metadata:

| Provider | Mode | Mechanism |
|---|---|---|
| OpenAI, Azure OpenAI, Gemini, Mistral, Groq | `JsonSchema` | native schema-constrained decoding |
| Anthropic, Bedrock, Cohere | `ToolCall` | single forced tool whose parameters are the schema |
| Ollama, vLLM, LM Studio, DeepSeek | `Json` | JSON mode plus the schema in the prompt |
| anything else | `MarkdownJson` | prompted, fenced, parsed defensively |

Override it when you know better:

```csharp
var options = new InstructorOptions { Mode = ExtractionMode.ToolCall };
```

### When a provider changes its mind

Provider behaviour is the part of this that rots fastest. The strategy seam is **public from
v1**, so you can fix it in your own codebase the same afternoon rather than waiting for a
release:

```csharp
public sealed class MyProviderStrategy : ExtractionStrategy
{
    public override ExtractionMode Mode => ExtractionMode.JsonSchema;
    public override int Priority => 1000;                       // beats the built-ins
    public override bool CanHandle(ChatClientMetadata? m) => m?.ProviderName == "my-provider";

    public override void Apply(StrategyContext context)
    {
        context.ChatOptions.AdditionalProperties ??= [];
        context.ChatOptions.AdditionalProperties["guided_json"] = context.SchemaText;
    }
}

var instructor = chatClient.AsInstructorBuilder()
    .AddStrategy(new MyProviderStrategy())
    .Build();
```

---

## Dependency injection

```csharp
builder.Services.AddChatClient(/* your provider */);
builder.Services.AddInstructorValidator<InvoiceRules, Invoice>();
builder.Services.AddInstructor(b => b.WithMaxAttempts(3).WithTokenBudget(10_000));
```

Then inject `IInstructor`. It is registered as a singleton, holds no per-request state, and
shares its schema cache across requests.

---

## Observability

An `ActivitySource` and a `Meter`, both named `InstructorSharp`:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(InstructorDiagnostics.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(InstructorDiagnostics.MeterName));
```

The histogram worth alerting on is `instructorsharp.attempts`. A service whose extractions
normally succeed first time and starts averaging two has had a model or prompt regression, and
that shows up here long before it shows up as an error rate.

---

## Trimming and Native AOT

The extraction path is trim-safe. Two things to do when publishing AOT:

```csharp
[JsonSerializable(typeof(Invoice))]
internal partial class AppJsonContext : JsonSerializerContext;

var options = new InstructorOptions
{
    SerializerOptions = AppJsonContext.Default.Options,
    ValidateDataAnnotations = false,   // attribute discovery is reflective by nature
};
```

Express your rules as `IInstructorValidator<T>` instead, which is fully AOT-safe.

Be aware that the defaults are the reflective path: `ValidateDataAnnotations` is true and
`SerializerOptions` is a reflection-based configuration. Those two lines are what switch it off;
without them a trimmed or AOT-published app will fail at runtime, not at build time.

---

## Prior art and honest positioning

- **[Instructor](https://github.com/567-labs/instructor)** (Python) — the original, and the design
  this follows. Six other languages have a port; C# did not.
- **[`Instructor.NET`](https://www.nuget.org/packages/Instructor.NET)** — an earlier, unrelated
  C# attempt (3 commits, June 2025, OpenAI-only, no repair loop). Not maintained. The NuGet ID
  being taken is why this package is named `InstructorSharp`.
- **[Ingot](https://github.com/landsharkiest/Ingot)** — a current .NET library with a genuinely
  similar goal and a good design. It is .NET 8+, and its roadmap lists streaming, FluentValidation
  and AOT support as future work. If you are on .NET 8+ and do not need those, it is a reasonable
  choice and you should look at it.

## Verifying against a real model

```bash
dotnet run --project samples/InstructorSharp.LiveCheck
```

It prints how to point itself at Gemini or a local Ollama, both free and neither requiring a
payment card, then checks objects, `List<T>` and `int` as root types, enums, the repair loop and
streaming against the real provider. See `samples/InstructorSharp.LiveCheck/README.md`.

## Contributing

Issues and PRs welcome. `dotnet test` runs the full suite offline in about a second; no API key
is needed and no network call is made. The suite runs on .NET 10, .NET 8 and .NET Framework 4.7.2,
so the netstandard2.0 asset is executed rather than merely compiled.

## License

MIT
