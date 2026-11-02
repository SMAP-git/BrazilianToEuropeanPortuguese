using System.Text.Json;
using SubtitleEdit.Plugins.BrazilianToEuropeanPortuguese;
using Xunit;

namespace BrazilianToEuropeanPortuguese.Tests;

// ReferenceTests changes XDG_DATA_HOME for the process; do not run beside it.
[Collection("XdgDataHome")]
public sealed class LocalChecksTests
{
    private static string[] Texts(string original, string converted) =>
        JudgeAlternatives.Generate(original, converted).Select(c => c.Text).ToArray();

    [Fact]
    public void PluralSubjectOffersTem()
    {
        Assert.Contains("Os adultos têm muitas coisas.", Texts("Os adultos tem muitas coisas.", "Os adultos tem muitas coisas."));
    }

    [Theory]
    [InlineData("Qual é, tem de nos dar algo mais.")]
    [InlineData("Mas tem de vir.")]
    [InlineData("Ele tem razão. Eles sabem.")]
    public void NoPluralSubjectOffersNoTem(string text) => Assert.DoesNotContain(Texts(text, text), t => t.Contains("têm"));

    [Fact]
    public void PluralVerbAlwaysOffersSingular()
    {
        Assert.Contains("Dar permissão para abrir os armários tem implicações.",
            Texts("Dar permissão para abrir os armários tem implicações.", "Dar permissão para abrir os armários têm implicações."));
    }

    [Fact]
    public void RapazOnlyForGarotoAndKeepsCase()
    {
        Assert.Contains("Rapaz, que susto.", Texts("Garoto, que susto.", "Miúdo, que susto."));
        Assert.Empty(Texts("Criança, que susto.", "Miúdo, que susto."));
    }

    [Fact]
    public void TiraOffersPoliciaAndMauOffersMa()
    {
        Assert.Contains("Ele é um polícia.", Texts("Ele é um tira.", "Ele é um tira."));
        Assert.Contains("Tem coisas realmente más.", Texts("Tem coisas realmente ruins.", "Tem coisas realmente maus."));
    }

    [Fact]
    public void ScoringTextHasNoTagsOrLineBreaks() =>
        Assert.Equal("Olá, Ann. Tudo bem?", LocalChecks.ForScoring("<i>Olá, Ann.</i>\r\n{\\an8}Tudo bem?"));

    [Fact]
    public void TokenGrammarForcesTokenIds() =>
        Assert.Equal("root ::= <[12]> <[345]>", EmbeddingRuntime.TokenGrammar(new[] { 12, 345 }));

    [Fact]
    public void CheckSettingsAreSavedButPathsAreNot()
    {
        var json = JsonSerializer.Serialize(new EmbeddingSettings(JudgeModelPath: "/x/judge.gguf", CheckMeaning: false, UseJudge: true));
        var back = JsonSerializer.Deserialize<EmbeddingSettings>(json)!;
        Assert.False(back.CheckMeaning);
        Assert.True(back.UseJudge);
        Assert.DoesNotContain("judge.gguf", json);
    }

    [Fact]
    public async Task NothingRunsWithoutModels()
    {
        var settings = new EmbeddingSettings(UseJudge: false, CheckMeaning: false);
        var lines = new[] { new CheckLine(0, "Eles tem razão.", "Eles tem razão.", null, null) };
        var (results, summary) = await LocalChecks.RunAsync(lines, settings, new Progress<string>(_ => { }), CancellationToken.None);
        Assert.Empty(results);
        Assert.Equal(string.Empty, summary);
    }

    [OptInFact("PTPT_TEST_JUDGE", "needs the downloaded Gervásio model and llama-server")]
    public async Task GervasioPrefersAgreement()
    {
        var settings = EmbeddingAssets.WithInstalledFiles(EmbeddingAssets.Load() with { UseJudge = true });
        await using var runtime = await EmbeddingRuntime.StartJudgeAsync(settings, new Progress<string>(_ => { }), CancellationToken.None);
        const string context = "Legendas de uma série, em português europeu (de Portugal):\n";
        Assert.True(await runtime.ScoreAsync(context, "Ela tem dois filhos.", CancellationToken.None)
            > await runtime.ScoreAsync(context, "Ela têm dois filhos.", CancellationToken.None));
        var lines = new[]
        {
            new CheckLine(0, "Ouvi dizer que o seu garoto é o detetive chefe?", "Ouvi dizer que o seu miúdo é o detetive chefe?", null, null),
            new CheckLine(1, "Ele tira o casaco.", "Ele tira o casaco.", null, null),
        };
        var (results, _) = await LocalChecks.RunAsync(lines, settings with { CheckMeaning = false }, new Progress<string>(_ => { }), CancellationToken.None);
        Assert.Equal("Ouvi dizer que o seu rapaz é o detetive chefe?", results[0].Text);
        Assert.False(results.ContainsKey(1));
    }
}
