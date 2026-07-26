using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using WingetStore.Pages;

namespace WingetStore.UITests;

[TestClass]
public partial class PageInstantiationTests
{
    [UITestMethod]
    public void HomePage_CanBeCreated()
    {
        var page = new HomePage();
        Assert.IsNotNull(page);
        Assert.IsInstanceOfType(page, typeof(Page));
    }

    [UITestMethod]
    public void InstalledPage_CanBeCreated()
    {
        var page = new InstalledPage();
        Assert.IsNotNull(page);
        Assert.IsInstanceOfType(page, typeof(Page));
    }

    [UITestMethod]
    public void UpdatesPage_CanBeCreated()
    {
        var page = new UpdatesPage();
        Assert.IsNotNull(page);
        Assert.IsInstanceOfType(page, typeof(Page));
    }

    [UITestMethod]
    public void SettingsPage_CanBeCreated()
    {
        var page = new SettingsPage();
        Assert.IsNotNull(page);
        Assert.IsInstanceOfType(page, typeof(Page));
    }

    [UITestMethod]
    public void AboutPage_CanBeCreated()
    {
        var page = new AboutPage();
        Assert.IsNotNull(page);
        Assert.IsInstanceOfType(page, typeof(Page));
    }

    [UITestMethod]
    public void DetailsPage_CanBeCreated()
    {
        var page = new DetailsPage();
        Assert.IsNotNull(page);
        Assert.IsInstanceOfType(page, typeof(Page));
    }

    [UITestMethod]
    public void NoWingetPage_CanBeCreated()
    {
        var page = new NoWingetPage();
        Assert.IsNotNull(page);
        Assert.IsInstanceOfType(page, typeof(Page));
    }
}
