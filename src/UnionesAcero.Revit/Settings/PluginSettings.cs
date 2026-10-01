using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Model;

namespace UnionesAcero.Revit.Settings;

/// <summary>Configuración persistente del complemento (JSON en %AppData%\UnionesAcero\settings.json).</summary>
public sealed class PluginSettings
{
    public string CodeKey { get; set; } = "ACI318";
    public double Fy { get; set; } = 420;
    public double Fc { get; set; } = 21;
    public bool SeismicJoint { get; set; } = true;
    public bool LightweightConcrete { get; set; }
    public bool EpoxyCoated { get; set; }

    /// <summary>Recubrimiento de columna si el modelo no define uno (mm).</summary>
    public double ColumnCover { get; set; } = 40;
    public double ColumnTieDiameter { get; set; } = 10;
    public double ColumnBarDiameter { get; set; } = 16;

    /// <summary>Recubrimiento de viga si el modelo no define uno (mm).</summary>
    public double BeamCover { get; set; } = 40;
    public double BeamStirrupDiameter { get; set; } = 10;
    public double TopBarDiameter { get; set; } = 16;
    public int TopBarCount { get; set; } = 3;
    public double BottomBarDiameter { get; set; } = 16;
    public int BottomBarCount { get; set; } = 3;

    /// <summary>Si la viga ya tiene barras modeladas, usar su diámetro y cantidad.</summary>
    public bool InferBarsFromModel { get; set; } = true;

    public bool EmbedToFarFace { get; set; } = true;
    public bool AutoStaggerCrossingLayers { get; set; } = true;
    public double? ExtensionIntoBeam { get; set; }
    public double HookClearance { get; set; } = 0;
    public double LengthRounding { get; set; } = 10;

    /// <summary>Distancia (mm) alrededor de la columna donde se buscan vigas.</summary>
    public double BeamSearchDistance { get; set; } = 300;

    [JsonIgnore]
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnionesAcero", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static PluginSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new PluginSettings();
        }
        catch
        {
            // Archivo corrupto: se usan valores por defecto.
        }
        return new PluginSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public JointOptions ToJointOptions() => new()
    {
        Code = AnchorageCodes.ByKey(CodeKey),
        Materials = new Materials { Fy = Fy, Fc = Fc, LightweightConcrete = LightweightConcrete, EpoxyCoated = EpoxyCoated },
        SeismicJoint = SeismicJoint,
        EmbedToFarFace = EmbedToFarFace,
        AutoStaggerCrossingLayers = AutoStaggerCrossingLayers,
        ExtensionIntoBeam = ExtensionIntoBeam,
        HookClearance = HookClearance,
        LengthRounding = LengthRounding
    };
}
