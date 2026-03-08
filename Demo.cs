using System;
using SymCalc;

/// <summary>
/// Demonstrates the core capabilities of the SymCalc library:
/// Lagrange polynomial interpolation with uniform vs. Chebyshev nodes,
/// applied to f(x) = e^(−x²)·sin(20x) on [−1, 1].
/// </summary>
class Demo
{
    static void Main(string[] args)
    {
        var f  = new Function("e^(-x^2)*sin(20*x)");
        int n  = 20;
        double lo = -1.0, hi = 1.0;

        RunInterpolation(f, n, lo, hi, useCheby: false);
        RunInterpolation(f, n, lo, hi, useCheby: true);
    }

    static void RunInterpolation(Function f, int n, double lo, double hi, bool useCheby)
    {
        string label = useCheby ? "Chebyshev" : "Uniform";

        // Build interpolation nodes
        double[] xs = new double[n];
        double[] ys = new double[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = useCheby
                ? Math.Cos((2.0 * i + 1.0) * Math.PI / (2.0 * (n + 1)))   // Chebyshev
                : lo + i * (hi - lo) / (n - 1);                             // uniform
            ys[i] = f.Evaluate(xs[i]);
        }

        // Construct Lagrange polynomial symbolically
        Function p = BuildLagrangePolynomial(xs, ys, n);

        // Convert to canonical polynomial form (sorted, collected terms)
        Function poly = p.ToPolynomial();

        // Render graph on Windows
        if (OperatingSystem.IsWindows())
            poly.Graph(lo, hi, $"{label.ToLower()}_nodes.png");

        // Report results
        double maxErr = MaxAbsError(f - p, lo, hi);
        Console.WriteLine($"=== {label} nodes ===");
        Console.WriteLine(poly);
        Console.WriteLine($"Max approximation error: {maxErr}");
        Console.WriteLine();
    }

    /// <summary>
    /// Constructs the Lagrange interpolating polynomial symbolically
    /// through the given node arrays.
    /// </summary>
    static Function BuildLagrangePolynomial(double[] xs, double[] ys, int n)
    {
        Function result = new Function("0");

        for (int i = 0; i < n; i++)
        {
            Function basis = new Function("1");
            double   denom = 1.0;

            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                basis  = basis * new Function($"(x-({Function.Format(xs[j])}))");
                denom *= xs[i] - xs[j];
            }

            result = result + basis * (ys[i] / denom);
        }

        return result;
    }

    /// <summary>Estimates the max-norm of <paramref name="err"/> on [lo, hi] via grid sampling.</summary>
    static double MaxAbsError(Function err, double lo, double hi)
        => Math.Max(Math.Abs(err.Min(lo, hi)), Math.Abs(err.Max(lo, hi)));
}
