using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;
using OpenQA.Selenium;

namespace NzbDrone.Automation.Test
{
    [TestFixture]
    public class InteractiveImportTest : AutomationTest
    {
        private PageBase _page;

        [SetUp]
        public void Setup()
        {
            _page = new PageBase(driver);
        }

        [Test]
        public void should_migrate_localstorage_to_backend_and_persist()
        {
            _page.WantedNavIcon.Click();
            _page.WaitForNoSpinner();

            // Inject localStorage to simulate previous client state
            var json = "{\"state\":{\"recentFolders\":[],\"favoriteFolders\":[],\"pinnedPaths\":[{\"id\":\"test1\",\"label\":\"My Pin\",\"path\":\"/some/path\"}],\"activePinnedPathId\":\"test1\"},\"version\":0}";
            ((IJavaScriptExecutor)driver).ExecuteScript($"window.localStorage.setItem('interactive_import_folders', '{json}');");

            // Open Manual Import modal to trigger migration
            _page.Find(By.CssSelector("button[title='Manual Import']")).Click();
            Thread.Sleep(2000); // Wait for modal to render and useEffect to run

            // Check if local storage was cleared
            var localData = ((IJavaScriptExecutor)driver).ExecuteScript("return window.localStorage.getItem('interactive_import_folders');");
            localData.Should().BeNull("Local storage should be cleared after migration");

            // Close modal
            _page.Find(By.CssSelector(".Modal-closeButton-b_zUu")).Click();
            Thread.Sleep(1000);

            // Simulate fresh client state
            ((IJavaScriptExecutor)driver).ExecuteScript("window.localStorage.clear();");
            driver.Navigate().Refresh();
            _page.WaitForNoSpinner();

            // Open Manual Import again
            _page.Find(By.CssSelector("button[title='Manual Import']")).Click();
            Thread.Sleep(2000);

            // Verify pin was retrieved from backend
            var pinnedRowAfterRefresh = _page.Find(By.XPath("//td[contains(text(), 'My Pin')]"));
            pinnedRowAfterRefresh.Should().NotBeNull("Pinned path should have persisted to the backend and retrieved");
        }

        [Test]
        public void should_fallback_when_active_pin_deleted()
        {
            _page.WantedNavIcon.Click();
            _page.WaitForNoSpinner();

            // Inject backend UI settings using API directly or just via Javascript
            // For simplicity, we just inject it into local storage, trigger migration to populate backend, then interact.
            var json = "{\"state\":{\"recentFolders\":[],\"favoriteFolders\":[],\"pinnedPaths\":[{\"id\":\"test2\",\"label\":\"Active Pin\",\"path\":\"/some/path2\"}],\"activePinnedPathId\":\"test2\"},\"version\":0}";
            ((IJavaScriptExecutor)driver).ExecuteScript($"window.localStorage.setItem('interactive_import_folders', '{json}');");

            _page.Find(By.CssSelector("button[title='Manual Import']")).Click();
            Thread.Sleep(2000);

            // Verify it landed in the browser (input has the path)
            var input = _page.Find(By.CssSelector(".PathInput-input-n_ZzS"));
            input.GetAttribute("value").Should().Be("/some/path2", "Browser should land on the active pinned path");

            // Click the delete button for the pin (assuming it has a trash icon)
            var deleteButton = _page.Find(By.XPath("//td[contains(text(), 'Active Pin')]/following-sibling::td//button[.//i[contains(@class, 'fa-trash-alt')]]"));
            deleteButton.Click();
            Thread.Sleep(1000);

            // Verify the active pin icon is now heart-outline or empty (active pin is null)
            // We can check if the input changes or if we close and reopen
            _page.Find(By.CssSelector(".Modal-closeButton-b_zUu")).Click();
            Thread.Sleep(1000);

            // Re-open
            _page.Find(By.CssSelector("button[title='Manual Import']")).Click();
            Thread.Sleep(2000);

            // Input should be empty now
            var inputAfter = _page.Find(By.CssSelector(".PathInput-input-n_ZzS"));
            inputAfter.GetAttribute("value").Should().Be("", "Active pin should fall back to empty when deleted");
        }
    }
}
