namespace UnionesAcero.Core.Codes;

/// <summary>NSR-10 (Colombia), título C, basado en ACI 318-08.</summary>
public sealed class Nsr10Code : AciFamilyCode
{
    public override string Key => "NSR10";
    public override string Name => "NSR-10 (Colombia)";

    // C.12.2.2: ψs = 0.8 para barras ≤ No.6 (19 mm) ya está implícito en los denominadores
    // 2.1 / 1.7 de la tabla simplificada; por eso SizeFactor = 1.0.

    protected override AnchorageLength GeneralHookLength(AnchorageContext ctx)
    {
        // C.12.5.2: ldh = (0.24·ψe·fy / (λ·√f'c)) · db ≥ máx(8db, 150 mm)
        var db = ctx.BarDiameter;
        var m = ctx.Materials;
        var psiE = EpoxyFactorHook(ctx);
        var l = 0.24 * psiE * m.Fy / (m.Lambda * SqrtFc(ctx)) * db;
        // C.12.5.3(a): recubrimiento lateral ≥ 65 mm y gancho 90° con recubrimiento ≥ 50 mm → 0.7
        if (ctx.HookConfinedInColumnCore) l *= 0.7;
        l = Math.Max(l, Math.Max(8 * db, 150));
        return new AnchorageLength(Round(l),
            ctx.HookConfinedInColumnCore
                ? "ldh = 0.7·0.24·ψe·fy/(λ·√f'c)·db (C.12.5.2 y C.12.5.3a)"
                : "ldh = 0.24·ψe·fy/(λ·√f'c)·db (C.12.5.2)");
    }
}
