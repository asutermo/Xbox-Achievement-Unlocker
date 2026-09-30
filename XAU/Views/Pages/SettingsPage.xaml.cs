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
                cleared ? "Auth Cache Cleared" : "Auth Cache Not Fully Cleared",
                cleared
                    ? "All tokens have been deleted. Log in again from the Home page."
                    : "Some cached authentication data could not be deleted or saved. Check file permissions and try again.",
                cleared ? ControlAppearance.Success : ControlAppearance.Caution,
                new SymbolIcon(cleared ? SymbolRegular.Checkmark24 : SymbolRegular.Warning24));
        }
    }
}
