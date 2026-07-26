using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using WingetStore.Pages;

namespace WingetStore.UITests;

public partial class UnitTestApp : Application
{
    private Window? _window;

    public UnitTestApp()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        App.Dispatch(() => _ = App.Services);

        Microsoft.VisualStudio.TestPlatform.TestExecutor.UnitTestClient.CreateDefaultUI();

        _window = new UnitTestAppWindow();
        _window.Activate();

        UITestMethodAttribute.DispatcherQueue = _window.DispatcherQueue;

        Microsoft.VisualStudio.TestPlatform.TestExecutor.UnitTestClient.Run(Environment.CommandLine);

        Application.Current.Exit();
    }
}
