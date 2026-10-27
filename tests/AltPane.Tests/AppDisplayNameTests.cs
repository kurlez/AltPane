using AltPane.Core.Apps;
using Xunit;

namespace AltPane.Tests;

public class AppDisplayNameTests
{
    [Fact]
    public void Path_wins_and_a_shared_process_name_does_not_merge_apps()
    {
        AppDisplayNames.Replace(new[]
        {
            new AppEntry
            {
                Name = "Google Chrome",
                ProcessName = "chrome",
                ResolvedPath = @"C:\Program Files\Google\Chrome\Application\chrome.exe"
            },
            new AppEntry
            {
                Name = "Chrome Beta",
                ProcessName = "chrome",
                ResolvedPath = @"C:\Program Files\Google\Chrome Beta\Application\chrome.exe"
            },
            new AppEntry
            {
                Name = "记事本",
                ProcessName = "notepad",
                ResolvedPath = @"C:\Windows\System32\notepad.exe"
            }
        });

        Assert.Equal("Google Chrome", AppDisplayNames.Resolve("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.Equal("Chrome Beta", AppDisplayNames.Resolve("chrome", @"C:\Program Files\Google\Chrome Beta\Application\chrome.exe"));
        Assert.Equal("chrome", AppDisplayNames.Resolve("chrome", @"D:\unknown\chrome.exe"));
        Assert.Equal("记事本", AppDisplayNames.Resolve("notepad", ""));
        Assert.Equal("Code", AppDisplayNames.Resolve("Code.exe", ""));
    }
}
