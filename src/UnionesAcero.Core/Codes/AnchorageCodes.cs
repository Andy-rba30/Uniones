namespace UnionesAcero.Core.Codes;

/// <summary>Registro de normativas disponibles.</summary>
public static class AnchorageCodes
{
    public static IReadOnlyList<IAnchorageCode> All { get; } = new IAnchorageCode[]
    {
        new Aci318Code(),
        new Nsr10Code(),
        new E060Code(),
        new Eurocode2Code()
    };

    public static IAnchorageCode Default => All[0];

    public static IAnchorageCode ByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Default;
        return All.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Default;
    }
}
