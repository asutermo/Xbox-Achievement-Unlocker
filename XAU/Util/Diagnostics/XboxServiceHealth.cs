using System.Text;

namespace XAU.Util.Diagnostics
{
    /// <summary>
    /// How a single Xbox Live endpoint answered a liveness probe. This exists to answer ONE
    /// question for the user: "is a 0h play-time / 'Unknown' profile MY problem or Microsoft's?".
    /// Any HTTP answer (2xx-4xx) proves the service is awake -- so it is NOT an outage; only a
    /// 5xx or "never got an HTTP response" points at an Xbox-side / network problem.
    /// </summary>
    public enum XblServiceVerdict
    {
        /// <summary>2xx -- fully operational.</summary>
        Operational,
        /// <summary>4xx (400/401/403/404...) -- the service answered but rejected the request;
        /// it is up, so a 0h/"Unknown" caused by our token/request must NOT be called an outage.</summary>
        RespondingClientError,
        /// <summary>429 -- throttled / "rate limit exceeded".</summary>
        RateLimited,
        /// <summary>5xx -- an Xbox-side server fault.</summary>
        ServerError,
        /// <summary>No HTTP response at all (DNS / refused / timeout). Could be an outage or the
        /// local network; surfaced distinctly from a clean 5xx.</summary>
        Unreachable
    }

    /// <summary>Immutable result of probing one endpoint.</summary>
    public sealed class XblServiceProbeResult
    {
        public string Service { get; }
        public string Url { get; }
        public bool Reached { get; }
        public int StatusCode { get; }
        public XblServiceVerdict Verdict { get; }
        public string Detail { get; }

        public XblServiceProbeResult(string service, string url, bool reached, int statusCode,
            XblServiceVerdict verdict, string detail)
        {
            Service = service;
            Url = url;
            Reached = reached;
            StatusCode = statusCode;
            Verdict = verdict;
            Detail = detail;
        }
    }

    /// <summary>Aggregate report handed back to the UI.</summary>
    public sealed class XblServiceHealthReport
    {
        public List<XblServiceProbeResult> Results { get; }
        public string Summary { get; }
        public bool LooksLikeServiceOutage { get; }

        public XblServiceHealthReport(List<XblServiceProbeResult> results, string summary, bool looksLikeServiceOutage)
        {
            Results = results ?? new List<XblServiceProbeResult>();
            Summary = summary;
            LooksLikeServiceOutage = looksLikeServiceOutage;
        }
    }

    /// <summary>
    /// Pure (no I/O) classification + summarisation logic, unit-testable without the network.
    /// </summary>
    public class XblServiceHealth
    {
        /// <summary>
        /// Classify one probe. Any 2xx is Operational; any 4xx still proves the service is awake
        /// (reported as "responding", NOT an outage). 5xx and "no response" are the only genuine
        /// "Microsoft's side" signals.
        /// </summary>
        public static XblServiceVerdict Classify(bool reached, int statusCode)
        {
            if (!reached)
                return XblServiceVerdict.Unreachable;
            if (statusCode >= 200 && statusCode < 300)
                return XblServiceVerdict.Operational;
            if (statusCode == 429)
                return XblServiceVerdict.RateLimited;
            if (statusCode >= 400 && statusCode < 500)
                return XblServiceVerdict.RespondingClientError;
            if (statusCode >= 500 && statusCode < 600)
                return XblServiceVerdict.ServerError;
            return XblServiceVerdict.Unreachable;
        }

        /// <summary>Short human label for one verdict (used in the banner + tests).</summary>
        public static string Label(XblServiceVerdict verdict)
        {
            switch (verdict)
            {
                case XblServiceVerdict.Operational: return "operational";
                case XblServiceVerdict.RespondingClientError: return "responding";
                case XblServiceVerdict.RateLimited: return "rate-limited";
                case XblServiceVerdict.ServerError: return "SERVER ERROR";
                case XblServiceVerdict.Unreachable: return "UNREACHABLE";
                default: return "unknown";
            }
        }

        /// <summary>
        /// True when any probe points at a Microsoft-side fault, justifying an "Xbox outage"
        /// warning. Deliberately conservative-toward-warning: one 5xx / unreachable among otherwise
        /// healthy endpoints is treated as "a service is down" -- which is exactly the partial-outage
        /// case (stats service lying about play-time while profile is fine) this feature exists to catch.
        /// </summary>
        public static bool LooksLikeServiceOutage(IEnumerable<XblServiceProbeResult> results)
        {
            if (results == null)
                return false;
            foreach (var r in results)
            {
                if (r.Verdict == XblServiceVerdict.ServerError || r.Verdict == XblServiceVerdict.Unreachable)
                    return true;
            }
            return false;
        }

        /// <summary>Build the one-line banner, e.g.
        /// "Xbox Status: healthy (profile=operational, achievements=operational, ...)".</summary>
        public static string Summarize(IEnumerable<XblServiceProbeResult> results)
        {
            if (results == null)
                return "Xbox Status: no endpoints checked";

            var parts = new List<string>();
            foreach (var r in results)
                parts.Add(r.Service + "=" + Label(r.Verdict));

            if (parts.Count == 0)
                return "Xbox Status: no endpoints checked";

            bool outage = LooksLikeServiceOutage(results);
            return "Xbox Status: " + (outage ? "possible OUTAGE" : "healthy")
                + " (" + string.Join(", ", parts) + ")";
        }

        /// <summary>
        /// User-facing one-word status. A 4xx from an UNAUTHENTICATED liveness ping is expected (these
        /// services need an auth header we deliberately don't send), so it is NOT "rejected" -- it is
        /// healthy, just like a 2xx. Only 5xx / unreachable read as problems.
        /// </summary>
        public static string UserStatus(XblServiceVerdict verdict)
        {
            switch (verdict)
            {
                case XblServiceVerdict.Operational:
                case XblServiceVerdict.RespondingClientError:
                    return "healthy";
                case XblServiceVerdict.RateLimited:
                    return "busy";
                case XblServiceVerdict.ServerError:
                    return "DOWN";
                case XblServiceVerdict.Unreachable:
                    return "unreachable";
                default:
                    return "unknown";
            }
        }

        /// <summary>Short per-service lines for a list view, e.g. "profile - healthy". Kept short so
        /// they never run off-screen the way one long comma-joined string would.</summary>
        public static List<string> StatusLines(IEnumerable<XblServiceProbeResult> results)
        {
            var lines = new List<string>();
            if (results == null)
                return lines;
            foreach (var r in results)
                lines.Add(r.Service + " - " + UserStatus(r.Verdict));
            return lines;
        }

        /// <summary>The single word shown as the overall header.</summary>
        public static string OverallStatus(IEnumerable<XblServiceProbeResult> results)
        {
            if (results == null)
                return "not checked";
            bool any = false;
            foreach (var _ in results) { any = true; break; }
            if (!any)
                return "not checked";
            return LooksLikeServiceOutage(results) ? "possible outage" : "healthy";
        }
    }
}
