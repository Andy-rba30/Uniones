using System.Text;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Verification;

namespace UnionesAcero.Core.Reporting;

/// <summary>Informe en texto plano (español) del análisis de un nudo.</summary>
public static class ReportFormatter
{
    public static string Summary(JointAnalysis a)
    {
        var connected = a.Joints.Count(j => j.Connected);
        var errors = a.AllDiagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = a.AllDiagnostics.Count(d => d.Severity == Severity.Warning);
        return $"Columna {a.Column.Name}: {connected} viga(s) conectada(s), {a.BarGroups.Count()} grupo(s) de barras, {errors} error(es), {warnings} aviso(s).";
    }

    public static string Detailed(JointAnalysis a)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"NUDO — columna {a.Column.Name}");
        sb.AppendLine($"Norma: {a.Options.Code.Name} | fy = {a.Options.Materials.Fy:0} MPa | f'c = {a.Options.Materials.Fc:0} MPa | {(a.Options.SeismicJoint ? "nudo sísmico" : "nudo no sísmico")}");
        sb.AppendLine($"Sección: {a.Column.Outline.Count} vértices, área {a.Column.Outline.Area / 1e4:0.0} cm², recubrimiento {a.Column.Cover:0} mm, estribo Ø{a.Column.TieDiameter:0}");
        sb.AppendLine();

        foreach (var j in a.Joints)
        {
            sb.AppendLine($"Viga {j.Beam.Name} ({j.Beam.Width:0} x {j.Beam.Depth:0})");
            if (!j.Connected)
            {
                foreach (var d in j.Diagnostics) sb.AppendLine($"   {Mark(d.Severity)} {d.Message}");
                sb.AppendLine();
                continue;
            }
            sb.AppendLine($"   Cara de llegada: arista {j.ContactEdgeIndex + 1}, profundidad disponible {j.AvailableDepth:0} mm, útil {j.UsableDepth:0} mm" +
                          (j.OppositeBeam != null ? $", viga opuesta {j.OppositeBeam.Name}" : ""));
            foreach (var l in j.Layers)
            {
                var g = l.Group;
                sb.AppendLine($"   Capa {JointAnalyzer.LayerName(l.Layer)}: {j.Beam.BarCount(l.Layer)}Ø{j.Beam.BarDiameter(l.Layer):0} → {JointAnalyzer.DecisionName(l.Decision)}");
                sb.AppendLine($"      ld = {l.StraightRequired:0} mm, ldh = {l.HookRequired:0} mm, provisto = {l.Provided:0} mm, cota {l.Elevation:0}");
                sb.AppendLine($"      {l.Formula}");
                if (g?.EndHook != null)
                    sb.AppendLine($"      Gancho {(int)g.EndHook.Angle}°, extensión {g.EndHook.Extension:0} mm, doblez interior Ø{g.EndHook.BendInnerDiameter:0}, hacia {(g.EndHook.Direction.Z < 0 ? "abajo" : "arriba")}");
                if (g != null)
                    sb.AppendLine($"      Longitud total de barra {g.TotalLength:0} mm");
            }
            foreach (var d in j.Diagnostics) sb.AppendLine($"   {Mark(d.Severity)} {d.Message}");
            sb.AppendLine();
        }

        foreach (var d in a.Diagnostics) sb.AppendLine($"{Mark(d.Severity)} {d.Element}: {d.Message}");
        return sb.ToString();
    }

    public static string Verification(IReadOnlyList<BarCheckResult> results)
    {
        var sb = new StringBuilder();
        var applicable = results.Where(r => r.Status != CheckStatus.NotApplicable).ToList();
        var fails = applicable.Count(r => r.Status == CheckStatus.Fail);
        sb.AppendLine($"Barras revisadas: {applicable.Count} (omitidas {results.Count - applicable.Count}); no cumplen: {fails}.");
        sb.AppendLine();
        foreach (var r in applicable.OrderBy(r => r.Status == CheckStatus.Ok).ThenBy(r => r.BeamName))
        {
            sb.AppendLine($"{Mark(r.Status == CheckStatus.Ok ? Severity.Info : Severity.Error)} Barra {r.Id} ({r.BeamName}, capa {(r.Layer.HasValue ? JointAnalyzer.LayerName(r.Layer.Value) : "?")}): {r.Message}");
        }
        return sb.ToString();
    }

    private static string Mark(Severity s) => s switch
    {
        Severity.Error => "✖",
        Severity.Warning => "⚠",
        _ => "•"
    };
}
