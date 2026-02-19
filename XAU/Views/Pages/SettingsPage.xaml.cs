using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using XAU.ViewModels.Pages;

namespace XAU.Views.Pages
{
    public partial class SettingsPage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }
        private readonly ISnackbarService _snackbarService;
        private readonly HomeViewModel _homeViewModel;

        public SettingsPage(SettingsViewModel viewModel, ISnackbarService snackbarService, HomeViewModel homeViewModel)
        {
            ViewModel = viewModel;
            _snackbarService = snackbarService;
            _homeViewModel = homeViewModel;
            DataContext = this;

            ViewModel.OnNavigatedToEvent += (_, _) =>
            {
                XauthTextBox.Text = HomeViewModel.XAUTH;
                EventsTokenBox.Text = AchievementsViewModel.EventsToken;
                UpdateEventsTokenStatus();
            };

            InitializeComponent();
        }

        private void XauthTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(XauthTextBox.Text) || string.IsNullOrEmpty(XauthTextBox.Text))
            {
                _snackbarService.Show(
                    "Error",
                    "XAuth Token cannot be empty/whitespace",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24)
                );
                return;
            }

            HomeViewModel.XAUTH = XauthTextBox.Text;
            SettingsViewModel.ManualXauth = true;
            HomeViewModel.XAUTHTested = false;
        }

        private void EventsToken_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EventsTokenBox.Text) || string.IsNullOrEmpty(EventsTokenBox.Text))
            {
                _snackbarService.Show(
                    "Error",
                    "Events Token cannot be empty/whitespace",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24)
                );
                return;
            }

            AchievementsViewModel.EventsToken = EventsTokenBox.Text;
            UpdateEventsTokenStatus();
        }

        private void XAuthBox_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            XauthTextBox.MaxWidth = e.NewSize.Width / 3;
        }

        private void GrabEventsToken_OnClick(object sender, RoutedEventArgs e)
        {
            if (!HomeViewModel._isLoggedIn)
            {
                _snackbarService.Show(
                    "Not Logged In",
                    "You must be logged in before scanning for an events token.",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24)
                );
                return;
            }

            _snackbarService.Show(
                "Scanning...",
                "Launching Solitaire and scanning for events token. This may take up to a minute.",
                ControlAppearance.Info,
                new SymbolIcon(SymbolRegular.Search24)
            );

            GrabEventsTokenButton.IsEnabled = false;
            _homeViewModel.ScanForEventsTokenManual();

            // Poll for the result - the worker may take up to ~60s
            // (process launch + Xbox Live init + scan retries)
            System.Threading.Tasks.Task.Run(async () =>
            {
                for (int i = 0; i < 65; i++)
                {
                    await System.Threading.Tasks.Task.Delay(1000);
                    if (!string.IsNullOrEmpty(AchievementsViewModel.EventsToken))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            EventsTokenBox.Text = AchievementsViewModel.EventsToken;
                            UpdateEventsTokenStatus();
                            GrabEventsTokenButton.IsEnabled = true;
                            _snackbarService.Show(
                                "Events Token Found",
                                "Successfully extracted events token from Solitaire.",
                                ControlAppearance.Success,
                                new SymbolIcon(SymbolRegular.Checkmark24)
                            );
                        });
                        return;
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    GrabEventsTokenButton.IsEnabled = true;
                    _snackbarService.Show(
                        "Events Token Not Found",
                        "Could not find events token. Make sure Solitaire is installed.",
                        ControlAppearance.Caution,
                        new SymbolIcon(SymbolRegular.Warning24)
                    );
                });
            });
        }

        private void UpdateEventsTokenStatus()
        {
            if (HomeViewModel.IsEventsTokenValid())
            {
                EventsTokenStatus.Text = "Valid Token";
                EventsTokenStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            }
            else if (!string.IsNullOrEmpty(AchievementsViewModel.EventsToken))
            {
                EventsTokenStatus.Text = "Invalid Format";
                EventsTokenStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
            }
            else
            {
                EventsTokenStatus.Text = "No Token";
                EventsTokenStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            }
        }

        private void EventsBoxGrid_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            EventsTokenBox.MaxWidth = e.NewSize.Width / 3;
        }
    }
}
