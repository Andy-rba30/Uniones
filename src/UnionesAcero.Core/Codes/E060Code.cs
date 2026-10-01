namespace UnionesAcero.Core.Codes;

/// <summary>
/// NTE E.060 (Perú, 2009). La norma está escrita en kg/cm²; aquí se usan los coeficientes
/// equivalentes en MPa (1 MPa = 10.197 kg/cm², √10.197 ≈ 3.19).
/// </summary>
public sealed class E060Code : AciFamilyCode
{
    public override string Key => "E060";
    public override string Name => "E.060 (Perú)";

    // 12.2.2: ld = fy·ψt·ψe/(6.6·λ·√f'c)·db [kg/cm²]  →  2.1 en MPa (db ≤ 3/4")
    //         ld = fy·ψt·ψe/(5.3·λ·√f'c)·db [kg/cm²]  →  1.7 en MPa (db > 3/4")
    // 21.5.x (nudos sísmicos): ldh = fy·db/(17.2·√f'c) [kg/cm²] → 5.4 en MPa.

    protected override AnchorageLength GeneralHookLength(AnchorageContext ctx)
    {
        // 12.5.2: ldh = 0.075·ψe·fy·db/√f'c [kg/cm²] → 0.24 en MPa ≥ máx(8db, 150 mm)
        var db = ctx.BarDiameter;
        var m = ctx.Materials;
        var psiE = EpoxyFactorHook(ctx);
        var l = 0.24 * psiE * m.Fy / (m.Lambda * SqrtFc(ctx)) * db;
        if (ctx.HookConfinedInColumnCore) l *= 0.7; // 12.5.3(a)
        l = Math.Max(l, Math.Max(8 * db, 150));
        return new AnchorageLength(Round(l),
            ctx.HookConfinedInColumnCore
                ? "ldh = 0.7·0.075·ψe·fy·db/√f'c [kg/cm²] (12.5.2 y 12.5.3a)"
                : "ldh = 0.075·ψe·fy·db/√f'c [kg/cm²] (12.5.2)");
    }
}
