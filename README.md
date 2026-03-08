# SymCalc

A lightweight C# (.NET 9) library for **symbolic mathematical expression manipulation**.

SymCalc lets you treat math expressions as first-class objects — parse them from strings, differentiate them symbolically, simplify them algebraically, evaluate them numerically, and interpolate them with Lagrange or Chebyshev polynomials.

---

## Features

| | |
|---|---|
| **Arbitrary precision** | `BigFloat` provides 50-digit decimal arithmetic, eliminating catastrophic cancellation during interpolation |
| **Expression parsing** | Full infix parser with implicit multiplication (`2x`, `3(x+1)`) and all standard functions |
| **Symbolic differentiation** | Product, quotient, chain rules; handles `sin`, `cos`, `ln`, `exp`, `sqrt`, `abs`, `log`, `log_b` |
| **Algebraic simplification** | Constant folding, like-term collection, power merging, common-factor extraction, canonical ordering |
| **Polynomial form** | Convert any polynomial expression to a clean coefficient array and back |
| **Root finding** | Grid sampling + bisection on a given interval |
| **Interpolation** | Lagrange and Chebyshev node interpolation via `BigFloat` arithmetic |
| **Graph rendering** | PNG output via GDI+ (Windows only) |

### Supported expression syntax

```
Arithmetic:     + - * / ^
Implicit mul:   2x   x(x+1)   3sin(x)
Trig:           sin(x)  cos(x)  tan(x)  ctg(x)
Exp / log:      exp(x)  e^x  ln(x)  log(x)  log_2(x)
Other:          sqrt(x)  abs(x)
```

---

## Project structure

```
SymCalc/
├── BigFloat.cs       # Arbitrary-precision decimal floating-point
├── BinTree.cs        # Generic binary tree (AST node)
├── Function.cs       # Symbolic expression engine
├── Demo.cs           # Example: Lagrange interpolation of e^(−x²)·sin(20x)
├── SymCalc.csproj
├── SymCalc.sln
├── .gitignore
└── README.md
```

---

## Getting started

### Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Build and run the demo

```bash
git clone https://github.com/panticfilip/SymCalc.git
cd SymCalc
dotnet run
```

The demo approximates **e^(−x²)·sin(20x)** on [−1, 1] with 20 nodes using both uniform and Chebyshev spacing, prints the resulting polynomial, and reports the max-norm approximation error.

On **Windows**, two plots are also saved: `uniform_nodes.png` and `chebyshev_nodes.png`.

---

## Usage examples

### Parse and evaluate

```csharp
var f = new Function("sin(x^2) + 2*x");
double y = f.Evaluate(1.5);
```

### Symbolic differentiation

```csharp
var f  = new Function("x^3 + 2*x");
var df = f.Differentiate();
Console.WriteLine(df);   // 3*x^2+2
```

### Lagrange interpolation

```csharp
var f    = new Function("sin(x)");
var poly = f.LagrangeInterpolate(n: 10, a: 0, b: Math.PI);
Console.WriteLine(poly.ToPolynomial());
```

### Chebyshev interpolation

```csharp
var poly = f.ChebyshevInterpolate(n: 20, a: -1, b: 1);
```

### Find roots

```csharp
var f     = new Function("x^2 - 4");
var roots = f.Roots(-5, 5);   // [-2.0, 2.0]
```

### Find local extrema

```csharp
foreach (var (x, type) in Function.AllExtrema(f, -5, 5))
    Console.WriteLine($"x = {x:F6}  →  {type}");
```

### Render a graph (Windows)

```csharp
if (OperatingSystem.IsWindows())
    f.Graph(-Math.PI, Math.PI, "output.png");
```

---

## How it works

1. **Parsing** — the expression string is tokenised into a binary AST. Implicit multiplication (`2x` → `2*x`) is inserted during a preprocessing step.

2. **Differentiation** — the AST is traversed recursively and standard derivative rules are applied node-by-node.

3. **Simplification** — a multi-pass pipeline repeatedly applies constant folding, identity elimination, power merging, like-term collection, common-factor extraction, and canonical ordering until the expression stabilises.

4. **Interpolation** — Lagrange basis polynomials are built using `BigFloat` arithmetic to avoid numerical cancellation, then accumulated into a coefficient array and converted back to a `Function`.

5. **Evaluation** — the AST can be evaluated interpretively, or JIT-compiled to a `Func<double, double>` delegate via `Compile()` for fast repeated calls.

---

## Author

Filip Pantić
