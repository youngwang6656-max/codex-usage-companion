namespace CodexUsageCompanion.Windows;

public readonly record struct WindowEventSubscription(
    uint EventMin,
    uint EventMax,
    uint ProcessId);

public static class WindowEventSubscriptionPlanner
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMoveSizeStart = 0x000A;
    private const uint EventSystemMoveSizeEnd = 0x000B;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectLocationChange = 0x800B;

    public static IReadOnlyList<WindowEventSubscription> ForStartup() =>
        [new(EventSystemForeground, EventSystemForeground, ProcessId: 0)];

    public static IReadOnlyList<WindowEventSubscription> ForCodexProcess(uint processId)
    {
        if (processId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        return
        [
            new(EventSystemMoveSizeStart, EventSystemMoveSizeEnd, processId),
            new(EventSystemMinimizeStart, EventSystemMinimizeEnd, processId),
            new(EventObjectDestroy, EventObjectLocationChange, processId),
        ];
    }
}
