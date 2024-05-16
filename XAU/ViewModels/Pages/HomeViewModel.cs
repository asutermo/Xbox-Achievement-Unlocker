using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        [ObservableProperty] private string _gamerPic = "pack://application:,,,/Assets/cirno.png";
        [ObservableProperty] private string _gamerTag = "Gamertag: Unknown   ";
        [ObservableProperty] private string _xuid = "XUID: Unknown";
        [ObservableProperty] private string _gamerScore = "Gamerscore: Unknown";
        [ObservableProperty] private string _profileRep = "Reputation: Unknown";
        [ObservableProperty] private string _accountTier = "Tier: Unknown";
        [ObservableProperty] private string _currentlyPlaying = "Currently Playing: Unknown";
        [ObservableProperty] private string _activeDevice = "Active Device: Unknown";
        [ObservableProperty] private string _isVerified = "Verified: Unknown";
        [ObservableProperty] private string _location = "Location: Unknown";
        [ObservableProperty] private string _tenure = "Tenure: Unknown";
        [ObservableProperty] private string _following = "Following: Unknown";
        [ObservableProperty] private string _followers = "Followers: Unknown";
        [ObservableProperty] private string _gamepass = "Gamepass: Unknown";
        [ObservableProperty] private string _bio = "Bio: Unknown";
        [ObservableProperty] public static bool _isLoggedIn = false;
        [ObservableProperty] public static bool _updateAvaliable = false;
        [ObservableProperty] private ObservableCollection<ImageItem> _watermarks = new ObservableCollection<ImageItem>();

        private readonly Lazy<XboxRestAPI> _xboxRestAPI;

        public static int SpoofingStatus = 0; //0 = NotSpoofing, 1 = Spoofing, 2 = AutoSpoofing
        public static string SpoofedTitleID = "0";
        public static string AutoSpoofedTitleID = "0";

        //SnackBar
        public HomeViewModel(ISnackbarService snackbarService, IContentDialogService contentDialogService)
        {
            _snackbarService = snackbarService;
            _contentDialogService = contentDialogService;

            // Assume XAUTH and System Language are set by the time this is actually instantiated
            _xboxRestAPI = new Lazy<XboxRestAPI>(() => new XboxRestAPI(XAUTH, currentSystemLanguage));
        }
        private readonly ISnackbarService _snackbarService;
        private TimeSpan _snackbarDuration = TimeSpan.FromSeconds(2);
        private readonly IContentDialogService _contentDialogService;

        private const string XAuthScanPattern = "58 42 4C 33 2E 30 20 78 3D";

        [RelayCommand]
        private void RefreshProfile()
        {
            GrabProfile();
        }

        Mem m = new Mem();
        public BackgroundWorker XauthWorker = new BackgroundWorker();
        bool IsAttached = false;
        bool GrabbedProfile = false;
        bool XAUTHTested = false;
        public static string XAUTH = "";
        public static string XUIDOnly;
        public static bool InitComplete = false;
        private bool _isInitialized = false;
        string SettingsFilePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU"), "settings.json");
        string EventsMetaFilePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU"), "Events", "meta.json");
        string currentSystemLanguage = System.Globalization.CultureInfo.CurrentCulture.Name;
        static HttpClientHandler handler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        HttpClient client = new HttpClient(handler);

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
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:108.0) Gecko/20100101 Firefox/108.0");
            client.DefaultRequestHeaders.Add(HeaderNames.AcceptEncoding, "gzip, deflate, br");
            client.DefaultRequestHeaders.Add(HeaderNames.Accept,
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            if (ToolVersion.Contains("DEV"))
            {
                client.DefaultRequestHeaders.Add(HeaderNames.Host, Hosts.GitHubRaw);
                var responseString =
                    await client.GetStringAsync("https://raw.githubusercontent.com/Fumo-Unlockers/Xbox-Achievement-Unlocker/Pre-Release/info.json");
                var Jsonresponse = (dynamic)(new JArray());
                Jsonresponse = (dynamic)JObject.Parse(responseString);

                if (("DEV-" + Jsonresponse.LatestBuildVersion.ToString()) != ToolVersion)
                {
                    var result = await _contentDialogService.ShowSimpleDialogAsync(
                        new SimpleContentDialogCreateOptions()
                        {
                            Title = $"Version {Jsonresponse.LatestBuildVersion.ToString()} available to download",
                            Content = "Would you like to update to this version?",
                            PrimaryButtonText = "Update",
                            CloseButtonText = "Cancel"
                        }
                    );
                    if (result == ContentDialogResult.Primary)
                    {
                        _snackbarService.Show("Downloading update...", "Please wait", ControlAppearance.Info, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                        string sourceFile = Jsonresponse.DownloadURL.ToString();
                        string destFile = @"XAU-new.exe";
                        var fileDownloader = new FileDownloader();
                        await fileDownloader.DownloadFileAsync(new Uri(sourceFile).ToString(), destFile, UpdateTool);
                    }
                }
            }
            else
            {
                client.DefaultRequestHeaders.Add(HeaderNames.Host, Hosts.GitHubApi);
                var responseString =
                    await client.GetStringAsync("https://api.github.com/repos/Fumo-Unlockers/Xbox-Achievement-unlocker/releases");
                var Jsonresponse = (dynamic)(new JArray());
                Jsonresponse = (dynamic)JArray.Parse(responseString);
                if (Jsonresponse[0].tag_name.ToString() != ToolVersion)
                {
                    var result = await _contentDialogService.ShowSimpleDialogAsync(
                        new SimpleContentDialogCreateOptions()
                        {
                            Title = $"Version {Jsonresponse[0].tag_name.ToString()} available to download",
                            Content = "Would you like to update to this version?",
                            PrimaryButtonText = "Update",
                            CloseButtonText = "Cancel"
                        }
                    );
                    if (result == ContentDialogResult.Primary)
                    {
                        _snackbarService.Show("Downloading update...", "Please wait", ControlAppearance.Info, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                        string sourceFile = Jsonresponse[0].assets[0].browser_download_url.ToString();
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
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:108.0) Gecko/20100101 Firefox/108.0");
            client.DefaultRequestHeaders.Add(HeaderNames.AcceptEncoding, "gzip, deflate, br");
            client.DefaultRequestHeaders.Add(HeaderNames.Accept,
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add(HeaderNames.Host, Hosts.GitHubRaw);
            var responseString =
                await client.GetStringAsync("https://raw.githubusercontent.com/Fumo-Unlockers/Xbox-Achievement-Unlocker/Events-Data/meta.json");
            var Jsonresponse = (dynamic)(new JObject());
            Jsonresponse = (dynamic)JObject.Parse(responseString);
            var EventsTimestamp = 0;
            if (File.Exists(EventsMetaFilePath))
            {
                var metaJson = File.ReadAllText(EventsMetaFilePath);
                var meta = JsonConvert.DeserializeObject<dynamic>(metaJson);
                EventsTimestamp = meta.Timestamp;
            }

            if (Jsonresponse.Timestamp > EventsTimestamp && Jsonresponse.DataVersion == EventsVersion)
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

            string zipUrl = "https://github.com/Fumo-Unlockers/Xbox-Achievement-Unlocker/raw/Events-Data/Events.zip";
            string zipFilePath = Path.Combine(XAUPath, "Events.zip");
            string extractPath = XAUPath;

            using (var client = new FileDownloader())
            {
                await client.DownloadFileAsync(zipUrl, zipFilePath);
            }
            ZipFile.ExtractToDirectory(zipFilePath, extractPath);
            File.Delete(zipFilePath);
            //download and place meta.json in the events folder
            string MetaURL = "https://raw.githubusercontent.com/Fumo-Unlockers/Xbox-Achievement-Unlocker/Events-Data/meta.json";
            string MetaFilePath = Path.Combine(eventsFolderPath, "meta.json");
            using (var client = new FileDownloader())
            {
                await client.DownloadFileAsync(MetaURL, MetaFilePath);
            }
            _snackbarService.Show("Events Update Complete", "Events have been updated to the latest version.", ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
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
                var defaultSettings = new
                {
                    SettingsVersion = "1",
                    ToolVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                    UnlockAllEnabled = false,
                    AutoSpooferEnabled = false,
                    AutoLaunchXboxAppEnabled = false,
                    FakeSignatureEnabled = true,
                    RegionOverride = false,
                    UseAcrylic = false,
                    PrivacyMode = false
                };
                string defaultSettingsJson = JsonConvert.SerializeObject(defaultSettings, Formatting.Indented);
                using (var file = new StreamWriter(SettingsFilePath))
                {
                    file.Write(defaultSettingsJson);
                }

            }
            CheckForEventUpdates();
            LoadSettings();
            _isInitialized = true;
            if (Settings.AutoLaunchXboxAppEnabled)
            {
                Process p = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = @"shell:appsFolder\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App"
                };
                p.StartInfo = startInfo;
                p.Start();
            }
            if (Settings.RegionOverride)
                currentSystemLanguage = "en-GB";
        }

        #region Xauth
        public void XauthWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            while (true)
            {
                if (m.OpenProcess(ProcessNames.XboxPcApp) != Mem.OpenProcessResults.Success)
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
        }
        public void XauthWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (IsAttached || XAUTH.Length > 0)
            {
                Attached = $"Attached to xbox app ({Mem.GetProcIdFromName(ProcessNames.XboxPcApp).ToString()})";
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
                    if (!SettingsViewModel.ManualXauth)
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
            if (Mem.GetProcIdFromName(ProcessNames.XboxPcApp) == 0)
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
            var XauthScanList = await m.AoBScan(XAuthScanPattern, true);
            string[] XauthStrings = new string[XauthScanList.Count()];
            var i = 0;
            foreach (var address in XauthScanList)
            {
                XauthStrings[i] = m.ReadStringMemory(address, length: 10000);
                i++;
            }

            var frequency = new Dictionary<string, int>();
            foreach (var str in XauthStrings)
            {
                if (!frequency.TryAdd(str, 1))
                {
                    frequency[str]++;
                }
            }

            if (XauthStrings.Length == 0)
            {
                return;
            }

            var mostCommon = XauthStrings[0];
            var highestFrequency = 0;
            foreach (var pair in frequency.Where(pair => pair.Value > highestFrequency))
            {
                mostCommon = pair.Key;
                highestFrequency = pair.Value;
            }

            if (highestFrequency > 3)
            {
                XAUTH = mostCommon;
            }
        }
        private async void TestXAUTH()
        {
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add(HeaderNames.ContractVersion, HeaderValues.ContractVersion2);
            client.DefaultRequestHeaders.Add(HeaderNames.AcceptEncoding, HeaderValues.AcceptEncoding);
            client.DefaultRequestHeaders.Add(HeaderNames.Accept, HeaderValues.Accept);
            client.DefaultRequestHeaders.Add(HeaderNames.AcceptLanguage, currentSystemLanguage);
            try
            {
                client.DefaultRequestHeaders.Add(HeaderNames.Authorization, XAUTH);
            }
            catch (Exception)
            {
                return;
            }
            client.DefaultRequestHeaders.Add(HeaderNames.Host, Hosts.Profile);
            client.DefaultRequestHeaders.Add(HeaderNames.Connection, HeaderValues.KeepAlive);
            try
            {
                var responseString =
                    await client.GetStringAsync(BasicXboxAPIUris.GamertagUrl);
                var Jsonresponse = (dynamic)(new JObject());
                Jsonresponse = (dynamic)JObject.Parse(responseString);
                if (Settings.PrivacyMode)
                {
                    GamerTag = $"Gamertag: Hidden";
                    Xuid = $"XUID: Hidden";
                }
                else
                {
                    GamerTag = $"Gamertag: {Jsonresponse.profileUsers[0].settings[0].value}";
                    Xuid = $"XUID: {Jsonresponse.profileUsers[0].id}";
                }

                XUIDOnly = Jsonresponse.profileUsers[0].id;
                IsLoggedIn = true;
                XAUTHTested = true;
                InitComplete = true;
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    IsLoggedIn = false;
                    XAUTHTested = false;

                }
            }
        }
        #endregion

        #region Profile
        private async void GrabProfile()
        {
            try
            {
                var profileResponse = await _xboxRestAPI.Value.GetProfileAsync(XUIDOnly);

                if (Settings.PrivacyMode)
                {
                    GamerTag = $"Gamertag: Hidden";
                    Xuid = $"XUID: Hidden";
                    GamerPic = "pack://application:,,,/Assets/cirno.png";
                    GamerScore = $"Gamerscore: Hidden";
                    ProfileRep = $"Reputation: Hidden";
                    AccountTier = $"Tier: Hidden";
                    CurrentlyPlaying = $"Currently Playing: Hidden";
                    ActiveDevice = $"Active Device: Hidden";
                    IsVerified = $"Verified: Hidden";
                    Location = $"Location: Hidden";
                    Tenure = $"Tenure: Hidden";
                    Following = $"Following: Hidden";
                    Followers = $"Followers: Hidden";
                    Gamepass = $"Gamepass: Hidden";
                    Bio = $"Bio: Hidden";
                }
                else
                {
                    GamerTag = $"Gamertag: {profileResponse.People[0].Gamertag}";
                    Xuid = $"XUID: {profileResponse.People[0].Xuid}";
                    GamerPic = profileResponse.People[0].DisplayPicRaw;
                    GamerScore = $"Gamerscore: {profileResponse.People[0].GamerScore}";
                    ProfileRep = $"Reputation: {profileResponse.People[0].XboxOneRep}";
                    AccountTier = $"Tier: {profileResponse.People[0].Detail.AccountTier}";
                    try
                    {
                        var gameTitle = await _xboxRestAPI.Value.GetGameTitleAsync(XUIDOnly, profileResponse.People[0].PresenceDetails[0].TitleId);
                        CurrentlyPlaying = $"Currently Playing: {gameTitle.Titles[0].Name}";
                    }
                    catch
                    {
                        CurrentlyPlaying = $"Currently Playing: Unknown ({profileResponse.People[0].PresenceDetails[0].TitleId})";
                    }

                    // GPU details
                    try
                    {
                        var gpuResponse = await _xboxRestAPI.Value.GetGamepassMembershipAsync(XUIDOnly);
                        if (!string.IsNullOrEmpty(gpuResponse.GamepassMembership))
                        {
                            Gamepass = $"Gamepass: {gpuResponse.GamepassMembership}";
                        }
                        else
                        {
                            Gamepass = $"Gamepass: {gpuResponse.Data.GamepassMembership}";
                        }
                    }
                    catch
                    {
                        Gamepass = $"Gamepass: Unknown";
                    }

                    ActiveDevice = $"Active Device: {profileResponse.People[0].PresenceDetails[0].Device}";
                    IsVerified = $"Verified: {profileResponse.People[0].Detail.IsVerified}";
                    Location = $"Location: {profileResponse.People[0].Detail.Location}";
                    Tenure = $"Tenure: {profileResponse.People[0].Detail.Tenure}";
                    Following = $"Following: {profileResponse.People[0].Detail.FollowingCount}";
                    Followers = $"Followers: {profileResponse.People[0].Detail.FollowerCount}";
                    Bio = $"Bio: {profileResponse.People[0].Detail.Bio}";

                    Watermarks.Clear();

                    // Tenure image format, 01..05..10
                    // https://dlassets-ssl.xboxlive.com/public/content/ppl/watermarks/tenure/15.png
                    // https://dlassets-ssl.xboxlive.com/public/content/ppl/watermarks/launch/ba75b64a-9a80-47ea-8c3a-76d3e2ea1422.png
                    // https://dlassets-ssl.xboxlive.com/public/content/ppl/watermarks/launch/xboxoneteam.png
                    var tenureString = profileResponse.People[0].Detail.Tenure;
                    if (int.TryParse(tenureString, out int tenureInt))
                    {
                        // Format the integer as a two-digit string
                        string tenureBadge = tenureInt.ToString("D2");
                        Watermarks.Add(new ImageItem { ImageUrl = $@"{BasicXboxAPIUris.WatermarksUrl}tenure/{tenureBadge}.png" });
                    }
                    else
                    {
                        // TODO: log error somewhere
                        Console.WriteLine("The string is not a valid integer.");
                    }

                    foreach (var watermark in profileResponse.People[0].Detail.Watermarks)
                    {
                        Watermarks.Add(new ImageItem { ImageUrl = $@"{BasicXboxAPIUris.WatermarksUrl}launch/{watermark.ToLower()}.png" });
                    }
                }
                GrabbedProfile = true;
                _snackbarService.Show("Success", "Profile information grabbed.", ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    IsLoggedIn = false;
                    XAUTHTested = false;
                    _snackbarService.Show("401 Unauthorized", "Something went wrong. Retrying", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                }

            }


        }
        #endregion

        #region Settings

        public class SettingsList
        {
            public string SettingsVersion { get; set; }
            public string ToolVersion { get; set; }
            public bool UnlockAllEnabled { get; set; }
            public bool AutoSpooferEnabled { get; set; }
            public bool AutoLaunchXboxAppEnabled { get; set; }
            public bool FakeSignatureEnabled { get; set; }
            public bool RegionOverride { get; set; }
            public bool UseAcrylic { get; set; }
            public bool PrivacyMode { get; set; }
        }
        public static SettingsList Settings = new SettingsList();

        public void LoadSettings()
        {
            string settingsJson = File.ReadAllText(SettingsFilePath);
            var settings = JsonConvert.DeserializeObject<dynamic>(settingsJson);
            Settings.SettingsVersion = settings.SettingsVersion;
            Settings.ToolVersion = settings.ToolVersion;
            Settings.UnlockAllEnabled = settings.UnlockAllEnabled;
            Settings.AutoSpooferEnabled = settings.AutoSpooferEnabled;
            Settings.AutoLaunchXboxAppEnabled = settings.AutoLaunchXboxAppEnabled;
            Settings.FakeSignatureEnabled = settings.FakeSignatureEnabled;
            Settings.RegionOverride = settings.RegionOverride;
            Settings.UseAcrylic = settings.UseAcrylic;
            Settings.PrivacyMode = settings.PrivacyMode;
        }

        #endregion
    }
}
