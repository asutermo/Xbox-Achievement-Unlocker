using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using XAU.Util.Etw;
using XAU.Util.Logging;
using XAU.Util.Diagnostics;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Memory;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net;
using System.Collections.ObjectModel;
using System.IO.Compression;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using XboxAuthNet.OAuth;
using XboxAuthNet.OAuth.CodeFlow;
using XboxAuthNet.XboxLive;
using XboxAuthNet.XboxLive.Requests;
using XboxAuthNet.XboxLive.Responses;

namespace XAU.ViewModels.Pages
{
    public partial class ImageItem : ObservableObject
    {
        [ObservableProperty]
        private string _imageUrl;
    }

    public partial class HomeViewModel : ObservableObject, INavigationAware
    {
        public static string ToolVersion = "26.06.14";
        public static string EventsVersion = "1.0";

        //attach vars
        [ObservableProperty] private string _attached = "Not Attached";
        [ObservableProperty] private Brush _attachedColor = new SolidColorBrush(Colors.Red);
        [ObservableProperty] private string _loggedIn = "Not Logged In";
        [ObservableProperty] private Brush _loggedInColor = new SolidColorBrush(Colors.Red);

        //profile vars
        [ObservableProperty] private string? _gamerPic = "pack://application:,,,/Assets/cirno.png";
        [ObservableProperty] private string? _gamerTag = "Gamertag: Unknown   ";
        [ObservableProperty] private string? _xuid = "XUID: Unknown";
        [ObservableProperty] private string? _gamerScore = "Gamerscore: Unknown";
        [ObservableProperty] private string? _profileRep = "Reputation: Unknown";
        [ObservableProperty] private string? _accountTier = "Tier: Unknown";
        [ObservableProperty] private string? _currentlyPlaying = "Currently Playing: Unknown";
        [ObservableProperty] private string? _activeDevice = "Active Device: Unknown";
        [ObservableProperty] private string? _isVerified = "Verified: Unknown";
        [ObservableProperty] private string? _location = "Location: Unknown";
        [ObservableProperty] private string? _tenure = "Tenure: Unknown";
        [ObservableProperty] private string? _following = "Following: Unknown";
        [ObservableProperty] private string? _followers = "Followers: Unknown";
        [ObservableProperty] private string? _gamepass = "Gamepass: Unknown";
        [ObservableProperty] private string? _bio = "Bio: Unknown";
        [ObservableProperty] private string _loginText = "Login";
        [ObservableProperty] public static bool _isLoggedIn = false;
        [ObservableProperty] public static bool _updateAvaliable = false;
        [ObservableProperty] private ObservableCollection<ImageItem> _watermarks = new ObservableCollection<ImageItem>();

        private readonly Lazy<XboxRestAPI> _xboxRestAPI;
        private readonly Lazy<GithubRestApi> _gitHubRestAPI = new Lazy<GithubRestApi>();

        public static int SpoofingStatus = 0; //0 = NotSpoofing, 1 = Spoofing, 2 = AutoSpoofing
        public static string SpoofedTitleID = "0";
        public static string AutoSpoofedTitleID = "0";

        //SnackBar
        public HomeViewModel(ISnackbarService snackbarService, IContentDialogService contentDialogService)
        {
            // Singleton in DI (App.xaml.cs) -- publish the one instance so background consumers
            // (heartbeat loops, HTTP-server routes) can reach instance methods like auth recovery.
            Instance = this;
            _snackbarService = snackbarService;
            _contentDialogService = contentDialogService;

            // Assume XAUTH and System Language are set by the time this is actually instantiated
            _xboxRestAPI = new Lazy<XboxRestAPI>(() => new XboxRestAPI(XAUTH));
        }
        private readonly ISnackbarService _snackbarService;
        private TimeSpan _snackbarDuration = TimeSpan.FromSeconds(2);
        private readonly IContentDialogService _contentDialogService;

        private const string XAuthScanPattern = "58 42 4C 33 2E 30 20 78 3D";

        [RelayCommand]
        private void RefreshProfile()
        {
            GrabProfile(force: true);
        }

        [RelayCommand]
        private void OpenXboxStatusPage()
        {
            try
            {
                var p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = "https://support.xbox.com/en-US/xbox-live-status"
                };
                p.Start();
            }
            catch (Exception ex)
            {
                EventsLog($"[XBLSTATUS] could not open status page: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Mem m = new Mem();
        public BackgroundWorker XauthWorker = new BackgroundWorker();
        public BackgroundWorker EventsTokenWorker = new BackgroundWorker();
        bool IsAttached = false;
        bool GrabbedProfile = false;
        bool _grabProfileInFlight = false;
        bool eventsTokenFound = false;
        public static bool XAUTHTested = false;
        public static string XAUTH = "";
        public static string XUIDOnly;
        public static bool InitComplete = false;

        // The single DI-registered HomeViewModel instance (see ctor). Static state like XAUTH lives
        // on this class; instance entry points (TryRecoverAuthAsync) are reached through this.
        public static HomeViewModel? Instance;

        // Cadence/anti-thrash state for the memory-scan token grabber. While we already hold a
        // (possibly expired) token we don't re-scan the whole user address space on every ~1s poll
        // tick -- only often enough to notice the Xbox app refreshing to a NEW token. See
        // ShouldScanForXauth / ShouldAdoptScannedToken.
        // How often the logged-in probe re-validates a HELD token. Without this an OAuth/SISU XBL3.0
        // token that expires hours into a session is never detected: TestXAUTH used to run only while
        // signed OUT, so the first sign of expiry was every other API call 401-ing at once.
        private static readonly TimeSpan LoggedInProbeInterval = TimeSpan.FromMinutes(15);

        private static readonly TimeSpan XauthScanInterval = TimeSpan.FromSeconds(5);
        private static DateTime _lastXauthScanUtc = DateTime.MinValue;
        private bool _xauthScanInFlight = false;

        // Re-test cadence, deliberately separate from adoption. Because we only adopt a CHANGED token
        // (ShouldAdoptScannedToken), a token whose first TestXAUTH transiently failed/401'd would never be
        // re-tested if the test were gated purely off XAUTHTested -- the old spammy code only self-healed
        // by re-adopting the same token every tick. So give the test its own bounded re-arm clock plus an
        // in-flight lock: at most one attempt per XauthScanInterval, no overlap, and it keeps retrying the
        // token we hold so a token that merely wasn't ready yet (fresh launch) still logs in.
        private static DateTime _lastXauthTestUtc = DateTime.MinValue;
        private bool _xauthTestInFlight = false;

        // Tunable, persisted (XAUSettings.XauthScanReadLength): how many bytes ReadString() copies at each
        // AoB hit while locating the sign-in token. ReadString is zero-terminated (it splits on the first
        // NUL), so a generous value is essentially free; the ONE real hazard is a value that is too SMALL,
        // which truncates a valid token so TestXAUTH then sees a malformed token and gets a 400 (not a 401)
        // -- exactly the "could my token just be invalid?" false positive. The floor sits far above the
        // ~2.6KB token actually observed, so a genuine token can never be cut. See NormalizeScanReadLength.
        public const int MinScanReadLength = 4096;
        public const int MaxScanReadLength = 262144;
        public const int DefaultScanReadLength = 16384;
        public static int ScanReadLength = DefaultScanReadLength;

        public static bool ShouldAcceptScanReadLength(int length)
            => length >= MinScanReadLength && length <= MaxScanReadLength;

        public static int NormalizeScanReadLength(int length)
        {
            if (length < MinScanReadLength)
                return MinScanReadLength;
            if (length > MaxScanReadLength)
                return MaxScanReadLength;
            return length;
        }

        private bool _isInitialized = false;
        private bool _isInitializing = false;
        string SettingsFilePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU"), "settings.json");
        string EventsMetaFilePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU"), "Events", "meta.json");
        string AuthFilePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU"), "auth.json");
        public CodeFlowAuthenticator oauth;
        public XboxAuthClient xboxAuthClient;
        public XboxSignedClient xboxSignedClient;

        public async void OnNavigatedTo()
        {
            // InitializeViewModel awaits network calls (update checks) before it flips
            // _isInitialized, so a quick navigate-away-and-back could re-enter it and call
            // XauthWorker.RunWorkerAsync() a second time -> "BackgroundWorker is currently
            // busy". Gate on an in-progress flag as well, not just the completed flag.
            if (!ShouldBeginInitialization(_isInitialized, _isInitializing))
                return;

            _isInitializing = true;
            try
            {
                await InitializeViewModel();
            }
            finally
            {
                _isInitializing = false;
            }
        }
        public void OnNavigatedFrom() { }

        /// <summary>
        /// True only when InitializeViewModel should run: it has neither completed nor is
        /// already in progress. Prevents re-entrant navigation from double-running init
        /// (which used to double-start the BackgroundWorkers and crash).
        /// </summary>
        public static bool ShouldBeginInitialization(bool initialized, bool initializing)
            => !initialized && !initializing;

        /// <summary>
        /// True when a BackgroundWorker may be (re)started. Starting an already-busy worker
        /// throws InvalidOperationException; this guard (plus the catch in StartWorker) keeps
        /// the "currently busy" crash from reaching the unhandled-exception dialog.
        /// </summary>
        public static bool ShouldStartWorker(bool isBusy) => !isBusy;

        private void StartWorker(BackgroundWorker worker)
        {
            if (!ShouldStartWorker(worker.IsBusy))
                return;
            try
            {
                worker.RunWorkerAsync();
            }
            catch (InvalidOperationException)
            {
                // Worker was started on another thread between the IsBusy check and the call.
            }
        }

        #region Update
        private async Task CheckForToolUpdates()
        {
            if (ToolVersion == "EmptyDevToolVersion")
                return;

            if (ToolVersion.Contains("DEV"))
            {
                var jsonResponse = await _gitHubRestAPI.Value.GetDevToolVersionAsync();

                if (("DEV-" + jsonResponse.LatestBuildVersion.ToString()) != ToolVersion)
                {
                    var result = await _contentDialogService.ShowSimpleDialogAsync(
                        new SimpleContentDialogCreateOptions()
                        {
                            Title = $"Version {jsonResponse.LatestBuildVersion.ToString()} available to download",
                            Content = "Would you like to update to this version?",
                            PrimaryButtonText = "Update",
                            CloseButtonText = "Cancel"
                        }
                    );
                    if (result == ContentDialogResult.Primary)
                    {
                        _snackbarService.Show("Downloading update...", "Please wait", ControlAppearance.Info, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                        string sourceFile = jsonResponse.DownloadURL.ToString();
                        string destFile = @"XAU-new.exe";
                        var fileDownloader = new FileDownloader();
                        await fileDownloader.DownloadFileAsync(new Uri(sourceFile).ToString(), destFile, UpdateTool);
                    }
                }
            }
            else
            {
                var jsonResponse = await _gitHubRestAPI.Value.GetReleaseVersionAsync();

                if (jsonResponse.Count == 0)
                    return;

                if (jsonResponse[0].tag_name.ToString() != ToolVersion)
                {
                    var result = await _contentDialogService.ShowSimpleDialogAsync(
                        new SimpleContentDialogCreateOptions()
                        {
                            Title = $"Version {jsonResponse[0].tag_name.ToString()} available to download",
                            Content = "Would you like to update to this version?",
                            PrimaryButtonText = "Update",
                            CloseButtonText = "Cancel"
                        }
                    );
                    if (result == ContentDialogResult.Primary)
                    {
                        _snackbarService.Show("Downloading update...", "Please wait", ControlAppearance.Info, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                        string sourceFile = jsonResponse[0].assets[0].browser_download_url.ToString();
                        string destFile = @"XAU-new.exe";
                        var fileDownloader = new FileDownloader();
                        await fileDownloader.DownloadFileAsync(sourceFile, destFile, UpdateTool);
                    }
                }
            }

        }
        private async void CheckForEventUpdates()
        {
            try
            {
                if (EventsVersion == "EmptyDevEventsVersion")
                    return;
                var response = await _gitHubRestAPI.Value.CheckForEventUpdatesAsync();
                var EventsTimestamp = 0;
                if (File.Exists(EventsMetaFilePath))
                {
                    var metaJson = File.ReadAllText(EventsMetaFilePath);
                    var meta = JsonConvert.DeserializeObject<EventsUpdateResponse>(metaJson);
                    EventsTimestamp = meta.Timestamp;
                }

                if (response.Timestamp > EventsTimestamp && response.DataVersion == EventsVersion)
                {
                    _snackbarService.Show("Downloading Events Update...", "Please wait", ControlAppearance.Info, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                    UpdateEvents();
                }
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Events Update Check Failed", $"Could not check for event updates: {ex.Message}",
                    ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), _snackbarDuration);
            }
        }

        private void UpdateTool(object sender, AsyncCompletedEventArgs e)
        {
            var path = Environment.ProcessPath.ToString();
            string[] splitpath = path.Split("\\");
            using (StreamWriter writer = new StreamWriter("XAU-Updater.bat"))
            {
                writer.WriteLine("@echo off");
                writer.WriteLine("timeout 1 > nul");
                writer.WriteLine("del \"" + Environment.ProcessPath + "\" ");
                writer.WriteLine("del \"" + splitpath[splitpath.Count() - 1] + "\" ");
                writer.WriteLine("ren XAU-new.exe \"" + splitpath[splitpath.Count() - 1] + "\" ");
                writer.WriteLine("start \"\" " + "\"" + splitpath[splitpath.Count() - 1] + "\"");
                writer.WriteLine("goto 2 > nul & del \"%~f0\"");
            }
            Process proc = new Process();
            proc.StartInfo.FileName = "XAU-Updater.bat";
            proc.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
            proc.Start();
            Environment.Exit(0);
        }

        private async void UpdateEvents()
        {
            string XAUPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU");
            string backupFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "XAU", "Events", "Backup");
            Directory.CreateDirectory(backupFolderPath);
            string eventsFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "XAU", "Events");
            string[] eventFiles = Directory.GetFiles(eventsFolderPath);
            string[] backupFiles = Directory.GetFiles(backupFolderPath);

            foreach (string file in backupFiles)
            {
                File.Delete(file);
            }
            foreach (string eventFile in eventFiles)
            {
                string fileName = Path.GetFileName(eventFile);
                string destinationPath = Path.Combine(backupFolderPath, fileName);
                File.Move(eventFile, destinationPath, true);
            }

            string zipFilePath = Path.Combine(XAUPath, "Events.zip");
            string extractPath = XAUPath;

            using (var client = new FileDownloader())
            {
                await client.DownloadFileAsync(EventsUrls.Zip, zipFilePath);
            }
            ZipFile.ExtractToDirectory(zipFilePath, extractPath);
            File.Delete(zipFilePath);
            //download and place meta.json in the events folder
            string MetaFilePath = Path.Combine(eventsFolderPath, "meta.json");
            using (var client = new FileDownloader())
            {
                await client.DownloadFileAsync(EventsUrls.MetaUrl, MetaFilePath);
            }
            _snackbarService.Show("Events Update Complete", "Events have been updated to the latest version.", ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
        }

        private async void CheckForXboxGamesDatabaseUpdate()
        {
            try
            {
                var fileInfo = await _gitHubRestAPI.Value.GetXboxGamesDatabaseInfoAsync();
                if (fileInfo == null)
                {
                    _snackbarService.Show("Error", "Could not check for database updates.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                string titleSearchPath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU"), "TitleSearch");
                string shaFilePath = Path.Combine(titleSearchPath, "xbox_games_sha.txt");
                string dbFilePath = Path.Combine(titleSearchPath, "xbox_games.db");

                Directory.CreateDirectory(titleSearchPath);

                string currentSha = string.Empty;
                try
                {
                    if (File.Exists(shaFilePath))
                    {
                        currentSha = (await File.ReadAllTextAsync(shaFilePath)).Trim();
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(currentSha) || !currentSha.Equals(fileInfo.Sha, StringComparison.OrdinalIgnoreCase))
                {
                    _snackbarService.Show("Database Update", "New Xbox games database available. Downloading...", ControlAppearance.Info, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);

                    using var client = new HttpClient();
                    var response = await client.GetAsync(fileInfo.DownloadUrl);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsByteArrayAsync();

                    await File.WriteAllBytesAsync(dbFilePath, content);

                    try
                    {
                        await File.WriteAllTextAsync(shaFilePath, fileInfo.Sha);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to store database SHA: {ex.Message}");
                    }

                    _snackbarService.Show("Success", "Xbox games database updated successfully!", ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                }
                else
                {
                    Console.WriteLine("Xbox games database is up to date.");
                }
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", $"Database update check failed: {ex.Message}", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
        }

        #endregion

        private async Task InitializeViewModel()
        {
            try
            {
                await CheckForToolUpdates();
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Update Check Failed", $"Could not check for updates: {ex.Message}",
                    ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), _snackbarDuration);
            }
            XauthWorker.DoWork += XauthWorker_DoWork;
            XauthWorker.ProgressChanged += XauthWorker_ProgressChanged;
            XauthWorker.RunWorkerCompleted += XauthWorker_RunWorkerCompleted;
            XauthWorker.WorkerReportsProgress = true;
            StartWorker(XauthWorker);
            EventsTokenWorker.DoWork += EventsTokenWorker_DoWork;
            if (!File.Exists(SettingsFilePath))
            {
                if (!Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "XAU")))
                {
                    Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU"));
                }

                if (!Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "XAU\\Events")))
                {
                    Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU\\Events"));
                }
                var defaultSettings = new XAUSettings
                {
                    SettingsVersion = "2",
                    ToolVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                    UnlockAllEnabled = false,
                    AutoSpooferEnabled = false,
                    AutoLaunchXboxAppEnabled = false,
                    FakeSignatureEnabled = true,
                    RegionOverride = false,
                    UseAcrylic = false,
                    PrivacyMode = false,
                    OAuthLogin = false
                };
                string defaultSettingsJson = JsonConvert.SerializeObject(defaultSettings, Formatting.Indented);
                using (var file = new StreamWriter(SettingsFilePath))
                {
                    file.Write(defaultSettingsJson);
                }
                if (Settings.OAuthLogin)
                {
                    OAuthLogin();
                }

            }
            CheckForEventUpdates();
            CheckForXboxGamesDatabaseUpdate();
            LoadSettings();
            if (Settings.OAuthLogin)
                OAuthLogin();
            _isInitialized = true;
            if (Settings.AutoLaunchXboxAppEnabled && Process.GetProcessesByName(ProcessNames.XboxPcApp).Length == 0)
            {
                var p = new Process();
                var startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = @"shell:appsFolder\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App"
                };

                if (Settings.LaunchHidden)
                {
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                }
                p.StartInfo = startInfo;
                p.Start();
            }
        }

        #region Xauth
        public void XauthWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            // OAuth mode: the token comes from SISU, not the Xbox app's memory, so there is nothing to
            // attach/scan. Stay ALIVE (idle tick) so ProgressChanged keeps firing: it drives the periodic
            // logged-in probe that detects an expired XBL3.0 token and triggers silent recovery. The old
            // `while (!Settings.OAuthLogin)` exit made this worker die forever on OAuth login, which is
            // why a token expiring hours later was never detected or refreshed.
            if (Settings.OAuthLogin)
            {
                Thread.Sleep(15000);
                XauthWorker.ReportProgress(0);
                return;
            }
            while (!Settings.OAuthLogin)
            {
                if (!m.OpenProcess((ProcessNames.XboxPcApp)))
                {
                    IsAttached = false;
                    Thread.Sleep(1000);
                }
                else
                {
                    IsAttached = true;
                }
                Thread.Sleep(1000);
                XauthWorker.ReportProgress(0);
            }
            Thread.Sleep(5000);
        }
        public void XauthWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (IsAttached || XAUTH.Length > 0)
            {
                Attached = $"Attached to xbox app ({m.GetProcIdFromName(ProcessNames.XboxPcApp).ToString()})";
                AttachedColor = new SolidColorBrush(Colors.Green);
                if (IsLoggedIn)
                {
                    if (!GrabbedProfile)
                        GrabProfile();
                    LoggedIn = "Logged In";
                    LoggedInColor = new SolidColorBrush(Colors.Green);

                    // Periodically re-validate the token even while logged in, so an expiry mid-session
                    // (typical after hours of spoofing) is caught here and recovered, instead of surfacing
                    // as random 401s from every consumer at once.
                    if (XAUTH.Length > 0 &&
                        ShouldTestXauth(_xauthTestInFlight, _lastXauthTestUtc, DateTime.UtcNow, LoggedInProbeInterval))
                    {
                        _xauthTestInFlight = true;
                        _lastXauthTestUtc = DateTime.UtcNow;
                        TestXAUTH();
                    }
                }
                else
                {
                    if (!SettingsViewModel.ManualXauth && !Settings.OAuthLogin)
                    {
                        GetXAUTH();
                        SettingsViewModel.ManualXauth = false;
                    }
                    LoggedIn = "Not Logged In";
                    LoggedInColor = new SolidColorBrush(Colors.Red);

                    // Test on its own bounded cadence rather than keying off XAUTHTested. Keep retrying the
                    // token we hold so a transient 401 / not-yet-ready token still logs the user in, instead
                    // of one failed attempt latching us out until a genuinely different token shows up.
                    if (XAUTH.Length > 0 &&
                        ShouldTestXauth(_xauthTestInFlight, _lastXauthTestUtc, DateTime.UtcNow, XauthScanInterval))
                    {
                        _xauthTestInFlight = true;
                        _lastXauthTestUtc = DateTime.UtcNow;
                        TestXAUTH();
                    }
                }
            }
            if (m.GetProcIdFromName(ProcessNames.XboxPcApp) == 0)
            {
                Attached = "Not Attached";
                AttachedColor = new SolidColorBrush(Colors.Red);
            }
        }
        public void XauthWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            StartWorker(XauthWorker);
        }
        /// <summary>
        /// True when the ~1s XauthWorker tick may start a fresh AoBScan. Once we hold a token we
        /// only re-scan on XauthScanInterval so we can notice the Xbox app refreshing to a new
        /// token, instead of hammering the whole address space (and the Xbox profile API, via the
        /// re-armed TestXAUTH) every tick. `inFlight` stops overlapping scans; the `DateTime.MinValue`
        /// fast-path makes the very first scan immediate.
        /// </summary>
        public static bool ShouldScanForXauth(DateTime lastScanUtc, DateTime nowUtc, bool inFlight)
        {
            if (inFlight)
                return false;
            if (lastScanUtc == DateTime.MinValue)
                return true;
            return (nowUtc - lastScanUtc) >= XauthScanInterval;
        }

        /// <summary>
        /// True when a scanned token should be adopted. Keeps the original frequency>3 confidence
        /// bar, but additionally requires the string to DIFFER from the token we already hold:
        /// re-adopting an identical token would reset XAUTHTested=false and re-arm TestXAUTH, so an
        /// expired token would be re-tested once per poll tick forever. Ignoring unchanged tokens
        /// lets that dead token settle until the Xbox app produces a genuinely new one.
        /// </summary>
        public static bool ShouldAdoptScannedToken(string scanned, string current, int frequency)
        {
            if (frequency <= 3)
                return false;
            if (string.IsNullOrEmpty(scanned))
                return false;
            return scanned != current;
        }

        /// <summary>
        /// Picks the most-frequent NON-empty scanned token (tie broken by the longer string, a better
        /// bet against a real token than a truncated read). Empties -- what ReadString returns "" for when
        /// an address is unreadable or below 0x10000 -- are ignored so they can never outrank a genuine
        /// token. That was the bug behind "can't log in": the old single top-1 pick let the most-common
        /// empty string (a very high duplicate count) defeat ShouldAdoptScannedToken's empty-check and
        /// block adoption outright. Returns ("", 0) when every candidate is empty. Pure; unit-testable.
        /// </summary>
        public static string SelectBestScannedToken(IEnumerable<KeyValuePair<string, int>> frequency, out int bestFrequency)
        {
            bestFrequency = 0;
            string best = "";
            if (frequency == null)
                return best;

            foreach (var pair in frequency)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;
                if (pair.Value > bestFrequency
                    || (pair.Value == bestFrequency && pair.Key.Length > best.Length))
                {
                    best = pair.Key;
                    bestFrequency = pair.Value;
                }
            }
            return best;
        }

        /// <summary>
        /// True when a TestXAUTH attempt may run: never overlapping (inFlight) and at most one per
        /// <paramref name="interval"/>. Deliberately does NOT consult XAUTHTested -- a token that 401'd once
        /// must still be retried, because it can become valid (Xbox app still authenticating after launch)
        /// or the failure may have been transient. Keying the retry off XAUTHTested as a permanent latch was
        /// what caused "not logging in on a fresh launch."
        /// </summary>
        public static bool ShouldTestXauth(bool inFlight, DateTime lastTestUtc, DateTime nowUtc, TimeSpan interval)
        {
            if (inFlight)
                return false;
            if (lastTestUtc == DateTime.MinValue)
                return true;
            return (nowUtc - lastTestUtc) >= interval;
        }

        private async void GetXAUTH()
        {
            if (!ShouldScanForXauth(_lastXauthScanUtc, DateTime.UtcNow, _xauthScanInFlight))
                return;

            _xauthScanInFlight = true;
            try
            {
                var swScan = System.Diagnostics.Stopwatch.StartNew();
                IEnumerable<long> XauthScanList = await m.AoBScan(XAuthScanPattern, true);
                long scanMs = swScan.ElapsedMilliseconds;
                // The expensive AoBScan has now run, so start backing off from here regardless of
                // whether this particular scan yielded a usable token.
                _lastXauthScanUtc = DateTime.UtcNow;

                var swRead = System.Diagnostics.Stopwatch.StartNew();
                string[] XauthStrings = new string[XauthScanList.Count()];
                var i = 0;
                foreach (var address in XauthScanList)
                {
                    XauthStrings[i] = m.ReadString(address.ToString("X"), length: ScanReadLength);
                    i++;
                }
                long readMs = swRead.ElapsedMilliseconds;

                Dictionary<string, int> frequency = new Dictionary<string, int>();
                foreach (string str in XauthStrings)
                {
                    if (!frequency.ContainsKey(str))
                    {
                        frequency[str] = 1;
                    }
                    else
                    {
                        frequency[str]++;
                    }
                }

                if (XauthStrings.Length == 0)
                {
                    DiagLog.Write(
                        $"[XAUTHDBG] scan found no XBL3.0 token candidates in Xbox app memory (signed in there yet?) " +
                        $"[scan={scanMs}ms read={readMs}ms]");
                    return;
                }

                // Highest count over ALL keys (including the empty string unreadable addresses read back as).
                // Kept only so the "below confidence" diagnostic keeps its original, comparable meaning.
                int topFreqAll = 0;
                foreach (var pair in frequency)
                {
                    if (pair.Value > topFreqAll)
                        topFreqAll = pair.Value;
                }

                // Adopt the most-frequent NON-empty candidate. The old code took the single top-1 key, so an
                // empty string (ReadString returns "" for an unreadable/<0x10000 address) could outrank the
                // real token and silently block adoption -- the exact "topFreq=7, adoptedNew=False, can't log
                // in" failure. Ignoring empties fixes that without touching the frequency>3 confidence bar.
                string best = SelectBestScannedToken(frequency, out int bestFreq);
                bool adoptedNew = ShouldAdoptScannedToken(best, XAUTH, bestFreq);
                if (adoptedNew)
                {
                    XAUTH = best;
                    XAUTHTested = false;
                }

                DiagLog.Write(
                    $"[XAUTHDBG] scan: candidates={XauthStrings.Length}, distinct={frequency.Count}, " +
                    $"topFreq={topFreqAll} (need >3), bestFreq={bestFreq}, bestLen={best.Length}, " +
                    $"currentXAUThLen={XAUTH.Length}, adoptedNew={adoptedNew} [scan={scanMs}ms read={readMs}ms]" +
                    (best.Length == 0 ? " (no readable token in memory -- Xbox app signed out?)" : ""));
            }
            catch (Exception ex)
            {
                // Hardening for async void: an unhandled exception here (e.g. AoBScan failing because
                // the Xbox app exited mid-scan and its memory was freed) escapes to the dispatcher
                // and crashes the whole app. Log it and let the next cadence retry.
                DiagLog.Write($"[XAUTHDBG] GetXAUTH scan threw {ex.GetType().Name}: {ex.Message} (swallowed; will retry on next cadence).");
            }
            finally
            {
                _xauthScanInFlight = false;
            }
        }
        private async void TestXAUTH()
        {
            DiagLog.Write($"[XAUTHDBG] TestXAUTH: verifying token (len={XAUTH.Length}) via GetBasicProfileAsync...");
            try
            {
                var response = await _xboxRestAPI.Value.GetBasicProfileAsync();
                if (Settings.PrivacyMode)
                {
                    GamerTag = $"Gamertag: Hidden";
                    Xuid = $"XUID: Hidden";
                }
                else
                {
                    GamerTag = $"Gamertag: {response.ProfileUsers[0].Settings[0].Value}";
                    Xuid = $"XUID: {response.ProfileUsers[0].Id}";
                }

                XUIDOnly = response.ProfileUsers[0].Id;
                IsLoggedIn = true;
                XAUTHTested = true;
                InitComplete = true;

                DiagLog.Write($"[XAUTHDBG] TestXAUTH: SUCCESS (XUID={XUIDOnly}) -- logged in; memory scan halts.");

                // Start the events token worker to periodically check/refresh the token
                StartWorker(EventsTokenWorker);
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    IsLoggedIn = false;
                    XAUTHTested = true;
                    DiagLog.Write("[XAUTHDBG] TestXAUTH: 401 Unauthorized -- token held is stale/expired; attempting silent re-acquisition...");
                    // Try to re-acquire instead of just telling the user to relaunch: OAuth mode can
                    // silently refresh (saved refresh token -> SISU -> new XAUTH); memory-scan mode
                    // forces an immediate rescan of the Xbox app's memory for a fresh token.
                    await TryRecoverAuthAsync();
                }
                else
                {
                    // Previously this non-401 case fell through silently and left the app stuck signed-out
                    // with no clue; surface the real status code. NOTE: HttpRequestException.StatusCode is
                    // NULLABLE -- it is null for connection-level failures (no HTTP response at all:
                    // socket reset, DNS failure, etc.). Casting it non-conditionally threw
                    // InvalidOperationException from inside this catch and crashed the app
                    // (async void + unhandled = crash dialog after ~9h of uptime).
                    DiagLog.Write($"[XAUTHDBG] TestXAUTH: HttpRequestException {(int?)ex.StatusCode ?? 0} {ex.StatusCode?.ToString() ?? "<no HTTP status - connection-level failure>"} (not 401) -- staying signed-out, will retry.");
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write($"[XAUTHDBG] TestXAUTH: threw {ex.GetType().Name}: {ex.Message} (swallowed; will retry on next cadence).");
            }
            finally
            {
                // Release the re-test lock so the next bounded attempt can run.
                _xauthTestInFlight = false;
            }
        }
        #endregion

        #region AuthRecovery
        // Central 401 recovery. Before this existed, an XBL3.0 token expiring mid-session (typical
        // after 24h of spoofing) left the app signed out forever: OAuth mode never re-ran SISU with
        // the saved refresh token, and memory-scan mode only recovered by luck when the Xbox app
        // happened to hold a fresh token. Every 401 consumer now funnels through here.
        private int _authRecoveryInFlight = 0;
        private DateTime _lastAuthRecoveryUtc = DateTime.MinValue;
        private static readonly TimeSpan AuthRecoveryCooldown = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Attempts silent token re-acquisition after a 401. Returns true ONLY when a fresh usable
        /// XAUTH was obtained (OAuth path). In memory-scan mode there is nothing to await -- we force
        /// an immediate rescan and let the usual scan/adopt/test loop take over, so returns false and
        /// the caller stays signed-out until that loop succeeds.
        /// Fire-and-forget callers use StartAuthRecovery().
        /// </summary>
        public async Task<bool> TryRecoverAuthAsync()
        {
            if (Interlocked.CompareExchange(ref _authRecoveryInFlight, 1, 0) != 0)
                return false;
            try
            {
                var now = DateTime.UtcNow;
                if (_lastAuthRecoveryUtc != DateTime.MinValue && now - _lastAuthRecoveryUtc < AuthRecoveryCooldown)
                    return false;
                _lastAuthRecoveryUtc = now;

                ShowSnackbar("Refreshing authentication...",
                    "Your session token was rejected -- XAU is re-authenticating in the background.",
                    ControlAppearance.Info, SymbolRegular.ArrowClockwise24);

                if (Settings.OAuthLogin)
                    return await TryRecoverOAuthAsync();

                // Memory-scan mode: force a scan right now instead of waiting for the next interval,
                // and make sure a manual-token choice doesn't block the rescan.
                DiagLog.Write("[AUTHRECOVERY] token rejected (401); forcing immediate XAUTH memory re-scan");
                SettingsViewModel.ManualXauth = false;
                _lastXauthScanUtc = DateTime.MinValue;
                GetXAUTH();
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref _authRecoveryInFlight, 0);
            }
        }

        private async Task<bool> TryRecoverOAuthAsync()
        {
            EnsureOAuthInitialized();

            MicrosoftOAuthResponse? saved = null;
            try
            {
                if (File.Exists(AuthFilePath))
                    saved = readSession();
            }
            catch (Exception ex)
            {
                DiagLog.Write($"[AUTHRECOVERY] could not read saved session: {ex.GetType().Name}: {ex.Message}");
            }

            if (saved == null || !saved.Validate() || string.IsNullOrEmpty(saved.RefreshToken))
            {
                DiagLog.Write("[AUTHRECOVERY] no usable saved OAuth session -- user must log in again");
                IsLoggedIn = false;
                return false;
            }

            try
            {
                var fresh = await oauth.AuthenticateSilently(saved.RefreshToken!);
                DiagLog.Write("[AUTHRECOVERY] silent OAuth refresh succeeded; regenerating XAUTH via SISU");
                CompleteLogin(fresh);
                ShowSnackbar("Authentication refreshed",
                    "A new session token was acquired -- you are still logged in.",
                    ControlAppearance.Success, SymbolRegular.Checkmark24);
                return true;
            }
            catch (Exception ex)
            {
                DiagLog.Write($"[AUTHRECOVERY] silent OAuth refresh failed: {ex.GetType().Name}: {ex.Message}");
                IsLoggedIn = false;
                ShowSnackbar("Re-authentication failed",
                    "Could not silently refresh your session. Please log in again from the Home page.",
                    ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                return false;
            }
        }

        /// <summary>
        /// Builds the OAuth/SISU clients on demand. OAuthLogin() and silent recovery share this so
        /// recovery works even if the interactive login flow never ran in this process.
        /// </summary>
        private void EnsureOAuthInitialized()
        {
            if (oauth != null)
                return;
            var OAuthhttpClient = new HttpClient();
            var apiClient = new CodeFlowLiveApiClient(XboxGameTitles.XboxAppPC, XboxAuthConstants.XboxScope, OAuthhttpClient);
            xboxAuthClient = new XboxAuthClient(OAuthhttpClient);
            xboxSignedClient = new XboxSignedClient(OAuthhttpClient);
            oauth = new CodeFlowBuilder(apiClient)
                .WithUIParent(this)
                .Build();
        }

        /// <summary>
        /// Thread-safe snackbar for background callers (auth recovery fires from heartbeat loops and
        /// the HTTP server, not just the UI thread). Wpf.Ui's SnackbarService is UI-bound, so marshal
        /// through the dispatcher when we're not already on it.
        /// </summary>
        private void ShowSnackbar(string title, string message, ControlAppearance appearance, SymbolRegular symbol)
        {
            void Show() => _snackbarService.Show(title, message, appearance, new SymbolIcon(symbol), _snackbarDuration);
            // Fully qualified: this project uses both WPF and WinForms, so the bare "Application"
            // name is ambiguous (CS0104).
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                Show();
            else
                dispatcher.Invoke(Show);
        }

        /// <summary>Fire-and-forget entry point for background consumers (heartbeat loops, API routes).</summary>
        public void StartAuthRecovery()
        {
            _ = TryRecoverAuthAsync();
        }

        #endregion

        #region EventsToken
        private bool solitaireLaunchedByUs = false;

        private static readonly TimeSpan EventsTokenCheckInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan EventsTokenMaxAge = TimeSpan.FromHours(23);

        private static DateTime _eventsTokenObtainedAt = DateTime.MinValue;
        private static string _eventsUserHash = null;

        private static readonly string EventsLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "events_debug.log");

        public static void EventsLog(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(EventsLogPath)!);
                File.AppendAllText(EventsLogPath, line + Environment.NewLine);
            }
            catch { }
        }


        public static readonly object SettingsWriteLock = new object();

        public void PersistEventsToken()
        {
            try
            {
                Settings.CachedEventsToken = AchievementsViewModel.EventsToken;
                Settings.EventsTokenObtainedAt = _eventsTokenObtainedAt;
                Settings.EventsUserHash = _eventsUserHash;
                var json = JsonConvert.SerializeObject(Settings);
                lock (SettingsWriteLock)
                {
                    File.WriteAllText(SettingsFilePath, json);
                }
            }
            catch { }
        }

        public void EventsTokenWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                EventsTokenWorkerLoop();
            }
            catch (Exception ex)
            {
                EventsLog($"Worker crashed: {ex.Message}");
            }
        }

        private void EventsTokenWorkerLoop()
        {
            EventsLog("Worker started");
            // Wait for login before scanning
            while (!IsLoggedIn)
            {
                Thread.Sleep(2000);
            }
            EventsLog("Logged in, entering refresh loop");

            // If a token already exists (e.g. from OAuth or cache), mark it as fresh
            if (!string.IsNullOrEmpty(AchievementsViewModel.EventsToken) && _eventsTokenObtainedAt == DateTime.MinValue)
                _eventsTokenObtainedAt = DateTime.UtcNow;

            while (true)
            {
                if (!Settings.AutoGrabEventsToken || !IsLoggedIn)
                {
                    Thread.Sleep(5000);
                    continue;
                }

                var currentToken = AchievementsViewModel.EventsToken;
                bool isEmpty = string.IsNullOrEmpty(currentToken);
                bool isValid = !isEmpty && IsEventsTokenValid();

                // A cached/captured token is bound to the Xbox account that produced it (the user hash
                // inside "x:XBL3.0 x={hash};..."). If the signed-in account changed since capture, the
                // token is useless even though it is still structurally "valid" -- discard it so the
                // loop re-captures for the CURRENT account instead of sending another account's ticket.
                if (!isEmpty && isValid &&
                    !string.IsNullOrEmpty(_eventsUserHash) &&
                    !string.IsNullOrEmpty(HomeViewModel.XUIDOnly) &&
                    _eventsUserHash != HomeViewModel.XUIDOnly)
                {
                    EventsLog($"Token belongs to user hash {_eventsUserHash}, not current {HomeViewModel.XUIDOnly} -- discarding and re-grabbing.");
                    AchievementsViewModel.EventsToken = null;
                    _eventsTokenObtainedAt = DateTime.MinValue;
                    PersistEventsToken();
                    continue;
                }

                var tokenAge = DateTime.UtcNow - _eventsTokenObtainedAt;
                bool isExpired = !isEmpty && isValid && tokenAge > EventsTokenMaxAge;

                EventsLog($"Check: empty={isEmpty}, valid={isValid}, age={tokenAge.TotalMinutes:F0}m, expired={isExpired}");

                if (isEmpty || !isValid || isExpired)
                {
                    if (isExpired)
                        EventsLog($"Token expired (age: {tokenAge.TotalMinutes:F0}m > {EventsTokenMaxAge.TotalMinutes:F0}m), refreshing...");
                    else
                        EventsLog("Token missing/invalid, refreshing...");

                    // Keep capturing until we get a token or settings change. Take the shared ETW gate
                    // (with a timeout so we can't wedge behind a long manual grab) so the auto worker and
                    // the manual button never drive the process-wide trace / Solitaire at the same time.
                    while (Settings.AutoGrabEventsToken && IsLoggedIn)
                    {
                        if (!_etwGate.Wait(TimeSpan.FromSeconds(2)))
                        {
                            EventsLog("ETW busy (manual grab in progress?), deferring capture...");
                            Thread.Sleep(1000);
                            continue;
                        }
                        try
                        {
                            // Drive the FULL capture flow (launch Solitaire if needed -> capture its
                            // telemetry burst -> extract -> close it). The old code called
                            // EtwTokenCapture.Capture(20) directly, which only listens to network
                            // traffic: with no game emitting telemetry it captured silence forever and
                            // the auto refresh never recovered an expired token. This is the exact
                            // flow the manual button uses, and it is safe under _etwGate (single
                            // consumer by construction).
                            GrabEventsTokenFromSolitaire();
                            if (!string.IsNullOrEmpty(AchievementsViewModel.EventsToken))
                            {
                                _eventsTokenObtainedAt = DateTime.UtcNow;
                                _eventsUserHash = HomeViewModel.XUIDOnly;
                                PersistEventsToken();
                                EventsLog("Auto capture success (Solitaire telemetry burst).");
                                break;
                            }
                        }
                        finally
                        {
                            _etwGate.Release();
                        }
                        EventsLog("Auto capture found no token, retrying in 30s...");
                        Thread.Sleep(30000);
                    }
                }

                EventsLog($"Sleeping {EventsTokenCheckInterval.TotalMinutes:F0}m...");
                Thread.Sleep(EventsTokenCheckInterval);
            }
        }

        /// <summary>
        /// Launches Solitaire (if needed), then continuously captures ETW network traffic
        /// and scans for the events token until one is found or Solitaire exits.
        /// </summary>
        private void GrabEventsTokenFromSolitaire()
        {
            bool alreadyRunning = Process.GetProcessesByName(ProcessNames.Solitaire).Length > 0;
            EventsLog($"Solitaire already running: {alreadyRunning}");

            // Start ETW capture FIRST — the token is sent in the initial telemetry burst
            // when Solitaire contacts Xbox Live, which happens within seconds of launch.
            EventsLog("Starting ETW before Solitaire launch...");
            EtwTokenCapture.Cleanup();
            string method = EtwTokenCapture.Start();
            if (method == null)
            {
                EventsLog("Failed to start ETW trace (not running as admin?)");
                return;
            }
            EventsLog($"ETW started via {method}");

            if (!alreadyRunning)
            {
                try
                {
                    EventsLog("Launching Solitaire...");
                    var p = new Process();
                    p.StartInfo = new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        FileName = AppLaunchUris.Solitaire
                    };
                    p.Start();
                    solitaireLaunchedByUs = true;
                }
                catch (Exception ex)
                {
                    EventsLog($"Failed to launch Solitaire: {ex.Message}");
                    EtwTokenCapture.Stop(method);
                    EtwTokenCapture.CleanupFiles();
                    return;
                }

                // Wait for the process to appear
                for (int i = 0; i < 15; i++)
                {
                    Thread.Sleep(1000);
                    if (Process.GetProcessesByName(ProcessNames.Solitaire).Length > 0)
                    {
                        EventsLog($"Solitaire process appeared after {i + 1}s");
                        break;
                    }
                }

                if (Process.GetProcessesByName(ProcessNames.Solitaire).Length == 0)
                {
                    EventsLog("Solitaire never appeared after 15s");
                    solitaireLaunchedByUs = false;
                    EtwTokenCapture.Stop(method);
                    EtwTokenCapture.CleanupFiles();
                    return;
                }
            }

            // Wait for Xbox Live init + initial telemetry burst (token is sent here)
            EventsLog("Waiting 25s for Xbox Live init + telemetry events...");
            Thread.Sleep(25000);

            // Stop first capture and try to extract
            EtwTokenCapture.Stop(method);
            Thread.Sleep(2000);

            string token = EtwTokenCapture.ExtractTokens();
            EtwTokenCapture.CleanupFiles();

            if (!string.IsNullOrEmpty(token))
            {
                EventsLog($"ETW capture success on initial capture, len={token.Length}");
                AchievementsViewModel.EventsToken = token;
                _eventsTokenObtainedAt = DateTime.UtcNow;
                _eventsUserHash = HomeViewModel.XUIDOnly;
                eventsTokenFound = true;
            }
            else
            {
                // Loop: keep capturing while Solitaire is running
                EventsLog("Initial capture found nothing, entering continuous scan loop...");
                int attempt = 0;
                while (!eventsTokenFound && Process.GetProcessesByName(ProcessNames.Solitaire).Length > 0)
                {
                    attempt++;
                    EventsLog($"Capture attempt {attempt}...");

                    token = EtwTokenCapture.Capture(20);
                    if (!string.IsNullOrEmpty(token))
                    {
                        EventsLog($"ETW capture success on attempt {attempt}, len={token.Length}");
                        AchievementsViewModel.EventsToken = token;
                        _eventsTokenObtainedAt = DateTime.UtcNow;
                        _eventsUserHash = HomeViewModel.XUIDOnly;
                        eventsTokenFound = true;
                        break;
                    }

                    // Brief pause before next capture
                    Thread.Sleep(3000);
                }

                if (!eventsTokenFound)
                    EventsLog("Solitaire exited before token was found");
            }

            // Close Solitaire if we launched it
            if (solitaireLaunchedByUs)
            {
                try
                {
                    foreach (var proc in Process.GetProcessesByName(ProcessNames.Solitaire))
                        proc.Kill();
                }
                catch { }
                solitaireLaunchedByUs = false;
            }
        }

        /// <summary>
        /// Manually triggers a scan (from the "Manually Refresh Token" button).
        /// Works regardless of the auto-grab setting.
        /// </summary>
        // Serialises every ETW/Solitaire consumer (the manual grab AND the auto-grab worker).
        // GrabEventsTokenFromSolitaire drives process-wide ETW sessions, launches/kills Solitaire and
        // shares eventsTokenFound/EventsToken, so two of them at once stomp each other: one tears down
        // the trace another is extracting, or one nulls EventsToken right after the other set it.
        // That is why a burst of clicks (or manual+auto overlap) made "refresh" appear to do nothing.
        private readonly SemaphoreSlim _etwGate = new SemaphoreSlim(1, 1);

        // Backed by a volatile field: the single-flight check+set happens on the UI thread (button
        // clicks) and the clear happens on the background Task, polled by the DispatcherTimer.
        private volatile bool _manualScanRunning;
        public bool ManualScanRunning
        {
            get => _manualScanRunning;
            private set => _manualScanRunning = value;
        }

        /// <summary>
        /// Single-flight gate for the manual "Manually Refresh Token" button: a scan may start only when
        /// none is already running. Repeated clicks -- or the 3s UI poll re-enabling the button while a
        /// long Solitaire/ETW capture is still in progress -- are ignored rather than piling up
        /// concurrent grabs that cancel each other out.
        /// </summary>
        public static bool ShouldStartManualScan(bool alreadyRunning) => !alreadyRunning;

        public void ScanForEventsTokenManual()
        {
            // Synchronous single-flight on the UI thread: set true before Task.Run so an immediate
            // re-entrant click is refused, and reset in the Task's finally (exactly one owner).
            if (!ShouldStartManualScan(ManualScanRunning))
                return;
            ManualScanRunning = true;

            System.Threading.Tasks.Task.Run(() =>
            {
                // Take the shared gate so we never overlap the background auto-grab worker's ETW session.
                _etwGate.Wait();
                try
                {
                    eventsTokenFound = false;
                    AchievementsViewModel.EventsToken = null;
                    _eventsTokenObtainedAt = DateTime.MinValue;

                    GrabEventsTokenFromSolitaire();
                    if (!string.IsNullOrEmpty(AchievementsViewModel.EventsToken))
                    {
                        _eventsTokenObtainedAt = DateTime.UtcNow;
                        _eventsUserHash = HomeViewModel.XUIDOnly;
                        PersistEventsToken();
                    }
                }
                finally
                {
                    _etwGate.Release();
                    ManualScanRunning = false;
                }
            });
        }

        /// <summary>
        /// Returns whether the current events token looks structurally valid.
        /// </summary>
        public static bool IsEventsTokenValid()
        {
            var token = AchievementsViewModel.EventsToken;
            return !string.IsNullOrWhiteSpace(token)
                && token.StartsWith("x:XBL3.0 x=")
                && token.Length > 30;
        }

        /// <summary>
        /// Returns whether the current events token has exceeded its max age.
        /// </summary>
        public static bool IsEventsTokenExpired()
        {
            if (_eventsTokenObtainedAt == DateTime.MinValue)
                return false; // no timestamp means we can't determine expiry
            return (DateTime.UtcNow - _eventsTokenObtainedAt) > EventsTokenMaxAge;
        }

        /// <summary>
        /// The UTC time the current events token was obtained.
        /// </summary>
        public static DateTime EventsTokenObtainedAtUtc => _eventsTokenObtainedAt;

        /// <summary>
        /// The UTC time the current events token is expected to expire.
        /// </summary>
        public static DateTime? EventsTokenExpiresAtUtc =>
            _eventsTokenObtainedAt == DateTime.MinValue
                ? null
                : _eventsTokenObtainedAt + EventsTokenMaxAge;
        #endregion

        #region OAuthLogin

        // Guards OAuthLogin() against re-entrancy: it fires from InitializeViewModel at startup AND
        // from the Login button, and two concurrent runs would race on the shared oauth clients and
        // silently double the restore/interactive attempts.
        private int _oauthLoginInFlight = 0;

        // Silent session-restore retry cadence (startup auto-login).
        private const int SilentRestoreAttempts = 3;
        private static readonly TimeSpan SilentRestoreRetryDelay = TimeSpan.FromSeconds(5);

        [RelayCommand]
        private async void OAuthLogin()
        {
            if (Interlocked.CompareExchange(ref _oauthLoginInFlight, 1, 0) != 0)
            {
                DiagLog.Write("[OAUTH] login already in progress; ignoring re-entrant call");
                return;
            }
            try
            {
                DiagLog.Write($"[OAUTH] login flow starting (savedSession={File.Exists(AuthFilePath)}, oauthMode={Settings.OAuthLogin})");
                EnsureOAuthInitialized();
                if (LoginText == "Logout")
                {
                    oauth.Signout();
                    try { File.Delete(AuthFilePath); } catch { }
                    ClearProfileState();
                    LoginText = "Login";
                    // Leaving this true made the memory-scan worker (and its token re-acquisition) stay
                    // dead after a logout; flipping it back lets the fallback path resume.
                    Settings.OAuthLogin = false;
                    PersistEventsToken();
                    return;
                }
                // NOTE: Settings.OAuthLogin is deliberately NOT set here. It was previously set before the
                // login attempt, so a failed/cancelled interactive login permanently disabled the
                // memory-scan worker (DoWork exits while OAuthLogin==true) with an empty XAUTH. The flag
                // is now set only when a session is actually established (CompleteLogin).

                // Use saved session if valid; otherwise interactive login
                MicrosoftOAuthResponse? response = await TryRestoreSessionAsync();
                if (response == null)
                    response = await TryInteractiveLoginAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _oauthLoginInFlight, 0);
            }
        }

        private void DeleteAuthFile()
        {
            try { File.Delete(AuthFilePath); } catch { }
        }

        /// <summary>
        /// Clears all cached authentication state (auth.json, XAUTH token, events token).
        /// The user will need to log in again after calling this.
        /// Does NOT require a reboot.
        /// </summary>
        public void ClearAuthCache()
        {
            // Delete saved OAuth session file
            DeleteAuthFile();

            // Invalidate XAUTH so next test/attempt will re-scan or prompt for login
            XAUTH = "";
            XAUTHTested = false;
            InitComplete = false;

            // Reset scan + test clocks so a cleared cache re-scans and re-tests immediately.
            _lastXauthScanUtc = DateTime.MinValue;
            _lastXauthTestUtc = DateTime.MinValue;

            // Reset manual xauth flag so auto-scan can resume
            SettingsViewModel.ManualXauth = false;

            // Clear events token from memory
            AchievementsViewModel.EventsToken = null;
            _eventsTokenObtainedAt = DateTime.MinValue;
            _eventsUserHash = null;

            // Clear events token cached in settings file
            Settings.CachedEventsToken = null;
            Settings.EventsTokenObtainedAt = null;
            Settings.EventsUserHash = null;
            PersistEventsToken();

            // Reset login state and profile display
            IsLoggedIn = false;
            ClearProfileState();

            // Re-enable the memory-scan fallback path. With OAuthLogin left true the XauthWorker
            // idles in the OAuth branch and nothing would try to re-acquire a token until the user
            // manually logs back in.
            Settings.OAuthLogin = false;
            PersistEventsToken();

            // Reset login button text
            LoginText = "Login";

            // Stop events token worker if running – it will idle once IsLoggedIn is false
            // (the worker loop checks IsLoggedIn every iteration).
            if (EventsTokenWorker.IsBusy)
            {
                try { EventsTokenWorker.CancelAsync(); } catch { }
            }
        }

        private void CompleteLogin(MicrosoftOAuthResponse response, string? successMessage = null)
        {
            writeSession(response);
            // Only mark OAuth mode as active once a session actually exists; persist so the OAuth
            // worker branch is taken after a restart.
            Settings.OAuthLogin = true;
            PersistEventsToken();
            if (!string.IsNullOrEmpty(successMessage))
                _snackbarService.Show("Success", successMessage, ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
            GenerateTokens(response);
        }

        private async Task<MicrosoftOAuthResponse?> TryRestoreSessionAsync()
        {
            if (!File.Exists(AuthFilePath))
                return null;

            MicrosoftOAuthResponse? response;
            try
            {
                response = readSession();
            }
            catch
            {
                DeleteAuthFile();
                _snackbarService.Show("Session invalid", "Saved session could not be read. Please log in again.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return null;
            }

            if (response == null || !response.Validate() || string.IsNullOrEmpty(response.RefreshToken))
            {
                DeleteAuthFile();
                if (response != null && !response.Validate())
                    _snackbarService.Show("Session expired", "Your saved session has expired. Please log in again.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return null;
            }

            // Silent restore can transiently fail right after app start (Wi-Fi/stack not up yet,
            // MS auth hiccup). Retry a few times before falling back to the interactive flow --
            // one unlucky request used to kill auto-login for the whole session.
            Exception? lastError = null;
            for (int attempt = 1; attempt <= SilentRestoreAttempts; attempt++)
            {
                try
                {
                    response = await oauth.AuthenticateSilently(response.RefreshToken!);
                    DiagLog.Write($"[OAUTH] silent session restore succeeded on attempt {attempt}");
                    CompleteLogin(response, "Logged in with previous session");
                    return response;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    DiagLog.Write($"[OAUTH] silent restore attempt {attempt}/{SilentRestoreAttempts} failed: {ex.GetType().Name}: {ex.Message}");
                    if (attempt < SilentRestoreAttempts)
                        await Task.Delay(SilentRestoreRetryDelay);
                }
            }
            _snackbarService.Show("Session invalid", $"Could not restore the saved session ({lastError?.GetType().Name}). Please log in again.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            ClearProfileState();
            return await TryInteractiveLoginAsync();
        }

        private async Task<MicrosoftOAuthResponse?> TryInteractiveLoginAsync()
        {
            try
            {
                var response = await oauth.AuthenticateInteractively();
                DiagLog.Write("[OAUTH] interactive login succeeded");
                CompleteLogin(response, "Logged in");
                return response;
            }
            catch (Exception ex)
            {
                DiagLog.Write($"[OAUTH] interactive login failed/cancelled: {ex.GetType().Name}: {ex.Message}");
                _snackbarService.Show("Error", "Failed to authenticate", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                // Don't leave the app wedged: with OAuthLogin persisted as true but NO session, the
                // XauthWorker idles in the OAuth branch and NOTHING attempts a login again -- the
                // exact "auto-login didn't work and it never recovered" state. Flipping the flag
                // re-enables the memory-scan fallback (and the Login button still works for OAuth).
                Settings.OAuthLogin = false;
                PersistEventsToken();
                return null;
            }
        }

        private async void GenerateTokens(MicrosoftOAuthResponse response)
        {
            DiagLog.Write($"[OAUTH] generating XAUTH via device token + SISU...");
            XboxSisuResponse sisuResult;
            try
            {
                var deviceToken = await xboxSignedClient.RequestDeviceToken(XboxDeviceTypes.Win32, "0.0.0");
                sisuResult = await xboxSignedClient.SisuAuth(new XboxSisuAuthRequest
                {
                    AccessToken = response.AccessToken,
                    ClientId = XboxGameTitles.XboxAppPC,
                    DeviceToken = deviceToken.Token,
                    RelyingParty = XboxAuthConstants.XboxLiveRelyingParty,
                });
            }
            catch (Exception ex)
            {
                // These awaits used to sit OUTSIDE the try: a failed device-token/SISU call escaped
                // the async void entirely -- auto-login "silently didn't work" with no snackbar and
                // no log line. Catch, surface, and leave the app in a recoverable state.
                DiagLog.Write($"[OAUTH] device token/SISU request failed: {ex.GetType().Name}: {ex.Message}");
                _snackbarService.Show("Error", $"XAUTH generation failed: {ex.Message}", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                LoginText = "Login";
                Settings.OAuthLogin = false;
                PersistEventsToken();
                return;
            }
            try
            {
                XAUTH = $"XBL3.0 x={sisuResult.AuthorizationToken.XuiClaims.UserHash};{sisuResult.AuthorizationToken.Token}";
                var xui = sisuResult.AuthorizationToken.XuiClaims;
                XUIDOnly = xui?.XboxUserId ?? "";
                if (!string.IsNullOrEmpty(XUIDOnly))
                {
                    IsLoggedIn = true;
                    XAUTHTested = true;
                    InitComplete = true;
                    DiagLog.Write($"[OAUTH] XAUTH generated OK (xuid={XUIDOnly}, tokenLen={XAUTH.Length})");
                    if (Settings.PrivacyMode)
                    {
                        GamerTag = "Gamertag: Hidden";
                        Xuid = "XUID: Hidden";
                    }
                    else
                    {
                        GamerTag = $"Gamertag: {xui?.Gamertag ?? "Unknown"}";
                        Xuid = $"XUID: {XUIDOnly}";
                    }
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write($"[OAUTH] XAUTH generation FAILED: {ex.GetType().Name}: {ex.Message}");
                _snackbarService.Show("Error", "Failed to generate XAUTH", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
            LoginText = "Logout";
            XauthWorker_ProgressChanged(null, null);
            if (IsLoggedIn && !GrabbedProfile)
                GrabProfile(force: true);

            // Start the events token worker to periodically check/refresh the token
            StartWorker(EventsTokenWorker);
        }
        private void ClearProfileState()
        {
            IsLoggedIn = false;
            XAUTHTested = false;
            GrabbedProfile = false;
            XAUTH = "";
            XUIDOnly = "";
            GamerTag = "Gamertag: Unknown   ";
            Xuid = "XUID: Unknown";
            GamerPic = "pack://application:,,,/Assets/cirno.png";
            GamerScore = "Gamerscore: Unknown";
            ProfileRep = "Reputation: Unknown";
            AccountTier = "Tier: Unknown";
            CurrentlyPlaying = "Currently Playing: Unknown";
            ActiveDevice = "Active Device: Unknown";
            IsVerified = "Verified: Unknown";
            Location = "Location: Unknown";
            Tenure = "Tenure: Unknown";
            Following = "Following: Unknown";
            Followers = "Followers: Unknown";
            Gamepass = "Gamepass: Unknown";
            Bio = "Bio: Unknown";
            Watermarks.Clear();
            AchievementsViewModel.EventsToken = null;
            _eventsTokenObtainedAt = DateTime.MinValue;
            _eventsUserHash = null;
            XauthWorker_ProgressChanged(null, null);
        }

        private static readonly byte[] AuthFileMagic = Encoding.ASCII.GetBytes("XAU1");
        private static readonly byte[] AuthDpapiEntropy = Encoding.UTF8.GetBytes("XAU-Auth-v1");

        private MicrosoftOAuthResponse readSession()
        {
            var raw = File.ReadAllBytes(AuthFilePath);
            string json;
            if (raw.Length >= AuthFileMagic.Length && raw.AsSpan(0, AuthFileMagic.Length).SequenceEqual(AuthFileMagic))
            {
                var encrypted = raw.AsSpan(AuthFileMagic.Length).ToArray();
                var plain = ProtectedData.Unprotect(encrypted, AuthDpapiEntropy, DataProtectionScope.CurrentUser);
                json = Encoding.UTF8.GetString(plain);
            }
            else
            {
                json = Encoding.UTF8.GetString(raw);
            }
            var response = JsonConvert.DeserializeObject<MicrosoftOAuthResponse>(json);
            return response;
        }

        private void writeSession(MicrosoftOAuthResponse response)
        {
            var json = JsonConvert.SerializeObject(response);
            var plain = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(plain, AuthDpapiEntropy, DataProtectionScope.CurrentUser);
            using (var fs = new FileStream(AuthFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(AuthFileMagic, 0, AuthFileMagic.Length);
                fs.Write(encrypted, 0, encrypted.Length);
            }
        }

        #endregion
        #region Profile
        /// <summary>
        /// Decides whether a profile fetch is allowed to start. Prevents the ~1s XauthWorker
        /// progress ticks (and the login/refresh paths) from re-invoking the async-void
        /// GrabProfile while an earlier fetch is still awaiting its network calls — which used
        /// to stack a burst of "Profile information grabbed" snackbars.
        /// - Automatic callers (worker ticks / login): require a logged-in user that has not
        ///   already been grabbed, and require no fetch currently running.
        /// - Manual caller (Refresh Profile button, force=true): allow a re-grab even when
        ///   already grabbed, but still do not stack while a fetch is in-flight.
        /// </summary>
        public static bool ShouldStartProfileGrab(bool isLoggedIn, bool alreadyGrabbed, bool inFlight, bool force = false)
        {
            if (inFlight)
                return false;
            if (!isLoggedIn)
                return false;
            return force || !alreadyGrabbed;
        }

        private async void GrabProfile(bool force = false)
        {
            // The guard runs synchronously before the first await, so the ~1s progress ticks
            // delivered on the dispatcher can never slip a second concurrent fetch in while the
            // first is still pending. The in-flight flag is cleared in the finally block below.
            if (!ShouldStartProfileGrab(IsLoggedIn, GrabbedProfile, _grabProfileInFlight, force))
                return;

            _grabProfileInFlight = true;
            try
            {
                var profileResponse = await _xboxRestAPI.Value.GetProfileAsync(XUIDOnly);

                if (profileResponse?.People?.Any() != true)
                {
                    _snackbarService.Show("Error", "Failed to grab profile information.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                var person = profileResponse.People.FirstOrDefault();
                if (Settings.PrivacyMode)
                {
                    // Display hidden profile details for privacy mode
                    GamerTag = "Gamertag: Hidden";
                    Xuid = "XUID: Hidden";
                    GamerPic = "pack://application:,,,/Assets/cirno.png";
                    GamerScore = "Gamerscore: Hidden";
                    ProfileRep = "Reputation: Hidden";
                    AccountTier = "Tier: Hidden";
                    CurrentlyPlaying = "Currently Playing: Hidden";
                    ActiveDevice = "Active Device: Hidden";
                    IsVerified = "Verified: Hidden";
                    Location = "Location: Hidden";
                    Tenure = "Tenure: Hidden";
                    Following = "Following: Hidden";
                    Followers = "Followers: Hidden";
                    Gamepass = "Gamepass: Hidden";
                    Bio = "Bio: Hidden";
                }
                else
                {
                    // Populate user profile details
                    GamerTag = $"Gamertag: {person?.Gamertag ?? "Unknown"}";
                    Xuid = $"XUID: {person?.Xuid ?? "Unknown"}";
                    GamerPic = (person?.DisplayPicRaw?.Replace("&mode=Padding", "")) ?? "pack://application:,,,/Assets/default.png";
                    GamerScore = $"Gamerscore: {person?.GamerScore ?? "Unknown"}";
                    ProfileRep = $"Reputation: {person?.XboxOneRep ?? "Unknown"}";
                    AccountTier = $"Tier: {person?.Detail?.AccountTier ?? "Unknown"}";

                    // Currently playing information
                    var presence = person?.PresenceDetails?.FirstOrDefault();
                    if (presence?.TitleId == null)
                    {
                        CurrentlyPlaying = "Currently Playing: Unknown (No Presence)";
                    }
                    else
                    {
                        var gameTitle = await _xboxRestAPI.Value.GetGameTitleAsync(XUIDOnly, presence.TitleId);
                        CurrentlyPlaying = gameTitle?.Titles?.FirstOrDefault()?.Name ?? $"Currently Playing: Unknown ({presence.TitleId})";
                    }

                    // Retrieve Gamepass Membership Information
                    try
                    {
                        var gpuResponse = await _xboxRestAPI.Value.GetGamepassMembershipAsync(XUIDOnly);
                        Gamepass = $"Gamepass: {gpuResponse?.GamepassMembership ?? gpuResponse?.Data?.GamepassMembership ?? "Unknown"}";
                    }
                    catch
                    {
                        Gamepass = "Gamepass: Unknown";
                    }

                    // Active Device Information
                    ActiveDevice = $"Active Device: {presence?.Device ?? "Unknown"}";

                    // Detailed profile information
                    if (person?.Detail != null)
                    {
                        IsVerified = $"Verified: {person.Detail.IsVerified}";
                        Location = $"Location: {person.Detail.Location ?? "Unknown"}";
                        Tenure = $"Tenure: {person.Detail.Tenure ?? "Unknown"}";
                        Following = $"Following: {person.Detail.FollowingCount}";
                        Followers = $"Followers: {person.Detail.FollowerCount}";
                        Bio = $"Bio: {person.Detail.Bio ?? "No Bio"}";

                        // Handle Watermarks
                        Watermarks.Clear();

                        // Parse Tenure Badge
                        if (int.TryParse(person.Detail.Tenure, out int tenureInt))
                        {
                            string tenureBadge = tenureInt.ToString("D2");
                            Watermarks.Add(new ImageItem { ImageUrl = $@"{BasicXboxAPIUris.WatermarksUrl}tenure/{tenureBadge}.png" });
                        }
                        else
                        {
                            Console.WriteLine("The tenure string is not a valid integer.");
                        }

                        // Add Launch Watermarks
                        if (person.Detail.Watermarks != null)
                        {
                            foreach (var watermark in person.Detail.Watermarks)
                            {
                                Watermarks.Add(new ImageItem { ImageUrl = $@"{BasicXboxAPIUris.WatermarksUrl}launch/{watermark.ToLower()}.png" });
                            }
                        }
                    }
                }

                GrabbedProfile = true;
                _snackbarService.Show("Success", "Profile information grabbed.", ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                IsLoggedIn = false;
                XAUTHTested = true;
                _snackbarService.Show("401 Unauthorized", "Session expired -- attempting to re-authenticate...", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                await TryRecoverAuthAsync();
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", "Failed to grab profile information. " + ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
            finally
            {
                _grabProfileInFlight = false;
            }
        }
        #endregion

        #region Settings

        public static XAUSettings Settings = new();

        private void LoadSettings()
        {
            // A corrupt/truncated settings.json (or a lock from a parallel write) used to crash
            // startup right here. Degrade to defaults instead; the user can still save over it.
            string? settingsJson;
            XAUSettings? settings;
            try
            {
                settingsJson = File.ReadAllText(SettingsFilePath);
                settings = JsonConvert.DeserializeObject<XAUSettings>(settingsJson);
            }
            catch (Exception ex)
            {
                DiagLog.Write($"[SETTINGS] failed to read/deserialise {SettingsFilePath}: {ex.GetType().Name}: {ex.Message} -- continuing with in-memory defaults.");
                _snackbarService.Show(
                    "Settings load failed",
                    "settings.json could not be read; defaults are in use. Saving settings will overwrite the bad file.",
                    ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), _snackbarDuration);
                return;
            }
            if (settings == null)
            {
                _snackbarService.Show(
                    "Error",
                    "Couldn't load settings.",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24)
                );
                return;
            }

            Settings.SettingsVersion = settings.SettingsVersion;
            Settings.ToolVersion = settings.ToolVersion;
            Settings.UnlockAllEnabled = settings.UnlockAllEnabled;
            Settings.AutoSpooferEnabled = settings.AutoSpooferEnabled;
            Settings.AutoLaunchXboxAppEnabled = settings.AutoLaunchXboxAppEnabled;
            Settings.LaunchHidden = settings.LaunchHidden;
            Settings.FakeSignatureEnabled = settings.FakeSignatureEnabled;
            Settings.RegionOverride = settings.RegionOverride;
            Settings.UseAcrylic = settings.UseAcrylic;
            Settings.PrivacyMode = settings.PrivacyMode;
            Settings.OAuthLogin = settings.OAuthLogin;
            Settings.EnableDiagnosticsLog = settings.EnableDiagnosticsLog;
            DiagLog.Enabled = Settings.EnableDiagnosticsLog;
            Settings.XauthScanReadLength = settings.XauthScanReadLength;
            ScanReadLength = NormalizeScanReadLength(Settings.XauthScanReadLength);
            Settings.AutoGrabEventsToken = settings.AutoGrabEventsToken;
            Settings.CachedEventsToken = settings.CachedEventsToken;
            Settings.EventsTokenObtainedAt = settings.EventsTokenObtainedAt;
            Settings.EventsUserHash = settings.EventsUserHash;
            _eventsUserHash = settings.EventsUserHash;

            // Restore cached events token if it's still fresh
            if (!string.IsNullOrEmpty(settings.CachedEventsToken) && settings.EventsTokenObtainedAt.HasValue)
            {
                var age = DateTime.UtcNow - settings.EventsTokenObtainedAt.Value;
                if (age < EventsTokenMaxAge)
                {
                    AchievementsViewModel.EventsToken = settings.CachedEventsToken;
                    _eventsTokenObtainedAt = settings.EventsTokenObtainedAt.Value;
                    EventsLog($"Restored cached events token (age: {age.TotalHours:F1}h)");
                }
                else
                {
                    EventsLog($"Cached events token expired (age: {age.TotalHours:F1}h), will re-grab");
                }
            }
        }

        #endregion
    }
}
