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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsPlausibleXblToken_ClassifiesScanCandidates(bool makeValid)
    {
        // A real observed token is ~2.5KB; the body charset is base64url-ish with '.' separators.
        string body = new string('A', 2400) + ". BBBB=".Replace(" ", "");
        string candidate = makeValid
            ? "XBL3.0 x=2533274805248103;" + body
            : "XBL3.0 x=2533274805248103;short body";
        Assert.Equal(makeValid, HomeViewModel.IsPlausibleXblToken(candidate));
    }

    [Fact]
    public void RejectedHeldToken_RotatesToNextCandidate_BelowBar()
    {
        // 17:41 log: valid=3 plausible candidates, freq 1 each, nothing logged in.
        // Frequency can't pick a winner; the profile API is the referee, so rotate.
        const string held = "XBL3.0 x=111;" + Pad;
        const string next = "XBL3.0 x=222;" + Pad;
        Assert.True(HomeViewModel.ShouldAdoptScannedToken(
            next, held, frequency: 1, distinctNonEmptyCandidates: 3, heldTokenRejected: true));
    }

    [Fact]
    public void LoggedInState_KeepsConfidenceBar_EvenWithCompetingCandidates()
    {
        const string held = "XBL3.0 x=111;" + Pad;
        const string next = "XBL3.0 x=222;" + Pad;
        // Once logged in, a below-bar candidate must not thrash the session.
        Assert.False(HomeViewModel.ShouldAdoptScannedToken(
            next, held, frequency: 1, distinctNonEmptyCandidates: 3, heldTokenRejected: false));
    }

    private const string Pad = "tokenbodyplaceholder-that-is-long-enough-for-plausibility-000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void RejectedHeldToken_RotatesToNextCandidate_BelowBar_CapsCompetitorCount()
    {
        // Rotation is bounded: a scan full of dozens of distinct candidates is not a
        // rotate-through-them-all situation.
        const string held = "XBL3.0 x=111;" + Pad;
        const string next = "XBL3.0 x=222;" + Pad;
        Assert.False(HomeViewModel.ShouldAdoptScannedToken(
            next, held, frequency: 1, distinctNonEmptyCandidates: 12, heldTokenRejected: true));
    }

    [Fact]
    public void LoneStructurallyValidCandidate_BelowBar_IsAdoptedWhenNothingHeld()
    {
        // The 5:36 PM scan: 11 distinct garbage reads (freq 1 each) around one real token.
        // After structural filtering, the real token is the lone valid candidate and must win.
        var frequency = new Dictionary<string, int>
        {
            [GarbagePrefixedToken] = 1,
            ["XBL3.0 x=99;junk " + new string('q', 16000)] = 1,
            ["XBL3.0 x=1234567890;" + new string('t', 2400)] = 1,
        };

        var filtered = HomeViewModel.FilterPlausibleTokens(frequency);
        var best = HomeViewModel.SelectBestScannedToken(filtered, out int bestFreq);

        Assert.Equal("XBL3.0 x=1234567890;" + new string('t', 2400), best);
        Assert.True(HomeViewModel.ShouldAdoptScannedToken(
            best, string.Empty, bestFreq, HomeViewModel.CountDistinctNonEmptyTokens(filtered)));
    }

    private static readonly string GarbagePrefixedToken = "XBL3.0 x=55;" + new string('g', 16383);
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

    [Fact]
    public void ShouldAdoptScannedToken_LoneCandidateBelowBar_AcceptedWhenNothingHeld()
    {
        // Regression: the Xbox app can expose only 3 copies of the token (log 2026-09-30 17:11,
        // bestFreq=3, need >3). With nothing held, a unique non-empty candidate must still be
        // adopted or login never happens.
        Assert.True(HomeViewModel.ShouldAdoptScannedToken(
            "XBL3.0 x=123;sometoken", "", frequency: 3, distinctNonEmptyCandidates: 1));
        Assert.True(HomeViewModel.ShouldAdoptScannedToken(
            "XBL3.0 x=123;sometoken", "", frequency: 1, distinctNonEmptyCandidates: 1));
    }

    [Theory]
    [InlineData("XBL3.0 x=1;a", "XBL3.0 x=2;b")]
    [InlineData("", "")]
    public void ShouldAdoptScannedToken_CompetingOrEmptyCandidates_KeepConfidenceBar(
        string held, string other)
    {
        // When several distinct tokens compete (or the scan found nothing), a below-bar
        // candidate stays rejected -- a truncated/garbage read must not win.
        Assert.False(HomeViewModel.ShouldAdoptScannedToken(
            held, "", frequency: 3, distinctNonEmptyCandidates: 2));
        Assert.False(HomeViewModel.ShouldAdoptScannedToken(
            other, "", frequency: 3, distinctNonEmptyCandidates: 0));
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
}
