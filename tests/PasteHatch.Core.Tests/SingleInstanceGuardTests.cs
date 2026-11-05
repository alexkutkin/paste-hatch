namespace PasteHatch.Core.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void IndependentScopes_HaveIndependentPrimaryInstances()
    {
        var scope = Guid.NewGuid().ToString("N");
        using var first = SingleInstanceGuard.TryAcquire(scope);
        using var other = SingleInstanceGuard.TryAcquire(scope + "-other");
        using var second = SingleInstanceGuard.TryAcquire(scope);

        Assert.True(first.IsPrimary);
        Assert.True(other.IsPrimary);
        Assert.False(second.IsPrimary);
        second.SignalPrimaryInstance();
        Assert.True(first.WaitForShowRequest(TimeSpan.FromSeconds(1)));
        Assert.False(other.WaitForShowRequest(TimeSpan.Zero));
    }
}
