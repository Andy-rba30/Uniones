using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Model;
using Xunit;

namespace UnionesAcero.Core.Tests;

public class CodeTests
{
    private static AnchorageContext Ctx(double db, bool seismic, bool top = false, double fc = 21, double fy = 420)
        => new() { BarDiameter = db, Materials = new Materials { Fy = fy, Fc = fc }, Seismic = seismic, IsTopBar = top };

    [Fact]
    public void Aci318SeismicHookAndStraight()
    {
        var code = new Aci318Code();
        // ldh = 420·16/(5.4·√21) = 271.5 → 272
        Assert.Equal(272, code.HookDevelopmentLength(Ctx(16, true)).Length);
        // ld = 2.5·ldh y 3.25·ldh se calculan con ldh sin redondear (271.5)
        Assert.Equal(679, code.StraightDevelopmentLength(Ctx(16, true)).Length);
        Assert.Equal(883, code.StraightDevelopmentLength(Ctx(16, true, top: true)).Length);
    }

    [Fact]
    public void Aci318SeismicHookHasMinimum()
    {
        var code = new Aci318Code();
        // Ø10 con f'c alto: fórmula da 420·10/(5.4·√42)=120 → mínimo 150
        Assert.Equal(150, code.HookDevelopmentLength(Ctx(10, true, fc: 42)).Length);
    }

    [Fact]
    public void Aci318GeneralStraight()
    {
        var code = new Aci318Code();
        // ld = 420·1·1·1/(2.1·√21)·16 = 698.3 → 699
        Assert.Equal(699, code.StraightDevelopmentLength(Ctx(16, false)).Length);
        // Superior: ×1.3 → 907.8 → 908
        Assert.Equal(908, code.StraightDevelopmentLength(Ctx(16, false, top: true)).Length);
        // Ø25: denominador 1.7 → 420/(1.7·4.583)·25.4 = 1369.4 → 1370
        Assert.Equal(1370, code.StraightDevelopmentLength(Ctx(25.4, false)).Length);
    }

    [Fact]
    public void Aci318GeneralHook()
    {
        var code = new Aci318Code();
        // ldh = 420·1·1·1·ψc/(23·√21)·16^1.5 ; ψc = 21/105+0.6 = 0.8 → 420·0.8/(105.40)·64 = 204.02 → 205
        Assert.Equal(205, code.HookDevelopmentLength(Ctx(16, false)).Length);
    }

    [Fact]
    public void Nsr10AndE060MatchAciFamily()
    {
        var nsr = new Nsr10Code();
        var e060 = new E060Code();
        var ctx = Ctx(16, false);
        // 0.7·0.24·420·16/√21 = 246.3 → 247
        Assert.Equal(247, nsr.HookDevelopmentLength(ctx).Length);
        Assert.Equal(247, e060.HookDevelopmentLength(ctx).Length);
        Assert.Equal(272, nsr.HookDevelopmentLength(Ctx(16, true)).Length);
    }

    [Fact]
    public void HookExtensionAndBend()
    {
        var code = new Aci318Code();
        Assert.Equal(192, code.HookExtension(16, HookAngle.Hook90));
        Assert.Equal(96, code.BendInnerDiameter(16));
        Assert.Equal(8 * 32, code.BendInnerDiameter(32));
    }

    [Fact]
    public void Eurocode2Lengths()
    {
        var ec2 = new Eurocode2Code();
        var ctx = new AnchorageContext { BarDiameter = 16, Materials = new Materials { Fy = 500, Fc = 25 } };
        // fctm = 0.3·25^(2/3) = 2.565; fctk05 = 1.795; fctd = 1.197; fbd = 2.69; fyd = 434.8
        // lb,rqd = 4·434.8/2.69 = 646.3; α2 (cd = 3φ) = 1 − 0.15·(48−16)/16 = 0.7 → 452.4 → 453
        var ld = ec2.StraightDevelopmentLength(ctx);
        Assert.InRange(ld.Length, 450, 456);
        var ldh = ec2.HookDevelopmentLength(ctx);
        // α1 = 1.0 con cd = 3φ (no > 3φ) → 646.3 → 647
        Assert.InRange(ldh.Length, 645, 649);
        Assert.Equal(80, ec2.HookExtension(16, HookAngle.Hook90));
        Assert.Null(ec2.MinimumJointDimensionForPassThrough(ctx));
    }

    [Fact]
    public void RegistryResolvesKeys()
    {
        Assert.Equal("ACI318", AnchorageCodes.ByKey(null).Key);
        Assert.Equal("NSR10", AnchorageCodes.ByKey("nsr10").Key);
        Assert.Equal("EC2", AnchorageCodes.ByKey("EC2").Key);
        Assert.Equal("ACI318", AnchorageCodes.ByKey("desconocida").Key);
    }
}
