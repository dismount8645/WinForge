using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WingetStore.Pages;

public sealed partial class OptimizerPage : Page
{
    public OptimizerPage()
    {
        InitializeComponent();
    }

    private void OptimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = RunScriptAsync("Optimize-Windows.ps1", "Windows System Optimization");
    }

    private void CleanDiskBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = RunScriptAsync("Clean-Disk.ps1", "Disk & Cache Cleanup");
    }

    private void FlushMemoryBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = RunScriptAsync("Flush-Memory.ps1", "Memory Flush");
    }

    private void AuditBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = RunScriptAsync("Audit-System.ps1", "System Audit");
    }

    private void UndoBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = RunScriptAsync("Undo-Optimization.ps1", "Restore Defaults");
    }

    private async Task RunScriptAsync(string scriptName, string taskTitle)
    {
        OptInfoBar.IsOpen = false;
        ConsoleOutputBox.Text = $"Starting {taskTitle} ({scriptName})...\n";

        var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "tools", "cli", "optimizer", "Core", scriptName);

        if (!File.Exists(scriptPath))
        {
            // Fallback check in tools/cli/optimizer
            scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "tools", "cli", "optimizer", scriptName);
        }

        if (!File.Exists(scriptPath))
        {
            OptInfoBar.Severity = InfoBarSeverity.Warning;
            OptInfoBar.Title = "Script Location";
            OptInfoBar.Message = $"PowerShell script '{scriptName}' is ready in the tools/cli/optimizer suite.";
            OptInfoBar.IsOpen = true;
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var stdout = await proc.StandardOutput.ReadToEndAsync();
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                ConsoleOutputBox.Text += stdout;
                if (!string.IsNullOrEmpty(stderr))
                {
                    ConsoleOutputBox.Text += $"\n[Errors/Warnings]:\n{stderr}";
                }

                OptInfoBar.Severity = proc.ExitCode == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error;
                OptInfoBar.Title = $"{taskTitle} Completed";
                OptInfoBar.Message = proc.ExitCode == 0 ? "Task finished successfully." : $"Process exited with code {proc.ExitCode}.";
                OptInfoBar.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            OptInfoBar.Severity = InfoBarSeverity.Error;
            OptInfoBar.Title = "Execution Error";
            OptInfoBar.Message = ex.Message;
            OptInfoBar.IsOpen = true;
        }
    }
}
