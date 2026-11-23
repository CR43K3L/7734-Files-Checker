using System.Globalization;
using System.Xml.Linq;

namespace Files7734;

/// <summary>Un réglage graphique de GTA V (balise de la section &lt;graphics&gt; de settings.xml).</summary>
public sealed record GtaOption(string Key, string Label, string Help, (string Value, string Label)[] Choices);

/// <summary>Lecture et modification du fichier de réglages de GTA V utilisé par FiveM (gta5_settings.xml).</summary>
public sealed class GtaSettings
{
    private static readonly (string, string)[] Q3 = { ("0", "Normal"), ("1", "Élevé"), ("2", "Très élevé") };
    private static readonly (string, string)[] Q4 = { ("0", "Normal"), ("1", "Élevé"), ("2", "Très élevé"), ("3", "Ultra") };
    private static readonly (string, string)[] OnOff = { ("false", "Non"), ("true", "Oui") };

    public static readonly GtaOption[] Options =
    {
        new("TextureQuality", "Textures", "Le plus gourmand en mémoire vidéo. Baisse-le si le jeu saccade en arrivant dans une zone.", Q3),
        new("ShaderQuality", "Shaders", "Qualité de l'éclairage des surfaces.", Q3),
        new("ShadowQuality", "Ombres", "Gros impact sur les FPS en ville.", Q4),
        new("Shadow_SoftShadows", "Ombres douces", "Bords des ombres.", new[] { ("0", "Nettes"), ("1", "Douces"), ("2", "Plus douces"), ("3", "Très douces"), ("4", "AMD CHS"), ("5", "NVIDIA PCSS") }),
        new("ReflectionQuality", "Reflets", "Reflets sur les voitures, l'eau et les vitres.", Q4),
        new("ReflectionMSAA", "MSAA des reflets", "", new[] { ("0", "Non"), ("2", "x2"), ("4", "x4"), ("8", "x8") }),
        new("WaterQuality", "Eau", "", Q3),
        new("ParticleQuality", "Particules", "Fumée, explosions, étincelles.", Q3),
        new("GrassQuality", "Herbe", "Énorme impact hors de la ville : « Normal » fait gagner beaucoup de FPS.", Q4),
        new("PostFX", "Effets post-traitement", "Flou, éclat lumineux, effets de caméra.", Q4),
        new("SSAO", "Occlusion ambiante", "Ombres de contact entre les objets.", new[] { ("0", "Non"), ("1", "Normal"), ("2", "Élevé") }),
        new("Tessellation", "Tessellation", "Relief des surfaces (arbres, routes).", new[] { ("0", "Non"), ("1", "Normal"), ("2", "Élevé"), ("3", "Très élevé") }),
        new("AnisotropicFiltering", "Filtrage anisotrope", "Netteté des textures au loin. Presque gratuit en FPS.", new[] { ("0", "Non"), ("2", "x2"), ("4", "x4"), ("8", "x8"), ("16", "x16") }),
        new("MSAA", "MSAA", "Anticrénelage très gourmand. Le FXAA suffit en général.", new[] { ("0", "Non"), ("2", "x2"), ("4", "x4"), ("8", "x8") }),
        new("FXAA_Enabled", "FXAA", "Anticrénelage léger, presque gratuit.", OnOff),
        new("DoF", "Profondeur de champ", "Flou d'arrière-plan en cinématique.", OnOff),
        new("Shadow_LongShadows", "Ombres longues", "Ombres au lever et coucher du soleil.", OnOff),
        new("UltraShadows_Enabled", "Ombres haute résolution", "", OnOff),
        new("LodScale", "Distance d'affichage", "Distance à laquelle les détails apparaissent.", new[] { ("0.000000", "Courte"), ("0.500000", "Moyenne"), ("1.000000", "Longue") }),
        new("CityDensity", "Densité de population", "En RP, les PNJ viennent surtout du serveur : réduire aide les petits PC.", new[] { ("0.000000", "Faible"), ("0.500000", "Moyenne"), ("1.000000", "Élevée") }),
        new("MotionBlurStrength", "Flou de mouvement", "", new[] { ("0.000000", "Non"), ("0.500000", "Moyen"), ("1.000000", "Fort") }),
    };

    public static readonly (string Name, string Description, Dictionary<string, string> Values)[] Presets =
    {
        ("Performance", "Le plus de FPS possible, pour les petits PC ou les grosses scènes RP.", new()
        {
            ["TextureQuality"] = "1", ["ShaderQuality"] = "0", ["ShadowQuality"] = "0", ["Shadow_SoftShadows"] = "0", ["ReflectionQuality"] = "0",
            ["ReflectionMSAA"] = "0", ["WaterQuality"] = "0", ["ParticleQuality"] = "0", ["GrassQuality"] = "0", ["PostFX"] = "0", ["SSAO"] = "0",
            ["Tessellation"] = "0", ["AnisotropicFiltering"] = "4", ["MSAA"] = "0", ["FXAA_Enabled"] = "true", ["DoF"] = "false",
            ["Shadow_LongShadows"] = "false", ["UltraShadows_Enabled"] = "false", ["LodScale"] = "0.000000", ["CityDensity"] = "0.500000", ["MotionBlurStrength"] = "0.000000",
        }),
        ("Équilibré", "Beau et fluide sur la plupart des PC de jeu.", new()
        {
            ["TextureQuality"] = "2", ["ShaderQuality"] = "1", ["ShadowQuality"] = "1", ["Shadow_SoftShadows"] = "2", ["ReflectionQuality"] = "1",
            ["ReflectionMSAA"] = "0", ["WaterQuality"] = "1", ["ParticleQuality"] = "1", ["GrassQuality"] = "1", ["PostFX"] = "1", ["SSAO"] = "1",
            ["Tessellation"] = "1", ["AnisotropicFiltering"] = "16", ["MSAA"] = "0", ["FXAA_Enabled"] = "true", ["DoF"] = "false",
            ["Shadow_LongShadows"] = "false", ["UltraShadows_Enabled"] = "false", ["LodScale"] = "0.500000", ["CityDensity"] = "0.500000", ["MotionBlurStrength"] = "0.000000",
        }),
        ("Qualité", "Le plus beau, pour les bonnes cartes graphiques (photos, vidéos).", new()
        {
            ["TextureQuality"] = "2", ["ShaderQuality"] = "2", ["ShadowQuality"] = "3", ["Shadow_SoftShadows"] = "3", ["ReflectionQuality"] = "3",
            ["ReflectionMSAA"] = "2", ["WaterQuality"] = "2", ["ParticleQuality"] = "2", ["GrassQuality"] = "2", ["PostFX"] = "3", ["SSAO"] = "2",
            ["Tessellation"] = "3", ["AnisotropicFiltering"] = "16", ["MSAA"] = "2", ["FXAA_Enabled"] = "true", ["DoF"] = "true",
            ["Shadow_LongShadows"] = "true", ["UltraShadows_Enabled"] = "false", ["LodScale"] = "1.000000", ["CityDensity"] = "1.000000", ["MotionBlurStrength"] = "0.000000",
        }),
    };

    private readonly XDocument _doc;
    private GtaSettings(XDocument doc) { _doc = doc; }

    public static GtaSettings Parse(string xml) => new(XDocument.Parse(xml));

    private XElement Graphics => _doc.Root?.Element("graphics") ?? throw new InvalidDataException("Section <graphics> introuvable.");

    public string? Get(string key) => Graphics.Element(key)?.Attribute("value")?.Value;

    public void Set(string key, string value)
    {
        var e = Graphics.Element(key);
        if (e == null) Graphics.Add(e = new XElement(key));
        e.SetAttributeValue("value", value);
    }

    public void ApplyPreset(IReadOnlyDictionary<string, string> values)
    {
        foreach (var (k, v) in values) Set(k, v);
        // MSAA : GTA garde aussi le nombre d'échantillons dans MSAAFragments.
        if (values.TryGetValue("MSAA", out var msaa)) Set("MSAAFragments", msaa);
    }

    /// <summary>Choix affiché pour une valeur lue (le plus proche pour les nombres à virgule).</summary>
    public static string? MatchChoice(GtaOption option, string? value)
    {
        if (value == null) return null;
        var exact = option.Choices.FirstOrDefault(c => c.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (exact.Value != null) return exact.Value;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            && option.Choices.All(c => double.TryParse(c.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            return option.Choices.OrderBy(c => Math.Abs(double.Parse(c.Value, CultureInfo.InvariantCulture) - v)).First().Value;
        return null;
    }

    /// <summary>Nom du profil qui correspond exactement aux réglages actuels, sinon null.</summary>
    public string? CurrentPreset() =>
        Presets.FirstOrDefault(p => p.Values.All(kv => MatchChoice(Options.First(o => o.Key == kv.Key), Get(kv.Key)) == kv.Value)).Name;

    public override string ToString()
    {
        var sb = new StringWriter();
        _doc.Save(sb, SaveOptions.None);
        return sb.ToString();
    }

    public void Save(string file)
    {
        var settings = new System.Xml.XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) };
        using var w = System.Xml.XmlWriter.Create(file, settings);
        _doc.Save(w);
    }
}
