using System.Text;
using SubtitleEdit.Plugins.BrazilianToEuropeanPortuguese;
using SubtitleEdit.Plugins.Shared;
using Xunit.Abstractions;

namespace BrazilianToEuropeanPortuguese.Tests;

public sealed class OptInFactAttribute : FactAttribute
{
    public OptInFactAttribute(string variable, string reason)
    {
        if (Environment.GetEnvironmentVariable(variable) != "1") Skip = $"Opt-in ({variable}=1): {reason}";
    }
}

[Collection("XdgDataHome")]
public sealed class ReferenceTests(ITestOutputHelper output)
{
    private static readonly PortugueseConverter Converter = new(ConversionOptions.Default);

    private static SrtBlock Cue(long start, long end, string text) => new() { StartMs = start, EndMs = end, Text = text };

    [Theory]
    [InlineData("Ninguém leva as tuas coisas.", false, false)]
    [InlineData("I already sent him his stuff.", false, true)]
    [InlineData("This was his last message to us,", false, true)]
    [InlineData("Now, your brother Tiago,", true, false)]
    [InlineData("I sent her your things.", true, true)]
    public void PossessiveHints(string english, bool second, bool third)
    {
        var hint = ReferenceHint.FromText(english)!;
        Assert.Equal(second, hint.SecondPossessive);
        Assert.Equal(third, hint.ThirdPossessive);
        Assert.Equal(third && !second, hint.KeepsThirdPersonPossessive);
    }

    [Theory]
    [InlineData("Is he okay?", true)]
    [InlineData("They're home.", true)]
    [InlineData("Are you okay?", false)]
    [InlineData("You're right, he is.", false)]
    [InlineData("Okay?", false)]
    public void DroppedYouHint(string english, bool excluded) =>
        Assert.Equal(excluded, ReferenceHint.FromText(english)!.ExcludesDroppedYou);

    [Fact]
    public void NoReferenceTextGivesNoHint()
    {
        Assert.Null(ReferenceHint.FromText(null));
        Assert.Null(ReferenceHint.FromText("  "));
    }

    [Theory]
    [InlineData("DEMARCUS (groans): Yeah, I'm seeing double.", "Yeah, I'm seeing double.")]
    [InlineData("TERESA: There he is!\n- Hey!", "There he is! Hey!")]
    [InlineData("<i>WOMAN: * Start spreading the news</i>", "Start spreading the news")]
    [InlineData("[door closes] It's 10:30.", "It's 10:30.")]
    [InlineData("{\\an8}Mr. Banse", "Mr. Banse")]
    public void CleanRemovesTagsAndHearingImpairedText(string text, string expected) =>
        Assert.Equal(expected, ReferenceAligner.Clean(text));

    [Fact]
    public void DecodesUtf8Bom16AndWindows1252()
    {
        Assert.Equal("ação", ReferenceAligner.Decode(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("ação")).ToArray()));
        Assert.Equal("“his”", ReferenceAligner.Decode(new byte[] { 0x93, 0x68, 0x69, 0x73, 0x94 }));
        Assert.Equal("you", ReferenceAligner.Decode(new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("you")).ToArray()));
    }

    [Fact]
    public void LooksEnglishNeedsEnglishFunctionWords()
    {
        Assert.True(ReferenceAligner.LooksEnglish(new[] { Cue(0, 1, "What are you doing?"), Cue(1, 2, "Fine.") }));
        Assert.False(ReferenceAligner.LooksEnglish(new[] { Cue(0, 1, "¿Qué estás haciendo?"), Cue(1, 2, "Nada.") }));
    }

    [Fact]
    public void TimingJoinsOverlappingCuesAndSkipsGaps()
    {
        var source = new[] { Cue(1000, 4000, "Já temos digitais e sua foto."), Cue(10_000, 12_000, "Oi."), Cue(20_000, 21_000, "♪") };
        var reference = new[]
        {
            Cue(900, 2400, "So we got prints"), Cue(2500, 4100, "and his picture now."), Cue(30_000, 31_000, "Hi."),
        };
        var alignment = ReferenceAligner.AlignByTiming(source, reference);
        Assert.Equal("So we got prints\nand his picture now.", alignment.Texts[0]);
        Assert.Null(alignment.Texts[1]);
        Assert.Null(alignment.Texts[2]);
        Assert.Equal(2, alignment.Total);
        Assert.Equal(1, alignment.Matched);
    }

    [Fact]
    public void ShortOverlapAtCueBoundaryIsIgnored()
    {
        var alignment = ReferenceAligner.AlignByTiming(new[] { Cue(1000, 3000, "Ele sabe.") },
            new[] { Cue(0, 1100, "Your car."), Cue(1050, 3000, "He knows.") });
        Assert.Equal("He knows.", alignment.Texts[0]);
    }

    [Fact]
    public void FitTimeMapRecoversFrameRateAndOffsetDespiteOutliers()
    {
        var random = new Random(7);
        var matches = Enumerable.Range(0, 60).Select(i =>
        {
            var source = i * 40_000.0;
            var reference = source * 1.0427 + 4200 + random.Next(-300, 300);
            return (source, i % 6 == 0 ? reference + 25_000 : reference);
        }).ToList();
        var map = ReferenceAligner.FitTimeMap(matches)!;
        Assert.InRange(map.Scale, 1.040, 1.045);
        Assert.InRange(map.Map(1_000_000) - (1_000_000 * 1.0427 + 4200), -500, 500);
    }

    [Fact]
    public void FitTimeMapKeepsIdentityAndRejectsTooFew()
    {
        var aligned = Enumerable.Range(0, 30).Select(i => (i * 50_000.0, i * 50_000.0 + (i % 2 == 0 ? 120 : -90))).ToList();
        Assert.True(ReferenceAligner.FitTimeMap(aligned)!.IsIdentity);
        Assert.Null(ReferenceAligner.FitTimeMap(aligned.Take(5).ToList()));
    }

    [Fact]
    public void MutualMatchesRejectAmbiguousPairs()
    {
        float[] V(params float[] v) => v;
        var source = new[] { V(1, 0, 0), V(0, 1, 0), V(0, 0, 1) };
        var reference = new[] { V(1, 0, 0), V(0, 0.9f, 0.436f), V(0, 0.9f, 0.436f), V(0, 0, 1) };
        var matches = ReferenceAligner.MutualMatches(source, reference);
        Assert.Contains((0, 0, 1.0), matches.Select(m => (m.Source, m.Reference, Math.Round(m.Score, 3))));
        Assert.DoesNotContain(matches, m => m.Source == 1);
        Assert.Contains(matches, m => m.Source == 2 && m.Reference == 3);
    }

    /// <summary>A reference 4 s late and stretched, with one scene cut out, still pairs every line.</summary>
    [Fact]
    public void EmbeddingAlignmentFixesShiftStretchAndCut()
    {
        const int count = 120;
        var source = new List<SrtBlock>();
        var reference = new List<SrtBlock>();
        var sourceVectors = new List<float[]?>();
        var referenceVectors = new List<float[]?>();
        for (var i = 0; i < count; i++)
        {
            long start = i * 20_000L;
            source.Add(Cue(start, start + 2500, $"Linha portuguesa número {i} com bastante texto"));
            sourceVectors.Add(Basis(i, count));
            var cut = i >= 80 ? 7000 : 0;
            var mapped = (long)(start * 1.0427) + 4000 - cut;
            reference.Add(Cue(mapped, mapped + (long)(2500 * 1.0427), $"English line number {i} with plenty of text"));
            referenceVectors.Add(Basis(i, count));
        }

        var timingOnly = ReferenceAligner.AlignByTiming(source, reference);
        Assert.True(timingOnly.Coverage < 0.3);
        var alignment = ReferenceAligner.AlignWithEmbeddings(source, reference, sourceVectors, referenceVectors);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal($"English line number {i} with plenty of text", alignment.Texts[i]);
        }
        Assert.InRange(alignment.Map.Scale, 1.04, 1.045);
    }

    [Fact]
    public void EmbeddingAlignmentDropsPairsThatAreNotTranslations()
    {
        var source = new[] { Cue(0, 2000, "Bom dia a todos vocês aqui presentes"), Cue(5000, 7000, "Bom.") };
        var reference = new[] { Cue(0, 2000, "Good morning to all of you here"), Cue(5000, 7000, "I hate keeping secrets.") };
        var alignment = ReferenceAligner.AlignWithEmbeddings(source, reference,
            new[] { Basis(0, 3), Basis(1, 3) }, new[] { Basis(0, 3), Basis(2, 3) });
        Assert.Equal("Good morning to all of you here", alignment.Texts[0]);
        Assert.Null(alignment.Texts[1]);
        Assert.Equal(1, alignment.Rejected);
    }

    private static float[] Basis(int index, int size)
    {
        var vector = new float[size];
        vector[index] = 1;
        return vector;
    }

    [Theory]
    [InlineData("Carteira de motorista da Flórida.\nJá temos digitais e sua foto.", "so we got prints and his picture now.",
        "Carta de condução da Flórida.\nJá temos impressões digitais e a sua foto.",
        "Carta de condução da Flórida.\nJá temos impressões digitais e a tua foto.")]
    [InlineData("Esta foi sua última mensagem\npara nós.", "This was his last message to us,",
        "Esta foi sua última mensagem\npara nós.", "Esta foi tua última mensagem\npara nós.")]
    [InlineData("Já até mandei suas coisas.", "I already sent him his stuff.",
        "Até, já lhe mandei as suas coisas.", "Já até mandei as tuas coisas.")]
    [InlineData("Está bem?", "Is he okay?", "Está bem?", "Estás bem?")]
    public void ThirdPersonReferenceKeepsSeuAndThirdPersonVerbs(string text, string english, string withReference, string without)
    {
        Assert.Equal(without, Converter.Convert(text));
        Assert.Equal(withReference, Converter.Convert(text, null, ReferenceHint.FromText(english)));
    }

    /// <summary>English "your" cannot tell "tu" from formal "o seu marido": it must never force "teu".</summary>
    [Theory]
    [InlineData("Precisamos falar com\nseu marido. Ele está em casa?", "We need to talk to your husband. Is he home?")]
    [InlineData("Agora, seu irmão Tiago,", "Now, your brother Tiago,")]
    [InlineData("Mamãe tá brava com ele\npor quê ficou ao seu lado.", "Mom's mad at him because he took your side.")]
    [InlineData("Você está bem?", "Are you okay?")]
    [InlineData("Não fala assim.", "Don't talk like that.")]
    [InlineData("Se afaste!", "Step aside!")]
    public void SecondPersonOrNeutralReferenceChangesNothing(string text, string english) =>
        Assert.Equal(Converter.Convert(text), Converter.Convert(text, null, ReferenceHint.FromText(english)));

    [Fact]
    public void ParsesVulkanDevicesAndSkipsSoftwareRenderers()
    {
        var devices = EmbeddingRuntime.ParseDevices(
            "Available devices:\n  Vulkan0: AMD Radeon RX 560 Series (RADV POLARIS11) (4096 MiB, 2045 MiB free)\n  Vulkan1: llvmpipe (LLVM 17)\n");
        var device = Assert.Single(devices);
        Assert.Equal("Vulkan0", device.Id);
        Assert.StartsWith("AMD Radeon RX 560", device.Name);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("/etc/passwd")]
    [InlineData("a\\b")]
    public void RuntimeArchivePathsCannotEscape(string entry) =>
        Assert.Throws<InvalidDataException>(() => EmbeddingAssets.SafePath(Path.GetTempPath(), entry));

    [Fact]
    public void ChangeProposalShowsReferenceOnOneLine()
    {
        Assert.Equal("EN: his\nstuff".Replace('\n', ' '), new ChangeProposal(0, "a", "b", "his\nstuff").ReferenceText);
        Assert.False(new ChangeProposal(0, "a", "b").HasReference);
    }

    // Real assets downloaded by the plugin, or PTPT_EMBEDDING_DIR with llama-server and the GGUF.
    private static EmbeddingSettings RealSettings(bool gpu)
    {
        var folder = Environment.GetEnvironmentVariable("PTPT_EMBEDDING_DIR") ?? EmbeddingAssets.DirectoryPath;
        var runtime = Directory.GetFiles(folder, "llama-server", SearchOption.AllDirectories).First();
        return new EmbeddingSettings(runtime, Path.Combine(folder, EmbeddingAssets.ModelFile), true, gpu,
            Environment.GetEnvironmentVariable("PTPT_VULKAN_DEVICE") ?? "Vulkan0");
    }

    private static (List<SrtBlock> Source, List<SrtBlock> Reference) RealPair()
    {
        // PTPT_SOURCE_SRT and PTPT_ENGLISH_SRT: a real PT-BR subtitle and the English subtitle of the same release.
        var source = Environment.GetEnvironmentVariable("PTPT_SOURCE_SRT")
                     ?? throw new InvalidOperationException("Set PTPT_SOURCE_SRT to a PT-BR .srt file.");
        var english = Environment.GetEnvironmentVariable("PTPT_ENGLISH_SRT")
                      ?? throw new InvalidOperationException("Set PTPT_ENGLISH_SRT to the English .srt of the same release.");
        return (ReferenceAligner.Load(source), ReferenceAligner.Load(english));
    }

    [OptInFact("PTPT_TEST_EMBEDDING", "real MiniLM on the CPU")]
    public Task RealEmbeddingAlignmentOnCpu() => RealEmbeddingAlignment(false);

    [OptInFact("PTPT_TEST_VULKAN", "real MiniLM on a Vulkan GPU")]
    public Task RealEmbeddingAlignmentOnVulkan() => RealEmbeddingAlignment(true);

    private async Task RealEmbeddingAlignment(bool gpu)
    {
        var (source, reference) = RealPair();
        var shifted = reference.Select(r => Cue((long)(r.StartMs * 25.0 / 23.976) + 4200, (long)(r.EndMs * 25.0 / 23.976) + 4200, r.Text)).ToList();
        await using var runtime = await EmbeddingRuntime.StartAsync(RealSettings(gpu), new Progress<string>(output.WriteLine), default);
        Assert.Equal(gpu, runtime.UsesGpu);
        var sourceVectors = await runtime.EmbedCuesAsync(source.Select(b => b.Text).ToList(), new Progress<string>(_ => { }), "pt", default);
        var referenceVectors = await runtime.EmbedCuesAsync(reference.Select(b => b.Text).ToList(), new Progress<string>(_ => { }), "en", default);

        var alignment = ReferenceAligner.AlignWithEmbeddings(source, reference, sourceVectors, referenceVectors);
        var moved = ReferenceAligner.AlignWithEmbeddings(source, shifted, sourceVectors, referenceVectors);
        output.WriteLine(alignment.Describe());
        output.WriteLine(moved.Describe());
        Assert.True(alignment.Coverage > 0.95);
        Assert.True(moved.Coverage > 0.95);
        Assert.True(ReferenceAligner.AlignByTiming(source, shifted).Coverage < 0.9);
        var same = Enumerable.Range(0, source.Count).Count(i => alignment.Texts[i] == moved.Texts[i]);
        Assert.True(same > source.Count * 0.93, $"{same} of {source.Count} identical");
        // Cues the user reported; 739 drifts in the original files and needs the local correction.
        Assert.Contains("his picture", alignment.Texts[331]);
        Assert.Contains("his last message", alignment.Texts[726]);
        Assert.Contains("Anybody find anything yet?", alignment.Texts[738]);
        Assert.Contains("your father", alignment.Texts[818]);
    }

    [OptInFact("PTPT_TEST_UI", "native Avalonia window (run under xvfb-run)")]
    public void MainWindowAppliesReferenceToProposals()
    {
        var data = Path.Combine(Path.GetTempPath(), "ptpt-ui-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", data);
        try
        {
            var english = Path.Combine(data, "episode.en.srt");
            Directory.CreateDirectory(data);
            File.WriteAllText(english, "1\n00:00:01,000 --> 00:00:04,000\nSo we got prints and his picture now.\n\n" +
                                       "2\n00:00:05,000 --> 00:00:07,000\nAre you okay?\n\n" +
                                       "3\n00:00:08,000 --> 00:00:10,000\nWe need to talk to your husband. Is he home?\n");
            var subtitle = Path.Combine(data, "episode.pt-br.srt");
            File.WriteAllText(subtitle, "");
            PluginBootstrap.BuildAvaloniaApp<App>().SetupWithoutStarting();
            var window = new MainWindow(new PluginRequest
            {
                Subtitle = new PluginSubtitle
                {
                    Format = "SubRip",
                    FileName = subtitle,
                    SubRip = "1\n00:00:01,000 --> 00:00:04,000\nJá temos digitais e sua foto.\n\n2\n00:00:05,000 --> 00:00:07,000\nVocê está bem?\n\n" +
                             "3\n00:00:08,000 --> 00:00:10,000\nPrecisamos falar com seu marido. Ele está em casa?\n",
                },
            });
            Assert.Contains("a tua foto", window.Proposals[0].ConvertedText);
            Assert.Contains("MiniLM is not installed", window.ReferenceStatusText);
            Assert.Equal("Model: not downloaded\nllama-server: not downloaded\nGervásio: not downloaded", window.AssetStatusText);
            window.FindReferenceAutomatically().GetAwaiter().GetResult();
            Assert.Equal(3, window.Alignment!.Matched);
            Assert.Contains("a sua foto", window.Proposals[0].ConvertedText);
            Assert.Equal("EN: So we got prints and his picture now.", window.Proposals[0].ReferenceText);
            Assert.Equal("EN: Are you okay?", window.Proposals[1].ReferenceText);
            var review = window.Proposals.Single(p => p.LineIndex == 2);
            Assert.True(review.IsChange);
            Assert.Contains("o seu marido", review.ConvertedText);
            Assert.Contains("keeps seu/sua", review.ReviewNote);
            Assert.StartsWith("Reference matched 3 of 3", window.ReferenceStatusText);
            Assert.Contains("1 line(s) marked for review", window.ReferenceStatusText);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            Directory.Delete(data, true);
        }
    }

    [Theory]
    [InlineData("Show.S01E01.pt-br.srt", "Show.S01E01.en.srt")]
    [InlineData("Show.S01E01.pt-br.srt", "Show.S01E01.English.srt")]
    [InlineData("Show S01E01 Title.Portuguese.srt", "Show S01E01 Title.eng.srt")]
    [InlineData("Movie.2010.srt", "Movie.2010.en.sdh.srt")]
    [InlineData("Movie.2010.pt.srt", "Movie_2010.[eng].srt")]
    [InlineData("Some.Show.S01E01.Pilot.720p.WEB.DL.DD5.1.H.264-GRP.pt-br.srt", "Some Show.S01E01.Pilot.English.srt")]
    public void FindsEnglishReferenceNextToSubtitle(string subtitle, string english)
    {
        using var folder = new TempFolder(subtitle, english, "Other.S01E02.en.srt", "Show.S01E01.pt-pt.srt");
        Assert.Equal(folder.Path(english), ReferenceFinder.Find(folder.Path(subtitle), null));
    }

    [Fact]
    public void FindsReferenceNamedAfterTheVideo()
    {
        using var folder = new TempFolder("Movie.mkv", "Movie.en.srt", "legendas.srt");
        Assert.Equal(folder.Path("Movie.en.srt"), ReferenceFinder.Find(folder.Path("legendas.srt"), folder.Path("Movie.mkv")));
    }

    [Theory]
    [InlineData("Show.S01E01.pt-br.srt", "Show.S01E01.pt-pt.srt", "Show.S01E01.srt")]
    [InlineData("Show.S01E01.720p.pt-br.srt", "Show.S01E01.Pilot.en.srt", "Show.S01E01.Pilot.English.forced.srt")]
    [InlineData("Show.S01E01.pt-br.srt", "Show.S01E02.en.srt", "Show2.S01E01.en.srt")]
    public void NoReferenceWithoutAnUnambiguousEnglishMatch(string subtitle, string other, string third)
    {
        using var folder = new TempFolder(subtitle, other, third);
        Assert.Null(ReferenceFinder.Find(folder.Path(subtitle), null));
        Assert.Null(ReferenceFinder.Find(null, null));
        Assert.Null(ReferenceFinder.Find(folder.Path("missing/Show.S01E01.pt-br.srt"), null));
    }

    [Theory]
    [InlineData("Você apaixona-se por um advogado", "You fall for a Harvard lawyer", "keeps você")]
    [InlineData("Ouvi dizer que o seu miúdo é o detetive chefe?", "I hear your boy is lead detective?", "keeps seu/sua")]
    [InlineData("Você viu a sua mãe?", "Did you see your mom?", "keeps você and seu/sua")]
    public void ReviewNotesForYouKeptAsVoceOrSeu(string text, string english, string expected)
    {
        var note = ReferenceReview.Note(text, ReferenceHint.FromText(english));
        Assert.Contains(expected, note);
    }

    [Theory]
    [InlineData("O Estado solicita mais tempo, Sua Excelência.", "The State pleads for more time, Your Honor.")]
    [InlineData("O senhor viu a sua mãe?", "Did you see your mom?")]
    [InlineData("Ele viu a sua mãe?", "Did he see his mom?")]
    [InlineData("Vocês vêm?", "Are you coming?")]
    [InlineData("Viste a tua mãe?", "Did you see your mom?")]
    [InlineData("Ele viu a sua mãe.", "He saw your mom and his dad.")]
    public void NoReviewForFormalThirdPersonOrAlreadyTu(string text, string english) =>
        Assert.Null(ReferenceReview.Note(text, ReferenceHint.FromText(english)));

    [Theory]
    [InlineData("Arrisquei o pescoço para conseguir prender esse tipo.", "I busted my hump trying to lock that guy up.", "dar cabo das costas")]
    [InlineData("Pare de quebrar minhas bolas.", "Stop busting my chops.", "chatear")]
    [InlineData("Isso é um pedaço de bolo.", "That's a piece of cake.", "canja")]
    [InlineData("Ele chutou o balde.", "He kicked the bucket.", "bater a bota")]
    [InlineData("Está a puxar a minha perna?", "Are you pulling my leg?", "a gozar")]
    public void IdiomHintsFromTheEnglish(string text, string english, string expected)
    {
        var note = IdiomHints.Note(text, english);
        Assert.NotNull(note);
        Assert.StartsWith("Hint:", note);
        Assert.Contains(expected, note);
    }

    [Theory]
    [InlineData("Dei cabo das costas para prender aquele tipo.", "I busted my hump trying to lock that guy up.")]
    [InlineData("Isso é canja.", "That's a piece of cake.")]
    [InlineData("Estás a gozar comigo?", "Are you pulling my leg?")]
    [InlineData("Quebrar o gelo.", "Let's break the ice.")]
    [InlineData("Vamos prendê-lo.", "Let's lock him up.")]
    [InlineData("Aposto que sim.", "You bet on the horse?")]
    [InlineData("Qualquer coisa.", null)]
    [InlineData("Sem acordo.", "If this goes down, there's no deal for you.")]
    public void NoIdiomHintWhenTranslatedOrNoIdiom(string text, string? english) =>
        Assert.Null(IdiomHints.Note(text, english));

    [Fact]
    public void ReviewOnlyLinesCannotBeSelected()
    {
        var review = new ChangeProposal(3, "Você primeiro.", "Você primeiro.", "You first.", "Review");
        Assert.False(review.IsChange);
        Assert.False(review.Include);
        review.Include = true;
        Assert.False(review.Include);
        Assert.True(new ChangeProposal(0, "a", "b").Include);
    }

    [Fact]
    public void SettingsNeverStoreOrUseFilesOutsideThePluginFolder()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new EmbeddingSettings("/x/llama-server", "/x/m.gguf", true, true, "Vulkan1"));
        Assert.DoesNotContain("/x/", json);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<EmbeddingSettings>(
            "{\"RuntimePath\":\"/opt/llama/llama-server\",\"UseEmbeddings\":true,\"Vulkan\":true,\"Device\":\"Vulkan1\"}")!;
        Assert.Equal((true, true, "Vulkan1", ""), (loaded.UseEmbeddings, loaded.Vulkan, loaded.Device, loaded.RuntimePath));
        var resolved = EmbeddingAssets.WithInstalledFiles(new EmbeddingSettings("/elsewhere/llama-server", "/elsewhere/m.gguf"));
        Assert.DoesNotContain("/elsewhere/", resolved.RuntimePath + resolved.ModelPath);
    }

    private sealed class TempFolder : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ptpt-find-" + Guid.NewGuid().ToString("N"));

        public TempFolder(params string[] files)
        {
            Directory.CreateDirectory(_root);
            foreach (var file in files) File.WriteAllText(Path(file), "");
        }

        public string Path(string name) => System.IO.Path.Combine(_root, name);
        public void Dispose() => Directory.Delete(_root, true);
    }
}

public sealed class FamilyPossessiveArticleTests
{
    private static readonly PortugueseConverter Converter = new(ConversionOptions.Default);

    [Theory]
    [InlineData("Entendemos que sua filha\ndesapareceu.", "Percebemos que a sua filha\ndesapareceu.")]
    [InlineData("Ouvi dizer que seu filho é detetive.", "Ouvi dizer que o seu filho é detetive.")]
    [InlineData("Disse que sua mãe ligou.", "Disse que a tua mãe ligou.")]
    [InlineData("Espero que seu irmão venha.", "Espero que o teu irmão venha.")]
    [InlineData("Antes que sua mãe chegue.", "Antes que a tua mãe chegue.")]
    [InlineData("Porque meu irmão disse.", "Porque o meu irmão disse.")]
    [InlineData("Até que meu pai\nvoltou.", "Até que o meu pai\nvoltou.")]
    public void ArticleBeforeFamilyNounAfterConjunction(string text, string expected) =>
        Assert.Equal(expected, Converter.Convert(text));

    [Theory]
    [InlineData("Mas meu filho, não podes.")]
    [InlineData("E meu amor!")]
    [InlineData("Sei que, minha filha, é difícil.")]
    [InlineData("E meu querido.")]
    public void AddressIsNotGivenAnArticle(string text) =>
        Assert.Equal(text, Converter.Convert(text));

    [Fact]
    public void ThirdPersonCueKeepsSuaWithArticle() =>
        Assert.Equal("Percebemos que a sua filha\ndesapareceu.",
            Converter.Convert("Entendemos que sua filha\ndesapareceu.", null,
                ReferenceHint.FromText("We understand your daughter went missing.")));
}

public sealed class YoungPersonTests
{
    private static readonly PortugueseConverter Converter = new(ConversionOptions.Default);

    [Theory]
    [InlineData("Ouvi dizer que o seu garoto\né o detetive chefe?", "I hear your boy is lead detective?", "Ouvi dizer que o seu rapaz\né o detetive chefe?")]
    [InlineData("O garoto é bom.", "The boy is good.", "O rapaz é bom.")]
    [InlineData("Esses garotos são polícias.", "Those guys are cops.", "Esses rapazes são polícias.")]
    [InlineData("A garota da loja ligou.", "The girl from the shop called.", "A rapariga da loja ligou.")]
    [InlineData("O garoto tem cinco anos.", "The kid is five.", "O miúdo tem cinco anos.")]
    [InlineData("O garoto chorou.", "The little boy cried.", "O miúdo chorou.")]
    [InlineData("O garoto chorou.", "He cried.", "O miúdo chorou.")]
    public void EnglishReferenceChoosesRapazOrMiudo(string text, string english, string expected) =>
        Assert.Equal(expected, Converter.Convert(text, null, ReferenceHint.FromText(english)));

    [Theory]
    [InlineData("Ouvi dizer que o seu garoto\né o detetive chefe?", "Ouvi dizer que o seu rapaz\né o detetive chefe?")]
    [InlineData("Meu garoto!", "O meu rapaz!")]
    [InlineData("O garoto chorou.", "O miúdo chorou.")]
    [InlineData("A minha garota.", "A minha miúda.")]
    [InlineData("Os garotos brincam.", "Os miúdos brincam.")]
    public void WithoutReferencePossessiveMakesGarotoRapaz(string text, string expected) =>
        Assert.Equal(expected, Converter.Convert(text));

    [Fact]
    public void LocalWordListStillWins()
    {
        var list = new PortugueseLocalWordList();
        list.Words.Add(("garoto", "puto"));
        list.Ignored.Add("garota");
        var converter = new PortugueseConverter(ConversionOptions.Default, list);
        var hint = ReferenceHint.FromText("The boy and the girl.");
        Assert.Equal("O puto e a garota.", converter.Convert("O garoto e a garota.", null, hint));
    }
}

public sealed class FutureSubjunctiveCliticTests
{
    private static readonly PortugueseConverter Converter = new(ConversionOptions.Default);

    [Theory]
    [InlineData("Tudo termina mais rápido\nse você pegá-lo.", "Tudo termina mais rápido\nse o apanhares.")]
    [InlineData("Se você vê-la, me liga.", "Se a vires, liga-me.")]
    [InlineData("Se o senhor vê-la, avise.", "Se o senhor a vir, avise.")]
    [InlineData("Quando ele fazê-lo, avise.", "Quando ele o fizer, avise.")]
    [InlineData("Assim que vocês encontrá-los, voltem.", "Assim que vocês os encontrarem, voltem.")]
    [InlineData("Se pegá-lo, ligue.", "Se o apanhar, ligue.")]
    [InlineData("Mas se dizê-lo, acabou.", "Mas se o disser, acabou.")]
    [InlineData("Não sei se fazê-lo.", "Não sei se fazê-lo.")]
    [InlineData("Sei quando apanhá-lo.", "Sei quando apanhá-lo.")]
    [InlineData("Como encontrá-lo?", "Como encontrá-lo?")]
    public void PronounMovesBeforeFutureSubjunctive(string text, string expected) =>
        Assert.Equal(expected, Converter.Convert(text, null, ReferenceHint.FromText("If you see him.")));
}

public sealed class NearDemonstrativeTests
{
    private static readonly PortugueseConverter Converter = new(ConversionOptions.Default);

    [Theory]
    [InlineData("- Amém.\n- Eu cuido disso.", "Amen. / I got this one.", "- Amém.\n- Eu cuido disto.")]
    [InlineData("Olhe essa foto.", "Look at this picture.", "Olha esta foto.")]
    [InlineData("Nesses casos, ligue.", "In these cases, call.", "Nestes casos, ligue.")]
    [InlineData("Eu cuido disso.", "I'll take care of that.", "Eu cuido disso.")]
    [InlineData("Eu cuido disso.", "I'll handle it.", "Eu cuido disso.")]
    [InlineData("Por isso essa foto.", "That's why this picture.", "Por isso essa foto.")]
    [InlineData("Por isso vim.", "This is why I came.", "Por isso vim.")]
    [InlineData("Se ele desse isso...", "If he gave this...", "Se ele desse isto...")]
    [InlineData("-Pode ser essa?\n-Claro.", "- This one? / - Sure.", "-Pode ser esta?\n-Claro.")]
    public void EnglishThisMakesIsto(string text, string english, string expected) =>
        Assert.Equal(expected, Converter.Convert(text, null, ReferenceHint.FromText(english)));

    [Fact]
    public void WithoutReferenceIssoStays() => Assert.Equal("Eu cuido disso.", Converter.Convert("Eu cuido disso."));
}

public sealed class ImpersonalQuestionTests
{
    private static readonly PortugueseConverter Converter = new(ConversionOptions.Default);

    [Theory]
    [InlineData("Pode ser essa?", "Pode ser essa?")]
    [InlineData("Pode haver mais?", "Pode haver mais?")]
    [InlineData("Pode me ajudar?", "Podes ajudar-me?")]
    public void PodeSerHasNoYou(string text, string expected) => Assert.Equal(expected, Converter.Convert(text));
}

public sealed class EmContractionTests
{
    private static readonly PortugueseConverter Converter = new(ConversionOptions.Default);

    [Theory]
    [InlineData("se eu fizer com que Becky testemunhe\nem um novo julgamento.", "se eu fizer com que Becky testemunhe\nnum novo julgamento.")]
    [InlineData("Em um avião para Scottsdale.", "Num avião para Scottsdale.")]
    [InlineData("Ela confiou em uma amostra.", "Ela confiou numa amostra.")]
    [InlineData("O livro está em outro lugar.", "O livro está noutro lugar.")]
    [InlineData("Pensa em outra pessoa.", "Pensa noutra pessoa.")]
    [InlineData("Viste em algum sítio?", "Viste nalgum sítio?")]
    [InlineData("Em umas horas.", "Numas horas.")]
    [InlineData("ESTÁ EM UM CARRO.", "ESTÁ NUM CARRO.")]
    [InlineData("Isto transformou-se em\num barril de pólvora.", "Isto transformou-se\nnum barril de pólvora.")]
    [InlineData("Ele tem um carro.", "Ele tem um carro.")]
    [InlineData("Item um.", "Item um.")]
    public void EmContractsWithIndefinites(string text, string expected) => Assert.Equal(expected, Converter.Convert(text));
}
