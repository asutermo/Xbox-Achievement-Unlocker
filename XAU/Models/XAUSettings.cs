public class XAUSettings
{
    public string? SettingsVersion { get; set; }
    public string? ToolVersion { get; set; }
    public bool UnlockAllEnabled { get; set; }
    public bool AutoSpooferEnabled { get; set; }
    public bool AutoLaunchXboxAppEnabled { get; set; }
    public bool LaunchHidden { get; set; }
    public bool FakeSignatureEnabled { get; set; }
    public bool RegionOverride { get; set; }
    public bool UseAcrylic { get; set; }
    public bool PrivacyMode { get; set; }
    public bool OAuthLogin { get; set; }
    public bool AutoGrabEventsToken { get; set; }
    // Default ON (initializer): an existing settings.json that predates this key is deserialised into
    // a fresh object, Newtonsoft only overwrites keys present in the JSON, so a missing key keeps
    // this true -- diagnostics stay on until the user opts out via the Settings toggle.
    public bool EnableDiagnosticsLog { get; set; } = true;
    public int XauthScanReadLength { get; set; } = 16384;
    public string? CachedEventsToken { get; set; }
    public DateTime? EventsTokenObtainedAt { get; set; }
    public string? EventsUserHash { get; set; }
}
