using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using System.Drawing;

namespace SymCalc;

/// <summary>
/// A symbolic mathematical expression backed by a binary AST.
///
/// Capabilities:
/// <list type="bullet">
///   <item>Parse a human-readable expression string (including implicit multiplication)</item>
///   <item>Symbolic differentiation with respect to <c>x</c></item>
///   <item>Multi-pass algebraic simplification</item>
///   <item>Conversion to canonical polynomial form</item>
///   <item>Numerical evaluation (interpreted or JIT-compiled)</item>
///   <item>Root finding (grid + bisection)</item>
///   <item>Lagrange / Chebyshev interpolation</item>
///   <item>Graph rendering to PNG (Windows only)</item>
/// </list>
///
/// Supported expression syntax:
/// <c>+  -  *  /  ^  sin  cos  tan  ctg  ln  log  log_b  sqrt  abs  exp  e</c>
/// Implicit multiplication is inserted automatically (e.g. <c>2x</c>, <c>3(x+1)</c>).
/// </summary>
public class Function
{
    private BinTree<string>? _root;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a mathematical expression string into an internal AST,
    /// inserting explicit <c>*</c> tokens wherever multiplication is implied.
    /// </summary>
    public Function(string expression)
    {
        expression = PreprocessImplicitMul(expression);
        _root = Parse(expression);
    }

    private Function(BinTree<string> root) => _root = root;

    // ── Serialisation ─────────────────────────────────────────────────────────

    public override string ToString() => Stringify(_root);

    private string Stringify(BinTree<string>? node)
    {
        if (node == null) return "";
        string op = node.Value;

        if (node.Left == null && node.Right == null) return op;

        if (op is "+" or "-" or "*" or "/" or "^")
        {
            string L = Stringify(node.Left);
            string R = Stringify(node.Right);

            bool Ladd = node.Left  is { Value: "+" or "-" };
            bool Lmul = node.Left  is { Value: "*" or "/" };
            bool Lpow = node.Left  is { Value: "^" };
            bool Radd = node.Right is { Value: "+" or "-" };
            bool Rmul = node.Right is { Value: "*" or "/" };
            bool Rpow = node.Right is { Value: "^" };

            return op switch
            {
                "+" => L + "+" + (Radd ? $"({R})" : R),
                "-" => L + "-" + (Radd || node.Right?.Value == "-" ? $"({R})" : R),
                "*" => (Ladd ? $"({L})" : L) + "*" + (Radd ? $"({R})" : R),
                "/" => (Ladd || Lmul ? $"({L})" : L) + "/" + (Radd || Rmul ? $"({R})" : R),
                "^" => (Ladd || Lmul || Lpow ? $"({L})" : L) + "^" + (Radd || Rmul || Rpow ? $"({R})" : R),
                _   => op
            };
        }

        // Unary function call: f(arg)
        if (node.Left != null && node.Right == null)
            return op + "(" + Stringify(node.Left) + ")";

        return op;
    }

    // ── Numeric helpers ───────────────────────────────────────────────────────

    private static string EvalBinaryOp(string op, string s1, string s2)
    {
        BigFloat a = BigFloat.Parse(s1), b = BigFloat.Parse(s2);
        BigFloat r = op switch
        {
            "+" => a + b, "-" => a - b, "*" => a * b, "/" => a / b,
            _ => throw new ArgumentException("Unknown operator: " + op)
        };
        if (BigFloat.Abs(r) < BigFloat.Parse("1e-50")) r = BigFloat.Zero;
        return Format(r);
    }

    private static bool IsNumber(string s) =>
        !string.IsNullOrWhiteSpace(s) && BigFloat.TryParse(s, out _);

    /// <summary>Formats a <see cref="double"/> as a plain decimal string.</summary>
    public static string Format(double val) => Format(BigFloat.FromDouble(val));

    /// <summary>Formats a <see cref="BigFloat"/> as a plain decimal string.</summary>
    public static string Format(BigFloat val) =>
        BigFloat.Abs(val) < BigFloat.Parse("1e-100") ? "0" : val.ToString();

    // ── Preprocessing ─────────────────────────────────────────────────────────

    private static bool IsInsideLogBaseName(string s, int index)
    {
        int prefix = s.LastIndexOf("log_", index, StringComparison.Ordinal);
        if (prefix < 0) return false;
        for (int k = prefix; k <= index; k++)
            if (s[k] == '(') return false;
        return s.IndexOf('(', index) > index;
    }

    private static bool IsFunctionCallPrefix(string tail)
    {
        foreach (var f in new[] { "sin", "cos", "tan", "ctg", "ln", "log", "sqrt", "abs", "exp", "e" })
            if (tail.StartsWith(f + "(")) return true;
        if (tail.StartsWith("log_") && tail.IndexOf('(') > 4) return true;
        return false;
    }

    private static string FixUnaryMinus(string s)
    {
        if (s.Length > 1 && s[0] == '-' && char.IsDigit(s[1]))
            s = "(0" + s + ")";

        for (int i = 1; i < s.Length - 1; i++)
        {
            if (s[i] == '-' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
            {
                char prev = s[i - 1];
                if (prev is '(' or '+' or '-' or '*' or '/' or '^')
                {
                    int j = i + 1;
                    while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.')) j++;
                    s = s[..i] + "(0" + s[i..j] + ")" + s[j..];
                    i = j;
                }
            }
        }
        return s;
    }

    private static string PreprocessImplicitMul(string s)
    {
        s = FixUnaryMinus(s);
        var result = new System.Text.StringBuilder();

        for (int i = 0; i < s.Length - 1; i++)
        {
            char c = s[i], d = s[i + 1];
            result.Append(c);

            string tail = s[i..];
            if (IsFunctionCallPrefix(tail) || IsInsideLogBaseName(s, i)) continue;
            if (char.IsDigit(c) && (char.IsDigit(d) || d == '.')) continue;
            if (c == '.' && char.IsDigit(d)) continue;
            if (char.IsLetter(c) && char.IsLetter(d)) continue;

            bool leftOk  = char.IsDigit(c) || c == 'x' || c == ')' || c == '.';
            bool rightOk = char.IsDigit(d) || d == 'x' || d == '(' || d == '.' || char.IsLetter(d);

            if (leftOk && rightOk && c is not '+' and not '-' and not '*' and not '/' and not '^')
                result.Append('*');
        }

        result.Append(s[^1]);
        return result.ToString();
    }

    // ── Parser ────────────────────────────────────────────────────────────────

    private BinTree<string>? Parse(string s)
    {
        s = s.Replace(" ", "");
        if (s == "") return null;
        if (s.StartsWith('-')) return Parse("0" + s);

        int p = FindMainOp(s, '+', '-');
        if (p >= 0) return new BinTree<string>(s[p].ToString(), Parse(s[..p]), Parse(s[(p + 1)..]));

        p = FindMainOp(s, '*', '/');
        if (p >= 0) return new BinTree<string>(s[p].ToString(), Parse(s[..p]), Parse(s[(p + 1)..]));

        p = FindMainOp(s, '^');
        if (p >= 0) return new BinTree<string>("^", Parse(s[..p]), Parse(s[(p + 1)..]));

        if (s[0] == '(' && MatchingParen(s, 0) == s.Length - 1)
            return Parse(s[1..^1]);

        // log_base(arg)  →  ln(arg)/ln(base)
        if (s.StartsWith("log_"))
        {
            int open = s.IndexOf('(');
            if (open > 4 && MatchingParen(s, open) == s.Length - 1)
            {
                string baseExpr = s[4..open];
                if (!string.IsNullOrWhiteSpace(baseExpr))
                {
                    var arg      = Parse(s[(open + 1)..^1]);
                    var baseNode = Parse(baseExpr);
                    return new BinTree<string>("/",
                        new BinTree<string>("ln", arg,      null),
                        new BinTree<string>("ln", baseNode, null));
                }
            }
        }

        foreach (var f in new[] { "sin", "cos", "tan", "ctg", "ln", "log", "sqrt", "abs", "exp" })
        {
            if (s.StartsWith(f) && s.Length > f.Length && s[f.Length] == '(')
            {
                int q = MatchingParen(s, f.Length);
                if (q == s.Length - 1)
                    return new BinTree<string>(f, Parse(s[(f.Length + 1)..^1]), null);
            }
        }

        if (s == ".") return new BinTree<string>("0");
        return new BinTree<string>(s);
    }

    private int FindMainOp(string s, params char[] ops)
    {
        int level = 0, best = -1;
        for (int i = 0; i < s.Length; i++)
        {
            if      (s[i] == '(') level++;
            else if (s[i] == ')') level--;
            else if (level == 0)
                foreach (var op in ops)
                    if (s[i] == op && !(op is '+' or '-' && i == 0))
                        best = i;
        }
        return best;
    }

    private int MatchingParen(string s, int i)
    {
        int level = 0;
        for (int k = i; k < s.Length; k++)
        {
            if      (s[k] == '(') level++;
            else if (s[k] == ')') { level--; if (level == 0) return k; }
        }
        return -1;
    }

    // ── Differentiation ───────────────────────────────────────────────────────

    /// <summary>Returns the first derivative with respect to <c>x</c>, simplified.</summary>
    public Function Differentiate() { var f = new Function(DifferentiateNode(_root)!); f.Simplify(); return f; }

    /// <summary>Returns the first derivative, optionally simplified.</summary>
    public Function Differentiate(bool simplify)
    {
        var f = new Function(DifferentiateNode(_root)!);
        if (simplify) f.Simplify();
        return f;
    }

    private BinTree<string>? DifferentiateNode(BinTree<string>? t)
    {
        if (t == null) return null;
        string op = t.Value;

        // Leaf
        if (t.Left == null && t.Right == null)
            return new BinTree<string>(op == "x" ? "1" : "0");

        // Sum / difference rule
        if (op is "+" or "-")
            return new BinTree<string>(op, DifferentiateNode(t.Left), DifferentiateNode(t.Right));

        // Product rule:  (uv)' = u'v + uv'
        if (op == "*")
            return new BinTree<string>("+",
                new BinTree<string>("*", DifferentiateNode(t.Left),  Clone(t.Right)),
                new BinTree<string>("*", Clone(t.Left), DifferentiateNode(t.Right)));

        // Quotient rule:  (u/v)' = (u'v − uv') / v²
        if (op == "/")
            return new BinTree<string>("/",
                new BinTree<string>("-",
                    new BinTree<string>("*", DifferentiateNode(t.Left),  Clone(t.Right)),
                    new BinTree<string>("*", Clone(t.Left), DifferentiateNode(t.Right))),
                new BinTree<string>("^", Clone(t.Right), new BinTree<string>("2")));

        if (op == "^")
        {
            // e^u  →  e^u · u'
            if (t.Left?.Value == "e")
                return new BinTree<string>("*", Clone(t), DifferentiateNode(t.Right));

            // u^n  →  n · u^(n−1) · u'
            if (t.Right != null && t.Right.Left == null && IsNumber(t.Right.Value))
            {
                BigFloat n = BigFloat.Parse(t.Right.Value);
                return new BinTree<string>("*",
                    new BinTree<string>(Format(n)),
                    new BinTree<string>("*",
                        new BinTree<string>("^", Clone(t.Left), new BinTree<string>(Format(n - BigFloat.One))),
                        DifferentiateNode(t.Left)));
            }
            return new BinTree<string>("0");
        }

        if (op == "sin")  return new BinTree<string>("*", new BinTree<string>("cos", Clone(t.Left), null), DifferentiateNode(t.Left));
        if (op == "cos")  return new BinTree<string>("*",
            new BinTree<string>("-", new BinTree<string>("0"), new BinTree<string>("sin", Clone(t.Left), null)),
            DifferentiateNode(t.Left));
        if (op == "ln")   return new BinTree<string>("/", DifferentiateNode(t.Left), Clone(t.Left));
        if (op == "log")  {
            var ln10 = new BinTree<string>(Format(BigFloat.FromDouble(Math.Log(10.0))));
            return new BinTree<string>("/", DifferentiateNode(t.Left),
                new BinTree<string>("*", Clone(t.Left), ln10));
        }
        if (op == "exp")  return new BinTree<string>("*", new BinTree<string>("exp", Clone(t.Left), null), DifferentiateNode(t.Left));
        if (op == "sqrt") return new BinTree<string>("/", DifferentiateNode(t.Left),
            new BinTree<string>("*", new BinTree<string>("2"), new BinTree<string>("sqrt", Clone(t.Left), null)));
        if (op == "abs")  return new BinTree<string>("*",
            new BinTree<string>("/", Clone(t.Left), new BinTree<string>("abs", Clone(t.Left), null)),
            DifferentiateNode(t.Left));

        return new BinTree<string>("0");
    }

    // ── AST clone ─────────────────────────────────────────────────────────────

    private static BinTree<string>? Clone(BinTree<string>? t) =>
        t == null ? null : new BinTree<string>(t.Value, Clone(t.Left), Clone(t.Right));

    // ── Simplification pipeline ───────────────────────────────────────────────

    /// <summary>
    /// Repeatedly applies all algebraic simplification rules until the
    /// expression stabilises.
    /// </summary>
    public void Simplify()
    {
        if (_root == null) return;
        bool changed = false;

        _root = Normalise(_root, ref changed)!;
        _root = ExpandAll(_root, ref changed);

        do
        {
            changed = false;
            _root = FoldConstants   (_root, ref changed);
            _root = SimplifyTrivial (_root, ref changed);
            _root = CombinePowers   (_root, ref changed);
            _root = CombineFractions(_root, ref changed);
            _root = CanonicalForm   (_root, ref changed);
            _root = CombineLikeTerms(_root, ref changed);
            _root = CanonicalForm   (_root, ref changed);
            _root = FactorTree      (_root, ref changed);
            _root = FoldConstants   (_root, ref changed);
            _root = SimplifyTrivial (_root, ref changed);
        } while (changed);

        _root = Beautify(_root);
    }

    // ─── Normalise: rewrite − and ÷ into + and ^ so other passes see a uniform form ───

    private static BinTree<string>? Normalise(BinTree<string>? t, ref bool changed)
    {
        if (t == null) return null;
        t.Left  = Normalise(t.Left,  ref changed);
        t.Right = Normalise(t.Right, ref changed);

        if (t.Value == "-")
        {
            changed = true;
            return new BinTree<string>("+", t.Left,
                new BinTree<string>("*", new BinTree<string>("-1"), t.Right));
        }
        if (t.Value == "/")
        {
            changed = true;
            return new BinTree<string>("*", t.Left,
                new BinTree<string>("^", t.Right, new BinTree<string>("-1")));
        }
        return t;
    }

    // ─── Flatten helpers ──────────────────────────────────────────────────────

    private static List<BinTree<string>> FlattenSum(BinTree<string>? t)
    {
        var r = new List<BinTree<string>>();
        if (t == null) return r;
        if (t.Value == "+") { r.AddRange(FlattenSum(t.Left)); r.AddRange(FlattenSum(t.Right)); }
        else r.Add(Clone(t)!);
        return r;
    }

    private static List<BinTree<string>> FlattenProduct(BinTree<string>? t)
    {
        var r = new List<BinTree<string>>();
        if (t == null) return r;
        if (t.Value == "*") { r.AddRange(FlattenProduct(t.Left)); r.AddRange(FlattenProduct(t.Right)); }
        else r.Add(Clone(t)!);
        return r;
    }

    private static List<List<BinTree<string>>> FlattenExpression(BinTree<string> t)
    {
        var result = new List<List<BinTree<string>>>();
        foreach (var term in FlattenSum(t)) result.Add(FlattenProduct(term));
        return result;
    }

    // ─── Factoring ────────────────────────────────────────────────────────────

    private static (BinTree<string>? factor, List<int> indices)
        FindCommonFactor(List<List<BinTree<string>>> terms)
    {
        BinTree<string>? best = null;
        var bestIdx = new List<int>();

        for (int t = 0; t < terms.Count; t++)
            foreach (var candidate in terms[t])
            {
                var found = new List<int>();
                for (int i = 0; i < terms.Count; i++)
                    foreach (var f in terms[i])
                        if (BinTree<string>.AreEqual(candidate, f)) { found.Add(i); break; }
                if (found.Count >= 2 && found.Count > bestIdx.Count) { bestIdx = found; best = Clone(candidate); }
            }

        return (best, bestIdx);
    }

    private static BinTree<string> RemoveOneFactor(List<BinTree<string>> term, BinTree<string> factor)
    {
        var rem = new List<BinTree<string>>();
        bool done = false;
        foreach (var f in term)
        {
            if (!done && BinTree<string>.AreEqual(f, factor)) { done = true; continue; }
            rem.Add(Clone(f)!);
        }
        if (rem.Count == 0) return new BinTree<string>("1");
        if (rem.Count == 1) return rem[0];
        var prod = rem[0];
        for (int i = 1; i < rem.Count; i++) prod = new BinTree<string>("*", prod, rem[i]);
        return prod;
    }

    private static BinTree<string> BuildSum(List<BinTree<string>> list)
    {
        if (list.Count == 0) return new BinTree<string>("0");
        var s = list[0];
        for (int i = 1; i < list.Count; i++) s = new BinTree<string>("+", s, list[i]);
        return s;
    }

    private static BinTree<string> FactorOnce(BinTree<string> t, ref bool changed)
    {
        var terms = FlattenExpression(t);
        var (factor, indices) = FindCommonFactor(terms);
        if (factor == null || indices.Count < 2) return t;

        changed = true;
        var residues = new List<BinTree<string>>();
        var leftover = new List<BinTree<string>>();

        for (int i = 0; i < terms.Count; i++)
        {
            if (indices.Contains(i)) residues.Add(RemoveOneFactor(terms[i], factor));
            else
            {
                var prod = terms[i][0];
                for (int j = 1; j < terms[i].Count; j++) prod = new BinTree<string>("*", prod, terms[i][j]);
                leftover.Add(prod);
            }
        }

        var factored = new BinTree<string>("*", factor, BuildSum(residues));
        return leftover.Count == 0 ? factored : new BinTree<string>("+", factored, BuildSum(leftover));
    }

    private static BinTree<string>? FactorTree(BinTree<string>? t, ref bool changed)
    {
        if (t == null) return null;
        if (t.Left  != null) t.Left  = FactorTree(t.Left,  ref changed);
        if (t.Right != null) t.Right = FactorTree(t.Right, ref changed);
        return FactorOnce(t, ref changed);
    }

    // ─── Power merging ────────────────────────────────────────────────────────

    private static BinTree<string>? CombinePowers(BinTree<string>? t, ref bool changed)
    {
        if (t == null) return t;
        if (t.Left  != null) t.Left  = CombinePowers(t.Left,  ref changed);
        if (t.Right != null) t.Right = CombinePowers(t.Right, ref changed);

        if (t.Value == "*")
        {
            var L = t.Left!; var R = t.Right!;

            BinTree<string>? TryMerge(BinTree<string> a, BinTree<string> b)
            {
                if (a.Value == "x" && b.Value == "x")
                    return new BinTree<string>("^", new BinTree<string>("x"), new BinTree<string>("2"));
                if (a.Value == "x" && b.Value == "^" && b.Left?.Value == "x" && IsNumber(b.Right!.Value))
                    return new BinTree<string>("^", new BinTree<string>("x"), new BinTree<string>(Format(BigFloat.Parse(b.Right.Value) + BigFloat.One)));
                if (a.Value == "^" && a.Left?.Value == "x" && IsNumber(a.Right!.Value) && b.Value == "x")
                    return new BinTree<string>("^", new BinTree<string>("x"), new BinTree<string>(Format(BigFloat.Parse(a.Right.Value) + BigFloat.One)));
                if (a.Value == "^" && a.Left?.Value == "x" && IsNumber(a.Right!.Value) &&
                    b.Value == "^" && b.Left?.Value == "x" && IsNumber(b.Right!.Value))
                    return new BinTree<string>("^", new BinTree<string>("x"), new BinTree<string>(Format(BigFloat.Parse(a.Right.Value) + BigFloat.Parse(b.Right.Value))));
                return null;
            }

            var merged = TryMerge(L, R);
            if (merged != null) { changed = true; return merged; }
        }

        if (t.Value == "^")
        {
            if (t.Right?.Value == "1") { changed = true; return t.Left!; }
            if (t.Right?.Value == "0") { changed = true; return new BinTree<string>("1"); }
            if (t.Left?.Value == "^" && IsNumber(t.Left.Right!.Value) && IsNumber(t.Right!.Value))
            {
                BigFloat n = BigFloat.Parse(t.Left.Right.Value), m = BigFloat.Parse(t.Right.Value);
                changed = true;
                return new BinTree<string>("^", Clone(t.Left.Left)!, new BinTree<string>(Format(n * m)));
            }
        }

        return t;
    }

    // ─── Fraction simplification ──────────────────────────────────────────────

    private static BinTree<string>? CombineFractions(BinTree<string>? t, ref bool changed)
    {
        if (t == null) return t;
        if (t.Left  != null) t.Left  = CombineFractions(t.Left,  ref changed);
        if (t.Right != null) t.Right = CombineFractions(t.Right, ref changed);

        if (t.Value == "/")
        {
            var A = t.Left; var B = t.Right;

            // (a/b)/c  →  a/(b*c)
            if (A?.Value == "/")
            { changed = true; return new BinTree<string>("/", Clone(A.Left)!, new BinTree<string>("*", Clone(A.Right)!, Clone(B)!)); }

            // a/(b^-1)  →  a*b
            if (B?.Value == "^" && B.Right?.Value == "-1")
            { changed = true; return new BinTree<string>("*", Clone(A)!, Clone(B.Left)!); }

            // 1/(1/b)  →  b
            if (A?.Value == "1" && B?.Value == "/" && B.Left?.Value == "1")
            { changed = true; return Clone(B.Right)!; }

            // a/((b^n)^m)  →  a/b^(n*m)
            if (B?.Value == "^" && B.Left?.Value == "^" && IsNumber(B.Left.Right!.Value) && IsNumber(B.Right!.Value))
            {
                changed = true;
                return new BinTree<string>("/", Clone(A)!,
                    new BinTree<string>("^", Clone(B.Left.Left)!,
                        new BinTree<string>(Format(BigFloat.Parse(B.Left.Right.Value) * BigFloat.Parse(B.Right.Value)))));
            }
        }

        if (t.Value == "*")
        {
            var A = t.Left; var B = t.Right;
            // (a/b)*(c/d)  →  (a*c)/(b*d)
            if (A?.Value == "/" && B?.Value == "/")
            {
                changed = true;
                return new BinTree<string>("/",
                    new BinTree<string>("*", Clone(A.Left)!, Clone(B.Left)!),
                    new BinTree<string>("*", Clone(A.Right)!, Clone(B.Right)!));
            }
        }

        return t;
    }

    // ─── Canonical ordering ───────────────────────────────────────────────────

    private static string AstKey(BinTree<string>? t) =>
        t == null ? "" :
        t.Left == null && t.Right == null ? t.Value :
        $"{t.Value}({AstKey(t.Left)},{AstKey(t.Right)})";

    private static BinTree<string>? CanonicalForm(BinTree<string>? t, ref bool changed)
    {
        if (t == null) return t;
        if (t.Left  != null) t.Left  = CanonicalForm(t.Left,  ref changed);
        if (t.Right != null) t.Right = CanonicalForm(t.Right, ref changed);

        if (t.Value is "*" or "+")
        {
            var list   = t.Value == "*" ? FlattenProduct(t) : FlattenSum(t);
            var before = list.ConvertAll(AstKey);
            list.Sort((a, b) => AstKey(a).CompareTo(AstKey(b)));

            bool same = true;
            for (int i = 0; i < list.Count; i++) if (before[i] != AstKey(list[i])) { same = false; break; }
            if (same) return t;

            var tree = list[0];
            for (int i = 1; i < list.Count; i++) tree = new BinTree<string>(t.Value, tree, list[i]);
            changed = true;
            return tree;
        }
        return t;
    }

    // ─── Like-term collection ─────────────────────────────────────────────────

    private static BinTree<string>? CombineLikeTerms(BinTree<string>? t, ref bool changed)
    {
        if (t == null) return t;
        if (t.Left  != null) t.Left  = CombineLikeTerms(t.Left,  ref changed);
        if (t.Right != null) t.Right = CombineLikeTerms(t.Right, ref changed);

        if (t.Value != "+") return t;

        var map = new Dictionary<string, (BigFloat coef, BinTree<string> core)>();

        foreach (var term in FlattenSum(t))
        {
            BigFloat coef = BigFloat.One;
            var core = term;

            if (core.Value == "*")
            {
                var factors = FlattenProduct(core);
                if (factors.Count > 0 && IsNumber(factors[0].Value))
                {
                    coef = BigFloat.Parse(factors[0].Value);
                    factors.RemoveAt(0);
                    if (factors.Count == 0) core = new BinTree<string>("1");
                    else { core = factors[0]; for (int i = 1; i < factors.Count; i++) core = new BinTree<string>("*", core, factors[i]); }
                }
            }

            string key = AstKey(core);
            if (map.ContainsKey(key)) { map[key] = (map[key].coef + coef, core); changed = true; }
            else map[key] = (coef, core);
        }

        var combined = new List<BinTree<string>>();
        foreach (var kv in map)
        {
            BigFloat c = kv.Value.coef;
            if (BigFloat.Abs(c) < BigFloat.Parse("1e-14")) c = BigFloat.Zero;
            var core = kv.Value.core;
            combined.Add(BigFloat.Abs(c - BigFloat.One) < BigFloat.Parse("1e-14")
                ? core
                : new BinTree<string>("*", new BinTree<string>(Format(c)), core));
        }

        if (combined.Count == 0) return new BinTree<string>("0");
        return BuildSum(combined);
    }

    // ─── Expansion ────────────────────────────────────────────────────────────

    private static List<BinTree<string>> FlattenSumNode(BinTree<string> t)
    {
        var r = new List<BinTree<string>>();
        if (t.Value == "+") { r.AddRange(FlattenSumNode(t.Left!)); r.AddRange(FlattenSumNode(t.Right!)); }
        else r.Add(t);
        return r;
    }

    private static BinTree<string> BuildBalancedSum(List<BinTree<string>> terms, int l, int r)
    {
        int len = r - l;
        if (len == 1) return Clone(terms[l])!;
        int mid = l + len / 2;
        return new BinTree<string>("+", BuildBalancedSum(terms, l, mid), BuildBalancedSum(terms, mid, r));
    }

    private static bool ContainsSum(BinTree<string>? t) =>
        t != null && (t.Value == "+" || ContainsSum(t.Left) || ContainsSum(t.Right));

    private static BinTree<string> ExpandAll(BinTree<string> t, ref bool changed)
    {
        if (t.Left  != null) t.Left  = ExpandAll(t.Left,  ref changed);
        if (t.Right != null) t.Right = ExpandAll(t.Right, ref changed);

        if (t.Value == "+")
        {
            var list = FlattenSumNode(t);
            var res  = BuildBalancedSum(list, 0, list.Count);
            if (!ReferenceEquals(res, t)) changed = true;
            return res;
        }

        if (t.Value == "*" && (ContainsSum(t.Left) || ContainsSum(t.Right)))
        {
            var listA = FlattenSumNode(t.Left!);
            var listB = FlattenSumNode(t.Right!);
            var terms = new List<BinTree<string>>();
            foreach (var a in listA) foreach (var b in listB)
                terms.Add(new BinTree<string>("*", Clone(a), Clone(b)));
            changed = true;
            return BuildBalancedSum(terms, 0, terms.Count);
        }

        return t;
    }

    // ─── Constant folding and trivial identities ──────────────────────────────

    private static BinTree<string>? FoldConstants(BinTree<string>? t, ref bool changed)
    {
        if (t == null) return null;
        if (t.Left  != null) t.Left  = FoldConstants(t.Left,  ref changed);
        if (t.Right != null) t.Right = FoldConstants(t.Right, ref changed);

        if (t.Left != null && t.Right != null
            && IsNumber(t.Left.Value) && IsNumber(t.Right.Value)
            && t.Value is "+" or "-" or "*" or "/")
        { changed = true; return new BinTree<string>(EvalBinaryOp(t.Value, t.Left.Value, t.Right.Value)); }

        return t;
    }

    private static BinTree<string>? SimplifyTrivial(BinTree<string>? t, ref bool changed)
    {
        if (t == null) return null;
        if (t.Left  != null) t.Left  = SimplifyTrivial(t.Left,  ref changed);
        if (t.Right != null) t.Right = SimplifyTrivial(t.Right, ref changed);

        if (t.Value == "+") {
            if (t.Left?.Value  == "0") { changed = true; return t.Right!; }
            if (t.Right?.Value == "0") { changed = true; return t.Left!; }
        }
        if (t.Value == "-" && t.Right?.Value == "0") { changed = true; return t.Left!; }
        if (t.Value == "*") {
            if (t.Left?.Value  == "0" || t.Right?.Value == "0") { changed = true; return new BinTree<string>("0"); }
            if (t.Left?.Value  == "1") { changed = true; return t.Right!; }
            if (t.Right?.Value == "1") { changed = true; return t.Left!; }
            // x^a * x^b  →  x^(a+b)
            if (t.Left?.Value == "^" && t.Right?.Value == "^"
                && BinTree<string>.AreEqual(t.Left.Left, t.Right.Left)
                && IsNumber(t.Left.Right!.Value) && IsNumber(t.Right.Right!.Value))
            {
                changed = true;
                return new BinTree<string>("^", Clone(t.Left.Left)!,
                    new BinTree<string>(Format(BigFloat.Parse(t.Left.Right.Value) + BigFloat.Parse(t.Right.Right.Value))));
            }
        }
        if (t.Value == "^") {
            if (t.Right?.Value == "1") { changed = true; return t.Left!; }
            if (t.Right?.Value == "0") { changed = true; return new BinTree<string>("1"); }
            if (t.Left?.Value  == "0") { changed = true; return new BinTree<string>("0"); }
            if (t.Left?.Value  == "1") { changed = true; return new BinTree<string>("1"); }
        }
        return t;
    }

    // ─── Beautify: restore − and ÷ for human-readable output ─────────────────

    private static BinTree<string>? Beautify(BinTree<string>? t)
    {
        if (t == null) return null;
        if (t.Left  != null) t.Left  = Beautify(t.Left);
        if (t.Right != null) t.Right = Beautify(t.Right);

        if (t.Value == "^"  && t.Right?.Value == "-1")
            return new BinTree<string>("/", new BinTree<string>("1"), Clone(t.Left)!);
        if (t.Value == "*"  && t.Right?.Value == "^" && t.Right.Right?.Value == "-1")
            return new BinTree<string>("/", Clone(t.Left)!, Clone(t.Right.Left)!);
        if (t.Value == "*"  && t.Left?.Value  == "^" && t.Left.Right?.Value  == "-1")
            return new BinTree<string>("/", Clone(t.Right)!, Clone(t.Left.Left)!);
        if (t.Value == "*"  && t.Right?.Value == "/" && t.Right.Left?.Value  == "1")
            return new BinTree<string>("/", Clone(t.Left)!, Clone(t.Right.Right)!);
        if (t.Value == "*"  && t.Left?.Value  == "/" && t.Left.Left?.Value   == "1")
            return new BinTree<string>("/", Clone(t.Right)!, Clone(t.Left.Right)!);
        if (t.Value == "+"  && t.Right?.Value == "-" && t.Right.Right == null)
            return new BinTree<string>("-", Clone(t.Left)!, Clone(t.Right.Left)!);
        if (t.Value == "+"  && t.Right != null && IsNumber(t.Right.Value) && t.Right.Value.StartsWith("-"))
            return new BinTree<string>("-", Clone(t.Left)!, new BinTree<string>(t.Right.Value[1..]));
        if (t.Value == "/"  && t.Right?.Value == "1") return Clone(t.Left)!;
        if (t.Value == "+"  && t.Right?.Value == "0") return Clone(t.Left)!;
        if (t.Value == "+"  && t.Left?.Value  == "0") return Clone(t.Right)!;

        return t;
    }

    // ── Arithmetic operators ──────────────────────────────────────────────────

    public static Function operator +(Function a, Function b)
    { var f = new Function("0"); f._root = new BinTree<string>("+", a._root, b._root); return f; }

    public static Function operator -(Function a, Function b)
    { var f = new Function("0"); f._root = new BinTree<string>("-", a._root, b._root); return f; }

    public static Function operator *(Function a, Function b)
    { var f = new Function("0"); f._root = new BinTree<string>("*", a._root, b._root); return f; }

    public static Function operator *(Function a, double   scalar) => a * BigFloat.FromDouble(scalar);
    public static Function operator *(Function a, BigFloat scalar)
    { var f = new Function("0"); f._root = new BinTree<string>("*", a._root, new BinTree<string>(Format(scalar))); return f; }

    // ── Polynomial conversion ─────────────────────────────────────────────────

    /// <summary>
    /// Converts the AST of a polynomial expression to a coefficient array where
    /// index <c>k</c> is the coefficient of <c>x^k</c>.
    /// Throws if the expression contains non-polynomial operations.
    /// </summary>
    public static BigFloat[] AstToCoefficients(BinTree<string> t)
    {
        if (t.Left == null && t.Right == null)
        {
            if (t.Value == "x") return new[] { BigFloat.Zero, BigFloat.One };
            if (BigFloat.TryParse(t.Value, out var val)) return new[] { val };
            throw new Exception("Not a polynomial. Found: " + t.Value);
        }
        if (t.Value == "+") return PolyAdd(AstToCoefficients(t.Left!), AstToCoefficients(t.Right!));
        if (t.Value == "-")
        {
            var neg = PolyMul(new[] { new BigFloat(-1, 0) }, AstToCoefficients(t.Right!));
            return PolyAdd(AstToCoefficients(t.Left!), neg);
        }
        if (t.Value == "*") return PolyMul(AstToCoefficients(t.Left!), AstToCoefficients(t.Right!));
        if (t.Value == "^" && t.Left?.Value == "x"
            && t.Right != null && t.Right.Left == null && BigFloat.TryParse(t.Right.Value, out var eBF))
        {
            double e = eBF.ToDouble();
            if (e >= 0 && Math.Abs(e - Math.Round(e)) < 1e-12)
            {
                int n = (int)Math.Round(e);
                var c = new BigFloat[n + 1]; c[n] = BigFloat.One; return c;
            }
        }
        throw new Exception("Cannot convert to polynomial: " + t.Value);
    }

    private static BigFloat[] PolyAdd(BigFloat[] A, BigFloat[] B)
    {
        int n = Math.Max(A.Length, B.Length);
        var R = new BigFloat[n];
        for (int i = 0; i < n; i++)
            R[i] = (i < A.Length ? A[i] : BigFloat.Zero) + (i < B.Length ? B[i] : BigFloat.Zero);
        return R;
    }

    private static BigFloat[] PolyMul(BigFloat[] A, BigFloat[] B)
    {
        var R = new BigFloat[A.Length + B.Length - 1];
        for (int i = 0; i < A.Length; i++)
            for (int j = 0; j < B.Length; j++)
                R[i + j] += A[i] * B[j];
        return R;
    }

    /// <summary>Converts a coefficient array back into a symbolic <see cref="Function"/>.</summary>
    public static Function CoefficientsToFunction(BigFloat[] coeffs)
    {
        var sb = new System.Text.StringBuilder();
        bool first = true;

        for (int k = 0; k < coeffs.Length; k++)
        {
            BigFloat c = coeffs[k];
            if (BigFloat.Abs(c) < BigFloat.Parse("1e-15")) continue;

            if (first) { if (c < BigFloat.Zero) sb.Append('-'); }
            else sb.Append(c >= BigFloat.Zero ? "+" : "-");

            if (c < BigFloat.Zero) c = -c;
            bool hasX  = k > 0;
            bool isOne = BigFloat.Abs(c - BigFloat.One) < BigFloat.Parse("1e-12");

            if (!isOne || !hasX) { sb.Append('(' + Format(c) + ')'); if (hasX) sb.Append('*'); }
            if (hasX) { sb.Append('x'); if (k > 1) { sb.Append('^'); sb.Append(k); } }
            first = false;
        }

        return first ? new Function("0") : new Function(sb.ToString());
    }

    /// <summary>Converts this function to its canonical polynomial form.</summary>
    public Function ToPolynomial()
    {
        var f = CoefficientsToFunction(AstToCoefficients(_root!));
        bool changed;
        do { changed = false; f._root = SimplifyTrivial(f._root!, ref changed); } while (changed);
        return f;
    }

    // ── Compilation and evaluation ────────────────────────────────────────────

    public Func<double, double>? Compiled;

    /// <summary>JIT-compiles the AST into a native delegate for fast repeated evaluation.</summary>
    public void Compile() => Compiled = CompileNode(_root ?? throw new Exception("Empty expression tree."));

    private Func<double, double> CompileNode(BinTree<string> t)
    {
        if (t.Left == null && t.Right == null)
        {
            if (t.Value == "x") return x => x;
            if (t.Value == "e") return x => Math.E;
            if (double.TryParse(t.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                return x => v;
            throw new Exception("Unknown leaf: " + t.Value);
        }

        string op = t.Value;
        if (op is "sin" or "cos" or "tan" or "ctg" or "ln" or "log" or "exp" or "sqrt" or "abs")
        {
            var A = CompileNode(t.Left!);
            return op switch
            {
                "sin"  => x => Math.Sin(A(x)),  "cos"  => x => Math.Cos(A(x)),
                "tan"  => x => Math.Tan(A(x)),  "ctg"  => x => 1.0 / Math.Tan(A(x)),
                "ln"   => x => Math.Log(A(x)),  "log"  => x => Math.Log10(A(x)),
                "exp"  => x => Math.Exp(A(x)),  "sqrt" => x => Math.Sqrt(A(x)),
                "abs"  => x => Math.Abs(A(x)),  _      => throw new Exception("Unknown unary: " + op)
            };
        }

        if ((op == "+" || op == "-") && (t.Left == null || t.Right == null))
        {
            var c = CompileNode(t.Left ?? t.Right!);
            return op == "-" ? x => -c(x) : c;
        }

        var L = CompileNode(t.Left!);
        var R = CompileNode(t.Right!);
        return op switch
        {
            "+" => x => L(x) + R(x), "-" => x => L(x) - R(x),
            "*" => x => L(x) * R(x), "/" => x => L(x) / R(x),
            "^" => x => Math.Pow(L(x), R(x)),
            _   => throw new Exception("Unknown operator: " + op)
        };
    }

    /// <summary>
    /// Evaluates the function at <paramref name="x"/>,
    /// using the compiled delegate when available.
    /// </summary>
    public double Evaluate(double x) => Compiled != null ? Compiled(x) : EvalNode(_root, x);

    private double EvalNode(BinTree<string>? t, double x)
    {
        if (t == null) return 0.0;
        string s = t.Value;
        if (IsNumber(s)) return double.Parse(s, CultureInfo.InvariantCulture);
        if (s == "x")    return x;
        if (s == "e")    return Math.E;
        if (s == "+")    return EvalNode(t.Left!, x) + EvalNode(t.Right!, x);
        if (s == "-")    return EvalNode(t.Left!, x) - EvalNode(t.Right!, x);
        if (s == "*")    return EvalNode(t.Left!, x) * EvalNode(t.Right!, x);
        if (s == "/")    return EvalNode(t.Left!, x) / EvalNode(t.Right!, x);
        if (s == "^")    return Math.Pow(EvalNode(t.Left!, x), EvalNode(t.Right!, x));
        if (s == "sin")  return Math.Sin (EvalNode(t.Left!, x));
        if (s == "cos")  return Math.Cos (EvalNode(t.Left!, x));
        if (s == "tan")  return Math.Tan (EvalNode(t.Left!, x));
        if (s == "ctg")  return 1.0 / Math.Tan(EvalNode(t.Left!, x));
        if (s == "ln")   return Math.Log (EvalNode(t.Left!, x));
        if (s == "log")  return Math.Log10(EvalNode(t.Left!, x));
        if (s == "exp")  return Math.Exp (EvalNode(t.Left!, x));
        if (s == "sqrt") return Math.Sqrt(EvalNode(t.Left!, x));
        if (s == "abs")  return Math.Abs (EvalNode(t.Left!, x));
        return 0.0;
    }

    // ── Root finding ──────────────────────────────────────────────────────────

    /// <summary>
    /// Finds all roots on [<paramref name="a"/>, <paramref name="b"/>]
    /// using coarse grid sampling followed by bisection.
    /// </summary>
    public List<double> Roots(double a, double b, double eps = 1e-10, int samples = 4000)
    {
        var roots = new List<double>();
        double prevX = a, prevY = Evaluate(prevX);
        if (Math.Abs(prevY) < eps) AddRootSafe(roots, prevX, eps);

        for (int i = 1; i <= samples; i++)
        {
            double x = a + (b - a) * i / samples, y = Evaluate(x);
            if (Math.Abs(y) < eps) AddRootSafe(roots, x, eps);
            if (prevY * y < 0) AddRootSafe(roots, Bisect(prevX, x, eps), eps);
            prevX = x; prevY = y;
        }

        roots.Sort();
        return roots;
    }

    private double Bisect(double a, double b, double eps)
    {
        double fa = Evaluate(a);
        for (int i = 0; i < 120; i++)
        {
            double m = 0.5 * (a + b), fm = Evaluate(m);
            if (Math.Abs(fm) < eps || Math.Abs(b - a) < eps) return m;
            if (fa * fm <= 0) b = m; else { a = m; fa = fm; }
        }
        return 0.5 * (a + b);
    }

    private void AddRootSafe(List<double> list, double r, double eps)
    {
        foreach (double x in list) if (Math.Abs(x - r) < eps * 5) return;
        list.Add(r);
    }

    // ── Extrema ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds all local extrema of <paramref name="f"/> on [<paramref name="a"/>, <paramref name="b"/>]
    /// using the second derivative test.
    /// </summary>
    public static List<(double x, string type)> AllExtrema(Function f, double a, double b, double eps = 1e-9)
    {
        var f2     = f.Differentiate(false).Differentiate(false);
        var result = new List<(double x, string type)>();
        foreach (double x in f.Differentiate(false).Roots(a, b))
        {
            double f2x = f2.Evaluate(x);
            result.Add((x, f2x > eps ? "minimum" : f2x < -eps ? "maximum" : "saddle/flat"));
        }
        return result;
    }

    private double GridExtreme(double a, double b, bool max, int samples = 50000)
    {
        if (Compiled == null) Compile();
        double best = max ? double.NegativeInfinity : double.PositiveInfinity;
        for (int i = 0; i <= samples; i++)
        {
            double y = Evaluate(a + (b - a) * i / samples);
            if (max ? y > best : y < best) best = y;
        }
        return best;
    }

    /// <summary>Returns the maximum value on [a, b] via grid sampling.</summary>
    public double Max(double a, double b, int samples = 60000) => GridExtreme(a, b, true,  samples);

    /// <summary>Returns the minimum value on [a, b] via grid sampling.</summary>
    public double Min(double a, double b, int samples = 60000) => GridExtreme(a, b, false, samples);

    // ── Interpolation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Estimates a suitable number of Chebyshev nodes for a good approximation
    /// on the given interval.
    /// </summary>
    public static int OptimalChebNodeCount(double a, double b, int derivOrder = 20)
        => Math.Clamp((int)Math.Ceiling(5 * derivOrder + Math.Log(Math.Abs(b - a), 2)), 20, 1000);

    private static BigFloat[] PolyLinearFactor(BigFloat c) => new[] { -c, BigFloat.One };

    private static BigFloat[] PolyAdd2(BigFloat[] A, BigFloat[] B)
    {
        int n = Math.Max(A.Length, B.Length);
        var R = new BigFloat[n];
        for (int i = 0; i < n; i++)
            R[i] = (i < A.Length ? A[i] : BigFloat.Zero) + (i < B.Length ? B[i] : BigFloat.Zero);
        return R;
    }

    private static BigFloat[] PolyMul2(BigFloat[] A, BigFloat[] B)
    {
        var R = new BigFloat[A.Length + B.Length - 1];
        for (int i = 0; i < A.Length; i++)
            for (int j = 0; j < B.Length; j++)
                R[i + j] += A[i] * B[j];
        return R;
    }

    private static Function BuildInterpolant(int n, double a, double b, bool chebyshev, Function f)
    {
        var xs = new double[n]; var ys = new BigFloat[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = chebyshev
                ? (a + b) / 2.0 + (b - a) / 2.0 * Math.Cos((2 * i + 1) * Math.PI / (2 * n))
                : a + (b - a) * i / (n - 1);
            ys[i] = BigFloat.FromDouble(f.Evaluate(xs[i]));
        }

        BigFloat[] poly = new[] { BigFloat.Zero };
        for (int i = 0; i < n; i++)
        {
            BigFloat[] Li = new[] { BigFloat.One }; BigFloat denom = BigFloat.One;
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                double diff = xs[i] - xs[j];
                if (Math.Abs(diff) < 1e-14) continue;
                Li    = PolyMul2(Li, PolyLinearFactor(BigFloat.FromDouble(xs[j])));
                denom *= BigFloat.FromDouble(diff);
            }
            double dd = denom.ToDouble();
            if (dd == 0.0 || double.IsInfinity(dd) || double.IsNaN(dd)) continue;
            BigFloat scalar = ys[i] / denom;
            for (int k = 0; k < Li.Length; k++) Li[k] *= scalar;
            poly = PolyAdd2(poly, Li);
        }
        return CoefficientsToFunction(poly);
    }

    /// <summary>Detects the sub-interval of [−radius, radius] where the function is non-negligible.</summary>
    public static (double a, double b) DetectActiveInterval(Function f, double radius = 20, double eps = 1e-6)
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (double x = -radius; x <= radius; x += 0.25)
        {
            double y = Math.Abs(f.Evaluate(x));
            if (double.IsNaN(y) || double.IsInfinity(y)) continue;
            if (y > eps) { if (x < lo) lo = x; if (x > hi) hi = x; }
        }
        if (double.IsInfinity(lo)) return (-5, 5);
        double margin = (hi - lo) * 0.1 + 1;
        return (lo - margin, hi + margin);
    }

    /// <summary>Returns a Chebyshev interpolant on the automatically detected active interval.</summary>
    public Function Approximate()
    {
        var (a, b) = DetectActiveInterval(this);
        return BuildInterpolant(OptimalChebNodeCount(a, b), a, b, true, this);
    }

    /// <summary>Returns a Lagrange (uniform-node) interpolant with <paramref name="n"/> nodes on [a, b].</summary>
    public Function LagrangeInterpolate(int n, double a, double b)   => BuildInterpolant(n, a, b, false, this);

    /// <summary>Returns a Chebyshev interpolant with <paramref name="n"/> nodes on [a, b].</summary>
    public Function ChebyshevInterpolate(int n, double a, double b)  => BuildInterpolant(n, a, b, true,  this);

    // ── Graph rendering (Windows only) ────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    /// <summary>
    /// Renders the function to a PNG file using GDI+.
    /// Only available on Windows due to System.Drawing.Common limitations.
    /// </summary>
    public void Graph(double a, double b, string filename, int width = 800, int height = 600)
    {
        Compile();
        using var bmp = new Bitmap(width, height);
        using var g   = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.White);

        int margin = 50, N = width * 2;
        var xs = new double[N]; var ys = new double[N];
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;

        for (int i = 0; i < N; i++)
        {
            xs[i] = a + (b - a) * i / (N - 1); ys[i] = Evaluate(xs[i]);
            if (ys[i] < minY) minY = ys[i]; if (ys[i] > maxY) maxY = ys[i];
        }

        int MapX(double x) => (int)(margin + (x - a)      / (b - a)          * (width  - 2 * margin));
        int MapY(double y) => (int)(height - margin - (y - minY) / (maxY - minY) * (height - 2 * margin));

        using var gridPen  = new Pen(Color.LightGray, 1);
        using var axisPen  = new Pen(Color.Gray,      2);
        using var graphPen = new Pen(Color.FromArgb(50, 100, 255), 3);

        for (int i = 0; i <= 10; i++)
        {
            g.DrawLine(gridPen, margin + i * (width  - 2 * margin) / 10, margin,
                                margin + i * (width  - 2 * margin) / 10, height - margin);
            g.DrawLine(gridPen, margin, margin + i * (height - 2 * margin) / 10,
                                width - margin, margin + i * (height - 2 * margin) / 10);
        }

        if (minY < 0 && maxY > 0) g.DrawLine(axisPen, margin, MapY(0), width - margin, MapY(0));
        if (a    < 0 && b    > 0) g.DrawLine(axisPen, MapX(0), margin, MapX(0), height - margin);

        for (int i = 1; i < N; i++)
            g.DrawLine(graphPen, MapX(xs[i-1]), MapY(ys[i-1]), MapX(xs[i]), MapY(ys[i]));

        bmp.Save(filename);
    }
}
