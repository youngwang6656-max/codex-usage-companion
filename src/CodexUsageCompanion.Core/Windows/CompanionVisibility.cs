namespace CodexUsageCompanion.Windows;

public enum CompanionVisibilityState
{
    Expanded,
    Expanding,
    Collapsed,
    Collapsing,
    DeferredFullscreenExpand,
}

public enum CompanionVisibilityPreference
{
    Expanded,
    Collapsed,
}

public enum FullscreenRevealPolicy
{
    DynamicIntegration,
    DeferredUntilWindowed,
}

public readonly record struct CompanionVisibilityDecision(
    CompanionVisibilityState State,
    CompanionVisibilityPreference Preference,
    bool ShowTray,
    bool ShowToggle,
    bool RequiresFullscreenIntegration,
    bool IsDeferred);

public sealed class CompanionVisibilityStateMachine
{
    public CompanionVisibilityStateMachine(CompanionVisibilityPreference preference)
    {
        Preference = preference;
        State = preference == CompanionVisibilityPreference.Expanded
            ? CompanionVisibilityState.Expanded
            : CompanionVisibilityState.Collapsed;
    }

    public CompanionVisibilityState State { get; private set; }

    public CompanionVisibilityPreference Preference { get; private set; }

    public CompanionVisibilityDecision Current => CreateDecision();

    public CompanionVisibilityDecision Toggle(
        bool isFullscreen,
        bool fullscreenSlotReserved,
        FullscreenRevealPolicy policy)
    {
        if (State is CompanionVisibilityState.Expanded
            or CompanionVisibilityState.Expanding)
        {
            Preference = CompanionVisibilityPreference.Collapsed;
            State = CompanionVisibilityState.Collapsing;
            return CreateDecision();
        }

        Preference = CompanionVisibilityPreference.Expanded;
        if (isFullscreen
            && !fullscreenSlotReserved
            && policy == FullscreenRevealPolicy.DeferredUntilWindowed)
        {
            State = CompanionVisibilityState.DeferredFullscreenExpand;
            return CreateDecision(isDeferred: true);
        }

        State = CompanionVisibilityState.Expanding;
        return CreateDecision(
            requiresFullscreenIntegration: isFullscreen && !fullscreenSlotReserved);
    }

    public CompanionVisibilityDecision CompleteAnimation()
    {
        State = State switch
        {
            CompanionVisibilityState.Expanding => CompanionVisibilityState.Expanded,
            CompanionVisibilityState.Collapsing => CompanionVisibilityState.Collapsed,
            _ => State,
        };
        return CreateDecision();
    }

    public CompanionVisibilityDecision FullscreenRevealFailed()
    {
        Preference = CompanionVisibilityPreference.Expanded;
        State = CompanionVisibilityState.DeferredFullscreenExpand;
        return CreateDecision(isDeferred: true);
    }

    public CompanionVisibilityDecision Windowed()
    {
        if (State == CompanionVisibilityState.DeferredFullscreenExpand)
        {
            State = CompanionVisibilityState.Expanding;
        }

        return CreateDecision();
    }

    private CompanionVisibilityDecision CreateDecision(
        bool requiresFullscreenIntegration = false,
        bool isDeferred = false)
    {
        bool showTray = State is CompanionVisibilityState.Expanded
            or CompanionVisibilityState.Expanding
            or CompanionVisibilityState.Collapsing;
        bool showToggle = State is CompanionVisibilityState.Collapsed
            or CompanionVisibilityState.Expanding
            or CompanionVisibilityState.Collapsing;
        bool deferred = isDeferred || State == CompanionVisibilityState.DeferredFullscreenExpand;
        if (deferred)
        {
            showTray = false;
            showToggle = false;
        }

        return new CompanionVisibilityDecision(
            State,
            Preference,
            showTray,
            showToggle,
            requiresFullscreenIntegration,
            deferred);
    }
}
