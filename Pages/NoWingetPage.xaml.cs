using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace WingetStore.Pages;

public sealed partial class NoWingetPage : Page
{
    private const string WingetDownloadUrl = "https://github.com/microsoft/winget-cli/releases/latest/download/Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle";
    private CancellationTokenSource? _installCts;
    public NoWingetPage() => InitializeComponent();

    public static double CalculateDownloadProgress(long totalRead, long totalBytes) => totalBytes > 0 ? Math.Min(totalRead * 100.0 / totalBytes, 100) : 0;

    public static string GetTempInstallerPath(string tempDir) => Path.Combine(tempDir, "Microsoft.DesktopAppInstaller.msixbundle");
    public static string GetPowershellInstallArguments(string tempPath) => $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '{tempPath}'\"";

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _installCts?.Cancel();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        var ct = _installCts.Token;

        InstallButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "WingetStore");
            Directory.CreateDirectory(tempDir);
            string tempPath = GetTempInstallerPath(tempDir);

            StatusText.Text = "Downloading Winget installer...";
            using (var client = new HttpClient())
            {
                using var response = await client.GetAsync(WingetDownloadUrl, HttpCompletionOption.ResponseHeadersRead).WaitAsync(ct);
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using var stream = await response.Content.ReadAsStreamAsync().WaitAsync(ct);
                using var fileStream = File.Create(tempPath);
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    totalRead += read;
                    if (totalBytes > 0)
                    {
                        DispatcherQueue.TryEnqueue(() => InstallProgress.Value = CalculateDownloadProgress(totalRead, totalBytes));
                    }
                }
            }

            ct.ThrowIfCancellationRequested();

            StatusText.Text = "Installing Winget...";
            InstallProgress.IsIndeterminate = true;

            var processInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = GetPowershellInstallArguments(tempPath),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            using (var process = Process.Start(processInfo))
            {
                if (process != null)
                {
                    await process.WaitForExitAsync(ct);
                    ct.ThrowIfCancellationRequested();
                    if (process.ExitCode == 0)
                    {
                        StatusText.Text = "Installation successful! Starting application...";
                        InstallProgress.IsIndeterminate = false;
                        InstallProgress.Value = 100;
                        await Task.Delay(2000, ct);
                        Frame.Navigate(typeof(HomePage));
                        return;
                    }
                }
            }

            ct.ThrowIfCancellationRequested();

            StatusText.Text = "Launching App Installer GUI...";
            Process.Start(new ProcessStartInfo { FileName = tempPath, UseShellExecute = true });
            StatusText.Text = "Please complete the installation in the App Installer window, then restart the application.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Installation cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed: {ex.Message}";
            InstallButton.IsEnabled = true;
            InstallProgress.IsIndeterminate = false;
        }
    }
}
