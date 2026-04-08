namespace ZibStack.NET.Query;

/// <summary>Base class for filter expression tree nodes.</summary>
public abstract class FilterExpression { }

/// <summary>A single filter condition (leaf node).</summary>
public sealed class FilterLeaf : FilterExpression
{
    public FilterClause Clause { get; }
    public FilterLeaf(FilterClause clause) => Clause = clause;
}

/// <summary>Logical AND of two expressions (comma-separated).</summary>
public sealed class FilterAnd : FilterExpression
{
    public FilterExpression Left { get; }
    public FilterExpression Right { get; }
    public FilterAnd(FilterExpression left, FilterExpression right)
    {
        Left = left;
        Right = right;
    }
}

/// <summary>Logical OR of two expressions (pipe-separated).</summary>
public sealed class FilterOr : FilterExpression
{
    public FilterExpression Left { get; }
    public FilterExpression Right { get; }
    public FilterOr(FilterExpression left, FilterExpression right)
    {
        Left = left;
        Right = right;
    }
}
