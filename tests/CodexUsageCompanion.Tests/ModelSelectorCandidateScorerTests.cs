using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class ModelSelectorCandidateScorerTests
{
    [Fact]
    public void ChoosesExpandableModelSelectorInsteadOfSendButton()
    {
        PixelRect window = new(0, 0, 1200, 900);
        ModelSelectorCandidate[] candidates =
        [
            new(
                new PixelRect(1080, 810, 1128, 858),
                AutomationControlKind.Button,
                SupportsExpandCollapse: false,
                "发送"),
            new(
                new PixelRect(430, 810, 620, 842),
                AutomationControlKind.Button,
                SupportsExpandCollapse: true,
                "5.6 Sol 极高"),
        ];

        bool found = ModelSelectorCandidateScorer.TrySelect(
            window,
            96,
            candidates,
            out ModelSelectorCandidate selected,
            out double confidence);

        Assert.True(found);
        Assert.Equal(candidates[1], selected);
        Assert.True(confidence >= 0.75);
    }

    [Theory]
    [InlineData("GPT-6 Auto")]
    [InlineData("o4.1 high")]
    [InlineData("Codex 中")]
    public void AcceptsLocalizedOrFutureModelNames(string name)
    {
        var candidate = new ModelSelectorCandidate(
            new PixelRect(430, 810, 620, 842),
            AutomationControlKind.ComboBox,
            SupportsExpandCollapse: true,
            name);

        Assert.True(ModelSelectorCandidateScorer.TrySelect(
            new PixelRect(0, 0, 1200, 900),
            96,
            [candidate],
            out ModelSelectorCandidate selected,
            out _));
        Assert.Equal(candidate, selected);
    }

    [Fact]
    public void RejectsExpandableControlOutsideBottomComposerBand()
    {
        var candidate = new ModelSelectorCandidate(
            new PixelRect(430, 200, 620, 232),
            AutomationControlKind.Button,
            SupportsExpandCollapse: true,
            "5.6 Sol 极高");

        Assert.False(ModelSelectorCandidateScorer.TrySelect(
            new PixelRect(0, 0, 1200, 900),
            96,
            [candidate],
            out _,
            out _));
    }

    [Fact]
    public void AcceptsNamedModelButtonWhenWebProviderOmitsExpandCollapsePattern()
    {
        var model = new ModelSelectorCandidate(
            new PixelRect(430, 810, 620, 842),
            AutomationControlKind.Button,
            SupportsExpandCollapse: false,
            "5.6 Sol 极高");
        var send = new ModelSelectorCandidate(
            new PixelRect(1080, 810, 1128, 858),
            AutomationControlKind.Button,
            SupportsExpandCollapse: false,
            "发送");

        Assert.True(ModelSelectorCandidateScorer.TrySelect(
            new PixelRect(0, 0, 1200, 900),
            96,
            [send, model],
            out ModelSelectorCandidate selected,
            out _));
        Assert.Equal(model, selected);
    }
}
