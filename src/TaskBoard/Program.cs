using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using TaskBoard.Persistence;
using TaskBoard.UI;

namespace TaskBoard
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("en-US")));
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
                        BoardDialog.Show(app.MainWindow, e.Exception.Message, "TaskBoard");
                        e.Handled = true;
                    };
                    app.Run(new MainWindow(new BoardStore(dataPath)));
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
