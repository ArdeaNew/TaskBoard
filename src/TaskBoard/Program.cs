using System;
using System.IO;
using System.Threading;
using System.Windows;
using TaskBoard.Persistence;
using TaskBoard.UI;

namespace TaskBoard
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskBoard");
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--data-dir") dataPath = Path.GetFullPath(args[++i]);

            string mutexName;
            using (var hash = System.Security.Cryptography.SHA256.Create())
                mutexName = "Local\\TaskBoard-" + BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dataPath.ToUpperInvariant()))).Replace("-", "").Substring(0, 24);
            bool first;
            using (var mutex = new Mutex(true, mutexName, out first))
            {
                if (!first) return;
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.DispatcherUnhandledException += (sender, e) =>
                    {
                        MessageBox.Show(e.Exception.Message, "Task", MessageBoxButton.OK, MessageBoxImage.Error);
                        e.Handled = true;
                    };
                    app.Run(new MainWindow(new BoardStore(dataPath)));
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
