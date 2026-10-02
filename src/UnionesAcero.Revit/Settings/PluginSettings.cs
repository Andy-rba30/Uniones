using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Model;

namespace UnionesAcero.Revit.Settings;

/// <summary>Qué hacer con las vigas que ya tienen barras longitudinales modeladas.</summary>
public enum ExistingBarsAction
{
    /// <summary>Verificarlas y corregir las que no cumplen (misma barra prolongada hasta el núcleo, con gancho si hace falta).</summary>
    Fix,
    /// <summary>Solo verificarlas; no se crea ni se modifica nada en esas capas.</summary>
    VerifyOnly,
    /// <summary>Ignorarlas y añadir barras nuevas como si la viga no tuviera armadura.</summary>
    AddNew
}

/// <summary>
/// Valores por defecto de la ventana (config.json junto a la DLL, como en los add-ins de columnas
/// y muros). Lo que se cambia en la ventana vale solo para esa vez salvo que se pulse
/// "Guardar como valores por defecto".
/// </summary>
public sealed class PluginSettings
{
    public string CodeKey { get; set; } = "ACI318";
    public double Fy { get; set; } = 420;
    public double Fc { get; set; } = 21;
    public bool SeismicJoint { get; set; } = true;
    public bool LightweightConcrete { get; set; }
    public bool EpoxyCoated { get; set; }

    /// <summary>Recubrimiento de columna si el modelo no define uno (mm).</summary>
    public double ColumnCoverMm { get; set; } = 40;
    public double ColumnTieDiameterMm { get; set; } = 10;

    /// <summary>Recubrimiento de viga si el modelo no define uno (mm).</summary>
    public double BeamCoverMm { get; set; } = 40;
    public double BeamStirrupDiameterMm { get; set; } = 10;

    /// <summary>Tipo de barra (RebarBarType) por defecto de la capa superior e inferior: nombre exacto o fragmento.</summary>
    public string TopBarTypeName { get; set; } = "";
    public int TopBarCount { get; set; } = 3;
    public string BottomBarTypeName { get; set; } = "";
    public int BottomBarCount { get; set; } = 3;

    /// <summary>Tipo de gancho (RebarHookType) de 90° para los anclajes: nombre exacto o fragmento.</summary>
    public string HookTypeName { get; set; } = "90";

    /// <summary>Si la viga ya tiene barras longitudinales modeladas, proponer su diámetro y cantidad.</summary>
    public bool InferBarsFromModel { get; set; } = true;

    /// <summary>
    /// Vigas con barras longitudinales ya modeladas: "fix" (verificar y corregir las que no cumplen),
    /// "verify" (solo verificar) o "add" (ignorarlas y añadir barras nuevas).
    /// </summary>
    public string ExistingBarsMode { get; set; } = "fix";

    [JsonIgnore]
    public ExistingBarsAction ExistingBarsAction => (ExistingBarsMode ?? "").Trim().ToLowerInvariant() switch
    {
        "verify" or "verificar" => ExistingBarsAction.VerifyOnly,
        "add" or "new" or "nuevas" => ExistingBarsAction.AddNew,
        _ => ExistingBarsAction.Fix
    };

    public static string ModeKey(ExistingBarsAction a) => a switch
    {
        ExistingBarsAction.VerifyOnly => "verify",
        ExistingBarsAction.AddNew => "add",
        _ => "fix"
    };

    public bool EmbedToFarFace { get; set; } = true;
    public bool AutoStaggerCrossingLayers { get; set; } = true;
    /// <summary>Extensión de la barra dentro de la viga desde la cara de la columna (mm). null = 2h + traslape.</summary>
    public double? ExtensionIntoBeamMm { get; set; }
    public double HookClearanceMm { get; set; } = 0;

    /// <summary>Correr en planta las barras de la viga que coinciden con las verticales de la columna.</summary>
    public bool ResolveColumnBarClash { get; set; } = true;

    /// <summary>Separación libre deseada entre barra de viga y vertical de columna (mm); si no cabe se admite el contacto.</summary>
    public double ColumnBarClearanceMm { get; set; } = 25;

    /// <summary>Retrasar el gancho de la viga secundaria cuando choca con la principal en la misma esquina.</summary>
    public bool ResolveHookLegClash { get; set; } = true;

    /// <summary>Barras pasantes ya modeladas: hacer continua la de la viga principal y empalmarla fuera del nudo.</summary>
    public bool SplicePassThroughBars { get; set; } = true;

    /// <summary>Longitud comercial de la varilla (mm).</summary>
    public double CommercialBarLengthMm { get; set; } = 9000;
    public double LengthRoundingMm { get; set; } = 10;

    /// <summary>Distancia (mm) alrededor de la columna donde se buscan vigas.</summary>
    public double BeamSearchDistanceMm { get; set; } = 300;

    /// <summary>Marcar en rojo en la vista activa las barras existentes que no cumplen.</summary>
    public bool HighlightFailingExistingBars { get; set; } = true;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ConfigPath()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        return Path.Combine(dir, "config.json");
    }

    public static PluginSettings Load()
    {
        try
        {
            var path = ConfigPath();
            if (File.Exists(path))
                return (JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(path), ReadOptions) ?? new PluginSettings()).Normalized();
        }
        catch
        {
            // config.json ilegible: valores por defecto
        }
        return new PluginSettings();
    }

    public void Save(string? path = null) => File.WriteAllText(path ?? ConfigPath(), JsonSerializer.Serialize(this, WriteOptions));

    public PluginSettings Clone() =>
        (JsonSerializer.Deserialize<PluginSettings>(JsonSerializer.Serialize(this, WriteOptions), ReadOptions) ?? new PluginSettings()).Normalized();

    public PluginSettings Normalized()
    {
        TopBarTypeName ??= ""; BottomBarTypeName ??= ""; HookTypeName ??= "";
        ExistingBarsMode = ModeKey(ExistingBarsAction);
        if (TopBarCount < 1) TopBarCount = 1;
        if (BottomBarCount < 1) BottomBarCount = 1;
        if (LengthRoundingMm < 0) LengthRoundingMm = 0;
        if (ColumnBarClearanceMm < 0) ColumnBarClearanceMm = 0;
        if (CommercialBarLengthMm < 1000) CommercialBarLengthMm = 9000;
        if (BeamSearchDistanceMm < 0) BeamSearchDistanceMm = 0;
        if (Fy <= 0) Fy = 420;
        if (Fc <= 0) Fc = 21;
        return this;
    }

    public JointOptions ToJointOptions() => new()
    {
        Code = AnchorageCodes.ByKey(CodeKey),
        Materials = new Materials { Fy = Fy, Fc = Fc, LightweightConcrete = LightweightConcrete, EpoxyCoated = EpoxyCoated },
        SeismicJoint = SeismicJoint,
        EmbedToFarFace = EmbedToFarFace,
        AutoStaggerCrossingLayers = AutoStaggerCrossingLayers,
        ExtensionIntoBeam = ExtensionIntoBeamMm,
        HookClearance = HookClearanceMm,
        LengthRounding = LengthRoundingMm,
        ResolveColumnBarClash = ResolveColumnBarClash,
        ColumnBarClearance = ColumnBarClearanceMm,
        ResolveHookLegClash = ResolveHookLegClash,
        SplicePassThroughBars = SplicePassThroughBars,
        CommercialBarLength = CommercialBarLengthMm
    };
}
