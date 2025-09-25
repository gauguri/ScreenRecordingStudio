// ScreenRecordingStudio.UI/App.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Windows;
using ScreenRecordingStudio.Core.Interfaces;
using ScreenRecordingStudio.Core.Services;

namespace ScreenRecordingStudio.UI
{
    public partial class App : Application
    {
        private IHost _host;
        public static IServiceProvider ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Build the host
            _host = CreateHostBuilder().Build();
            ServiceProvider = _host.Services;

            // Start the host
            _host.Start();

            // Create and show the main window
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            base.OnExit(e);
        }

        private static IHostBuilder CreateHostBuilder()
        {
            return Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Register core services
                    services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
                    services.AddSingleton<IVideoEncoderService, WindowsGraphicsCaptureService>();
                    services.AddSingleton<IRecordingService, RecordingService>();

                    // Register UI services
                    services.AddTransient<MainWindow>();
                });
        }
    }
}