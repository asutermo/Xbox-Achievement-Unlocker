using HtmlAgilityPack;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Diagnostics;
using System.DirectoryServices;
using System.IO;
using System.Text;
using System.Windows.Input;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using Wpf.Ui.Services;


namespace XAU.ViewModels.Pages
{
    public partial class MiscViewModel : ObservableObject, INavigationAware
    {
        private readonly IContentDialogService _contentDialogService;
        private readonly ISnackbarService _snackbarService;
        private TimeSpan _snackbarDuration = TimeSpan.FromSeconds(2);
        private Lazy<XboxRestAPI> _xboxRestAPI = new Lazy<XboxRestAPI>(() => new XboxRestAPI(HomeViewModel.XAUTH));



        public MiscViewModel(ISnackbarService snackbarService)
        {
            _snackbarService = snackbarService;
            _contentDialogService = new ContentDialogService();
        }

        public void OnNavigatedTo()
        {
            if (!IsInitialized && HomeViewModel.InitComplete)
                InitializeViewModel();
        }

        public void OnNavigatedFrom()
        {
        }

        private void InitializeViewModel()
        {
            IsInitialized = true;
        }

        #region Spoofer

        [ObservableProperty] private string _gameName = "Name: ";
        [ObservableProperty] private string _gameTitleID = "Title ID: ";
        [ObservableProperty] private string _gamePFN = "PFN: ";
        [ObservableProperty] private string _gameType = "Type: ";
        [ObservableProperty] private string _gameGamepass = "Gamepass: ";
        [ObservableProperty] private string _gameDevices = "Devices: ";
        [ObservableProperty] private string _gameGamerscore = "Gamerscore: ?/?";
        [ObservableProperty] private string? _gameImage = "pack://application:,,,/Assets/cirno.png";
        [ObservableProperty] private string _gameTime = "Time Played: ";
        [ObservableProperty] private bool _isInitialized = false;
        [ObservableProperty] private string _currentSpoofingID = "";
        [ObservableProperty] private string _newSpoofingID = "";
        [ObservableProperty] private string _spoofingText = "Spoofing Not Started";
        [ObservableProperty] private string _spoofingButtonText = "Start Spoofing";
        private bool SpoofingUpdate = false;
        private bool CurrentlySpoofing = false;
        private GameTitle GameInfoResponse;
        private GameStatsResponse GameStatsResponse;

        /// <summary>
        /// Diagnostic dump of every stat bucket (name/type/value/properties) so a spurious
        /// "Time Played: Unknown" can be explained from the log alone.
        /// </summary>
        public static string DumpStatBuckets(GameStatsResponse response)
        {
            if (response == null || response.StatListsCollection == null)
                return "<no stat lists>";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < response.StatListsCollection.Count; i++)
            {
                var list = response.StatListsCollection[i];
                if (list == null || list.Stats == null)
                    continue;
                for (int j = 0; j < list.Stats.Count; j++)
                {
                    var s = list.Stats[j];
                    sb.Append($"[{i}][{j}] name={s?.Name} type={s?.Type} value={(s?.Value == null ? "<null>" : "\"" + s.Value + "\"")}");
                    sb.Append("; ");
                }
            }
            return sb.Length == 0 ? "<empty>" : sb.ToString();
        }

        /// <summary>
        /// Pulls the MinutesPlayed figure (in minutes) out of a userstats batch response by scanning
        /// EVERY stat bucket for one whose Name is "MinutesPlayed", instead of trusting [0][0]. Returns
        /// the LARGEST matching value (the all-time aggregate dominates any seasonal/device slice) or -1
        /// when no MinutesPlayed stat exists at all (caller then shows "Unknown").
        /// </summary>
        public static double GetMinutesPlayed(GameStatsResponse response)
        {
            if (response == null || response.StatListsCollection == null)
                return -1;

            double best = -1;
            foreach (var list in response.StatListsCollection)
            {
                if (list == null || list.Stats == null)
                    continue;
                foreach (var stat in list.Stats)
                {
                    if (stat == null)
                        continue;
                    // Only ever consider the MinutesPlayed statistic, so a Gamerscore/Score/etc. can
                    // never be mistaken for play-time (which the old positional [0][0] read risked).
                    if (!string.Equals(stat.Name?.Trim(), "MinutesPlayed", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // The figure usually sits in Value; some responses surface it only via Properties /
                    // GroupProperties, so fall back to a numeric found there before giving up.
                    if ((TryParseMinutes(stat.Value, out double value)
                            || TryParseAnyNumeric(stat.Properties, out value)
                            || TryParseAnyNumeric(stat.GroupProperties, out value))
                        && value > best)
                    {
                        best = value;
                    }
                }
            }
            return best;
        }

        private static bool TryParseMinutes(string candidate, out double value)
        {
            value = 0;
            return !string.IsNullOrWhiteSpace(candidate)
                && double.TryParse(candidate.Trim(),
                    System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseAnyNumeric(Dictionary<string, object> props, out double value)
        {
            value = 0;
            if (props == null)
                return false;
            foreach (var kv in props)
            {
                if (kv.Value == null)
                    continue;
                if (double.TryParse(kv.Value.ToString()?.Trim(),
                        System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
            }
            return false;
        }

        [RelayCommand]
        public async Task SpooferButtonClicked()
        {
            if (CurrentlySpoofing)
            {
                SpoofingUpdate = true;
                CurrentlySpoofing = false;
                SpoofingText = "Spoofing Not Started";
                SpoofingButtonText = "Start Spoofing";
                //reset game info
                GameName = "Name: ";
                GameTitleID = "Title ID: ";
                GamePFN = "PFN: ";
                GameType = "Type: ";
                GameGamepass = "Gamepass: ";
                GameDevices = "Devices: ";
                GameGamerscore = "Gamerscore: ?/?";
                GameImage = "pack://application:,,,/Assets/cirno.png";
                GameTime = "Time Played: ";
                HomeViewModel.SpoofingStatus = 0;
                await _xboxRestAPI.Value.StopHeartbeatAsync(HomeViewModel.XUIDOnly);
                return;
            }
            HomeViewModel.SpoofedTitleID = NewSpoofingID;

            if (HomeViewModel.SpoofingStatus == 2)
            {
                HomeViewModel.SpoofingStatus = 1;
                AchievementsViewModel.SpoofingUpdate = true;
            }
            HomeViewModel.SpoofingStatus = 1;
            SpoofGame();
        }

        public async void SpoofGame()
        {
            CurrentSpoofingID = NewSpoofingID;
            GameInfoResponse = await _xboxRestAPI.Value.GetGameTitleAsync(HomeViewModel.XUIDOnly, NewSpoofingID);
            GameStatsResponse = await _xboxRestAPI.Value.GetGameStatsAsync(HomeViewModel.XUIDOnly, NewSpoofingID);

            if (GameInfoResponse == null || GameStatsResponse == null || !GameInfoResponse.Titles.Any())
            {
                _snackbarService.Show("Error: Unable to acquire game info or stats",
                    $"The game info was invalid.",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }

            try
            {
                GameName = "Name: " + GameInfoResponse.Titles[0].Name;
                GameImage = !string.IsNullOrEmpty(GameInfoResponse.Titles[0].DisplayImage.ToString()) ? GameInfoResponse.Titles[0].DisplayImage.ToString() : "pack://application:,,,/Assets/cirno.png";
                GameTitleID = "Title ID: " + GameInfoResponse.Titles[0].TitleId;
                GamePFN = "PFN: " + GameInfoResponse.Titles[0].Pfn;
                GameType = "Type: " + GameInfoResponse.Titles[0].Type;
                GameGamepass = "Gamepass: " + GameInfoResponse.Titles[0].GamePass?.IsGamePass;
                GameDevices = "Devices: ";
                foreach (var device in GameInfoResponse.Titles[0].Devices)
                {
                    GameDevices += device.ToString() + ", ";
                }

                GameDevices = GameDevices.Remove(GameDevices.Length - 2);
                GameGamerscore = "Gamerscore: " + GameInfoResponse.Titles[0].Achievement?.CurrentGamerscore.ToString() +
                                 "/" + GameInfoResponse.Titles[0].Achievement?.TotalGamerscore.ToString();
                try
                {
                    var timePlayed = TimeSpan.FromMinutes(Convert.ToDouble(GameStatsResponse.StatListsCollection[0].Stats[0].Value));
                    var formattedTime = $"{timePlayed.Days} Days, {timePlayed.Hours} Hours and {timePlayed.Minutes} minutes";
                    GameTime = "Time Played: " + formattedTime;
                }
                catch
                {
                    GameTime = "Time Played: Unknown";
                }

            }
            catch
            {
                GameName = "Name: ";
                _snackbarService.Show("Error: Invalid TitleID",
                    $"The TitleID entered is invalid or does not return information from the API",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }

            SpoofingUpdate = true;
            CurrentlySpoofing = true;
            SpoofingButtonText = "Stop Spoofing";
            SpoofingText = $"Spoofing {GameInfoResponse.Titles[0].Name}";
            await Task.Run(() => Spoofing());

        }

        // TODO: this code seems like it's duplicated in AchievementsViewModel.cs too.
        public async Task Spoofing()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            SpoofingText = "Spoofing started...";
            await _xboxRestAPI.Value.SendHeartbeatAsync(HomeViewModel.XUIDOnly, CurrentSpoofingID);
            var lastHeartbeat = DateTime.UtcNow;
            SpoofingUpdate = false;
            while (!SpoofingUpdate)
            {
                await Task.Delay(1000);
                if (SpoofingUpdate)
                {
                    HomeViewModel.SpoofingStatus = 0;
                    HomeViewModel.SpoofedTitleID = "0";
                    break;
                }
                    SpoofingText = $"Spoofing {GameInfoResponse.Titles[0].Name} For: {stopwatch.Elapsed.ToString(@"hh\:mm\:ss")}";
                if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds >= 300)
                {
                    await _xboxRestAPI.Value.SendHeartbeatAsync(HomeViewModel.XUIDOnly, CurrentSpoofingID);
                    lastHeartbeat = DateTime.UtcNow;
                }
            }
        }

        #endregion

        #region GameSearch
        [ObservableProperty] private List<GameItem> _tSearchResults = new List<GameItem>();
        [ObservableProperty] private List<string> _tSearchTitleNames = new List<string>();
        [ObservableProperty] private string _tSearchText = "";
        [ObservableProperty] private string _tSearchGameName = "Name: ";
        [ObservableProperty] private string _tSearchGameTitleID = "";
        [ObservableProperty] private string _tSearchGameTitleBased = "Title Based: Unknown";

        private string GetDatabasePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "TitleSearch", "xbox_games.db");
        }

        [RelayCommand]
        public async Task SearchGame()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TSearchText))
                {
                    TSearchTitleNames = new List<string>();
                    TSearchResults = new List<GameItem>();
                    return;
                }

                string dbPath = GetDatabasePath();

                if (!File.Exists(dbPath))
                {
                    _snackbarService.Show("Error", "Game database not found. Please wait for it to download.",
                        ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                var results = await Task.Run(() => SearchGamesInDatabase(dbPath, TSearchText));

                if (!results.Any())
                {
                    _snackbarService.Show("Error", $"No results were found for '{TSearchText}'",
                        ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    TSearchTitleNames = new List<string>();
                    TSearchResults = new List<GameItem>();
                    return;
                }

                results = results.OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase).ToList();

                TSearchResults = results;
                TSearchTitleNames = results.Select(game => game.Title).ToList();
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", $"Search failed: {ex.Message}",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                TSearchTitleNames = new List<string>();
                TSearchResults = new List<GameItem>();
            }
        }

        private List<GameItem> SearchGamesInDatabase(string dbPath, string searchText)
        {
            var results = new List<GameItem>();

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            // Search for games that contain the search text (case-insensitive)
            string sql = @"
                    SELECT title, titleId, isTitleBased 
                    FROM games 
                    WHERE title LIKE @searchText 
                    ORDER BY title COLLATE NOCASE
                    LIMIT 100";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@searchText", $"%{searchText}%");

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new GameItem
                {
                    Title = reader.GetString("title"),
                    TitleId = reader.GetString("titleId"),
                    IsTitleBased = reader.GetInt32("isTitleBased") == 1
                });
            }

            return results;
        }

        public void DisplayGameInfo(int index)
        {
            try
            {
                if (TSearchResults == null || TSearchResults.Count <= index || index < 0)
                {
                    _snackbarService.Show("Error", "No game found at the selected index.",
                        ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                var selectedGame = TSearchResults[index];

                TSearchGameName = selectedGame.Title;
                TSearchGameTitleID = selectedGame.TitleId;
                TSearchGameTitleBased = $"Title Based: {(selectedGame.IsTitleBased ? "True" : "False")}";

            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", "Failed to display game info.",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
        }

        #endregion

        #region GamertagSearch
        [ObservableProperty] private string _gamertag = "";
        [ObservableProperty] private string _gamertagName = "Gamertag:";
        [ObservableProperty] private string _gamertagImage = "pack://application:,,,/Assets/cirno.png";
        [ObservableProperty] private string _gamertagScore = "Gamerscore: ";
        [ObservableProperty] private string _gamertagXuid;
        [ObservableProperty] private bool _excludeZeroGamerscoreGames;
        [ObservableProperty] private bool _excludeXbox360Games;

        [RelayCommand]
        public async Task SearchGamertag()
        {
            if (string.IsNullOrWhiteSpace(Gamertag))
            {
                _snackbarService.Show("Error", "Please enter a valid gamertag.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }

            var profileData = await _xboxRestAPI.Value.GetGamertagProfileAsync(Gamertag) ?? new JObject();
            var profileUsers = profileData["profileUsers"]?.FirstOrDefault();
            if (profileUsers == null)
            {
                _snackbarService.Show("Error", "Failed to fetch gamertag information.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }

            GamertagXuid = profileUsers["id"]?.ToString() ?? string.Empty;
            GamertagName = "Gamertag: " + profileUsers["settings"]?.FirstOrDefault(setting => setting["id"]?.ToString() == "Gamertag")?["value"]?.ToString() ?? "Unknown";
            GamertagScore = "Gamerscore: " + profileUsers["settings"]?.FirstOrDefault(setting => setting["id"]?.ToString() == "Gamerscore")?["value"]?.ToString() ?? "Unknown";
            GamertagImage = profileUsers["settings"]?.FirstOrDefault(setting => setting["id"]?.ToString() == "GameDisplayPicRaw")?["value"]?.ToString()?.Replace("&mode=Padding", "") ?? string.Empty;

        }

        public async Task ExportToCsvAsync()
        {
            if (string.IsNullOrWhiteSpace(GamertagXuid))
            {
                _snackbarService.Show("Error", "Search for a user first.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }
            try
            {
                _snackbarService.Show("Fetching Games", "Trying to get games. This may take a moment depending on the number of games the user has.", ControlAppearance.Primary, new SymbolIcon(SymbolRegular.XboxController24), _snackbarDuration);
                var gamesResponse = await _xboxRestAPI.Value.GetGamesListAsync(GamertagXuid);

                if (gamesResponse == null || gamesResponse.Titles == null)
                {
                    await Task.Delay(2500);
                    _snackbarService.Show("Error", "Failed to fetch games list.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                if (gamesResponse.Titles.Count == 0)
                {
                    await Task.Delay(2500);
                    _snackbarService.Show("No Titles Found", "No games found for this user. This could be due to user privacy settings or other reasons.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("\"Title ID\",\"Title\",\"CurrentAchievements\",\"Gamerscore\",\"Progress\",\"Devices\",\"Genres\"");

                foreach (var title in gamesResponse.Titles)
                {
                    if (ExcludeZeroGamerscoreGames && title.Achievement.TotalGamerscore == 0)
                    {
                        continue;
                    }

                    if (ExcludeXbox360Games && title.Devices != null && title.Devices.Contains("Xbox360"))
                    {
                        continue;
                    }

                    var titleName = title.Name.Replace("\"", "\"\"");
                    var devices = title.Devices != null ? string.Join(", ", title.Devices).Replace("\"", "\"\"") : string.Empty;
                    var genres = title.Detail?.Genres != null ? string.Join(", ", title.Detail.Genres).Replace("\"", "\"\"") : string.Empty;

                    sb.AppendLine($"\"{title.TitleId}\",\"{titleName}\",\"{title.Achievement.CurrentAchievements}\",\"{title.Achievement.CurrentGamerscore}/{title.Achievement.TotalGamerscore}\",\"{title.Achievement.ProgressPercentage}\",\"{devices}\",\"{genres}\"");
                }

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = $"{GamertagXuid}.csv"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    await Task.Run(() =>
                    {
                        File.WriteAllText(saveFileDialog.FileName, sb.ToString());
                    });

                    _snackbarService.Show("Success", "Games list exported successfully.", ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                }
                else
                {
                    _snackbarService.Show("Cancelled", "Game export was not completed", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.Warning24), _snackbarDuration);
                }
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", "Failed to export games list: " + ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
        }
        #endregion
    }
}
