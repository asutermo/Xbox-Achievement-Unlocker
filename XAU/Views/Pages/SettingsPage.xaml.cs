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
                "Looking for events token in running game processes.",
                ControlAppearance.Info,
                new SymbolIcon(SymbolRegular.Search24)
            );

            _homeViewModel.ScanForEventsTokenManual();

            // Poll briefly for the result since the worker runs in the background
            System.Threading.Tasks.Task.Run(async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    await System.Threading.Tasks.Task.Delay(1000);
                    if (!string.IsNullOrEmpty(AchievementsViewModel.EventsToken))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            EventsTokenBox.Text = AchievementsViewModel.EventsToken;
                            _snackbarService.Show(
                                "Events Token Found",
                                "Successfully extracted events token from a running game.",
                                ControlAppearance.Success,
                                new SymbolIcon(SymbolRegular.Checkmark24)
                            );
                        });
                        return;
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    _snackbarService.Show(
                        "Events Token Not Found",
                        "No events token found. Make sure a game (e.g. Solitaire) is running.",
                        ControlAppearance.Caution,
                        new SymbolIcon(SymbolRegular.Warning24)
                    );
                });
            });
        }

        private void EventsBoxGrid_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            EventsTokenBox.MaxWidth = e.NewSize.Width / 3;
        }
    }
}
