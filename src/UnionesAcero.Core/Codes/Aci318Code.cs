namespace UnionesAcero.Core.Codes;

/// <summary>ACI 318-19 (unidades SI). Normativa por defecto.</summary>
public sealed class Aci318Code : AciFamilyCode
{
    public override string Key => "ACI318";
    public override string Name => "ACI 318-19";

    protected override double GradeFactor(double fy)
    {
        if (fy <= 420) return 1.0;
        if (fy <= 550) return 1.15;
        return 1.3;
    }

    protected override AnchorageLength GeneralHookLength(AnchorageContext ctx)
    {
        // ACI 318-19 25.4.3.1: ldh = (fy·ψe·ψr·ψo·ψc / (23·λ·√f'c)) · db^1.5
        var db = ctx.BarDiameter;
        var m = ctx.Materials;
        var psiE = EpoxyFactorHook(ctx);
        var psiR = ctx.HookConfinedInColumnCore ? 1.0 : 1.6;
        var psiO = ctx.HookConfinedInColumnCore ? 1.0 : 1.25;
        var psiC = m.Fc < 42 ? m.Fc / 105.0 + 0.6 : 1.0;
        var l = m.Fy * psiE * psiR * psiO * psiC / (23.0 * m.Lambda * SqrtFc(ctx)) * Math.Pow(db, 1.5);
        l = Math.Max(l, Math.Max(8 * db, 150));
        return new AnchorageLength(Round(l),
            $"ldh = fy·ψe·ψr·ψo·ψc/(23·λ·√f'c)·db^1.5, ψr={psiR}, ψo={psiO}, ψc={psiC:0.00}");
    }
}
