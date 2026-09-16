using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using XAU.ViewModels.Pages;

namespace XAU.Views.Pages
{
    public partial class SettingsPage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }

        private void ClearAuthCacheButton_OnClick(object sender, RoutedEventArgs e)
        {
            var homeViewModel = App.GetService<HomeViewModel>();
            homeViewModel.ClearAuthCache();

            _snackbarService = App.GetService<ISnackbarService>();
            _snackbarService.Show(
                "Auth Cache Cleared",
                "All tokens have been deleted. Log in again from the Home page.",
                ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Checkmark24));
        }
    }
}
