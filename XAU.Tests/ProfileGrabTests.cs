using XAU.ViewModels.Pages;
using Xunit;

namespace XAU.Tests;

/// <summary>
/// Tests for the "Profile information grabbed" snackbar-spam regression.
///
/// Schlop's WAM architecture removed the isLoggedIn/alreadyGrabbed latches (the WAM login
/// flow re-grabs the profile on every login), so the guard now carries only the
/// in-flight/force decision: automatic ticks must not stack fetches, while the Refresh
/// Profile button may force a re-grab at any time.
/// </summary>
public class ProfileGrabTests
{
    [Fact]
    public void ShouldStartProfileGrab_AutomaticWhenIdle_ReturnsTrue()
    {
        Assert.True(HomeViewModel.ShouldStartProfileGrab(
            inFlight: false, force: false));
    }

    [Fact]
    public void ShouldStartProfileGrab_AutomaticWhileInFlight_ReturnsFalse()
    {
        // THE REGRESSION: a tick arriving while a fetch is mid-flight must NOT start another.
        Assert.False(HomeViewModel.ShouldStartProfileGrab(
            inFlight: true, force: false));
    }

    [Fact]
    public void ShouldStartProfileGrab_ManualWhileInFlight_ReturnsTrue()
    {
        // The Refresh Profile button may force a re-grab even while an earlier fetch runs.
        Assert.True(HomeViewModel.ShouldStartProfileGrab(
            inFlight: true, force: true));
    }

    [Fact]
    public void RepeatedAutomaticTicks_DoNotStackWhileSingleFetchInFlight()
    {
        // Simulates the reported scenario: several ~1s ticks land while one async fetch
        // is still pending. Only the first tick may start a fetch; the rest are suppressed.
        bool inFlight = false;
        int fetchesStarted = 0;

        void Tick()
        {
            if (!HomeViewModel.ShouldStartProfileGrab(inFlight, force: false))
                return;

            fetchesStarted++;     // one network fetch actually begins
            inFlight = true;      // GrabProfile sets this synchronously before its first await
                                  // (stays true while the fetch is pending; cleared on completion)
        }

        Tick(); // starts the fetch
        Tick(); // blocked (in-flight)
        Tick(); // blocked
        Tick(); // blocked
        Tick(); // blocked

        Assert.Equal(1, fetchesStarted);
    }

    [Theory]
    [InlineData(false, false, true)]   // automatic when idle -> allowed
    [InlineData(true,  false, false)]  // automatic while in-flight -> blocked
    [InlineData(true,  true,  true)]   // manual while in-flight -> allowed
    [InlineData(false, true,  true)]   // manual when idle -> allowed
    public void ShouldStartProfileGrab_DecisionMatrix(bool inFlight, bool force, bool expected)
    {
        Assert.Equal(expected,
            HomeViewModel.ShouldStartProfileGrab(inFlight, force));
    }
}
