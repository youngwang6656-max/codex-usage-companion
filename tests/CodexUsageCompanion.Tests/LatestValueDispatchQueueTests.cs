using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class LatestValueDispatchQueueTests
{
    [Fact]
    public void Offer_SchedulesOnlyOneConsumer_AndReplacesPendingValue()
    {
        var queue = new LatestValueDispatchQueue<int>();

        Assert.True(queue.Offer(1));
        Assert.False(queue.Offer(2));
        Assert.False(queue.Offer(3));

        Assert.True(queue.TryTake(out int value));
        Assert.Equal(3, value);
        Assert.False(queue.TryTake(out _));
    }

    [Fact]
    public void OfferWhileConsumerIsActive_IsConsumedWithoutAnotherDispatch()
    {
        var queue = new LatestValueDispatchQueue<string>();
        Assert.True(queue.Offer("first"));
        Assert.True(queue.TryTake(out string? first));
        Assert.Equal("first", first);

        Assert.False(queue.Offer("newest"));
        Assert.True(queue.TryTake(out string? newest));
        Assert.Equal("newest", newest);
        Assert.False(queue.TryTake(out _));

        Assert.True(queue.Offer("next dispatch"));
    }

    [Fact]
    public void Queue_AllowsNullAsTheLatestValue()
    {
        var queue = new LatestValueDispatchQueue<object?>();

        Assert.True(queue.Offer(new object()));
        Assert.False(queue.Offer(null));

        Assert.True(queue.TryTake(out object? value));
        Assert.Null(value);
    }

    [Fact]
    public void NonCoalescibleControlValueIsNotOverwrittenByLatestPosition()
    {
        var queue = new LatestValueDispatchQueue<string>();

        Assert.True(queue.Offer("move-start", coalesce: false));
        Assert.False(queue.Offer("position-1", coalesce: true));
        Assert.False(queue.Offer("position-2", coalesce: true));

        Assert.True(queue.TryTake(out string? first));
        Assert.True(queue.TryTake(out string? second));
        Assert.Equal("move-start", first);
        Assert.Equal("position-2", second);
    }
}
