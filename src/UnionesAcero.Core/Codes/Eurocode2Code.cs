using UnionesAcero.Core.Model;

namespace UnionesAcero.Core.Codes;

/// <summary>EN 1992-1-1 (Eurocódigo 2), apartado 8.4. fy se interpreta como fyk y fc como fck.</summary>
public sealed class Eurocode2Code : IAnchorageCode
{
    public string Key => "EC2";
    public string Name => "Eurocódigo 2 (EN 1992-1-1)";

    public double GammaC { get; init; } = 1.5;
    public double GammaS { get; init; } = 1.15;
    public double AlphaCt { get; init; } = 1.0;

    private static double Fctd(double fck, double gammaC, double alphaCt)
    {
        var fckEff = Math.Min(fck, 60); // fctd limitado al valor de C60/75
        var fctm = fckEff <= 50 ? 0.30 * Math.Pow(fckEff, 2.0 / 3.0) : 2.12 * Math.Log(1 + (fckEff + 8) / 10.0);
        var fctk05 = 0.7 * fctm;
        return alphaCt * fctk05 / gammaC;
    }

    /// <summary>lb,rqd = (φ/4)·(σsd/fbd) con σsd = fyd.</summary>
    private double BasicRequiredLength(AnchorageContext ctx, out string detail)
    {
        var phi = ctx.BarDiameter;
        var m = ctx.Materials;
        var eta1 = ctx.IsTopBar ? 0.7 : 1.0; // condiciones de adherencia "pobres" para barras superiores
        var eta2 = phi <= 32 ? 1.0 : (132 - phi) / 100.0;
        var fbd = 2.25 * eta1 * eta2 * Fctd(m.Fc, GammaC, AlphaCt);
        var sigmaSd = m.Fy / GammaS;
        var lb = phi / 4.0 * (sigmaSd / fbd);
        detail = $"lb,rqd = (φ/4)·(fyd/fbd), fbd={fbd:0.00} MPa, η1={eta1}";
        return lb;
    }

    private static double Alpha2(AnchorageContext ctx, bool bent)
    {
        var phi = ctx.BarDiameter;
        var cd = ctx.EdgeDistance ?? 3 * phi;
        var a2 = bent ? 1 - 0.15 * (cd - 3 * phi) / phi : 1 - 0.15 * (cd - phi) / phi;
        return Math.Clamp(a2, 0.7, 1.0);
    }

    public AnchorageLength StraightDevelopmentLength(AnchorageContext ctx)
    {
        var lb = BasicRequiredLength(ctx, out var detail);
        var a2 = Alpha2(ctx, bent: false);
        var lbd = Math.Max(a2 * lb, LbMin(lb, ctx.BarDiameter));
        return new AnchorageLength(Math.Ceiling(lbd), $"lbd = α2·lb,rqd, α2={a2:0.00}; {detail}");
    }

    public AnchorageLength HookDevelopmentLength(AnchorageContext ctx)
    {
        // Longitud equivalente lb,eq = α1·lb,rqd medida hasta la cara exterior del codo (fig. 8.1b).
        var lb = BasicRequiredLength(ctx, out var detail);
        var phi = ctx.BarDiameter;
        var cd = ctx.EdgeDistance ?? 3 * phi;
        var a1 = cd > 3 * phi ? 0.7 : 1.0;
        var lbeq = Math.Max(a1 * lb, LbMin(lb, phi));
        return new AnchorageLength(Math.Ceiling(lbeq), $"lb,eq = α1·lb,rqd, α1={a1}; {detail}");
    }

    private static double LbMin(double lbRqd, double phi) => Math.Max(Math.Max(0.3 * lbRqd, 10 * phi), 100);

    public double HookExtension(double barDiameter, HookAngle angle) => angle switch
    {
        HookAngle.None => 0,
        _ => 5 * barDiameter // fig. 8.1: prolongación ≥ 5φ tras el codo
    };

    public double BendInnerDiameter(double barDiameter) => barDiameter <= 16 ? 4 * barDiameter : 7 * barDiameter;

    public AnchorageLength LapSpliceLength(AnchorageContext ctx)
    {
        // 8.7.3: l0 = α6·lbd ≥ l0,min, con α6 = 1.5 (100 % de barras traslapadas en la sección).
        var lbd = StraightDevelopmentLength(ctx);
        var l0 = Math.Max(1.5 * lbd.Length, Math.Max(15 * ctx.BarDiameter, 200));
        return new AnchorageLength(Math.Ceiling(l0), "l0 = 1.5·lbd (α6 = 1.5)");
    }

    public double? MinimumJointDimensionForPassThrough(AnchorageContext ctx) => null;
}
