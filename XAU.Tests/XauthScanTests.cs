using XAU.ViewModels.Pages;
using Xunit;

namespace XAU.Tests;

/// <summary>
/// Regression tests for the "memory scan ..." debug spam every ~1s + repeated TestXAUTH against
/// an expired/out-of-scope token.
///
/// Root cause: XauthWorker fires ~every second; in the not-logged-in branch it called GetXAUTH
/// which did a FULL address-space AoBScan, then unconditionally did `XAUTH = mostCommon;
/// XAUTHTested = false;`. When TestXAUTH had just 401'd (XAUTHTested=true), the next tick
/// re-adopted the SAME still-in-memory token, un-reset the tested flag, and re-tested it — a 1 Hz
/// scan + profile-API hammering that only stops when the Xbox app is relaunched.
///
/// Fixes pinned here:
///  - ShouldScanForXauth: throttle the scan once we hold a token (in-flight guard + interval,
///    with an immediate first scan).
///  - ShouldAdoptScannedToken: only adopt a genuinely DIFFERENT token, so a dead token settles
///    instead of re-arming the test every tick.
/// </summary>
public class XauthScanTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    #region Scan throttling

    [Fact]
    public void ShouldScanForXauth_FirstScan_WhenNeverScanned_ReturnsTrue()
    {
        Assert.True(HomeViewModel.ShouldScanForXauth(
            DateTime.MinValue, DateTime.UtcNow, inFlight: false));
    }

    [Fact]
    public void ShouldScanForXauth_ReturnsFalse_WhenScanInFlight()
    {
        // Even before the interval elapses, an in-flight scan must not be started twice.
        Assert.False(HomeViewModel.ShouldScanForXauth(
            DateTime.MinValue, DateTime.UtcNow, inFlight: true));
    }

    [Fact]
    public void ShouldScanForXauth_ReturnsFalse_BeforeIntervalElapsed()
    {
        var now = DateTime.UtcNow;
        var lastScan = now - TimeSpan.FromSeconds(1); // < 5s ago

        Assert.False(HomeViewModel.ShouldScanForXauth(lastScan, now, inFlight: false));
    }

    [Fact]
    public void ShouldScanForXauth_ReturnsTrue_AfterIntervalElapsed()
    {
        var now = DateTime.UtcNow;
        var lastScan = now - Interval; // exactly at the boundary

        Assert.True(HomeViewModel.ShouldScanForXauth(lastScan, now, inFlight: false));
    }

    #endregion

    #region Adopt-on-change

    [Fact]
    public void ShouldAdoptScannedToken_RejectsUnchangedToken()
    {
        // THE REGRESSION: re-finding the same (expired) token must NOT be adopted, because that
        // is what reset XAUTHTested=false and re-armed TestXAUTH every tick.
        const string token = "XBL3.0 x=1234567890;sometoken";

        Assert.False(HomeViewModel.ShouldAdoptScannedToken(token, token, frequency: 5));
    }

    [Fact]
    public void ShouldAdoptScannedToken_AcceptsChangedToken()
    {
        Assert.True(HomeViewModel.ShouldAdoptScannedToken(
            "XBL3.0 x=999;newtoken", "XBL3.0 x=1234567890;oldtoken", frequency: 5));
    }

    [Fact]
    public void ShouldAdoptScannedToken_FirstAdoption_FromEmptyCurrent()
    {
        Assert.True(HomeViewModel.ShouldAdoptScannedToken(
            "XBL3.0 x=123;tok", string.Empty, frequency: 5));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)] // original code required frequency > 3
    [InlineData(4, true)]
    public void ShouldAdoptScannedToken_EnforcesFrequencyThreshold(int frequency, bool expected)
    {
        Assert.Equal(expected,
            HomeViewModel.ShouldAdoptScannedToken("XBL3.0 x=1;a", "XBL3.0 x=2;b", frequency));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ShouldAdoptScannedToken_RejectsNullOrEmpty(string? scanned)
    {
        Assert.False(HomeViewModel.ShouldAdoptScannedToken(scanned!, "XBL3.0 x=2;b", frequency: 5));
    }

    #endregion

    #region Expired-token spin simulation

    [Fact]
    public void ExpiredTokenKeptInMemory_DoesNotReArmTestEveryTick()
    {
        // Simulate the reported spin: we hold an expired token (XAUTHTested=true after a 401) and
        // the Xbox app still exposes the SAME string in memory. Across many ticks the test must be
        // armed at most once, not once per tick.
        const string expired = "XBL3.0 x=1234567890;dead";

        string xauth = expired;
        bool xauthTested = true;   // 401 handler set this
        int testArmed = 0;

        for (int tick = 0; tick < 30; tick++)
        {
            // The poll finds the identical token still sitting in the Xbox app's memory.
            string scanned = expired;
            int frequency = 5;

            if (HomeViewModel.ShouldAdoptScannedToken(scanned, xauth, frequency))
            {
                xauth = scanned;
                xauthTested = false;
            }

            // The worker's "if (!XAUTHTested && XAUTH.Length > 0) TestXAUTH();" guard.
            if (!xauthTested && xauth.Length > 0)
            {
                testArmed++;
                xauthTested = true; // a re-401
            }
        }

        Assert.Equal(0, testArmed); // unchanged token -> test never re-armed
    }

    [Fact]
    public void NewTokenAppearing_GetsAdoptedAndTestedOnce()
    {
        // Positive path: once the Xbox app is relaunched and produces a fresh token, it IS adopted
        // and the test runs again.
        string xauth = "XBL3.0 x=1234567890;dead";
        bool xauthTested = true;
        int testArmed = 0;

        string fresh = "XBL3.0 x=999;alive";
        if (HomeViewModel.ShouldAdoptScannedToken(fresh, xauth, frequency: 5))
        {
            xauth = fresh;
            xauthTested = false;
        }

        if (!xauthTested && xauth.Length > 0)
        {
            testArmed++;
            xauthTested = true;
        }

        Assert.Equal(1, testArmed);
        Assert.Equal(fresh, xauth);
    }

    #endregion

    #region Re-test cadence (regression: "not logging in on fresh launch")

    [Theory]
    [InlineData(true, false)]   // in flight -> no
    [InlineData(false, true)]   // never tested -> yes
    public void ShouldTestXauth_InFlightAndFirstAttempt(bool inFlight, bool expected)
    {
        Assert.Equal(expected, HomeViewModel.ShouldTestXauth(
            inFlight, DateTime.MinValue, DateTime.UtcNow, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ShouldTestXauth_Blocked_BeforeIntervalElapsed()
    {
        var now = DateTime.UtcNow;
        var last = now - TimeSpan.FromSeconds(1);
        Assert.False(HomeViewModel.ShouldTestXauth(false, last, now, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ShouldTestXauth_Allowed_AfterIntervalElapsed()
    {
        var now = DateTime.UtcNow;
        var last = now - TimeSpan.FromSeconds(5);
        Assert.True(HomeViewModel.ShouldTestXauth(false, last, now, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TransientFirstTestFailure_StillLogsInOnceTokenIsReady_UnchangedToken()
    {
        // Reproduces the reported "not logging in on fresh launch": the held token does NOT change, so
        // adoption never re-arms; the gate must therefore be re-test cadence (not XAUTHTested) so that a
        // token that simply wasn't ready on the first attempt still logs in on a later bounded attempt.
        const string token = "XBL3.0 x=1234567890;tok"; // present from the start, becomes valid at t=6s

        string xauth = token;
        bool loggedIn = false;
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var interval = TimeSpan.FromSeconds(5);
        var lastTest = DateTime.MinValue;
        bool inFlight = false;
        int attempts = 0;

        for (int t = 0; t <= 10; t++)
        {
            var now = start + TimeSpan.FromSeconds(t);
            if (xauth.Length > 0 && HomeViewModel.ShouldTestXauth(inFlight, lastTest, now, interval))
            {
                inFlight = true;
                lastTest = now;
                attempts++;

                // The API only accepts the token from the 2nd bounded attempt onward (transient "not ready
                // yet" on the very first attempt right after launch).
                loggedIn = t >= 5;
                inFlight = false;
            }

            if (loggedIn)
                break;
        }

        Assert.True(loggedIn);
        Assert.Equal(2, attempts); // one failed attempt at t=0, one success at t=5 (bounded, not once-per-tick)
    }

    [Fact]
    public void HeldButAlwaysInvalidToken_IsTestedAtBoundedRate_NotOncePerTick()
    {
        // A genuinely dead token must still be re-tested (so recovery works when it becomes valid) but at
        // the bounded cadence, NOT every tick -- guarding against re-introducing the 1 Hz API hammering.
        string xauth = "XBL3.0 x=1234567890;dead";
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var interval = TimeSpan.FromSeconds(5);
        var lastTest = DateTime.MinValue;
        bool inFlight = false;
        int attempts = 0;

        for (int t = 0; t < 60; t++) // 60 one-second ticks
        {
            var now = start + TimeSpan.FromSeconds(t);
            if (xauth.Length > 0 && HomeViewModel.ShouldTestXauth(inFlight, lastTest, now, interval))
            {
                inFlight = true;
                lastTest = now;
                attempts++;
                inFlight = false; // test fails (still not logged in)
            }
        }

        // ~one per 5s over 60s -> ~12 attempts, definitively far fewer than 60 (no per-tick hammering).
        Assert.InRange(attempts, 10, 13);
    }

    #endregion
}
