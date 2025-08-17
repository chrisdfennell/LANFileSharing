/* ====================================================================== */
/* == SettingsWindow.xaml.cs - The Logic for the new Settings Window   == */
/* ====================================================================== */
/* This C# code has been updated to handle the new QoL settings.        */
/* ====================================================================== */
using System.Windows;
using Ookii.Dialogs.Wpf;

namespace LANFileSharing
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            SavePathTextBox.Text = Properties.Settings.Default.SavePath;
            ComputerNameTextBox.Text = Properties.Settings.Default.ComputerName;
            ShowNotificationCheckBox.IsChecked = Properties.Settings.Default.ShowReceiveNotification;
            PlaySoundCheckBox.IsChecked = Properties.Settings.Default.PlaySoundOnCompletion;
            MinimizeToTrayCheckBox.IsChecked = Properties.Settings.Default.MinimizeToTray;
            AutoRefreshCheckBox.IsChecked = Properties.Settings.Default.AutoRefreshEnabled;
            AutoRefreshIntervalSlider.Value = Properties.Settings.Default.AutoRefreshIntervalSeconds;
            MaxTransfersSlider.Value = Properties.Settings.Default.MaxConcurrentTransfers;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.SavePath = SavePathTextBox.Text;
            Properties.Settings.Default.ComputerName = ComputerNameTextBox.Text;
            Properties.Settings.Default.ShowReceiveNotification = ShowNotificationCheckBox.IsChecked.GetValueOrDefault(true);
            Properties.Settings.Default.PlaySoundOnCompletion = PlaySoundCheckBox.IsChecked.GetValueOrDefault(true);
            Properties.Settings.Default.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked.GetValueOrDefault(true);
            Properties.Settings.Default.AutoRefreshEnabled = AutoRefreshCheckBox.IsChecked.GetValueOrDefault(true);
            Properties.Settings.Default.AutoRefreshIntervalSeconds = (int)AutoRefreshIntervalSlider.Value;
            Properties.Settings.Default.MaxConcurrentTransfers = (int)MaxTransfersSlider.Value;

            Properties.Settings.Default.Save();
            this.DialogResult = true;
            this.Close();
        }

        private void BrowseSavePathButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "Select the folder where you want to save received files.",
                UseDescriptionForTitle = true,
                SelectedPath = SavePathTextBox.Text
            };

            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                SavePathTextBox.Text = dialog.SelectedPath;
            }
        }
    }
}