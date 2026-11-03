using SubtitleEdit.Plugins.BrazilianToEuropeanPortuguese;

namespace BrazilianToEuropeanPortuguese.Tests;

public sealed class ClothingTests
{
    private static readonly PortugueseConverter Converter = new();

    [Theory]
    [InlineData("a calcinha", "as cuecas")]
    [InlineData("a cueca", "as cuecas")]
    [InlineData("na minha cueca", "nas minhas cuecas")]
    [InlineData("uma cueca limpa", "umas cuecas limpas")]
    [InlineData("Arrancou a calcinha.", "Arrancou as cuecas.")]
    [InlineData("A sua calcinha branca.", "As tuas cuecas brancas.")]
    [InlineData("As cuecas estão limpas.", "As cuecas estão limpas.")]
    [InlineData("Cueca boxer.", "Boxers.")]
    [InlineData("A cueca está limpa.", "As cuecas estão limpas.")]
    [InlineData("A cueca estava suja.", "As cuecas estavam sujas.")]
    [InlineData("A cueca é branca.", "As cuecas são brancas.")]
    [InlineData("A cueca ficou molhada.", "As cuecas ficaram molhadas.")]
    public void UnderwearUsesPluralWithAgreement(string source, string expected) => Converts(source, expected);

    [Theory]
    [InlineData("Ele jogou ela no chão.", "Ele mandou-a para o chão.")]
    [InlineData("Ela jogou ele no chão.", "Ela mandou-o para o chão.")]
    [InlineData("Eles jogaram elas no chão.", "Eles mandaram-nas para o chão.")]
    [InlineData("Não jogou ela no chão.", "Não a mandou para o chão.")]
    [InlineData("Tirou a camisa dela.", "Tirou-lhe a camisa.")]
    [InlineData("Arrancou o casaco dele.", "Arrancou-lhe o casaco.")]
    [InlineData("Tirou os sapatos deles.", "Tirou-lhes os sapatos.")]
    [InlineData("Tirou minha camisa.", "Tirou-me a camisa.")]
    [InlineData("Não tirou a camisa dela.", "Não lhe tirou a camisa.")]
    public void PersonAndExplicitClothingOwner(string source, string expected) => Converts(source, expected);

    [Fact]
    public void EpisodeSentenceUsesReferenceConfirmedOwner()
    {
        var reference = ReferenceHint.FromText("She was on her way home from the library. Next thing she knows, perp's got her on the ground, yanks her jeans, rips her underwear.");
        const string source = "Depois, ele a jogou no chão tirou\nseu jeans e arrancou a calcinha.";
        const string expected = "Depois, ele mandou-a para o chão, tirou-lhe\nos jeans e arrancou-lhe as cuecas.";
        Assert.Equal(expected, Converter.Convert(source, null, reference));
        Assert.Equal(expected, Converter.Convert(expected, null, reference));
    }

    [Theory]
    [InlineData("Tirou o seu casaco.")]
    [InlineData("Ele mandou-a para o chão.")]
    [InlineData("Tirou-lhe a camisa.")]
    [InlineData("Arrancou a porta.")]
    [InlineData("Ele jogou-o no chão.")]
    public void AmbiguousOwnersAndValidOutputsStayUnchanged(string source)
    {
        var formal = new ReferenceHint(false, false, true, true) { FormalAddress = true };
        Assert.Equal(source, Converter.Convert(source, null, formal));
    }

    [Fact]
    public void ThrowsObjectsWithoutChangingTheirMeaning()
    {
        Assert.Equal("Ele jogou a bola no chão.", Converter.Convert("Ele jogou a bola no chão."));
    }

    [Fact]
    public void VocabularyAndPronounGroupsRemainIndependent()
    {
        Assert.Equal("Arrancou a calcinha.", new PortugueseConverter(new ConversionOptions(Vocabulary: false)).Convert("Arrancou a calcinha."));
        Assert.Equal("Tirou as cuecas dela.", new PortugueseConverter(new ConversionOptions(Pronouns: false)).Convert("Tirou a calcinha dela."));
    }

    private static void Converts(string source, string expected)
    {
        Assert.Equal(expected, Converter.Convert(source));
        Assert.Equal(expected, Converter.Convert(expected));
    }
}
