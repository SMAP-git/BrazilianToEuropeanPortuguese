using SubtitleEdit.Plugins.Shared;
using System.Text.Json;

namespace SubtitleEdit.Plugins.BrazilianToEuropeanPortuguese;

/// <summary>Which rule groups run. Remembered by Subtitle Edit (window and Batch convert).</summary>
public sealed record ConversionOptions(
    bool Spelling = true,
    bool Vocabulary = true,
    bool Progressive = true,
    bool Pronouns = true,
    bool Colloquial = true,
    bool YouToTu = true,
    bool PostAgreement = true,
    bool ValidateSpelling = false,
    bool UseReference = true)
{
    public const int Version = 1;

    public static ConversionOptions Default => new();

    public JsonElement ToJson() => JsonSerializer.SerializeToElement(new
    {
        spelling = Spelling,
        vocabulary = Vocabulary,
        progressive = Progressive,
        pronouns = Pronouns,
        colloquial = Colloquial,
        youToTu = YouToTu,
        postAgreement = PostAgreement,
        validateSpelling = ValidateSpelling,
        useReference = UseReference,
    });

    public static ConversionOptions Load(PluginRequest request)
    {
        var settings = request.Settings;
        if (request.SettingsVersion != Version || settings is not { ValueKind: JsonValueKind.Object })
        {
            return Default;
        }

        var json = settings.Value;
        return new ConversionOptions(
            Read(json, "spelling"),
            Read(json, "vocabulary"),
            Read(json, "progressive"),
            Read(json, "pronouns"),
            Read(json, "colloquial"),
            Read(json, "youToTu"),
            Read(json, "postAgreement"),
            json.TryGetProperty("validateSpelling", out var validation) && validation.ValueKind == JsonValueKind.True,
            Read(json, "useReference"));
    }

    private static bool Read(JsonElement json, string name) =>
        !json.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.False;
}
