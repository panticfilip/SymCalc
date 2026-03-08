using System;
using System.Globalization;
using System.Numerics;

namespace SymCalc;

/// <summary>
/// Arbitrary-precision decimal floating-point number.
/// Internally represented as <c>Mantissa × 10^(-Scale)</c>.
/// All arithmetic results are rounded to <see cref="Precision"/> significant digits.
/// </summary>
public readonly struct BigFloat : IComparable<BigFloat>
{
    /// <summary>Number of significant decimal digits kept after every operation.</summary>
    public const int Precision = 50;

    public BigInteger Mantissa { get; }
    public int        Scale    { get; }

    /// <summary>
    /// Creates a normalised BigFloat, stripping trailing decimal zeros
    /// from the mantissa and adjusting the scale accordingly.
    /// </summary>
    public BigFloat(BigInteger mantissa, int scale)
    {
        if (mantissa.IsZero) { Mantissa = BigInteger.Zero; Scale = 0; return; }

        var m = mantissa;
        var s = scale;
        while (!m.IsZero)
        {
            var div = BigInteger.DivRem(m, 10, out var rem);
            if (rem != 0) break;
            m = div; s--;
        }
        Mantissa = m; Scale = s;
    }

    public static BigFloat Zero => new(BigInteger.Zero, 0);
    public static BigFloat One  => new(BigInteger.One,  0);

    // ── Construction ────────────────────────────────────────────────────────

    /// <summary>Converts a <see cref="double"/> to BigFloat. Throws on NaN or Infinity.</summary>
    public static BigFloat FromDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentException("Cannot convert NaN or Infinity to BigFloat.");
        return Parse(value.ToString("0.############################################################", CultureInfo.InvariantCulture));
    }

    /// <summary>Parses a decimal string into a BigFloat. Throws on invalid input.</summary>
    public static BigFloat Parse(string s)
    {
        if (!TryParse(s, out var v))
            throw new FormatException($"Invalid BigFloat literal: {s}");
        return v;
    }

    /// <summary>Attempts to parse a decimal string. Returns success via the out parameter.</summary>
    public static bool TryParse(string s, out BigFloat value)
    {
        s = s.Trim();
        if (string.IsNullOrWhiteSpace(s)) { value = Zero; return false; }

        int expShift = 0;
        int ePos = s.IndexOfAny(new[] { 'e', 'E' });
        if (ePos > 0)
        {
            if (!int.TryParse(s[(ePos + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out expShift))
            { value = Zero; return false; }
            s = s[..ePos];
        }

        bool neg = false;
        if      (s.StartsWith('+')) s = s[1..];
        else if (s.StartsWith('-')) { neg = true; s = s[1..]; }

        int dot = s.IndexOf('.');
        int scale = 0;
        string digits = s;
        if (dot >= 0) { scale = s.Length - dot - 1; digits = s.Remove(dot, 1); }

        if (!BigInteger.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mantissa))
        { value = Zero; return false; }

        if (neg) mantissa = -mantissa;
        value = new BigFloat(mantissa, scale - expShift);
        return true;
    }

    // ── Comparison ──────────────────────────────────────────────────────────

    public int CompareTo(BigFloat other)
    {
        var (aM, bM, _) = AlignScales(this, other);
        return aM.CompareTo(bM);
    }

    public static BigFloat Abs(BigFloat v) =>
        v.Mantissa.Sign < 0 ? new BigFloat(BigInteger.Abs(v.Mantissa), v.Scale) : v;

    // ── Arithmetic operators ────────────────────────────────────────────────

    public static BigFloat operator -(BigFloat v) => new(-v.Mantissa, v.Scale);

    public static BigFloat operator +(BigFloat a, BigFloat b)
    { var (am, bm, s) = AlignScales(a, b); return Round(new BigFloat(am + bm, s)); }

    public static BigFloat operator -(BigFloat a, BigFloat b)
    { var (am, bm, s) = AlignScales(a, b); return Round(new BigFloat(am - bm, s)); }

    public static BigFloat operator *(BigFloat a, BigFloat b)
        => Round(new BigFloat(a.Mantissa * b.Mantissa, a.Scale + b.Scale));

    public static BigFloat operator /(BigFloat a, BigFloat b)
    {
        if (b.Mantissa.IsZero) throw new DivideByZeroException();
        int extra = Precision + Math.Abs(a.Scale - b.Scale) + 5;
        var boost = BigInteger.Pow(10, extra);
        return Round(new BigFloat(a.Mantissa * boost / b.Mantissa, a.Scale - b.Scale + extra));
    }

    // ── Comparison operators ────────────────────────────────────────────────

    public static bool operator ==(BigFloat a, BigFloat b)  => a.CompareTo(b) == 0;
    public static bool operator !=(BigFloat a, BigFloat b)  => !(a == b);
    public static bool operator < (BigFloat a, BigFloat b)  => a.CompareTo(b) <  0;
    public static bool operator > (BigFloat a, BigFloat b)  => a.CompareTo(b) >  0;
    public static bool operator <=(BigFloat a, BigFloat b)  => a.CompareTo(b) <= 0;
    public static bool operator >=(BigFloat a, BigFloat b)  => a.CompareTo(b) >= 0;

    public override bool Equals(object? obj) => obj is BigFloat b && this == b;
    public override int  GetHashCode()       => HashCode.Combine(Mantissa, Scale);

    // ── Conversion ──────────────────────────────────────────────────────────

    /// <summary>Returns the value as a plain decimal string (no scientific notation).</summary>
    public override string ToString()
    {
        if (Mantissa.IsZero) return "0";
        var  s   = BigInteger.Abs(Mantissa).ToString(CultureInfo.InvariantCulture);
        bool neg = Mantissa.Sign < 0;

        if      (Scale == 0)        { /* nothing */ }
        else if (Scale < 0)         { s += new string('0', -Scale); }
        else if (Scale >= s.Length) { s  = "0." + new string('0', Scale - s.Length) + s; }
        else                        { s  = s.Insert(s.Length - Scale, "."); }

        return neg ? "-" + s : s;
    }

    /// <summary>Converts this value to a <see cref="double"/>.</summary>
    public double ToDouble() => (double)Mantissa * Math.Pow(10.0, -Scale);

    // ── Math functions ──────────────────────────────────────────────────────

    public static BigFloat Pow(BigFloat a, BigFloat b) => FromDouble(Math.Pow(a.ToDouble(), b.ToDouble()));
    public static BigFloat Exp (BigFloat a) => FromDouble(Math.Exp (a.ToDouble()));
    public static BigFloat Sin (BigFloat a) => FromDouble(Math.Sin (a.ToDouble()));
    public static BigFloat Cos (BigFloat a) => FromDouble(Math.Cos (a.ToDouble()));
    public static BigFloat Tan (BigFloat a) => FromDouble(Math.Tan (a.ToDouble()));
    public static BigFloat Ctg (BigFloat a) => FromDouble(1.0 / Math.Tan(a.ToDouble()));
    public static BigFloat Ln  (BigFloat a) => FromDouble(Math.Log (a.ToDouble()));
    public static BigFloat Sqrt(BigFloat a) => FromDouble(Math.Sqrt(a.ToDouble()));

    // ── Private helpers ─────────────────────────────────────────────────────

    private static (BigInteger aMant, BigInteger bMant, int scale) AlignScales(BigFloat a, BigFloat b)
    {
        int scale = Math.Max(a.Scale, b.Scale);
        var aMant = a.Mantissa * (scale - a.Scale == 0 ? BigInteger.One : BigInteger.Pow(10, scale - a.Scale));
        var bMant = b.Mantissa * (scale - b.Scale == 0 ? BigInteger.One : BigInteger.Pow(10, scale - b.Scale));
        return (aMant, bMant, scale);
    }

    private static BigFloat Round(BigFloat v)
    {
        var m      = v.Mantissa;
        var s      = v.Scale;
        int digits = m.ToString(CultureInfo.InvariantCulture).TrimStart('-').Length;
        if (digits <= Precision) return new BigFloat(m, s);

        int drop    = digits - Precision;
        var divisor = BigInteger.Pow(10, drop);
        var q       = BigInteger.DivRem(BigInteger.Abs(m), divisor, out var rem);
        if (rem * 2 >= divisor) q++;
        if (m.Sign < 0) q = -q;
        return new BigFloat(q, s - drop);
    }
}
