using CommunityToolkit.Mvvm.ComponentModel;

namespace SubtitleEdit.Plugins.BrazilianToEuropeanPortuguese;

public partial class ChangeProposal : ObservableObject
{
    [ObservableProperty] private bool _include;

    public int LineIndex { get; }
    public string LineNumber => (LineIndex + 1).ToString();
    public string OriginalText { get; }
    public string ConvertedText { get; }
    public string ReferenceText { get; }
    public bool HasReference => ReferenceText.Length > 0;
    public string ReviewNote { get; }
    public bool HasReview => ReviewNote.Length > 0;
    /// <summary>False for review-only lines: there is nothing to apply.</summary>
    public bool IsChange { get; }

    public ChangeProposal(int lineIndex, string originalText, string convertedText, string? referenceText = null, string? reviewNote = null)
    {
        LineIndex = lineIndex;
        OriginalText = originalText;
        ConvertedText = convertedText;
        ReferenceText = string.IsNullOrWhiteSpace(referenceText) ? string.Empty : "EN: " + referenceText.Replace('\n', ' ');
        ReviewNote = reviewNote ?? string.Empty;
        IsChange = originalText != convertedText;
        _include = IsChange;
    }

    partial void OnIncludeChanged(bool value)
    {
        if (value && !IsChange) Include = false;
    }
}
