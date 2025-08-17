using System.Windows;

namespace LANFileSharing
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create the main window
            MainWindow mainWindow = new MainWindow();

            // Check if there are any command-line arguments (from "Send To")
            if (e.Args.Length > 0)
            {
                // Pass the file paths to the main window for processing
                mainWindow.ProcessCommandLineArgs(e.Args);
            }

            // Show the main window
            mainWindow.Show();
        }
    }
}
