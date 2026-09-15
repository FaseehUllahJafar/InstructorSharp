using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace InstructorSharp.Tests.Fakes;

public sealed class UserInfo
{
    [Required]
    [Description("The person's full name")]
    public string Name { get; set; } = string.Empty;

    [Range(0, 130)]
    public int Age { get; set; }

    public string? City { get; set; }
}

public sealed class Invoice
{
    [Required]
    public string Number { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal Total { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];
}

public sealed class InvoiceLine
{
    [Required]
    [MinLength(2)]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }
}

public sealed class Node
{
    public string Name { get; set; } = string.Empty;

    public Node? Child { get; set; }
}

public enum Priority
{
    Low,
    Medium,
    High,
}

public sealed class Task_
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public Priority Priority { get; set; }
}

/// <summary>
/// A node whose two children can point at the same object, forming a diamond. Chaining these
/// builds a graph with an exponential number of distinct paths but only a linear number of
/// distinct nodes, which is what separates a correct visited-set from a broken one.
/// </summary>
public sealed class DiamondNode
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public DiamondNode? Left { get; set; }

    public DiamondNode? Right { get; set; }
}
