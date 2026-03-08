namespace SymCalc;

/// <summary>
/// A single node in a generic binary tree.
/// Used internally as the AST (Abstract Syntax Tree) for symbolic expressions.
/// </summary>
public class BinTree<T>
{
    public T          Value;
    public BinTree<T>? Left  = null;
    public BinTree<T>? Right = null;

    /// <summary>Creates a leaf node with the given value.</summary>
    public BinTree(T value)
    {
        Value = value;
    }

    /// <summary>Creates an internal node with the given value and children.</summary>
    public BinTree(T value, BinTree<T>? left, BinTree<T>? right)
    {
        Value = value;
        Left  = left;
        Right = right;
    }

    /// <summary>
    /// Returns <c>true</c> when two trees are structurally equal —
    /// same shape and equal values at every node.
    /// </summary>
    public static bool AreEqual(BinTree<T>? a, BinTree<T>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (!a.Value!.Equals(b.Value)) return false;
        return AreEqual(a.Left, b.Left) && AreEqual(a.Right, b.Right);
    }
}
