using TonysGo.Listener.Bbva;

namespace Listener.Tests;

public sealed class ListenerRecoveryPlanTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void DoesNotRebindWhenHealthyNotPermittedOrLocked(bool connected, bool permitted, bool unlocked)
    {
        var plan = new ListenerRecoveryPlan();
        for (long now = 0; now < 120000; now += 1000)
        {
            var step = plan.Check(now, connected, permitted, unlocked);
            Assert.False(step.RequestBinding);
            Assert.False(step.RefreshRegistration);
            Assert.False(plan.NeedsHelp);
        }
    }

    [Fact]
    public void FrequentScreenRefreshDoesNotPostponeTheNextAttempt()
    {
        var plan = new ListenerRecoveryPlan();
        Assert.True(plan.Check(0, false, true, true).RequestBinding);
        for (var now = 100; now < 1000; now += 100)
            Assert.False(plan.Check(now, false, true, true).RequestBinding);
        Assert.True(plan.Check(1000, false, true, true).RequestBinding);
        Assert.Equal(2000, plan.Check(1000, false, true, true).CheckAfterMilliseconds);
    }

    [Fact]
    public void PersistentDisconnectRefreshesRegistrationThenShowsHelp()
    {
        var plan = new ListenerRecoveryPlan();
        plan.Check(0, false, true, true);
        Assert.False(plan.Check(19999, false, true, true).RefreshRegistration);
        Assert.True(plan.Check(20000, false, true, true).RefreshRegistration);
        Assert.False(plan.NeedsHelp);
        Assert.False(plan.Check(44999, false, true, true).RefreshRegistration);
        plan.Check(45000, false, true, true);
        Assert.True(plan.NeedsHelp);
    }

    [Fact]
    public void RepeatedRequestsAndReconnectsCannotSpamRegistrationChanges()
    {
        var plan = new ListenerRecoveryPlan();
        plan.Check(0, false, true, true);
        Assert.True(plan.Check(20000, false, true, true).RefreshRegistration);
        plan.Check(21000, true, true, true);
        Assert.False(plan.NeedsHelp);
        for (long now = 22000; now < 920000; now += 1000)
            Assert.False(plan.Check(now, false, true, true).RefreshRegistration);
        Assert.True(plan.Check(920000, false, true, true).RefreshRegistration);
    }

    [Fact]
    public void PermissionRevokedOrPhoneLockedRestartsWaitingWithoutChangingTheGrant()
    {
        var plan = new ListenerRecoveryPlan();
        plan.Check(0, false, true, true);
        plan.Check(45000, false, true, true);
        Assert.True(plan.NeedsHelp);
        Assert.False(plan.Check(46000, false, false, true).RefreshRegistration);
        Assert.False(plan.NeedsHelp);
        plan.Check(50000, false, true, false);
        var unlocked = plan.Check(120000, false, true, true);
        Assert.True(unlocked.RequestBinding);
        Assert.False(unlocked.RefreshRegistration);
        Assert.False(plan.NeedsHelp);
    }
}
