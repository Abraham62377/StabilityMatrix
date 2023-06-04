using System.Windows;
using Markdown.Xaml;
using StabilityMatrix.ViewModels;
using Wpf.Ui.Controls.Navigation;

namespace StabilityMatrix
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class SettingsPage : INavigableView<SettingsViewModel>
    {
        public SettingsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = ViewModel;

            InitializeComponent();
            
            var textToFlowDocumentConverter = Resources["TextToFlowDocumentConverter"] as TextToFlowDocumentConverter;
            ViewModel.TextToFlowDocumentConverter = textToFlowDocumentConverter;
        }

        private async void SettingsPage_OnLoaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.OnLoaded();
        }

        public SettingsViewModel ViewModel { get; }
    }
}
