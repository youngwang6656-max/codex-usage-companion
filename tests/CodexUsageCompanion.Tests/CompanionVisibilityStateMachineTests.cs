using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class CompanionVisibilityStateMachineTests
{
    [Fact]
    public void CollapsedWindowedToggleStartsExpandWithoutFullscreenResize()
    {
        var machine = new CompanionVisibilityStateMachine(
            CompanionVisibilityPreference.Collapsed);

        CompanionVisibilityDecision decision = machine.Toggle(
            isFullscreen: false,
            fullscreenSlotReserved: false,
            FullscreenRevealPolicy.DynamicIntegration);

        Assert.Equal(CompanionVisibilityState.Expanding, decision.State);
        Assert.Equal(CompanionVisibilityPreference.Expanded, decision.Preference);
        Assert.False(decision.RequiresFullscreenIntegration);
        Assert.False(decision.IsDeferred);
    }

    [Fact]
    public void CollapsedFullscreenToggleRequestsDynamicIntegrationWhenNoSlotExists()
    {
        var machine = new CompanionVisibilityStateMachine(
            CompanionVisibilityPreference.Collapsed);

        CompanionVisibilityDecision decision = machine.Toggle(
            isFullscreen: true,
            fullscreenSlotReserved: false,
            FullscreenRevealPolicy.DynamicIntegration);

        Assert.Equal(CompanionVisibilityState.Expanding, decision.State);
        Assert.True(decision.RequiresFullscreenIntegration);
    }

    [Fact]
    public void CollapsedFullscreenToggleDefersWhenPolicyDisablesDynamicIntegration()
    {
        var machine = new CompanionVisibilityStateMachine(
            CompanionVisibilityPreference.Collapsed);

        CompanionVisibilityDecision decision = machine.Toggle(
            isFullscreen: true,
            fullscreenSlotReserved: false,
            FullscreenRevealPolicy.DeferredUntilWindowed);

        Assert.Equal(CompanionVisibilityState.DeferredFullscreenExpand, decision.State);
        Assert.Equal(CompanionVisibilityPreference.Expanded, decision.Preference);
        Assert.True(decision.IsDeferred);
    }

    [Fact]
    public void FailedDynamicIntegrationDefersUntilWindowedThenExpands()
    {
        var machine = new CompanionVisibilityStateMachine(
            CompanionVisibilityPreference.Collapsed);
        _ = machine.Toggle(true, false, FullscreenRevealPolicy.DynamicIntegration);

        CompanionVisibilityDecision failed = machine.FullscreenRevealFailed();
        CompanionVisibilityDecision windowed = machine.Windowed();

        Assert.Equal(CompanionVisibilityState.DeferredFullscreenExpand, failed.State);
        Assert.False(failed.ShowTray);
        Assert.False(failed.ShowToggle);
        Assert.Equal(CompanionVisibilityState.Expanding, windowed.State);
        Assert.True(windowed.ShowTray);
    }

    [Fact]
    public void ToggleDuringAnimationReversesState()
    {
        var machine = new CompanionVisibilityStateMachine(
            CompanionVisibilityPreference.Collapsed);
        _ = machine.Toggle(false, false, FullscreenRevealPolicy.DynamicIntegration);

        CompanionVisibilityDecision reversed = machine.Toggle(
            isFullscreen: false,
            fullscreenSlotReserved: false,
            FullscreenRevealPolicy.DynamicIntegration);

        Assert.Equal(CompanionVisibilityState.Collapsing, reversed.State);
        Assert.Equal(CompanionVisibilityPreference.Collapsed, reversed.Preference);
    }

    [Fact]
    public void ReopeningReservedFullscreenSlotDoesNotResizeCodexAgain()
    {
        var machine = new CompanionVisibilityStateMachine(
            CompanionVisibilityPreference.Expanded);
        _ = machine.Toggle(
            isFullscreen: true,
            fullscreenSlotReserved: true,
            FullscreenRevealPolicy.DynamicIntegration);
        _ = machine.CompleteAnimation();

        CompanionVisibilityDecision reopen = machine.Toggle(
            isFullscreen: true,
            fullscreenSlotReserved: true,
            FullscreenRevealPolicy.DynamicIntegration);

        Assert.Equal(CompanionVisibilityState.Expanding, reopen.State);
        Assert.False(reopen.RequiresFullscreenIntegration);
    }

    [Fact]
    public void CollapsedPreferenceStartsWithoutRequestingFullscreenSlot()
    {
        var machine = new CompanionVisibilityStateMachine(
            CompanionVisibilityPreference.Collapsed);

        CompanionVisibilityDecision current = machine.Current;

        Assert.Equal(CompanionVisibilityState.Collapsed, current.State);
        Assert.False(current.ShowTray);
        Assert.True(current.ShowToggle);
        Assert.False(current.RequiresFullscreenIntegration);
    }
}
