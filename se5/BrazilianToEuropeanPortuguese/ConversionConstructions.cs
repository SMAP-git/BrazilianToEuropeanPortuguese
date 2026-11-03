using System.Text.RegularExpressions;

namespace SubtitleEdit.Plugins.BrazilianToEuropeanPortuguese;

/// <summary>Complement-aware constructions; ambiguous senses are not blanket word replacements.</summary>
internal static class ConversionConstructions
{
    private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Before = @"(?<![\p{L}\p{M}-])";
    private const string After = @"(?![\p{L}\p{M}-])";
    private const string Adverbs = @"(?:(?:não|nunca|já|ainda|sempre|também|só|realmente|talvez)\s+){0,3}";
    private static string Alternatives(IEnumerable<string> words) =>
        string.Join("|", words.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(w => w.Length).Select(Regex.Escape));

    private static string[] Forms(string infinitive)
    {
        if (!VerbConjugator.TryConjugate(infinitive, out var forms))
            throw new InvalidOperationException($"Unsupported regular construction verb: {infinitive}");
        return forms;
    }

    private static string FinitePattern(string infinitive) =>
        Alternatives(Forms(infinitive).Where((_, index) => index is not (34 or 35 or 36 or 37 or 38)));

    private static readonly Regex RememberClause = new(
        Before + @"(?<v>" + FinitePattern("lembrar") + @")(?:-(?<cl>me|te|se|nos|vos))?(?<space>\s+)" +
        @"(?<what>o\s+que|a\s+que|os\s+que|as\s+que|aquilo\s+que|isso|isto|aquilo)" + After, Ci);
    private static readonly Regex DatePerson = new(
        Before + @"(?<v>" + Alternatives(Forms("namorar").Where((_, i) => i is not (35 or 36 or 37 or 38))) + @")" +
        @"(?<space>\s+)(?<p>ele|ela|eles|elas|você|vocês|tu|mim|ti)" + After, Ci);
    private static readonly Regex DirectRememberName = new(
        Before + @"(?<v>" + FinitePattern("lembrar") + @")\s+(?<p>o|a)\s+(?=(?:nome|endereço|morada|número|data|hora|senha|código)\b)", Ci);

    public static string Government(string text)
    {
        text = RememberClause.Replace(text, m =>
            m.Groups["v"].Value + (m.Groups["cl"].Success ? "-" + m.Groups["cl"].Value : "") +
            m.Groups["space"].Value + ContractDe(m.Groups["what"].Value));
        text = DirectRememberName.Replace(text, m => m.Groups["v"].Value + " " +
            CaseHelper.MatchCase(m.Groups["p"].Value, m.Groups["p"].Value.Equals("o", StringComparison.OrdinalIgnoreCase) ? "do" : "da") + " ");
        return DatePerson.Replace(text, m =>
        {
            var p = m.Groups["p"].Value;
            var target = p.ToLowerInvariant() switch { "mim" => "comigo", "ti" or "tu" => "contigo", _ => "com " + p };
            return m.Groups["v"].Value + m.Groups["space"].Value + target;
        });
    }

    private static string ContractDe(string phrase) => phrase.ToLowerInvariant() switch
    {
        "o que" => CaseHelper.MatchCase(phrase, "do que"),
        "a que" => CaseHelper.MatchCase(phrase, "da que"),
        "os que" => CaseHelper.MatchCase(phrase, "dos que"),
        "as que" => CaseHelper.MatchCase(phrase, "das que"),
        "isso" => CaseHelper.MatchCase(phrase, "disso"),
        "isto" => CaseHelper.MatchCase(phrase, "disto"),
        "aquilo" => CaseHelper.MatchCase(phrase, "daquilo"),
        _ => "d" + phrase,
    };

    private static readonly Regex Personal = new(
        Before + @"(?<pre>antes\s+de|depois\s+de|apesar\s+de|em\s+vez\s+de|para|por|sem|até|após|ao|" +
        @"é\s+(?:melhor|pior|importante|preciso|necessário|bom|difícil|fácil))\s+" +
        @"(?<subject>você|tu|vocês|nós|eles|elas)\s+(?<adv>" + Adverbs + @")(?:(?<cl>me|te|se|nos|vos|lhe|lhes)\s+)?" +
        @"(?<inf>\p{L}+(?:ar|er|ir)|pôr)" + After, Ci);
    private static readonly Regex PersonalContinuation = new(
        Before + @"(?<head>(?:antes\s+de|depois\s+de|sem|para)\s+(?:você|tu)\s+" + Adverbs +
        @"(?:(?:me|te|se|nos|vos)\s+)?(?<inf>\p{L}+(?:ar|er|ir)|pôr)" + After + @",\s+)" +
        @"(?<neg>não\s+)?(?:(?<cl>me|nos|lhe|lhes|se)\s+)?(?<v>\p{L}+)" + After, Ci);

    public static string PersonalInfinitives(string text, bool informal)
    {
        if (informal)
            text = PersonalContinuation.Replace(text, m =>
            {
                if (!PortugueseVerbs.IsKnownInfinitive(m.Groups["inf"].Value)) return m.Value;
                var verb = m.Groups["v"].Value;
                string? target;
                if (ControlledVerbs.Contains(verb))
                {
                    if (m.Groups["cl"].Value.Equals("se", StringComparison.OrdinalIgnoreCase)) return m.Value;
                    var following = Regex.Match(text[(m.Index + m.Length)..], @"^\s+(?<inf>\p{L}+)").Groups["inf"].Value;
                    target = ControlledActions.Contains(following) && Second(verb, out var second) ? second : null;
                }
                else
                {
                    target = PortugueseConverter.TuImperative.ToTu(verb);
                    if (target != null && m.Groups["neg"].Success)
                        target = Second(verb, out var second) ? second : null;
                }
                if (target == null) return m.Value;
                return m.Groups["head"].Value + m.Groups["neg"].Value +
                       (m.Groups["cl"].Success ? m.Groups["cl"].Value + " " : "") + CaseHelper.MatchCase(verb, target);
            });
        return Personal.Replace(text, m =>
        {
            var infinitive = m.Groups["inf"].Value;
            if (!PortugueseVerbs.IsKnownInfinitive(infinitive) &&
                !(PortugueseVerbs.IsInfinitive(infinitive) && NaturaDictionary.Load(true).Check(infinitive))) return m.Value;
            var subject = m.Groups["subject"].Value.ToLowerInvariant();
            if (!informal && subject is ("você" or "tu")) return m.Value;
            var ending = subject switch { "você" or "tu" => "es", "nós" => "mos", _ => "em" };
            var personal = PortugueseVerbs.PersonalInfinitive(infinitive, ending);
            var clitic = m.Groups["cl"].Value;
            if (clitic.Equals("se", StringComparison.OrdinalIgnoreCase))
                clitic = subject switch { "você" or "tu" => "te", "nós" => "nos", _ => "se" };
            var keepSubject = subject is "eles" or "elas" ? subject + " " : "";
            return m.Groups["pre"].Value + " " + keepSubject + m.Groups["adv"].Value +
                   (clitic.Length > 0 ? clitic + " " : "") + CaseHelper.MatchCase(infinitive, personal);
        });
    }

    private static readonly Regex ControlledClause = new(
        Before + @"(?<subject>você|tu)\s+(?<headAdv>" + Adverbs + @")(?<head>\p{L}+)\s+que\s+" +
        @"(?<tail>" + Adverbs + @")(?:(?<cl>se|me|te|nos|lhe|lhes)\s+)?(?<v>\p{L}+)" + After, Ci);
    private static readonly HashSet<string> ControlHeads = new(
        new[] { "achar", "pensar", "acreditar", "saber" }.SelectMany(Forms), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ControlledVerbs = new(
        new[] { "pode", "podia", "poderia", "puder", "pudesse", "consegue", "conseguia", "conseguiria",
            "deve", "devia", "deveria", "precisa", "precisava", "precisaria", "quer", "queria", "quereria",
            "vai", "iria", "teria", "tinha" }, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ControlledActions = new(new[]
    {
        "resolver", "ajudar", "fazer", "conseguir", "terminar", "acabar", "encontrar", "salvar", "ganhar", "vencer",
        "entrar", "sair", "voltar", "vir", "ir", "trabalhar", "estudar", "conduzir", "dirigir", "pagar", "comprar",
        "vender", "explicar", "contar", "dizer", "falar", "esconder", "fugir", "escapar", "lutar", "mudar",
    }, StringComparer.OrdinalIgnoreCase);
    private static readonly Regex ConditionalTail = new(
        Before + @"(?<pre>se\s+(?:você|tu)\s+" + Adverbs + @")(?:se\s+)?(?<v>\p{L}+)(?<gap>[^.!?;:\n]{0,100},\s*)" +
        @"(?<adv>" + Adverbs + @")(?:(?<cl>me|te|se|nos|lhe|lhes)\s+)?(?<tail>\p{L}+)" + After, Ci);
    private static readonly Regex ExplicitReferenceClause = new(
        Before + @"(?<pre>(?:você|tu)\s+" + Adverbs + @")(?:disse|sabia|percebeu|achou|pensou|descobriu)\s+que\s+" +
        @"(?<adv>" + Adverbs + @")(?:(?<cl>se|me|te|nos|lhe|lhes)\s+)?(?<v>\p{L}+)" + After, Ci);

    private static bool Second(string word, out string second) =>
        PortugueseVerbs.TryThirdToSecond(word, out second) || PortugueseVerbs.TryGuessThirdToSecond(word, out second);

    public static string Clauses(string text, ReferenceHint? reference)
    {
        text = ControlledClause.Replace(text, m =>
        {
            var head = m.Groups["head"].Value;
            var verb = m.Groups["v"].Value;
            if (!ControlHeads.Contains(head) || !ControlledVerbs.Contains(verb) || !Second(verb, out var second)) return m.Value;
            // Explicitly different English subjects block even a plausible subject-continuity reading.
            if (reference is { ThirdPerson: true, RepeatedYouSubject: false }) return m.Value;
            if (m.Groups["cl"].Value.Equals("se", StringComparison.OrdinalIgnoreCase)) return m.Value;
            var following = Regex.Match(text[(m.Index + m.Length)..], @"^\s+(?<inf>\p{L}+)").Groups["inf"].Value;
            if (!ControlledActions.Contains(following) ||
                reference is not { RepeatedYouSubject: true } &&
                (!Regex.IsMatch(text[(m.Index + m.Length)..], @"^[^.!?;]*\?") ||
                 verb.ToLowerInvariant() is not ("pode" or "consegue" or "deve" or "precisa" or "quer"))) return m.Value;
            return m.Value[..(m.Groups["v"].Index - m.Index)] + second;
        });
        text = ConditionalTail.Replace(text, m =>
        {
            var verb = m.Groups["v"].Value;
            var tail = m.Groups["tail"].Value;
            if (Regex.IsMatch(m.Groups["gap"].Value, Before + @"(?:ele|ela|eles|elas|nós|eu|você|vocês)\b|(?-i:\p{Lu}\p{Ll}+)", Ci))
                return m.Value;
            if (!verb.EndsWith("sse", StringComparison.OrdinalIgnoreCase) && verb.ToLowerInvariant() is not ("fosse" or "tivesse" or "pudesse")) return m.Value;
            if (!PortugueseVerbs.TrySplitFuture(tail, out _, out var ending, false) ||
                !ending.Equals("ia", StringComparison.OrdinalIgnoreCase) || reference is { ThirdPerson: true } ||
                !Second(tail, out var second)) return m.Value;
            return m.Value[..(m.Groups["tail"].Index - m.Index)] + second;
        });
        if (reference is { RepeatedYouSubject: true })
            text = ExplicitReferenceClause.Replace(text, m => Second(m.Groups["v"].Value, out var second)
                ? m.Value[..(m.Groups["v"].Index - m.Index)] + second : m.Value);
        return text;
    }

    private static readonly Dictionary<string, string> TurnIntoForms = MapForms("virar", "tornar");
    private static readonly Regex Become = new(
        Before + @"(?<v>" + Alternatives(TurnIntoForms.Keys) + @")(?<space>\s+)" +
        @"(?=(?:(?:um|uma|uns|umas)\s+)?(?:médic[oa]s?|advogad[oa]s?|políci[ao]s?|professor(?:a|es|as)?|" +
        @"detetives?|engenheir[oa]s?|enfermeir[oa]s?|presidente|ministro|ministra|criminos[oa]s?|assassin[oa]s?|" +
        @"amig[oa]s?|inimig[oa]s?|herói|heroína|chefes?|problema|pesadelo|hábito|rotina|obsessão)\b)", Ci);
    private static readonly Dictionary<string, string> FaceForms = MapForms("encarar", "enfrentar");
    private static readonly Regex FaceProblem = new(
        Before + @"(?<v>" + Alternatives(FaceForms.Keys) + @")(?<space>\s+)" +
        @"(?=(?:(?:o|a|os|as|um|uma|este|esta|esse|essa|teu|tua|seu|sua)\s+){0,2}" +
        @"(?:problemas?|realidade|verdade|consequências|riscos?|desafios?|situação|medos?|dificuldades|responsabilidades)\b)", Ci);
    private static readonly Dictionary<string, string> StareForms = MapForms("encarar", "olhar");
    private static readonly Regex StarePerson = new(
        Before + @"(?<v>" + Alternatives(StareForms.Keys) + @")\s+(?<p>você|vocês|ele|ela|eles|elas|mim|ti)" + After, Ci);
    private static readonly Regex Tasty = new(
        Before + @"(?<food>(?:o|a|este|esta|esse|essa)\s+(?:bolo|sopa|comida|jantar|almoço|peixe|carne|pão|café|chá|molho|chocolate))" +
        @"(?<middle>\s+(?:está|estava|é|ficou|parece)\s+(?:muito\s+|mesmo\s+|tão\s+)?)(?<adj>gostoso|gostosa)" + After, Ci);
    private static readonly Dictionary<string, string> EnjoyForms = MapForms("curtir", "gostar");
    private static readonly Regex EnjoyThing = new(
        Before + @"(?<v>" + Alternatives(EnjoyForms.Keys) + @")\s+(?<det>o|a|os|as|este|esta|esse|essa)\s+" +
        @"(?=(?:filmes?|músicas?|canções|livros?|concertos?|séries?|espetáculos?|jogos?|ideia|viagem|festa)\b)", Ci);
    private static readonly Dictionary<string, string> ComplainForms = MapForms("reclamar", "queixar");
    private static readonly Regex ComplainAbout = new(
        Before + @"(?<v>" + Alternatives(ComplainForms.Keys) + @")\s+(?<p>de|do|da|dos|das|disso|disto|daquilo)" + After, Ci);
    private static readonly Dictionary<string, string> FetchForms = MapForms("pegar", "buscar");
    private static readonly Regex CollectPerson = new(
        Before + @"(?<v>" + Alternatives(FetchForms.Keys) + @")\s+(?<p>você|vocês|ele|ela|eles|elas)\s+" +
        @"(?<where>no|na)\s+(?<place>estação|aeroporto|escola|hotel|hospital|trabalho)" + After, Ci);
    private static readonly Regex HitBodyPart = new(
        Before + @"(?<v>" + Alternatives(Forms("acertar").Where((_, index) => index is not (35 or 36 or 37 or 38))) + @")\s+" +
        @"(?<det>a|o|as|os)\s+(?<part>cabeça|cara|rosto|testa|nuca|boca|nariz|queixo|olho|olhos|ouvido|ouvidos|orelha|orelhas|pescoço|garganta|peito|costas|barriga|estômago|ombro|ombros|braço|braços|perna|pernas|joelho|joelhos|mão|mãos|pé|pés)" +
        @"(?<owner>\s+d(?:e|o|a|os|as)\s+(?:ele|ela|eles|elas|mim|ti|nós|você|vocês|tu|outro(?:s|as)?(?:\s+\p{L}+){0,2}|mesm[oa]s?|rapaz|rapariga|tipo|gajo|homem|mulher|suspeit[oa]s?|agente|detetive|polícia|(?-i:\p{Lu})[\p{L}\p{M}'’-]*))?" + After, Ci);

    private static Dictionary<string, string> MapForms(string source, string target)
    {
        var from = Forms(source);
        var to = Forms(target);
        return from.Select((form, i) => (form, target: to[i])).GroupBy(pair => pair.form, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().target, StringComparer.OrdinalIgnoreCase);
    }

    private static string Reflexive(string text, Match match, string verb, bool informal)
    {
        var before = Regex.Match(text[..match.Index], @"[^.!?;:]*$").Value;
        var subjects = Regex.Matches(before, Before + @"(?<s>eu|tu|você|nós|vocês|eles|elas|ele|ela)\b", Ci);
        var subject = subjects.Count > 0 ? subjects[^1].Groups["s"].Value.ToLowerInvariant() : "";
        var lower = verb.ToLowerInvariant();
        var previous = Regex.Match(before, @"\p{L}+\s*$").Value.Trim().ToLowerInvariant();
        if (subject.Length == 0 && ReflexiveAmbiguous.Contains(lower))
        {
            subject = previous switch
            {
                "vou" or "posso" or "quero" or "estou" => "eu",
                "vais" or "podes" or "queres" or "estás" => "tu",
                "vamos" or "podemos" or "queremos" or "estamos" => "nós",
                "vai" or "pode" or "quer" or "está" or "vão" or "podem" or "querem" or "estão" => "ele",
                _ => "",
            };
            if (subject.Length == 0) return match.Groups["v"].Value;
        }
        var clitic = subject switch
        {
            "eu" => "me", "tu" => "te", "você" when informal => "te", "nós" => "nos",
            "" when ReflexiveFirst.Contains(lower) => "me",
            "" when lower.EndsWith("mos", StringComparison.OrdinalIgnoreCase) => "nos",
            "" when informal && ReflexiveSecond.Contains(lower) => "te",
            _ => "se",
        };
        if (previous is "me" or "te" or "se" or "nos" or "vos") return verb;
        if (CaseHelper.IsAllUpper(verb)) clitic = clitic.ToUpperInvariant();
        if (informal && subject is ("você" or "tu")) return clitic + " " + verb;
        if (Regex.IsMatch(before, Before + @"(?:não|nunca|já|sempre|também|que|se|talvez|quando)\b[^,]*$", Ci))
            return clitic + " " + verb;
        return PortugueseVerbs.AttachMesoclitic(verb, clitic, false) ?? PortugueseVerbs.AttachEnclitic(verb, clitic);
    }

    private static readonly HashSet<string> ReflexiveFirst = new(new[] { "tornar", "queixar" }.SelectMany(Forms)
        .Where(f => f.EndsWith('o') || f.EndsWith("ei", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ReflexiveSecond = new(new[] { "tornar", "queixar" }
        .SelectMany(infinitive => Forms(infinitive).Where((_, index) => index is 2 or 7 or 11 or 15 or 20 or 24 or 28 or 31 or 40)),
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ReflexiveAmbiguous = new(new[] { "tornar", "queixar" }
        .SelectMany(infinitive => Forms(infinitive).Where((_, index) => index is 0 or 10 or 19 or 23 or 27 or 34 or 39)),
        StringComparer.OrdinalIgnoreCase);

    public static string Vocabulary(string text, bool pronouns, bool informal)
    {
        if (pronouns)
        {
            text = Become.Replace(text, m => Reflexive(text, m,
                CaseHelper.MatchCase(m.Groups["v"].Value, TurnIntoForms[m.Groups["v"].Value]), informal) + m.Groups["space"].Value);
        }
        text = FaceProblem.Replace(text, m => CaseHelper.MatchCase(m.Groups["v"].Value, FaceForms[m.Groups["v"].Value]) + m.Groups["space"].Value);
        text = StarePerson.Replace(text, m => CaseHelper.MatchCase(m.Groups["v"].Value, StareForms[m.Groups["v"].Value]) +
            " fixamente para " + m.Groups["p"].Value);
        text = Tasty.Replace(text, m => m.Groups["food"].Value + m.Groups["middle"].Value +
            CaseHelper.MatchCase(m.Groups["adj"].Value, m.Groups["adj"].Value.EndsWith("a", StringComparison.OrdinalIgnoreCase) ? "saborosa" : "saboroso"));
        text = EnjoyThing.Replace(text, m =>
        {
            var det = m.Groups["det"].Value.ToLowerInvariant();
            var preposition = det switch { "o" => "do", "a" => "da", "os" => "dos", "as" => "das", _ => "d" + det };
            return CaseHelper.MatchCase(m.Groups["v"].Value, EnjoyForms[m.Groups["v"].Value]) + " " + preposition + " ";
        });
        text = HitBodyPart.Replace(text, m =>
        {
            var tail = text[(m.Index + m.Length)..];
            if (m.Groups["owner"].Length == 0 && Regex.IsMatch(tail, @"^\s+d(?:e|o|a|os|as)\s+\p{L}+", Ci)) return m.Value;
            return m.Groups["v"].Value + " " +
                   CaseHelper.MatchCase(m.Groups["det"].Value, m.Groups["det"].Value.ToLowerInvariant() switch
                   { "a" => "na", "o" => "no", "as" => "nas", _ => "nos" }) + " " + m.Groups["part"].Value + m.Groups["owner"].Value;
        });
        return CollectPerson.Replace(text, m => CaseHelper.MatchCase(m.Groups["v"].Value, FetchForms[m.Groups["v"].Value]) +
            " " + m.Groups["p"].Value + " " + (m.Groups["where"].Value.Equals("na", StringComparison.OrdinalIgnoreCase) ? "à" : "ao") +
            " " + m.Groups["place"].Value);
    }

    public static string Complaints(string text, bool informal) => ComplainAbout.Replace(text, m =>
        Reflexive(text, m, CaseHelper.MatchCase(m.Groups["v"].Value, ComplainForms[m.Groups["v"].Value]), informal) +
        " " + m.Groups["p"].Value);

    private static readonly Regex FutureMesoclisis = new(
        Before + @"(?<root>\p{L}+)-(?<cl>me|te|se|nos|vos|lhe|lhes|lo|la|los|las)-(?<ending>ei|ás|á|emos|ão)" + After, Ci);
    private static readonly Regex RedundantSubject = new(
        @"(?<pre>^|[.!?]\s+)(?<subject>eu|nós)\s+(?<adv>" + Adverbs + @")(?:(?<cl>me|nos)\s+)?(?<v>\p{L}+)(?<attached>-(?:me|nos|te|o|a|os|as|lhe|lhes))?" + After, Ci);
    private static readonly HashSet<string> FirstSingular = new(StringComparer.OrdinalIgnoreCase)
    {
        "sou", "estou", "tenho", "vou", "posso", "quero", "sei", "faço", "digo", "vejo", "ouço", "peço",
        "fui", "estive", "tive", "fiz", "vi", "vim", "amo", "gosto", "lembro", "esqueço", "preciso",
        "penso", "acho", "acredito", "espero", "sinto", "durmo", "venho", "trago", "leio", "falo",
        "trabalho", "estudo", "ajudo", "encontro", "chamo",
    };
    private static readonly HashSet<string> FirstPlural = new(StringComparer.OrdinalIgnoreCase)
    {
        "somos", "estamos", "temos", "vamos", "podemos", "queremos", "sabemos", "fazemos", "dizemos", "vemos", "ouvimos",
        "fomos", "estivemos", "tivemos", "fizemos", "vimos", "dissemos",
    };

    public static string Conversational(string text, ReferenceHint? reference)
    {
        if (reference is { FormalAddress: true } || PortugueseConverter.YouToTuRules.IsFormal(text)) return text;
        text = FutureMesoclisis.Replace(text, m =>
        {
            var root = m.Groups["root"].Value.ToLowerInvariant();
            var clitic = m.Groups["cl"].Value.ToLowerInvariant();
            var infinitive = root switch { "dir" or "di" => "dizer", "far" or "fá" => "fazer", "trar" or "trá" => "trazer", _ => root };
            if (clitic.StartsWith('l') && !infinitive.EndsWith('r'))
                infinitive = root.TrimEnd('á', 'ê') + (root.EndsWith('á') ? "ar" : root.EndsWith('ê') ? "er" : "r");
            if (!PortugueseVerbs.IsKnownInfinitive(infinitive)) return m.Value;
            var auxiliary = m.Groups["ending"].Value.ToLowerInvariant() switch
            { "ei" => "vou", "ás" => "vais", "á" => "vai", "emos" => "vamos", _ => "vão" };
            return CaseHelper.MatchCase(m.Value, auxiliary + " " + PortugueseVerbs.AttachEnclitic(infinitive, clitic));
        });
        if (Regex.IsMatch(text, Before + @"(?:mas|porém|enquanto|só|apenas|também|própri[oa]s?|mesm[oa]s?)\b", Ci) ||
            Regex.IsMatch(text, Before + @"(?:ele|ela|eles|elas|tu|vocês|você)\b", Ci)) return text;
        return RedundantSubject.Replace(text, m =>
        {
            var verb = m.Groups["v"].Value;
            var plural = m.Groups["subject"].Value.Equals("nós", StringComparison.OrdinalIgnoreCase);
            var known = plural ? FirstPlural.Contains(verb) || verb.EndsWith("mos", StringComparison.OrdinalIgnoreCase) && PortugueseVerbs.IsKnownVerbForm(verb)
                : FirstSingular.Contains(verb) || verb.EndsWith('o') && PortugueseVerbs.IsKnownVerbForm(verb);
            if (!known) return m.Value;
            var replacement = m.Groups["adv"].Value + (m.Groups["cl"].Success ? m.Groups["cl"].Value + " " : "") +
                              verb + m.Groups["attached"].Value;
            return m.Groups["pre"].Value + CaseHelper.MatchCase(m.Groups["subject"].Value, replacement);
        });
    }

    public static string Idioms(string text, ReferenceHint? reference)
    {
        var english = reference?.EnglishText;
        if (string.IsNullOrWhiteSpace(english)) return text;
        if (Regex.IsMatch(english, @"\b(?:cost|costs|costing|costed)\b[^.!?]{0,60}\ban\s+arm\s+and\s+a\s+leg\b", Ci))
            text = Regex.Replace(text, Before + @"(?<v>custa|custou|custava|custam|custaram|custar|custaria|custará|custando)(?<cl>-(?:me|te|nos|lhe|lhes))?\s+(?:um\s+braço\s+e\s+uma\s+perna)", "${v}${cl} os olhos da cara", Ci);
        if (Regex.IsMatch(english, @"\ball\s+ears\b", Ci))
            text = Regex.Replace(text, Before + @"(?<v>estou|estás|está|estamos|estão|estava|estavas|estávamos|estavam)\s+(?<all>tod[oa]s?)\s+ouvidos\b", m =>
                CaseHelper.MatchCase(m.Groups["v"].Value, m.Groups["v"].Value.ToLowerInvariant() switch
                { "estou" => "sou", "estás" => "és", "está" => "é", "estamos" => "somos", "estão" => "são", "estava" => "era", "estavas" => "eras", "estávamos" => "éramos", _ => "eram" }) +
                " " + m.Groups["all"].Value + " ouvidos", Ci);
        if (Regex.IsMatch(english, @"\bon\s+the\s+house\b", Ci))
            text = Regex.Replace(text, Before + @"(?<v>é|era|foi|será)\s+por\s+conta\s+da\s+casa\b", "${v} oferta da casa", Ci);
        if (Regex.IsMatch(english, @"\bonce\s+in\s+a\s+blue\s+moon\b", Ci))
            text = Regex.Replace(text, Before + @"uma\s+vez\s+(?:em|numa)\s+(?:uma\s+)?lua\s+azul\b",
                m => CaseHelper.MatchCase(m.Value, "muito de vez em quando"), Ci);
        if (Regex.IsMatch(english, @"\b(?:spill|spills|spilled|spilling)\s+the\s+beans\b", Ci) &&
            !Regex.IsMatch(english, @"\b(?:floor|plate|pot|table|kitchen|cook|cooking|cooked)\b", Ci) &&
            !Regex.IsMatch(text, Before + @"(?:chão|panela|prato|mesa|cozinha|cozinhar)\b", Ci))
            text = Regex.Replace(text, Before + @"(?<v>derramar|derramou|derramei|derramaram|derrama|derramas|derramam|derramando)\s+os\s+feijões\b", m =>
            {
                var forms = MapForms("derramar", "contar");
                return CaseHelper.MatchCase(m.Groups["v"].Value, forms[m.Groups["v"].Value]) + " tudo";
            }, Ci);
        return text;
    }
}
