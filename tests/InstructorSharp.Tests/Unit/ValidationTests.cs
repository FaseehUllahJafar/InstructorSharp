using InstructorSharp.Tests.Fakes;
using InstructorSharp.Validation;
using Xunit;

namespace InstructorSharp.Tests.Unit;

public class ValidationTests
{
    private static IReadOnlyList<ValidationFailure> Validate(object value) =>
        new DataAnnotationsValidator(InstructorJson.Default, maxDepth: 32).Validate(value);

    [Fact]
    public void A_valid_object_produces_no_failures()
    {
        Assert.Empty(Validate(new UserInfo { Name = "Ali", Age = 29 }));
    }

    [Fact]
    public void Top_level_failures_are_addressed_by_json_path()
    {
        IReadOnlyList<ValidationFailure> failures = Validate(new UserInfo { Name = "Ali", Age = 500 });

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal("$.age", failure.Path);
    }

    [Fact]
    public void Property_paths_use_the_json_name_not_the_clr_name()
    {
        // The model is being asked to fix the document it wrote. It has never seen "Age".
        IReadOnlyList<ValidationFailure> failures = Validate(new UserInfo { Name = "Ali", Age = -1 });
        Assert.DoesNotContain(failures, f => f.Path.Contains("Age", StringComparison.Ordinal));
    }

    [Fact]
    public void Nested_collection_failures_carry_their_index()
    {
        // This is the case the framework's own shallow validator misses entirely.
        var invoice = new Invoice
        {
            Number = "INV-1",
            Total = 100,
            Lines =
            [
                new InvoiceLine { Description = "Good line", Amount = 50 },
                new InvoiceLine { Description = "Also fine", Amount = 50 },
                new InvoiceLine { Description = "x", Amount = -5 },
            ],
        };

        IReadOnlyList<ValidationFailure> failures = Validate(invoice);

        Assert.Contains(failures, f => f.Path == "$.lines[2].amount");
        Assert.Contains(failures, f => f.Path == "$.lines[2].description");
        Assert.DoesNotContain(failures, f => f.Path.StartsWith("$.lines[0]", StringComparison.Ordinal));
        Assert.DoesNotContain(failures, f => f.Path.StartsWith("$.lines[1]", StringComparison.Ordinal));
    }

    [Fact]
    public void Required_string_that_is_empty_is_reported()
    {
        IReadOnlyList<ValidationFailure> failures = Validate(new UserInfo { Name = string.Empty, Age = 30 });
        Assert.Contains(failures, f => f.Path == "$.name");
    }

    [Fact]
    public void Reference_cycles_do_not_cause_infinite_recursion()
    {
        var a = new Node { Name = "a" };
        var b = new Node { Name = "b" };
        a.Child = b;
        b.Child = a;

        // The assertion is simply that this returns at all.
        Assert.Empty(Validate(a));
    }

    [Fact]
    public void Depth_limit_stops_a_pathological_graph()
    {
        var root = new Node { Name = "0" };
        Node current = root;
        for (int i = 1; i < 200; i++)
        {
            current.Child = new Node { Name = i.ToString() };
            current = current.Child;
        }

        IReadOnlyList<ValidationFailure> failures =
            new DataAnnotationsValidator(InstructorJson.Default, maxDepth: 5).Validate(root);

        Assert.Empty(failures);
    }

    [Fact]
    public void Null_is_tolerated()
    {
        Assert.Empty(Validate(new Invoice { Number = "A", Total = 1, Lines = null! }));
    }
}
