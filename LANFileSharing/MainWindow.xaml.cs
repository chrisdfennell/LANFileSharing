/* ====================================================================== */
/* == MainWindow.xaml.cs - The Code-Behind Logic for the Application   == */
/* ====================================================================== */
/* This C# code has been updated to fix the reported errors.            */
/* ====================================================================== */

using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LANFileSharing // <-- Make sure this namespace matches your project name
{
    public class Peer
    {
        public string HostName { get; set; }
        public string IpAddress { get; set; }
        public override string ToString() => $"{HostName} ({IpAddress})";
    }

    public class TransferProgress : INotifyPropertyChanged
    {
        public string FileName { get; set; }
        public string Direction { get; set; }
        public string FullPath { get; set; } // To store the final path for opening
        public bool IsFile { get; set; } // To distinguish between a file and a folder transfer

        private string _status;
        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsInProgress));
                OnPropertyChanged(nameof(IsLink));
            }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }

        private string _throughput;
        public string Throughput
        {
            get => _throughput;
            set { _throughput = value; OnPropertyChanged(nameof(Throughput)); }
        }

        public bool IsInProgress => Status == "In Progress";
        public bool IsLink => Status == "Complete" && Direction == "Receiving" && !string.IsNullOrEmpty(FullPath);

        public CancellationTokenSource Cts { get; } = new CancellationTokenSource();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public enum ContentType : byte
    {
        Files = 0x01,
        Text = 0x02,
        Folder = 0x03
    }

    public partial class MainWindow : Window
    {
        private string[] selectedPaths = Array.Empty<string>();
        private const int TcpPort = 8888;
        private const int UdpPort = 8889;
        private string savePath;
        private string computerName;

        private IPAddress localIpAddress;
        private TcpListener tcpListener;

        private readonly ObservableCollection<Peer> discoveredPeers = new ObservableCollection<Peer>();
        private readonly ObservableCollection<TransferProgress> activeTransfers = new ObservableCollection<TransferProgress>();
        private UdpClient udpListener;

        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private SemaphoreSlim transferSemaphore;
        private bool isExiting = false;

        public MainWindow()
        {
            InitializeComponent();
            GetLocalIPAddress();
            DiscoveredPeersList.ItemsSource = discoveredPeers;
            TransfersList.ItemsSource = activeTransfers;

            LoadSettings();
            ApplyInitialTheme(); // Set the theme on startup
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged; // Listen for theme changes

            var token = cancellationTokenSource.Token;
            Task.Run(() => StartUdpListener(token));
            Task.Run(() => StartTcpListening(token));
        }

        private void LoadSettings()
        {
            Properties.Settings.Default.Reload();

            savePath = Properties.Settings.Default.SavePath;
            if (string.IsNullOrEmpty(savePath) || !Directory.Exists(savePath))
            {
                savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Properties.Settings.Default.SavePath = savePath;
                Properties.Settings.Default.Save();
            }

            computerName = Properties.Settings.Default.ComputerName;
            if (string.IsNullOrWhiteSpace(computerName))
            {
                computerName = Dns.GetHostName();
            }

            transferSemaphore = new SemaphoreSlim(Properties.Settings.Default.MaxConcurrentTransfers);
        }

        // ======================================================================
        // == UI EVENT HANDLERS (QoL Features)
        // ======================================================================

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && Properties.Settings.Default.MinimizeToTray)
            {
                this.Hide();
                TrayIcon.Visibility = Visibility.Visible;
                TrayIcon.ShowBalloonTip("LAN File Sharer", "Running in the background.", BalloonIcon.Info);
            }
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            TrayIcon.Visibility = Visibility.Collapsed;
        }

        private void ShowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TrayIcon_TrayMouseDoubleClick(sender, e);
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            isExiting = true;
            this.Close();
        }

        private void FileName_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is TransferProgress transfer)
            {
                OpenItemInExplorer(transfer);
            }
        }

        private void ClearCompletedButton_Click(object sender, RoutedEventArgs e)
        {
            var completed = activeTransfers.Where(t => t.Status == "Complete" || t.Status == "Cancelled" || t.Status == "Failed").ToList();
            foreach (var item in completed)
            {
                activeTransfers.Remove(item);
            }
        }

        private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string themePreference)
            {
                Properties.Settings.Default.ThemePreference = themePreference;
                Properties.Settings.Default.Save();
                ApplyTheme(themePreference);
                UpdateThemeCheckmarks(themePreference);
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is TransferProgress transfer && transfer.IsLink && transfer.IsFile)
            {
                try
                {
                    // UseShellExecute is important for opening files with their default application
                    Process.Start(new ProcessStartInfo(transfer.FullPath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log($"Error opening file: {ex.Message}");
                }
            }
        }

        private void OpenContainingFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is TransferProgress transfer)
            {
                OpenItemInExplorer(transfer);
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // This event fires when system settings change, including the app theme.
            if (e.Category == UserPreferenceCategory.General && Properties.Settings.Default.ThemePreference == "Auto")
            {
                // We need to run this on the UI thread.
                Dispatcher.Invoke(() => ApplyTheme("Auto"));
            }
        }

        private void ApplyInitialTheme()
        {
            string themePreference = Properties.Settings.Default.ThemePreference;
            ApplyTheme(themePreference);
            UpdateThemeCheckmarks(themePreference);
        }

        private void ApplyTheme(string themePreference)
        {
            string themeToApply = themePreference;
            if (themePreference == "Auto")
            {
                themeToApply = GetWindowsTheme();
            }
            SwitchTheme(themeToApply);
        }

        private string GetWindowsTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        // A value of 1 means light mode, 0 means dark mode.
                        object registryValue = key.GetValue("AppsUseLightTheme");
                        if (registryValue is int value)
                        {
                            return value > 0 ? "LightTheme" : "DarkTheme";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Could not detect Windows theme: {ex.Message}");
            }
            // Default to light theme if detection fails for any reason.
            return "LightTheme";
        }

        private void SwitchTheme(string themeName)
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var currentTheme = dictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Theme"));

            if (currentTheme != null)
            {
                dictionaries.Remove(currentTheme);
            }

            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"Themes/{themeName}.xaml", UriKind.Relative)
            });
            Log($"Applied {themeName}");
        }

        private void UpdateThemeCheckmarks(string themePreference)
        {
            AutoThemeMenuItem.IsChecked = themePreference == "Auto";
            LightThemeMenuItem.IsChecked = themePreference == "LightTheme";
            DarkThemeMenuItem.IsChecked = themePreference == "DarkTheme";
        }

        // ======================================================================
        // == DRAG & DROP LOGIC
        // ======================================================================

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedPaths = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (droppedPaths != null && droppedPaths.Length > 0)
                {
                    selectedPaths = droppedPaths;

                    if (droppedPaths.Length == 1 && Directory.Exists(droppedPaths[0])) FilePathText.Text = $"Folder: {Path.GetFileName(droppedPaths[0])}";
                    else FilePathText.Text = $"{selectedPaths.Length} file(s) selected";

                    SendButton.IsEnabled = true;
                    SendTabControl.SelectedItem = SendTabControl.Items[0];
                    Log($"{selectedPaths.Length} item(s) selected via drag and drop.");
                }
            }
        }

        // ======================================================================
        // == UDP DISCOVERY LOGIC
        // ======================================================================

        private void StartUdpListener(CancellationToken token)
        {
            if (localIpAddress == null) { Log("Could not start listener: No local IP found."); return; }
            try
            {
                udpListener = new UdpClient(new IPEndPoint(localIpAddress, UdpPort));
                IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, UdpPort);
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var receiveTask = udpListener.ReceiveAsync();
                        receiveTask.Wait(token);
                        var result = receiveTask.Result;
                        if (result.RemoteEndPoint.Address.Equals(localIpAddress)) continue;

                        string message = Encoding.UTF8.GetString(result.Buffer);
                        string[] parts = message.Split('|');
                        if (parts.Length != 2) continue;

                        if (parts[0] == "DISCOVER")
                        {
                            byte[] response = Encoding.UTF8.GetBytes($"RESPONSE|{computerName}");
                            udpListener.Send(response, response.Length, result.RemoteEndPoint);
                        }
                        else if (parts[0] == "RESPONSE")
                        {
                            Dispatcher.Invoke(() => {
                                if (!discoveredPeers.Any(p => p.IpAddress == result.RemoteEndPoint.Address.ToString()))
                                {
                                    discoveredPeers.Add(new Peer { HostName = parts[1], IpAddress = result.RemoteEndPoint.Address.ToString() });
                                }
                            });
                        }
                    }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (Exception ex) { if (!token.IsCancellationRequested) Log($"UDP Listener error: {ex.Message}"); }
            finally { udpListener?.Close(); }
        }

        private void DiscoverButton_Click(object sender, RoutedEventArgs e)
        {
            discoveredPeers.Clear();
            Log("Searching for peers...");
            try
            {
                if (udpListener == null) { Log("Listener not ready."); return; }
                udpListener.EnableBroadcast = true;
                IPEndPoint broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, UdpPort);
                byte[] request = Encoding.UTF8.GetBytes($"DISCOVER|{computerName}");
                udpListener.Send(request, request.Length, broadcastEndpoint);
            }
            catch (Exception ex) { Log($"Error sending broadcast: {ex.Message}"); }
        }

        private void DiscoveredPeersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DiscoveredPeersList.SelectedItem is Peer selectedPeer)
            {
                TargetIpTextBox.Text = selectedPeer.IpAddress;
            }
        }

        // ======================================================================
        // == TCP FILE TRANSFER LOGIC
        // ======================================================================

        private void GetLocalIPAddress()
        {
            try
            {
                foreach (var host in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (host.AddressFamily == AddressFamily.InterNetwork)
                    {
                        localIpAddress = host;
                        IpAddressText.Text = $"Your IP Address: {localIpAddress}";
                        return;
                    }
                }
                IpAddressText.Text = "Your IP Address: Not Found";
            }
            catch (Exception ex) { Log($"Error getting IP: {ex.Message}"); }
        }

        private void BrowseFilesButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Multiselect = true };
            if (openFileDialog.ShowDialog() == true)
            {
                selectedPaths = openFileDialog.FileNames;
                FilePathText.Text = $"{selectedPaths.Length} file(s) selected";
                SendButton.IsEnabled = true;
            }
        }

        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog();
            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                selectedPaths = new string[] { dialog.SelectedPath };
                FilePathText.Text = $"Folder: {Path.GetFileName(dialog.SelectedPath)}";
                SendButton.IsEnabled = true;
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPaths.Length == 0) { MessageBox.Show("Please select files or a folder first.", "Error"); return; }
            if (!IPAddress.TryParse(TargetIpTextBox.Text, out IPAddress targetIP)) { MessageBox.Show("Please select a valid peer.", "Error"); return; }

            SendButton.IsEnabled = false;
            await Task.Run(() => SendContent(targetIP, selectedPaths));
            SendButton.IsEnabled = true;
        }

        private async void SendTextButton_Click(object sender, RoutedEventArgs e)
        {
            string text = TextToSendBox.Text;
            if (string.IsNullOrWhiteSpace(text)) { MessageBox.Show("Please enter some text or a URL to send.", "Error"); return; }
            if (!IPAddress.TryParse(TargetIpTextBox.Text, out IPAddress targetIP)) { MessageBox.Show("Please select a valid peer.", "Error"); return; }

            SendTextButton.IsEnabled = false;
            await Task.Run(() => SendText(targetIP, text));
            SendTextButton.IsEnabled = true;
        }

        private void SendContent(IPAddress ip, string[] paths)
        {
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                client.Connect(ip, TcpPort);
                using (var writer = new BinaryWriter(client.GetStream()))
                {
                    bool isFolder = paths.Length == 1 && Directory.Exists(paths[0]);

                    if (isFolder)
                    {
                        string rootPath = paths[0];
                        writer.Write((byte)ContentType.Folder);
                        writer.Write(Path.GetFileName(rootPath));

                        var allFiles = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories);
                        writer.Write(allFiles.Length);

                        foreach (var file in allFiles)
                        {
                            string relativePath = file.Substring(rootPath.Length + 1);
                            writer.Write(relativePath);
                            writer.Write(new FileInfo(file).Length);
                        }

                        foreach (var file in allFiles)
                        {
                            var progress = new TransferProgress { FileName = Path.GetFileName(file), Direction = "Sending", Status = "In Progress" };
                            Dispatcher.Invoke(() => activeTransfers.Add(progress));
                            try
                            {
                                var progressReporter = new Progress<(double percentage, double speed)>(p => {
                                    progress.Progress = p.percentage;
                                    progress.Throughput = $"{p.speed:F2} MB/s";
                                });
                                SendStreamWithProgress(writer.BaseStream, file, progressReporter, progress.Cts.Token);
                                Dispatcher.Invoke(() => {
                                    progress.Progress = 100;
                                    progress.Status = "Complete";
                                    progress.Throughput = "";
                                });
                            }
                            catch (OperationCanceledException)
                            {
                                Dispatcher.Invoke(() => progress.Status = "Cancelled");
                                Log($"Send cancelled for {progress.FileName}");
                            }
                        }
                    }
                    else
                    {
                        writer.Write((byte)ContentType.Files);
                        writer.Write(paths.Length);

                        foreach (var path in paths)
                        {
                            var fileInfo = new FileInfo(path);
                            writer.Write(fileInfo.Name);
                            writer.Write(fileInfo.Length);
                        }

                        foreach (var path in paths)
                        {
                            var progress = new TransferProgress { FileName = Path.GetFileName(path), Direction = "Sending", Status = "In Progress" };
                            Dispatcher.Invoke(() => activeTransfers.Add(progress));
                            try
                            {
                                var progressReporter = new Progress<(double percentage, double speed)>(p => {
                                    progress.Progress = p.percentage;
                                    progress.Throughput = $"{p.speed:F2} MB/s";
                                });
                                SendStreamWithProgress(writer.BaseStream, path, progressReporter, progress.Cts.Token);
                                Dispatcher.Invoke(() => {
                                    progress.Progress = 100;
                                    progress.Status = "Complete";
                                    progress.Throughput = "";
                                });
                            }
                            catch (OperationCanceledException)
                            {
                                Dispatcher.Invoke(() => progress.Status = "Cancelled");
                                Log($"Send cancelled for {progress.FileName}");
                            }
                        }
                    }
                }
            }
        }

        private void SendStreamWithProgress(Stream networkStream, string filePath, IProgress<(double, double)> progress, CancellationToken token)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[8192];
                int bytesRead;
                long totalBytesSent = 0;
                var stopwatch = Stopwatch.StartNew();

                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    networkStream.Write(buffer, 0, bytesRead);
                    totalBytesSent += bytesRead;

                    double percentage = (double)totalBytesSent / fileStream.Length * 100;
                    double speed = totalBytesSent / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds;
                    progress.Report((percentage, speed));
                }
            }
        }

        private void SendText(IPAddress ip, string text)
        {
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                client.Connect(ip, TcpPort);
                using (var writer = new BinaryWriter(client.GetStream()))
                {
                    writer.Write((byte)ContentType.Text);
                    writer.Write(text);
                }
                Log($"Sent text: '{text.Substring(0, Math.Min(text.Length, 50))}'");
            }
        }

        private async void StartTcpListening(CancellationToken token)
        {
            try
            {
                tcpListener = new TcpListener(IPAddress.Any, TcpPort);
                tcpListener.Start();
                Log("Ready to receive content.");

                while (!token.IsCancellationRequested)
                {
                    await transferSemaphore.WaitAsync(token);

                    var acceptTask = tcpListener.AcceptTcpClientAsync();
                    await Task.WhenAny(acceptTask, Task.Delay(-1, token));
                    if (token.IsCancellationRequested) break;

                    _ = Task.Run(() => HandleClient(acceptTask.Result));
                }
            }
            catch (OperationCanceledException) { Log("TCP listener shutting down."); }
            catch (Exception ex) { if (!token.IsCancellationRequested) Log($"TCP Listener error: {ex.Message}"); }
            finally { tcpListener?.Stop(); }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (var reader = new BinaryReader(client.GetStream()))
                {
                    ContentType type = (ContentType)reader.ReadByte();
                    if (type == ContentType.Files) HandleFileTransfer(reader);
                    else if (type == ContentType.Text) HandleTextTransfer(reader);
                    else if (type == ContentType.Folder) HandleFolderTransfer(reader);
                }
            }
            catch (Exception ex)
            {
                Log($"Error handling client: {ex.Message}");
            }
            finally
            {
                client.Close();
                transferSemaphore.Release();
            }
        }

        private void HandleFileTransfer(BinaryReader reader)
        {
            int fileCount = reader.ReadInt32();
            var manifest = new List<(string Name, long Size)>();
            for (int i = 0; i < fileCount; i++)
            {
                manifest.Add((reader.ReadString(), reader.ReadInt64()));
            }

            foreach (var (name, size) in manifest)
            {
                var progress = new TransferProgress { FileName = name, Direction = "Receiving", Status = "In Progress" };
                Dispatcher.Invoke(() => activeTransfers.Add(progress));
                progress.IsFile = true; // Mark as a single file transfer
                try
                {
                    var progressReporter = new Progress<(double percentage, double speed)>(p => {
                        progress.Progress = p.percentage;
                        progress.Throughput = $"{p.speed:F2} MB/s";
                    });
                    string sanitizedFileName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                    string finalPath = Path.Combine(savePath, sanitizedFileName);
                    progress.FullPath = finalPath; // Set the full path for the link
                    ReceiveStreamToFile(reader, finalPath, size, progressReporter, progress.Cts.Token);
                    Dispatcher.Invoke(() => {
                        progress.Progress = 100;
                        progress.Status = "Complete";
                        progress.Throughput = "";
                    });
                    Log($"Received '{sanitizedFileName}'");
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.Invoke(() => progress.Status = "Cancelled");
                    Log($"Receive cancelled for {progress.FileName}");
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => progress.Status = "Failed");
                    Log($"Failed to receive {progress.FileName}: {ex.Message}");
                }
            }

            if (Properties.Settings.Default.ShowReceiveNotification)
            {
                if (Properties.Settings.Default.PlaySoundOnCompletion) SystemSounds.Asterisk.Play();
                Dispatcher.Invoke(() => MessageBox.Show($"{fileCount} file(s) received and saved to your '{Path.GetFileName(savePath)}' folder.", "Transfer Complete", MessageBoxButton.OK, MessageBoxImage.Information));
            }
        }

        private void HandleFolderTransfer(BinaryReader reader)
        {
            string rootFolderName = reader.ReadString();
            string sanitizedRoot = string.Join("_", rootFolderName.Split(Path.GetInvalidFileNameChars()));
            string finalFolderPath = Path.Combine(savePath, sanitizedRoot);
            Directory.CreateDirectory(finalFolderPath);

            int fileCount = reader.ReadInt32();
            var manifest = new List<(string RelativePath, long Size)>();
            for (int i = 0; i < fileCount; i++)
            {
                manifest.Add((reader.ReadString(), reader.ReadInt64()));
            }

            foreach (var (relativePath, size) in manifest)
            {
                var progress = new TransferProgress { FileName = relativePath, Direction = "Receiving", Status = "In Progress" };
                Dispatcher.Invoke(() => activeTransfers.Add(progress));
                progress.IsFile = false; // Part of a folder transfer
                try
                {
                    var progressReporter = new Progress<(double percentage, double speed)>(p => {
                        progress.Progress = p.percentage;
                        progress.Throughput = $"{p.speed:F2} MB/s";
                    });
                    string finalPath = Path.Combine(finalFolderPath, relativePath);
                    progress.FullPath = finalFolderPath; // Set the full path to the root folder
                    Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
                    ReceiveStreamToFile(reader, finalPath, size, progressReporter, progress.Cts.Token);
                    Dispatcher.Invoke(() => {
                        progress.Progress = 100;
                        progress.Status = "Complete";
                        progress.Throughput = "";
                    });
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.Invoke(() => progress.Status = "Cancelled");
                    Log($"Receive cancelled for {progress.FileName}");
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => progress.Status = "Failed");
                    Log($"Failed to receive {progress.FileName}: {ex.Message}");
                }
            }

            if (Properties.Settings.Default.ShowReceiveNotification)
            {
                if (Properties.Settings.Default.PlaySoundOnCompletion) SystemSounds.Asterisk.Play();
                Dispatcher.Invoke(() => MessageBox.Show($"Folder '{sanitizedRoot}' received and saved to your '{Path.GetFileName(savePath)}' folder.", "Transfer Complete", MessageBoxButton.OK, MessageBoxImage.Information));
            }
        }

        private void ReceiveStreamToFile(BinaryReader reader, string filePath, long size, IProgress<(double, double)> progress, CancellationToken token)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[8192];
                long totalBytesReceived = 0;
                var stopwatch = Stopwatch.StartNew();

                while (totalBytesReceived < size)
                {
                    token.ThrowIfCancellationRequested();
                    int bytesToRead = (int)Math.Min(buffer.Length, size - totalBytesReceived);
                    int bytesRead = reader.Read(buffer, 0, bytesToRead);
                    if (bytesRead == 0) break;
                    fileStream.Write(buffer, 0, bytesRead);
                    totalBytesReceived += bytesRead;

                    double percentage = (double)totalBytesReceived / size * 100;
                    double speed = totalBytesReceived / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds;
                    progress.Report((percentage, speed));
                }
            }
        }

        private void HandleTextTransfer(BinaryReader reader)
        {
            string text = reader.ReadString();
            Log($"Received text: '{text}'");

            bool isUrl = Uri.TryCreate(text, UriKind.Absolute, out Uri uriResult)
                         && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

            if (isUrl)
            {
                Log("Content is a URL, opening in browser.");
                try
                {
                    Process.Start(new ProcessStartInfo(text) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log($"Failed to open URL: {ex.Message}");
                    MessageBox.Show($"Could not open the URL:\n{text}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                Log("Content is plain text, opening in Notepad.");
                try
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), $"LANShare_{Path.GetRandomFileName()}.txt");
                    File.WriteAllText(tempFile, text);
                    Process.Start("notepad.exe", tempFile);
                }
                catch (Exception ex) { Log($"Failed to open text in Notepad: {ex.Message}"); }
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
            LoadSettings();
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() => StatusLog.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}"));
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Unsubscribe from the event to prevent memory leaks
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            if (Properties.Settings.Default.MinimizeToTray && !isExiting)
            {
                e.Cancel = true;
                this.Hide();
                TrayIcon.Visibility = Visibility.Visible;
            }
            else
            {
                cancellationTokenSource.Cancel();
                udpListener?.Close();
                tcpListener?.Stop();
                TrayIcon.Dispose();
            }
        }

        private void StopTransferButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is TransferProgress transfer)
            {
                transfer.Cts.Cancel();
            }
        }

        private void OpenItemInExplorer(TransferProgress transfer)
        {
            if (transfer.IsLink)
            {
                try
                {
                    if (File.Exists(transfer.FullPath))
                    {
                        // Open the folder and select the file
                        Process.Start("explorer.exe", $"/select,\"{transfer.FullPath}\"");
                    }
                    else if (Directory.Exists(transfer.FullPath))
                    {
                        // Open the folder itself
                        Process.Start("explorer.exe", $"\"{transfer.FullPath}\"");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error opening item: {ex.Message}");
                }
            }
        }
    }
}