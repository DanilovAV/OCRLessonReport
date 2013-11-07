using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using OCRLessonReport.ViewModels;

namespace OCRLessonReport.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel viewModel;

        public MainWindow()
        {
            viewModel = new MainWindowViewModel();
            this.DataContext = viewModel;
            
            InitializeComponent();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            viewModel.Close();
            base.OnClosing(e);
        }
    }
}