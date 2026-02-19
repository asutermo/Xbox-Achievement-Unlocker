using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
        public static string ToolVersion = "EmptyDevToolVersion";
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
            _snackbarService = snackbarService;
            _contentDialogService = contentDialogService;

            // Assume XAUTH and System Language are set by the time this is actually instantiated
            _xboxRestAPI = new Lazy<XboxRestAPI>(() => new XboxRestAPI(XAUTH));
        }
        private readonly ISnackbarService _snackbarService;
        private TimeSpan _snackbarDuration = TimeSpan.FromSeconds(2);
        private readonly IContentDialogService _contentDialogService;

        private const string XAuthScanPattern = "58 42 4C 33 2E 30 20 78 3D";
        // "x:XBL3.0 x=" - events-specific token prefix used in the tickets header
        private const string EventsTokenScanPattern = "78 3A 58 42 4C 33 2E 30 20 78 3D";
        // UTF-16LE versions for scanning managed (.NET/WinRT) strings
        // "XBL3.0 x=" in UTF-16LE: each ASCII char followed by 0x00
        private const string XAuthScanPatternW = "58 00 42 00 4C 00 33 00 2E 00 30 00 20 00 78 00 3D 00";
        // "x:XBL3.0 x=" in UTF-16LE
        private const string EventsTokenScanPatternW = "78 00 3A 00 58 00 42 00 4C 00 33 00 2E 00 30 00 20 00 78 00 3D 00";

        [RelayCommand]
        private void RefreshProfile()
        {
            GrabProfile();
        }

        Mem m = new Mem();
        Mem eventsMem = new Mem();
        public BackgroundWorker XauthWorker = new BackgroundWorker();
        public BackgroundWorker EventsTokenWorker = new BackgroundWorker();
        bool IsAttached = false;
        bool GrabbedProfile = false;
        bool eventsTokenFound = false;
        public static bool XAUTHTested = false;
        public static string XAUTH = "";
        public static string XUIDOnly;
        public static bool InitComplete = false;
        private bool _isInitialized = false;
        string SettingsFilePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU"), "settings.json");
        string EventsMetaFilePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU"), "Events", "meta.json");
        string AuthFilePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU"), "auth.json");
        public CodeFlowAuthenticator oauth;
        public XboxAuthClient xboxAuthClient;
        public XboxSignedClient xboxSignedClient;

        public async void OnNavigatedTo()
        {
            if (!_isInitialized)
                await InitializeViewModel();
        }
        public void OnNavigatedFrom() { }

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
            await CheckForToolUpdates();
            XauthWorker.DoWork += XauthWorker_DoWork;
            XauthWorker.ProgressChanged += XauthWorker_ProgressChanged;
            XauthWorker.RunWorkerCompleted += XauthWorker_RunWorkerCompleted;
            XauthWorker.WorkerReportsProgress = true;
            XauthWorker.RunWorkerAsync();
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
                    if (!XAUTHTested && XAUTH.Length > 0)
                    {
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
            if (!XauthWorker.IsBusy)
                XauthWorker.RunWorkerAsync();
        }
        private async void GetXAUTH()
        {
            IEnumerable<long> XauthScanList = await m.AoBScan(XAuthScanPattern, true);
            string[] XauthStrings = new string[XauthScanList.Count()];
            var i = 0;
            foreach (var address in XauthScanList)
            {
                XauthStrings[i] = m.ReadString(address.ToString("X"), length: 10000);
                i++;
            }

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
                return;
            }

            string mostCommon = XauthStrings[0];
            int highestFrequency = 0;
            foreach (KeyValuePair<string, int> pair in frequency)
            {
                if (pair.Value > highestFrequency)
                {
                    mostCommon = pair.Key;
                    highestFrequency = pair.Value;
                }
            }

            if (highestFrequency > 3)
            {
                XAUTH = mostCommon;
                XAUTHTested = false;
            }
        }
        private async void TestXAUTH()
        {
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

                // Start the events token worker to periodically check/refresh the token
                if (Settings.AutoGrabEventsToken && !EventsTokenWorker.IsBusy)
                    EventsTokenWorker.RunWorkerAsync();
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    IsLoggedIn = false;
                    XAUTHTested = true;

                }
            }
        }
        #endregion

        #region EventsToken
        private bool solitaireLaunchedByUs = false;

        // How often the worker loop checks
        private static readonly TimeSpan EventsTokenCheckInterval = TimeSpan.FromMinutes(10);
        // How old a token can be before we proactively refresh it (~24h XSTS expiry, refresh early)
        private static readonly TimeSpan EventsTokenMaxAge = TimeSpan.FromHours(23);

        private static DateTime _eventsTokenObtainedAt = DateTime.MinValue;
        // The user hash for the events relying party - used to identify the correct token in memory
        private static string _eventsUserHash = null;

        private static readonly string EventsLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "events_debug.log");

        private static void EventsLog(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Debug.WriteLine($"[EventsToken] {msg}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(EventsLogPath)!);
                File.AppendAllText(EventsLogPath, line + Environment.NewLine);
            }
            catch { /* best-effort */ }
        }


        private void PersistEventsToken()
        {
            try
            {
                Settings.CachedEventsToken = AchievementsViewModel.EventsToken;
                Settings.EventsTokenObtainedAt = _eventsTokenObtainedAt;
                Settings.EventsUserHash = _eventsUserHash;
                var json = JsonConvert.SerializeObject(Settings);
                File.WriteAllText(SettingsFilePath, json);
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
                var tokenAge = DateTime.UtcNow - _eventsTokenObtainedAt;
                bool isExpired = !isEmpty && isValid && tokenAge > EventsTokenMaxAge;

                EventsLog($"Check: empty={isEmpty}, valid={isValid}, age={tokenAge.TotalMinutes:F0}m, expired={isExpired}");

                if (isEmpty || !isValid || isExpired)
                {
                    if (isExpired)
                        EventsLog($"Token expired (age: {tokenAge.TotalMinutes:F0}m > {EventsTokenMaxAge.TotalMinutes:F0}m), refreshing...");
                    else
                        EventsLog("Token missing/invalid, refreshing...");

                    // Primary: try DiagTrack memory scan (GRTS tokens that actually work)
                    EventsLog("Trying DiagTrack scan first...");
                    var diagToken = ScanDiagTrackForToken();
                    if (!string.IsNullOrEmpty(diagToken))
                    {
                        AchievementsViewModel.EventsToken = diagToken;
                        _eventsTokenObtainedAt = DateTime.UtcNow;
                        PersistEventsToken();
                        EventsLog("DiagTrack scan success. GRTS events token obtained.");
                    }
                    else
                    {
                        // Fallback: SISU (produces structurally valid but non-working tokens)
                        EventsLog("DiagTrack scan failed, falling back to SISU...");
                        RefreshEventsTokenViaSisu();

                        if (!string.IsNullOrEmpty(AchievementsViewModel.EventsToken))
                        {
                            _eventsTokenObtainedAt = DateTime.UtcNow;
                            PersistEventsToken();
                            EventsLog("SISU refresh complete (note: SISU tokens may not work for achievements).");
                        }
                        else
                        {
                            EventsLog("SISU refresh also failed. No token obtained.");
                        }
                    }
                }

                EventsLog($"Sleeping {EventsTokenCheckInterval.TotalMinutes:F0}m...");
                Thread.Sleep(EventsTokenCheckInterval);
            }
        }

        private void RefreshEventsTokenViaSisu()
        {
            try
            {
                var session = readSession();
                if (session == null || string.IsNullOrEmpty(session.AccessToken))
                {
                    EventsLog("Refresh: no saved session/access token");
                    return;
                }

                EventsLog("Refresh: requesting device token...");
                var deviceToken = xboxSignedClient.RequestDeviceToken(XboxDeviceTypes.Win32, "0.0.0").GetAwaiter().GetResult();

                // Use SISU with Solitaire's MSA App ID for title-scoped events token
                var eventsToken = RequestEventsTokenViaSisu(session.AccessToken, deviceToken.Token, XboxGameTitles.Solitaire).GetAwaiter().GetResult();
                if (eventsToken != null)
                {
                    _eventsUserHash = eventsToken.XuiClaims?.UserHash;
                    AchievementsViewModel.EventsToken = $"x:XBL3.0 x={_eventsUserHash};{eventsToken.Token}";
                    EventsLog($"SISU refresh success: hash={_eventsUserHash}");
                }
                else
                {
                    EventsLog("SISU refresh returned null");
                }
            }
            catch (Exception ex)
            {
                EventsLog($"SISU refresh error: {ex.Message}");
            }
        }

        /// <summary>
        /// Use SISU to get an events token with a real game's MSA App ID.
        /// SISU is the only endpoint that can produce events RP tokens (direct XSTS returns 8015dc37).
        /// Using a game's MSA App ID instead of Xbox App PC should produce a properly title-scoped token.
        /// </summary>
        private async Task<XboxAuthResponse?> RequestEventsTokenViaSisu(string accessToken, string deviceToken, string gameAppId)
        {
            EventsLog($"SISU events: requesting with AppId={gameAppId}, RP={XboxAuthConstants.XboxEventsRelyingParty}");
            var sisuResult = await xboxSignedClient.SisuAuth(new XboxSisuAuthRequest
            {
                AccessToken = accessToken,
                ClientId = gameAppId,
                DeviceToken = deviceToken,
                RelyingParty = XboxAuthConstants.XboxEventsRelyingParty,
            });

            EventsLog($"SISU events response: AuthToken={sisuResult.AuthorizationToken != null}, " +
                       $"TitleToken={sisuResult.TitleToken != null}, " +
                       $"UserToken={sisuResult.UserToken != null}");

            var authToken = sisuResult.AuthorizationToken;
            if (authToken != null)
            {
                EventsLog($"  AuthToken: hash={authToken.XuiClaims?.UserHash}, expires={authToken.ExpireOn}");
                // Decode and log the full JWE header to verify RP/audience
                try
                {
                    var token = authToken.Token;
                    if (!string.IsNullOrEmpty(token))
                    {
                        var firstDot = token.IndexOf('.');
                        if (firstDot > 0)
                        {
                            var headerB64 = token.Substring(0, firstDot);
                            // Fix base64url padding
                            var padded = headerB64.Replace('-', '+').Replace('_', '/');
                            switch (padded.Length % 4)
                            {
                                case 2: padded += "=="; break;
                                case 3: padded += "="; break;
                            }
                            var headerJson = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                            EventsLog($"  JWE Header: {headerJson}");
                        }
                        // Log token length and part count for format comparison
                        var parts = token.Split('.');
                        EventsLog($"  Token parts: {parts.Length}, total length: {token.Length}");
                    }
                }
                catch (Exception ex)
                {
                    EventsLog($"  Header decode error: {ex.Message}");
                }
            }
            if (sisuResult.TitleToken != null)
                EventsLog($"  TitleToken: expires={sisuResult.TitleToken.ExpireOn}");

            return authToken;
        }

        /// <summary>
        /// Fires a test Solitaire event (achievement 67) to verify the SISU events token.
        /// Logs the full HTTP response so we can see if the server accepts or rejects it.
        /// </summary>
        private async Task TestEventsTokenAsync()
        {
            try
            {
                var eventsToken = AchievementsViewModel.EventsToken;
                if (string.IsNullOrEmpty(eventsToken) || string.IsNullOrEmpty(XUIDOnly) || string.IsNullOrEmpty(XAUTH))
                {
                    EventsLog("[TEST] Skipping: missing eventsToken, XUID, or XAUTH");
                    return;
                }

                EventsLog($"[TEST] XUID={XUIDOnly}, XAUTH hash={XAUTH.Substring(0, Math.Min(30, XAUTH.Length))}...");
                EventsLog("[TEST] Firing test event: Solitaire achievement 56 'Call the Expert' (LevelEarned, GameMode=0, Level=50)");

                // Build the event JSON from the template with replacements for achievement 56
                var timestamp = DateTime.UtcNow;
                var seq = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var eventName = "Microsoft.XboxLive.T85494077.LevelEarned";
                var metadata = "{\"f\":{\"baseData\":{\"f\":{\"properties\":{\"f\":{\"GameMode\":4,\"Level\":4}}}}},\"policies\":1}";
                var data = $"{{\"baseType\":\"Microsoft.XboxLive.InGame\",\"baseData\":{{\"name\":\"LevelEarned\",\"serviceConfigId\":\"2eba0100-c9de-47f7-bfaa-6def0518893d\",\"playerSessionId\":\"11111111-1111-1111-1111-111111111111\",\"titleId\":85494077,\"userId\":\"{XUIDOnly}\",\"ver\":1,\"properties\":{{\"GameMode\":0,\"Level\":50}},\"measurements\":{{}}}}}}";

                var body = new JObject
                {
                    ["ver"] = "4.0",
                    ["name"] = eventName,
                    ["time"] = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                    ["iKey"] = "o:0890af88a9ed4cc886a14f5e174a2827",
                    ["ext"] = new JObject
                    {
                        ["utc"] = new JObject
                        {
                            ["shellId"] = 1,
                            ["eventFlags"] = 1,
                            ["pgName"] = "XBOX",
                            ["flags"] = 1,
                            ["epoch"] = "1",
                            ["seq"] = seq
                        },
                        ["privacy"] = new JObject { ["isRequired"] = false },
                        ["metadata"] = JToken.Parse(metadata),
                        ["os"] = new JObject
                        {
                            ["bootId"] = 1,
                            ["name"] = "Windows",
                            ["ver"] = "1",
                            ["expId"] = "1"
                        },
                        ["app"] = new JObject
                        {
                            ["id"] = "U:Microsoft.MicrosoftSolitaireCollection_4.23.7100.0_x64__8wekyb3d8bbwe!App",
                            ["ver"] = "4.23.7100.0_x64_!2025/07/11:01:04:47!0!solitaire.exe",
                            ["is1P"] = 1,
                            ["asId"] = 1
                        },
                        ["device"] = new JObject
                        {
                            ["localId"] = "s:5F885A49-7DE3-47FE-8CFD-B9E228D065FE",
                            ["deviceClass"] = "Windows.Desktop"
                        },
                        ["protocol"] = new JObject
                        {
                            ["devMake"] = "1",
                            ["devModel"] = "1",
                            ["ticketKeys"] = new JArray("1", "1")
                        },
                        ["user"] = new JObject { ["localId"] = "m:1111111111111111" },
                        ["loc"] = new JObject { ["tz"] = "00:00" }
                    },
                    ["data"] = JToken.Parse(data)
                };

                var bodyStr = body.ToString(Formatting.None);
                EventsLog($"[TEST] Body length: {bodyStr.Length}");

                var api = new XboxRestAPI(XAUTH);
                var content = new StringContent(bodyStr, Encoding.UTF8, "application/x-json-stream");
                await api.UnlockEventBasedAchievement(eventsToken, content);

                EventsLog("[TEST] Request sent - check console for HTTP response");
            }
            catch (Exception ex)
            {
                EventsLog($"[TEST] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Launches Solitaire (if needed), scans its memory for the events token,
        /// and closes it again if we launched it.
        /// </summary>
        private void GrabEventsTokenFromSolitaire()
        {
            bool alreadyRunning = Process.GetProcessesByName(ProcessNames.Solitaire).Length > 0;
            EventsLog($"Solitaire already running: {alreadyRunning}");

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
                    return;
                }
            }

            // Give Xbox Live services time to initialise and fire initial telemetry events.
            // The events XSTS token only appears in memory after the game actually sends events,
            // which can take 20-40s after launch (Xbox Live init + first telemetry batch).
            EventsLog("Waiting 20s for Xbox Live init + initial events...");
            Thread.Sleep(20000);

            // Retry the scan over ~2 minutes - events may not fire immediately
            const int maxAttempts = 18;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                EventsLog($"Scan attempt {attempt + 1}/{maxAttempts}");

                if (!string.IsNullOrEmpty(AchievementsViewModel.EventsToken))
                {
                    EventsLog("Token already set externally, done");
                    eventsTokenFound = true;
                    break;
                }

                // Primary: scan DiagTrack (holds GRTS events tokens that actually work)
                string token = ScanDiagTrackForToken();
                if (!string.IsNullOrEmpty(token))
                {
                    EventsLog($"DiagTrack token found: {token.Substring(0, Math.Min(30, token.Length))}...");
                    AchievementsViewModel.EventsToken = token;
                    _eventsTokenObtainedAt = DateTime.UtcNow;
                    eventsTokenFound = true;
                    break;
                }

                // Fallback: scan Solitaire directly (finds XAUTH tokens, less reliable for events)
                token = ScanSolitaireForToken();
                if (!string.IsNullOrEmpty(token))
                {
                    EventsLog($"Solitaire token found: {token.Substring(0, Math.Min(30, token.Length))}...");
                    AchievementsViewModel.EventsToken = token;
                    _eventsTokenObtainedAt = DateTime.UtcNow;
                    eventsTokenFound = true;
                    break;
                }

                EventsLog("No token found, waiting 7s before retry...");
                Thread.Sleep(7000);
            }

            if (!eventsTokenFound)
                EventsLog("All scan attempts failed (scanned for ~2.5 minutes)");

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

        private string ScanSolitaireForToken()
        {
            var procs = Process.GetProcessesByName(ProcessNames.Solitaire);
            if (procs.Length == 0)
            {
                EventsLog("ScanSolitaire: no Solitaire process found");
                return null;
            }

            int pid = procs[0].Id;
            EventsLog($"ScanSolitaire: found PID {pid}");

            try
            {
                if (!eventsMem.OpenProcess(pid, out string failReason))
                {
                    EventsLog($"ScanSolitaire: OpenProcess failed: {failReason}");
                    return null;
                }

                // === ASCII scans (HTTP wire buffers) ===
                EventsLog("ScanSolitaire: ASCII primary scan for 'x:XBL3.0 x='...");
                var results = eventsMem.AoBScan(0, long.MaxValue, EventsTokenScanPattern, true, true, true, false).Result;
                EventsLog($"ScanSolitaire: ASCII primary found {results.Count()} matches");
                if (results.Any())
                {
                    string token = FindBestToken(results, "x:XBL3.0", Encoding.UTF8);
                    if (token != null)
                        return token;
                    EventsLog("ScanSolitaire: ASCII primary didn't yield a valid token");
                }

                // === UTF-16 scans (managed/WinRT strings) ===
                EventsLog("ScanSolitaire: UTF-16 primary scan for 'x:XBL3.0 x='...");
                var resultsW = eventsMem.AoBScan(0, long.MaxValue, EventsTokenScanPatternW, true, true, true, false).Result;
                EventsLog($"ScanSolitaire: UTF-16 primary found {resultsW.Count()} matches");
                if (resultsW.Any())
                {
                    string token = FindBestToken(resultsW, "x:XBL3.0", Encoding.Unicode);
                    if (token != null)
                        return token;
                    EventsLog("ScanSolitaire: UTF-16 primary didn't yield a valid token");
                }

                EventsLog("ScanSolitaire: UTF-16 fallback scan for 'XBL3.0 x='...");
                var fallbackW = eventsMem.AoBScan(0, long.MaxValue, XAuthScanPatternW, true, true, true, false).Result;
                EventsLog($"ScanSolitaire: UTF-16 fallback found {fallbackW.Count()} matches");
                if (fallbackW.Any())
                {
                    string token = FindFallbackEventsToken(fallbackW, Encoding.Unicode);
                    if (token != null)
                        return "x:" + token;
                    EventsLog("ScanSolitaire: UTF-16 fallback didn't yield a valid token");
                }

                // === ASCII fallback (least reliable) ===
                EventsLog("ScanSolitaire: ASCII fallback scan for 'XBL3.0 x='...");
                var fallbackResults = eventsMem.AoBScan(0, long.MaxValue, XAuthScanPattern, true, true, true, false).Result;
                EventsLog($"ScanSolitaire: ASCII fallback found {fallbackResults.Count()} matches");
                if (fallbackResults.Any())
                {
                    string token = FindFallbackEventsToken(fallbackResults, Encoding.UTF8);
                    if (token != null)
                        return "x:" + token;
                    EventsLog("ScanSolitaire: ASCII fallback didn't yield a valid token");
                }
            }
            catch (Exception ex)
            {
                EventsLog($"ScanSolitaire: exception: {ex.Message}");
            }

            return null;
        }

        private string FindBestToken(IEnumerable<long> addresses, string expectedPrefix, Encoding encoding)
        {
            var frequency = new Dictionary<string, int>();
            foreach (var address in addresses)
            {
                string raw = eventsMem.ReadString(address.ToString("X"), length: 10000, stringEncoding: encoding);
                EventsLog($"FindBestToken({encoding.EncodingName}): addr=0x{address:X}, rawLen={raw?.Length ?? -1}, first80={Truncate(raw, 80)}");

                if (string.IsNullOrEmpty(raw))
                    continue;

                // The token in the tickets header is: x:XBL3.0 x={hash};{jwt}
                // ReadString may include trailing data after the token (quotes, semicolons, next entries).
                // Extract just the token: starts at beginning, ends at first quote or null.
                string token = raw;

                // Trim at first double-quote (end of value in tickets header: ..."x:XBL3.0 x=...jwt";"next"=...)
                int quoteIdx = token.IndexOf('"');
                if (quoteIdx > 0)
                    token = token.Substring(0, quoteIdx);

                EventsLog($"FindBestToken({encoding.EncodingName}): cleaned tokenLen={token.Length}, first80={Truncate(token, 80)}");

                if (!token.StartsWith(expectedPrefix) || token.Length < 20)
                    continue;

                if (!frequency.ContainsKey(token))
                    frequency[token] = 1;
                else
                    frequency[token]++;
            }

            if (frequency.Count == 0)
                return null;

            return frequency.OrderByDescending(p => p.Value).First().Key;
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "(null)";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        /// <summary>
        /// Extracts the user hash from "XBL3.0 x={hash};{jwt}" or "x:XBL3.0 x={hash};{jwt}"
        /// </summary>
        private static string ExtractUserHash(string token)
        {
            int xEq = token.IndexOf("x=");
            if (xEq < 0) return null;
            int semi = token.IndexOf(';', xEq);
            if (semi < 0) return null;
            return token.Substring(xEq + 2, semi - (xEq + 2));
        }

        private string FindFallbackEventsToken(IEnumerable<long> addresses, Encoding encoding)
        {
            var frequency = new Dictionary<string, int>();
            foreach (var address in addresses)
            {
                string raw = eventsMem.ReadString(address.ToString("X"), length: 10000, stringEncoding: encoding);
                if (string.IsNullOrEmpty(raw) || !raw.StartsWith("XBL3.0"))
                    continue;

                // Trim at first double-quote (tickets header format: "key"="XBL3.0 x=...jwt";"next")
                string str = raw;
                int quoteIdx = str.IndexOf('"');
                if (quoteIdx > 0)
                    str = str.Substring(0, quoteIdx);

                if (str.Length < 100)
                    continue;

                // Only accept tokens encrypted for the events RP (not XAUTH RP)
                bool isEventsRp = IsEventsRpToken(str);
                EventsLog($"Fallback token: hash={ExtractUserHash(str)}, len={str.Length}, eventsRP={isEventsRp}, first60={Truncate(str, 60)}");

                if (!isEventsRp)
                    continue;

                if (!frequency.ContainsKey(str))
                    frequency[str] = 1;
                else
                    frequency[str]++;
            }

            if (frequency.Count == 0)
                return null;

            var best = frequency.OrderByDescending(p => p.Value).First();
            EventsLog($"FindFallbackEventsToken: selected events RP token (count={best.Value}, len={best.Key.Length})");
            return best.Key;
        }

        #region DiagTrack Scanning

        // Events RP x5t — used to distinguish events tokens from XAUTH tokens.
        // This is the certificate thumbprint for events.xboxlive.com; it appears
        // in the decoded JWE header JSON as "x5t":"9wLGzMJDNz..."
        private const string EventsRpX5t = "9wLGzMJDNz";

        /// <summary>
        /// Checks whether a token was encrypted for the events RP by decoding
        /// the JWE header and verifying the x5t certificate thumbprint.
        /// Token format: "x:XBL3.0 x={hash};{JWE}" or "XBL3.0 x={hash};{JWE}"
        /// </summary>
        private static bool IsEventsRpToken(string token)
        {
            try
            {
                int semiIdx = token.IndexOf(';');
                if (semiIdx < 0) return false;

                string jwe = token.Substring(semiIdx + 1);
                int dotIdx = jwe.IndexOf('.');
                if (dotIdx <= 0) return false;

                string headerB64 = jwe.Substring(0, dotIdx);
                // Base64url → standard Base64
                string padded = headerB64.Replace('-', '+').Replace('_', '/');
                switch (padded.Length % 4)
                {
                    case 2: padded += "=="; break;
                    case 3: padded += "="; break;
                }

                string headerJson = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                return headerJson.Contains(EventsRpX5t);
            }
            catch
            {
                return false;
            }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAll, ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        private static bool EnableSeDebugPrivilege()
        {
            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0028, out IntPtr tokenHandle))
                    return false;
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out long luid))
                    return false;
                var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 0x00000002 };
                return AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch { return false; }
        }

        /// <summary>
        /// Finds the PID of the svchost process hosting the DiagTrack (Connected User Experiences and Telemetry) service.
        /// DiagTrack is the service that sends telemetry events to OneCollector and holds the GRTS events tokens in memory.
        /// </summary>
        private static int GetDiagTrackPid()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId FROM Win32_Service WHERE Name = 'DiagTrack'");
                foreach (var obj in searcher.Get())
                {
                    var pid = Convert.ToInt32(obj["ProcessId"]);
                    if (pid > 0) return pid;
                }
            }
            catch (Exception ex)
            {
                EventsLog($"GetDiagTrackPid WMI error: {ex.Message}");
            }
            return 0;
        }

        /// <summary>
        /// Scans the DiagTrack svchost process memory for GRTS events tokens.
        /// The DiagTrack service holds fully formatted "x:XBL3.0 x={hash};{JWE}" tokens in memory.
        /// We filter by the events RP certificate thumbprint (x5t) to distinguish from XAUTH tokens.
        /// Requires admin privileges (SeDebugPrivilege) since DiagTrack runs as SYSTEM.
        /// </summary>
        private string ScanDiagTrackForToken()
        {
            int pid = GetDiagTrackPid();
            if (pid == 0)
            {
                EventsLog("ScanDiagTrack: DiagTrack service not found or not running");
                return null;
            }
            EventsLog($"ScanDiagTrack: DiagTrack PID={pid}");

            if (!EnableSeDebugPrivilege())
            {
                EventsLog("ScanDiagTrack: Failed to enable SeDebugPrivilege (not running as admin?)");
                return null;
            }

            Mem diagMem = new Mem();
            try
            {
                if (!diagMem.OpenProcess(pid, out string failReason))
                {
                    EventsLog($"ScanDiagTrack: OpenProcess failed: {failReason}");
                    return null;
                }

                // Primary scan: UTF-16 "x:XBL3.0 x=" (most common in DiagTrack memory)
                EventsLog("ScanDiagTrack: UTF-16 scan for 'x:XBL3.0 x='...");
                var resultsW = diagMem.AoBScan(0, long.MaxValue, EventsTokenScanPatternW, true, true, true, false).Result;
                EventsLog($"ScanDiagTrack: UTF-16 found {resultsW.Count()} matches");

                string eventsToken = FindEventsRpToken(diagMem, resultsW, Encoding.Unicode);
                if (eventsToken != null) return eventsToken;

                // Fallback: ASCII "x:XBL3.0 x="
                EventsLog("ScanDiagTrack: ASCII scan for 'x:XBL3.0 x='...");
                var results = diagMem.AoBScan(0, long.MaxValue, EventsTokenScanPattern, true, true, true, false).Result;
                EventsLog($"ScanDiagTrack: ASCII found {results.Count()} matches");

                eventsToken = FindEventsRpToken(diagMem, results, Encoding.UTF8);
                if (eventsToken != null) return eventsToken;

                // Broader fallback: scan for "XBL3.0 x=" (without x: prefix) in UTF-16, then filter for events RP
                EventsLog("ScanDiagTrack: UTF-16 broad scan for 'XBL3.0 x='...");
                var broadW = diagMem.AoBScan(0, long.MaxValue, XAuthScanPatternW, true, true, true, false).Result;
                EventsLog($"ScanDiagTrack: UTF-16 broad found {broadW.Count()} matches");
                eventsToken = FindEventsRpTokenBroad(diagMem, broadW, Encoding.Unicode);
                if (eventsToken != null) return eventsToken;

                // ASCII broad
                EventsLog("ScanDiagTrack: ASCII broad scan for 'XBL3.0 x='...");
                var broadA = diagMem.AoBScan(0, long.MaxValue, XAuthScanPattern, true, true, true, false).Result;
                EventsLog($"ScanDiagTrack: ASCII broad found {broadA.Count()} matches");
                eventsToken = FindEventsRpTokenBroad(diagMem, broadA, Encoding.UTF8);
                if (eventsToken != null) return eventsToken;

                EventsLog("ScanDiagTrack: no events RP token found in DiagTrack memory");
            }
            catch (Exception ex)
            {
                EventsLog($"ScanDiagTrack: exception: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Filters scanned tokens to find one encrypted for the events RP (x5t starts with "9wLGzMJDNz").
        /// This distinguishes real GRTS events tokens from XAUTH tokens which have a different x5t.
        /// </summary>
        private string FindEventsRpToken(Mem mem, IEnumerable<long> addresses, Encoding encoding)
        {
            var candidates = new Dictionary<string, int>();
            foreach (var address in addresses)
            {
                // Read enough bytes for a full GRTS token (~2500 chars). UTF-16 = 2 bytes/char, so need ~6000 bytes.
                string raw = mem.ReadString(address.ToString("X"), length: 8000, stringEncoding: encoding);
                EventsLog($"FindEventsRpToken({encoding.EncodingName}): addr=0x{address:X}, rawLen={raw?.Length ?? -1}, first120={Truncate(raw, 120)}");

                if (string.IsNullOrEmpty(raw) || !raw.StartsWith("x:XBL3.0"))
                {
                    EventsLog($"  skipped: empty or wrong prefix");
                    continue;
                }

                // Trim at first double-quote or control character
                string token = raw;
                int quoteIdx = token.IndexOf('"');
                if (quoteIdx > 0)
                    token = token.Substring(0, quoteIdx);

                EventsLog($"  after trim: len={token.Length}, isEventsRP={IsEventsRpToken(token)}");

                if (token.Length < 100)
                {
                    EventsLog($"  skipped: too short ({token.Length})");
                    continue;
                }

                // Decode JWE header and check if x5t matches the events RP certificate
                if (!IsEventsRpToken(token))
                {
                    EventsLog($"  skipped: wrong RP (x5t doesn't match events RP)");
                    continue;
                }

                EventsLog($"FindEventsRpToken: MATCH at 0x{address:X}, len={token.Length}, hash={ExtractUserHash(token)}");

                if (!candidates.ContainsKey(token))
                    candidates[token] = 1;
                else
                    candidates[token]++;
            }

            if (candidates.Count == 0)
                return null;

            // Return the most frequent match
            var best = candidates.OrderByDescending(p => p.Value).First();
            EventsLog($"FindEventsRpToken: selected token (count={best.Value}, len={best.Key.Length})");
            return best.Key;
        }

        /// <summary>
        /// Like FindEventsRpToken but for broader "XBL3.0 x=" matches (without x: prefix).
        /// Prepends "x:" to form the full events token format.
        /// </summary>
        private string FindEventsRpTokenBroad(Mem mem, IEnumerable<long> addresses, Encoding encoding)
        {
            var candidates = new Dictionary<string, int>();
            foreach (var address in addresses)
            {
                string raw = mem.ReadString(address.ToString("X"), length: 8000, stringEncoding: encoding);
                if (string.IsNullOrEmpty(raw) || !raw.StartsWith("XBL3.0"))
                    continue;

                string token = raw;
                int quoteIdx = token.IndexOf('"');
                if (quoteIdx > 0)
                    token = token.Substring(0, quoteIdx);

                if (token.Length < 100 || !IsEventsRpToken(token))
                    continue;

                // Prepend "x:" to match the expected format
                token = "x:" + token;
                EventsLog($"FindEventsRpTokenBroad: MATCH at 0x{address:X}, len={token.Length}, hash={ExtractUserHash(token)}");

                if (!candidates.ContainsKey(token))
                    candidates[token] = 1;
                else
                    candidates[token]++;
            }

            if (candidates.Count == 0)
                return null;

            var best = candidates.OrderByDescending(p => p.Value).First();
            EventsLog($"FindEventsRpTokenBroad: selected (count={best.Value}, len={best.Key.Length})");
            return best.Key;
        }

        #endregion

        /// <summary>
        /// Manually triggers a scan (from the "Grab from Game" button).
        /// Works regardless of the auto-grab setting.
        /// </summary>
        public void ScanForEventsTokenManual()
        {
            eventsTokenFound = false;
            AchievementsViewModel.EventsToken = null;
            _eventsTokenObtainedAt = DateTime.MinValue;

            System.Threading.Tasks.Task.Run(() =>
            {
                GrabEventsTokenFromSolitaire();
                if (!string.IsNullOrEmpty(AchievementsViewModel.EventsToken))
                {
                    _eventsTokenObtainedAt = DateTime.UtcNow;
                    PersistEventsToken();
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
        #endregion

        #region OAuthLogin

        [RelayCommand]
        private async void OAuthLogin()
        {
            //init oauth stuff
            var OAuthhttpClient = new HttpClient();
            var apiClient = new CodeFlowLiveApiClient(XboxGameTitles.XboxAppPC, XboxAuthConstants.XboxScope, OAuthhttpClient);
            xboxAuthClient = new XboxAuthClient(OAuthhttpClient);
            xboxSignedClient = new XboxSignedClient(OAuthhttpClient);
            oauth = new CodeFlowBuilder(apiClient)
                .WithUIParent(this)
                .Build();
            if (LoginText == "Logout")
            {
                oauth.Signout();
                try { File.Delete(AuthFilePath); } catch { }
                ClearProfileState();
                LoginText = "Login";
                return;
            }
            Settings.OAuthLogin = true;

            // Use saved session if valid; otherwise interactive login
            MicrosoftOAuthResponse? response = await TryRestoreSessionAsync();
            if (response == null)
                response = await TryInteractiveLoginAsync();
        }

        private void DeleteAuthFile()
        {
            try { File.Delete(AuthFilePath); } catch { }
        }

        private void CompleteLogin(MicrosoftOAuthResponse response, string? successMessage = null)
        {
            writeSession(response);
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

            try
            {
                response = await oauth.AuthenticateSilently(response.RefreshToken!);
                CompleteLogin(response, "Logged in with previous session");
                return response;
            }
            catch
            {
                _snackbarService.Show("Session invalid", "You are required to log in again as the session has expired", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                ClearProfileState();
                return await TryInteractiveLoginAsync();
            }
        }

        private async Task<MicrosoftOAuthResponse?> TryInteractiveLoginAsync()
        {
            try
            {
                var response = await oauth.AuthenticateInteractively();
                CompleteLogin(response, "Logged in");
                return response;
            }
            catch
            {
                _snackbarService.Show("Error", "Failed to authenticate", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return null;
            }
        }

        private async void GenerateTokens(MicrosoftOAuthResponse response)
        {
            var deviceToken = await xboxSignedClient.RequestDeviceToken(XboxDeviceTypes.Win32, "0.0.0");
            var sisuResult = await xboxSignedClient.SisuAuth(new XboxSisuAuthRequest
            {
                AccessToken = response.AccessToken,
                ClientId = XboxGameTitles.XboxAppPC,
                DeviceToken = deviceToken.Token,
                RelyingParty = XboxAuthConstants.XboxLiveRelyingParty,
            });
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
            catch
            {
                _snackbarService.Show("Error", "Failed to generate XAUTH", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
            try
            {
                // Use SISU with a real game's MSA App ID for the events RP.
                // Using Solitaire's App ID so the token is title-scoped to a real game.
                var eventsToken = await RequestEventsTokenViaSisu(response.AccessToken, deviceToken.Token, XboxGameTitles.Solitaire);
                if (eventsToken != null)
                {
                    _eventsUserHash = eventsToken.XuiClaims?.UserHash;
                    AchievementsViewModel.EventsToken = $"x:XBL3.0 x={_eventsUserHash};{eventsToken.Token}";
                    _eventsTokenObtainedAt = DateTime.UtcNow;
                    PersistEventsToken();
                    EventsLog($"SISU events token obtained: hash={_eventsUserHash}");

                    // === AUTO-TEST: fire a Solitaire achievement 67 event to check server response ===
                    await TestEventsTokenAsync();
                }
            }
            catch (Exception ex)
            {
                EventsLog($"SISU events token failed: {ex.Message}");
                _snackbarService.Show("Error", "Failed to generate Events Token", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
            LoginText = "Logout";
            XauthWorker_ProgressChanged(null, null);
            if (IsLoggedIn && !GrabbedProfile)
                GrabProfile();

            // Start the events token worker to periodically check/refresh the token
            if (Settings.AutoGrabEventsToken && !EventsTokenWorker.IsBusy)
                EventsTokenWorker.RunWorkerAsync();
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
        private async void GrabProfile()
        {
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
                _snackbarService.Show("401 Unauthorized", "Something went wrong. Retrying.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", "Failed to grab profile information. " + ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
        }
        #endregion

        #region Settings

        public static XAUSettings Settings = new();

        private void LoadSettings()
        {
            var settingsJson = File.ReadAllText(SettingsFilePath);
            var settings = JsonConvert.DeserializeObject<XAUSettings>(settingsJson);
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
