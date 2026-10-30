using Jwmv.Infrastructure.Shell;

namespace Jwmv.Tests;

public sealed class InstallLocationAdvisorTests
{
    private const string UserProfile = @"C:\Users\me";
    private const string TempDirectory = @"C:\Users\me\AppData\Local\Temp";

    [Theory]
    [InlineData(@"C:\Users\me\.jwmv\bin\jwmv.exe")]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WinGet\Packages\stescobedo92.jwmv_Microsoft.Winget.Source_8wekyb3d8bbwe\jwmv.exe")]
    [InlineData(@"C:\Program Files\jwmv\jwmv.exe")]
    [InlineData(@"C:\Users\me\.dotnet\tools\jwmv.exe")]
    public void GetWarning_ReturnsNullForInstallationLocations(string executablePath)
    {
        Assert.Null(InstallLocationAdvisor.GetWarning(executablePath, UserProfile, TempDirectory));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\jwmv-win-x64\jwmv.exe")]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\extract\jwmv.exe")]
    [InlineData(@"\\fileserver\share\tools\jwmv.exe")]
    [InlineData(@"C:\Users\me\Desktop\jwmv.exe")]
    public void GetWarning_FlagsTransientOrRemoteLocations(string executablePath)
    {
        var warning = InstallLocationAdvisor.GetWarning(executablePath, UserProfile, TempDirectory);

        Assert.NotNull(warning);
        Assert.Contains(executablePath, warning, StringComparison.Ordinal);
    }
}
