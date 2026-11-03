using CommunityToolkit.Mvvm.ComponentModel;

namespace SubtitleEdit.Plugins.BrazilianToEuropeanPortuguese;

public partial class ChangeProposal : ObservableObject
{
    [ObservableProperty] private bool _include;
    [ObservableProperty] private bool _includeLayout = true;
    [ObservableProperty] private string _expectedText = "";

    public int LineIndex { get; }
    public string LineNumber => (LineIndex + 1).ToString();
    public string OriginalText { get; }
    public string LanguageText { get; }
    public string WrappedText { get; }
    public string ConvertedText => IncludeLayout ? WrappedText : LanguageText;
    public bool HasLayoutChange => LanguageText != WrappedText;
    public IReadOnlyList<RuleChange> Rules { get; }
    public string Explanation => string.Join("\n", Rules.Select(r => $"{r.Id}: {r.Description} [{r.Basis}]")) +
        (Rules.Count > 0 ? "\nRule-based result; context/reference inferences still need review." : "");
    public bool HasExplanation => Rules.Count > 0;
    public int SentenceGroup { get; }
    public string ReferenceText { get; }
    public bool HasReference => ReferenceText.Length > 0;
    private readonly string _reviewNote;
    private readonly string _wrappedLayoutNote;
    private readonly string _languageLayoutNote;
    public string ReviewNote => string.Join("\n", new[] { _reviewNote, IncludeLayout ? _wrappedLayoutNote : _languageLayoutNote }
        .Where(n => n.Length > 0));
    public bool HasReview => ReviewNote.Length > 0;
    /// <summary>False for review-only lines: there is nothing to apply.</summary>
    public bool IsChange { get; }

    public ChangeProposal(int lineIndex, string originalText, string convertedText, string? referenceText = null, string? reviewNote = null,
        IReadOnlyList<RuleChange>? rules = null, string? wrappedText = null, int sentenceGroup = -1,
        string? wrappedLayoutNote = null, string? languageLayoutNote = null)
    {
        LineIndex = lineIndex;
        OriginalText = originalText;
        LanguageText = convertedText;
        WrappedText = wrappedText ?? convertedText;
        Rules = rules ?? Array.Empty<RuleChange>();
        SentenceGroup = sentenceGroup;
        _expectedText = WrappedText;
        ReferenceText = string.IsNullOrWhiteSpace(referenceText) ? string.Empty : "EN: " + referenceText.Replace('\n', ' ');
        _reviewNote = reviewNote ?? string.Empty;
        _wrappedLayoutNote = wrappedLayoutNote ?? string.Empty;
        _languageLayoutNote = languageLayoutNote ?? string.Empty;
        IsChange = originalText != convertedText || originalText != WrappedText;
        _include = IsChange;
    }

    partial void OnIncludeChanged(bool value)
    {
        if (value && !IsChange) Include = false;
    }

    partial void OnIncludeLayoutChanged(bool value)
    {
        OnPropertyChanged(nameof(ConvertedText));
        OnPropertyChanged(nameof(ReviewNote));
        OnPropertyChanged(nameof(HasReview));
        ExpectedText = ConvertedText;
    }
}
