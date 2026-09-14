using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using InstructorSharp;
using Microsoft.Extensions.AI;

// This sample runs against a scripted in-memory model so you can see the repair loop work
// without an API key or a network call:
//
//     dotnet run --project samples/InstructorSharp.Sample
//
// To point it at a real model, install a provider package and replace CreateClient with, say:
//
//     new OpenAI.Chat.ChatClient("gpt-4o-mini", key).AsIChatClient()

IChatClient client = new ScriptedChatClient(
    // First answer: the line amounts add up to 450, but the model claims 999.
    """
    Sure! Here is the invoice:

    ```json
    {
      "number": "INV-2026-114",
      "total": 999,
      "lines": [
        { "description": "Platform migration", "amount": 300 },
        { "description": "On-call support",    "amount": 150 }
      ]
    }
    ```
    """,

    // Second answer, after being told exactly which field was wrong.
    """
    {
      "number": "INV-2026-114",
      "total": 450,
      "lines": [
        { "description": "Platform migration", "amount": 300 },
        { "description": "On-call support",    "amount": 150 }
      ]
    }
    """);

IInstructor instructor = client.AsInstructorBuilder()
    .WithMaxAttempts(3)
    .WithTokenBudget(20_000)
    .AddValidator<Invoice>(invoice =>
    {
        decimal sum = invoice.Lines.Sum(l => l.Amount);
        return sum == invoice.Total
            ? []
            : [new ValidationFailure("$.total", $"total must equal the sum of the lines, which is {sum}")];
    })
    .Build();

ExtractionResult<Invoice> result = await instructor.TryExtractAsync<Invoice>(
    "Extract the invoice from this email.");

Console.WriteLine($"Succeeded : {result.Succeeded}");
Console.WriteLine($"Attempts  : {result.Attempts.Count}");
Console.WriteLine();

foreach (ExtractionAttempt attempt in result.Attempts)
{
    Console.WriteLine($"  attempt {attempt.AttemptNumber} via {attempt.Mode}: " +
                      (attempt.Succeeded ? "accepted" : "rejected"));

    foreach (ValidationFailure failure in attempt.Failures)
    {
        Console.WriteLine($"      {failure}");
    }
}

Console.WriteLine();

if (result.Succeeded)
{
    Invoice invoice = result.Value!;
    Console.WriteLine($"Invoice {invoice.Number} totalling {invoice.Total:N2}");
    foreach (InvoiceLine line in invoice.Lines)
    {
        Console.WriteLine($"  - {line.Description}: {line.Amount:N2}");
    }
}

internal sealed class Invoice
{
    [Required]
    [Description("The invoice number as printed on the document")]
    public string Number { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal Total { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];
}

internal sealed class InvoiceLine
{
    [Required]
    [MinLength(2)]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }
}

/// <summary>A stand-in model that replays fixed answers, so the sample needs no credentials.</summary>
internal sealed class ScriptedChatClient(params string[] responses) : IChatClient
{
    private int _index;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChatMessage last = messages.Last();
        if (_index > 0)
        {
            Console.WriteLine("--- the model was told: -------------------------------------");
            Console.WriteLine(last.Text);
            Console.WriteLine("-------------------------------------------------------------");
            Console.WriteLine();
        }

        string text = responses[Math.Min(_index++, responses.Length - 1)];
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
