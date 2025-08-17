using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;

namespace LANFileSharing
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // A unique name for the mutex. This should be unique for your application.
        private const string AppMutexName = "LANFileSharing-SingleInstanceMutex";
        private Mutex _appMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _appMutex = new Mutex(true, AppMutexName, out bool createdNew);

            if (!createdNew)
            {
                // Another instance of the application is already running.
                // If there are command-line arguments, send them to the existing instance.
                if (e.Args.Length > 0)
                {
                    SendArgsToFirstInstance(e.Args);
                }

                // Shutdown this new instance.
                Shutdown();
                return;
            }

            // This is the first instance of the application.
            base.OnStartup(e);
            StartArgumentListener();

            MainWindow mainWindow = new MainWindow();

            if (e.Args.Length > 0)
            {
                mainWindow.ProcessCommandLineArgs(e.Args);
            }

            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _appMutex?.ReleaseMutex();
            base.OnExit(e);
        }

        private void SendArgsToFirstInstance(string[] args)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", AppMutexName, PipeDirection.Out))
                {
                    client.Connect(200); // Timeout of 200ms
                    if (client.IsConnected)
                    {
                        // Join all arguments into a single string, separated by a unique delimiter.
                        var message = string.Join("|||", args);
                        var buffer = Encoding.UTF8.GetBytes(message);
                        client.Write(buffer, 0, buffer.Length);
                    }
                }
            }
            catch (Exception)
            {
                // Could not connect to the first instance. It might be closing.
            }
        }

        private void StartArgumentListener()
        {
            // Start a background thread to listen for arguments from new instances.
            var listenerThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(AppMutexName, PipeDirection.In))
                        {
                            server.WaitForConnection();
                            using (var reader = new StreamReader(server))
                            {
                                var message = reader.ReadToEnd();
                                var args = message.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries);

                                // Pass the received arguments to the main window on the UI thread.
                                Dispatcher.Invoke(() =>
                                {
                                    if (Current.MainWindow is MainWindow mainWindow)
                                    {
                                        mainWindow.ProcessCommandLineArgs(args);
                                        // Bring the window to the front.
                                        if (mainWindow.WindowState == WindowState.Minimized)
                                        {
                                            mainWindow.WindowState = WindowState.Normal;
                                        }
                                        mainWindow.Activate();
                                    }
                                });
                            }
                        }
                    }
                    catch
                    {
                        // If something goes wrong, break the loop.
                        break;
                    }
                }
            })
            {
                IsBackground = true
            };
            listenerThread.Start();
        }
    }
}
