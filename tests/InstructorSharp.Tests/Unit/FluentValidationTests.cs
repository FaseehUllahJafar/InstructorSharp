using FluentValidation;
using InstructorSharp.FluentValidation;
using InstructorSharp.Tests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InstructorSharp.Tests.Unit;

public class FluentValidationTests
{
    private sealed class InvoiceValidator : AbstractValidator<Invoice>
    {
        internal InvoiceValidator()
        {
            RuleFor(i => i.Number).NotEmpty().WithMessage("invoice number is required");
            RuleForEach(i => i.Lines).ChildRules(line =>
                line.RuleFor(l => l.Amount).GreaterThan(0).WithMessage("amount must be positive"));
            RuleFor(i => i).Must(i => i.Total == i.Lines.Sum(l => l.Amount))
                           .WithName("Total")
                           .WithMessage("total must equal the sum of the line amounts");
        }
    }

    [Fact]
    public async Task A_fluent_rule_failure_is_reported_as_a_json_path()
    {
        var adapter = new FluentValidationAdapter<Invoice>(new InvoiceValidator());

        var invoice = new Invoice
        {
            Number = "INV-1",
            Total = 10,
            Lines = [new InvoiceLine { Description = "Work", Amount = -5 }],
        };

        IReadOnlyList<ValidationFailure> failures = await adapter.ValidateAsync(invoice);

        Assert.Contains(failures, f => f.Path == "$.lines[0].amount");
    }

    [Fact]
    public async Task A_valid_object_produces_no_failures()
    {
        var adapter = new FluentValidationAdapter<Invoice>(new InvoiceValidator());

        var invoice = new Invoice
        {
            Number = "INV-1",
            Total = 50,
            Lines = [new InvoiceLine { Description = "Work", Amount = 50 }],
        };

        Assert.Empty(await adapter.ValidateAsync(invoice));
    }

    [Fact]
    public async Task Fluent_failures_drive_the_repair_loop()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"number":"INV-1","total":999,"lines":[{"description":"Work","amount":50}]}""")
            .RespondWith("""{"number":"INV-1","total":50,"lines":[{"description":"Work","amount":50}]}""");

        IInstructor instructor = client.AsInstructorBuilder()
            .AddFluentValidator(new InvoiceValidator())
            .Build();

        Invoice invoice = await instructor.ExtractAsync<Invoice>("read this invoice");

        Assert.Equal(50, invoice.Total);
        Assert.Contains("sum of the line amounts", client.Calls[1].LastMessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validators_registered_in_the_container_are_picked_up()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Ali","age":29}""")
            .RespondWith("""{"name":"Ali","age":29}""");

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(client);
        services.AddInstructorValidator(new RejectsEveryone());
        services.AddInstructor();

        using ServiceProvider provider = services.BuildServiceProvider();
        var instructor = provider.GetRequiredService<IInstructor>();

        var options = new InstructorOptions { MaxAttempts = 2 };
        await Assert.ThrowsAsync<ExtractionFailedException>(
            () => instructor.ExtractAsync<UserInfo>("who is Ali", options));

        Assert.Equal(2, client.CallCount);
    }

    private sealed class RejectsEveryone : Validation.IInstructorValidator<UserInfo>
    {
        public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
            UserInfo value,
            CancellationToken cancellationToken = default) =>
            new([new ValidationFailure("$.name", "nobody is acceptable")]);
    }
}
