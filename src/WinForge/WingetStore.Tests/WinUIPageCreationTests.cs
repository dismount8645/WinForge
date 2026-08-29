namespace WingetStore.Tests;

public class WinUIPageCreationTests
{
    public WinUIPageCreationTests() => WinUIApp.EnsureStarted();

    [Fact]
    public void CanCreateSettingsPage()
    {
        SettingsPage? page = null;
        WinUIApp.Run(() => { page = new SettingsPage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateHomePage()
    {
        HomePage? page = null;
        WinUIApp.Run(() => { page = new HomePage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateInstalledPage()
    {
        InstalledPage? page = null;
        WinUIApp.Run(() => { page = new InstalledPage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateUpdatesPage()
    {
        UpdatesPage? page = null;
        WinUIApp.Run(() => { page = new UpdatesPage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateDetailsPage()
    {
        DetailsPage? page = null;
        WinUIApp.Run(() => { page = new DetailsPage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateAboutPage()
    {
        AboutPage? page = null;
        WinUIApp.Run(() => { page = new AboutPage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateNoWingetPage()
    {
        NoWingetPage? page = null;
        WinUIApp.Run(() => { page = new NoWingetPage(); });
        Assert.NotNull(page);
    }
}
