using UnionesAcero.Core.Model;

namespace UnionesAcero.Core.Codes;

/// <summary>
/// Base para las normas derivadas de ACI 318 (NSR-10, E.060). Todas las expresiones están en
/// MPa y mm. Cada norma concreta fija sus constantes.
/// </summary>
public abstract class AciFamilyCode : IAnchorageCode
{
    public abstract string Key { get; }
    public abstract string Name { get; }

    /// <summary>Denominador de la fórmula simplificada para db ≤ 19 mm (ACI: 2.1).</summary>
    protected virtual double SmallBarDenominator => 2.1;

    /// <summary>Denominador de la fórmula simplificada para db ≥ 22 mm (ACI: 1.7).</summary>
    protected virtual double LargeBarDenominator => 1.7;

    /// <summary>Denominador cuando no se cumplen cover/separación, db ≤ 19 (ACI: 1.4).</summary>
    protected virtual double SmallBarDenominatorUnfavorable => 1.4;

    /// <summary>Denominador cuando no se cumplen cover/separación, db ≥ 22 (ACI: 1.1).</summary>
    protected virtual double LargeBarDenominatorUnfavorable => 1.1;

    /// <summary>Denominador de ldh sísmico: ldh = fy·db / (k·λ·√f'c). ACI 18.8.5.1: 5.4.</summary>
    protected virtual double SeismicHookDenominator => 5.4;

    /// <summary>Límite superior de √f'c (ACI 25.4.1.4: 8.3 MPa).</summary>
    protected virtual double MaxSqrtFc => 8.3;

    protected static double TopBarFactor(AnchorageContext ctx) => ctx.IsTopBar ? 1.3 : 1.0;

    protected static double EpoxyFactorStraight(AnchorageContext ctx) => ctx.Materials.EpoxyCoated ? 1.2 : 1.0;

    protected static double EpoxyFactorHook(AnchorageContext ctx) => ctx.Materials.EpoxyCoated ? 1.2 : 1.0;

    /// <summary>ψs: factor de tamaño (ACI 318-08/NSR-10/E.060: 0.8 para db ≤ 19 mm).</summary>
    protected virtual double SizeFactor(double db) => 1.0;

    protected double SqrtFc(AnchorageContext ctx) => Math.Min(Math.Sqrt(ctx.Materials.Fc), MaxSqrtFc);

    public virtual AnchorageLength StraightDevelopmentLength(AnchorageContext ctx)
    {
        if (ctx.Seismic)
        {
            // Barras rectas en nudos de pórticos especiales: ld = 2.5·ldh (inferiores) ó 3.25·ldh (superiores).
            var ldh = SeismicHookLength(ctx);
            var factor = ctx.IsTopBar ? 3.25 : 2.5;
            return new AnchorageLength(Round(factor * ldh), $"ld = {factor}·ldh = {factor}·{ldh:0}");
        }

        var db = ctx.BarDiameter;
        var m = ctx.Materials;
        var psiT = TopBarFactor(ctx);
        var psiE = EpoxyFactorStraight(ctx);
        var psiTE = Math.Min(psiT * psiE, 1.7);
        var psiG = GradeFactor(m.Fy);
        var denom = db <= 19
            ? (ctx.FavorableSpacingAndCover ? SmallBarDenominator : SmallBarDenominatorUnfavorable)
            : (ctx.FavorableSpacingAndCover ? LargeBarDenominator : LargeBarDenominatorUnfavorable);
        var ld = m.Fy * psiTE * psiG * SizeFactor(db) / (denom * m.Lambda * SqrtFc(ctx)) * db;
        ld = Math.Max(ld, 300);
        return new AnchorageLength(Round(ld), $"ld = fy·ψt·ψe·ψg/({denom}·λ·√f'c)·db, ψt·ψe={psiTE:0.00}");
    }

    /// <summary>ψg: factor por grado del acero (solo ACI 318-19). Por defecto 1.0.</summary>
    protected virtual double GradeFactor(double fy) => 1.0;

    public virtual AnchorageLength HookDevelopmentLength(AnchorageContext ctx)
    {
        if (ctx.Seismic)
        {
            var l = SeismicHookLength(ctx);
            return new AnchorageLength(Round(l), $"ldh = fy·db/({SeismicHookDenominator}·λ·√f'c) ≥ máx(8db, 150)");
        }
        return GeneralHookLength(ctx);
    }

    /// <summary>ldh para nudos sísmicos (ACI 18.8.5.1, NSR-10 C.21.7.5, E.060 21.7.x).</summary>
    protected virtual double SeismicHookLength(AnchorageContext ctx)
    {
        var db = ctx.BarDiameter;
        var m = ctx.Materials;
        var l = m.Fy * db / (SeismicHookDenominator * m.Lambda * SqrtFc(ctx));
        return Math.Max(l, Math.Max(8 * db, 150));
    }

    /// <summary>ldh general (no sísmico). Cada norma define su expresión.</summary>
    protected abstract AnchorageLength GeneralHookLength(AnchorageContext ctx);

    public virtual double HookExtension(double db, HookAngle angle) => angle switch
    {
        HookAngle.Hook90 => 12 * db,
        HookAngle.Hook135 => Math.Max(6 * db, 75),
        HookAngle.Hook180 => Math.Max(4 * db, 65),
        _ => 0
    };

    public virtual double BendInnerDiameter(double db)
    {
        if (db <= 25.4) return 6 * db;   // No.3 a No.8
        if (db <= 36) return 8 * db;     // No.9 a No.11
        return 10 * db;                  // No.14 y No.18
    }

    public virtual AnchorageLength LapSpliceLength(AnchorageContext ctx)
    {
        var ld = StraightDevelopmentLength(ctx with { Seismic = false });
        var lap = Math.Max(1.3 * ld.Length, 300);
        return new AnchorageLength(Round(lap), "Traslape clase B = 1.3·ld");
    }

    public virtual double? MinimumJointDimensionForPassThrough(AnchorageContext ctx)
    {
        if (!ctx.Seismic) return null;
        // ACI 318-19 18.8.2.3: 20·db para Grado 420 (26·db para Grado 550).
        var factor = ctx.Materials.Fy > 450 ? 26 : 20;
        return factor * ctx.BarDiameter;
    }

    protected static double Round(double v) => Math.Ceiling(v);
}
