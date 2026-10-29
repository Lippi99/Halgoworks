namespace Halgoworks.Core.Ip;

public enum Sense
{
    LessEqual,
    Equal,
    GreaterEqual,
}

public sealed record LinearConstraint(double[] Coefficients, Sense Sense, double Rhs);

public enum LpStatus
{
    Optimal,
    Infeasible,
    Unbounded,
}

public sealed record LpResult(LpStatus Status, double Objective, double[] X, int Pivots);

/// <summary>
/// A dense two-phase simplex for small linear programs: maximize c·x subject to the constraints and x ≥ 0.
/// Bland's rule (lowest index enters and leaves) guarantees it never cycles; it's slow on big models, which
/// is fine for the handful of variables shown in the lab.
/// </summary>
public static class Simplex
{
    private const double Eps = 1e-9;

    public static LpResult Maximize(double[] c, IReadOnlyList<LinearConstraint> constraints)
    {
        var n = c.Length;
        var m = constraints.Count;

        // Normalize so every right-hand side is non-negative.
        var rows = constraints.Select(r => r.Rhs < 0
            ? new LinearConstraint(r.Coefficients.Select(a => -a).ToArray(), r.Sense switch { Sense.LessEqual => Sense.GreaterEqual, Sense.GreaterEqual => Sense.LessEqual, _ => Sense.Equal }, -r.Rhs)
            : r).ToList();

        var slackCount = rows.Count(r => r.Sense != Sense.Equal);
        var artificialCount = rows.Count(r => r.Sense != Sense.LessEqual);
        var width = n + slackCount + artificialCount + 1;
        var t = new double[m + 1, width];
        var basis = new int[m];
        var artificialStart = n + slackCount;
        int slack = n, artificial = artificialStart;

        for (var i = 0; i < m; i++)
        {
            var r = rows[i];
            for (var j = 0; j < n; j++) t[i, j] = r.Coefficients[j];
            t[i, width - 1] = r.Rhs;
            switch (r.Sense)
            {
                case Sense.LessEqual:
                    t[i, slack] = 1;
                    basis[i] = slack++;
                    break;
                case Sense.GreaterEqual:
                    t[i, slack++] = -1;
                    t[i, artificial] = 1;
                    basis[i] = artificial++;
                    break;
                default:
                    t[i, artificial] = 1;
                    basis[i] = artificial++;
                    break;
            }
        }

        var pivots = 0;

        // Phase 1: drive the artificial variables to zero (maximize minus their sum).
        if (artificialCount > 0)
        {
            var phase1 = new double[width - 1];
            for (var j = artificialStart; j < width - 1; j++) phase1[j] = -1;
            SetObjective(t, basis, phase1, m, width);
            if (!Run(t, basis, m, width, width - 1, ref pivots)) return new LpResult(LpStatus.Unbounded, 0, new double[n], pivots);
            if (t[m, width - 1] < -1e-7) return new LpResult(LpStatus.Infeasible, 0, new double[n], pivots);

            // Pivot any artificial still in the basis (at zero) out, if a real column can replace it.
            for (var i = 0; i < m; i++)
            {
                if (basis[i] < artificialStart) continue;
                for (var j = 0; j < artificialStart; j++)
                {
                    if (Math.Abs(t[i, j]) <= Eps) continue;
                    Pivot(t, basis, m, width, i, j);
                    pivots++;
                    break;
                }
            }
        }

        // Phase 2: the real objective, with artificial columns barred from entering.
        var phase2 = new double[width - 1];
        Array.Copy(c, phase2, n);
        SetObjective(t, basis, phase2, m, width);
        if (!Run(t, basis, m, width, artificialStart, ref pivots)) return new LpResult(LpStatus.Unbounded, 0, new double[n], pivots);

        var x = new double[n];
        for (var i = 0; i < m; i++)
            if (basis[i] < n) x[basis[i]] = Math.Abs(t[i, width - 1]) < Eps ? 0 : t[i, width - 1];
        return new LpResult(LpStatus.Optimal, x.Select((v, j) => v * c[j]).Sum(), x, pivots);
    }

    // Row m holds reduced costs as (c_B B⁻¹A − c); its last cell is the objective value.
    private static void SetObjective(double[,] t, int[] basis, double[] c, int m, int width)
    {
        for (var j = 0; j < width; j++) t[m, j] = j < width - 1 ? -c[j] : 0;
        for (var i = 0; i < m; i++)
        {
            var cb = c[basis[i]];
            if (cb == 0) continue;
            for (var j = 0; j < width; j++) t[m, j] += cb * t[i, j];
        }
    }

    // Returns false if unbounded. Only columns below `limit` may enter.
    private static bool Run(double[,] t, int[] basis, int m, int width, int limit, ref int pivots)
    {
        while (true)
        {
            var enter = -1;
            for (var j = 0; j < limit; j++)
            {
                if (t[m, j] >= -Eps) continue;
                enter = j;
                break;
            }

            if (enter < 0) return true;

            var leave = -1;
            var best = double.MaxValue;
            for (var i = 0; i < m; i++)
            {
                if (t[i, enter] <= Eps) continue;
                var ratio = t[i, width - 1] / t[i, enter];
                if (ratio < best - Eps || (Math.Abs(ratio - best) <= Eps && leave >= 0 && basis[i] < basis[leave]))
                {
                    best = ratio;
                    leave = i;
                }
            }

            if (leave < 0) return false;
            Pivot(t, basis, m, width, leave, enter);
            if (++pivots > 5000) return true;
        }
    }

    private static void Pivot(double[,] t, int[] basis, int m, int width, int row, int col)
    {
        var p = t[row, col];
        for (var j = 0; j < width; j++) t[row, j] /= p;
        for (var i = 0; i <= m; i++)
        {
            if (i == row) continue;
            var f = t[i, col];
            if (f == 0) continue;
            for (var j = 0; j < width; j++) t[i, j] -= f * t[row, j];
        }

        basis[row] = col;
    }
}
