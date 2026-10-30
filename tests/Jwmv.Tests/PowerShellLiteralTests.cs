using System.Diagnostics;
using System.Text;
using Jwmv.Core.Utilities;
using Jwmv.Infrastructure.Windows;

namespace Jwmv.Tests;

public sealed class PowerShellLiteralTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("it's", "it''s")]
    [InlineData("a\u2018b", "a\u2018\u2018b")]
    [InlineData("a\u2019b", "a\u2019\u2019b")]
    [InlineData("a\u201Ab", "a\u201A\u201Ab")]
    [InlineData("a\u201Bb", "a\u201B\u201Bb")]
    [InlineData("$env:TEMP `n \"x\" ; & { }", "$env:TEMP `n \"x\" ; & { }")]
    public void Escape_DoublesEveryCharacterPowerShellTreatsAsASingleQuote(string value, string expected)
    {
        Assert.Equal(expected, PowerShellLiteral.Escape(value));
    }

    [Fact]
    public void SingleQuote_WrapsTheEscapedValue()
    {
        Assert.Equal("'it''s \u2019\u2019'", PowerShellLiteral.SingleQuote("it's \u2019"));
    }

    [Theory]
    [InlineData("v1\u2019; Write-Output \"INJECTED\"; #")]
    [InlineData("C:\\Users\\O\u2019Brien\\.jwmv\\candidates\\java\\21")]
    [InlineData("it's \u2018quoted\u2019 \u201Alow\u201B $env:TEMP `n \"x\" ; & { }")]
    public async Task SingleQuote_RoundTripsThroughARealPowerShell(string value)
    {
        // Windows always ships powershell.exe in System32, so the locator never returns null on the supported platform.
        var powerShell = ProcessLocator.FindPowerShell();
        Assert.NotNull(powerShell);

        var scriptPath = Path.Combine(Path.GetTempPath(), "jwmv-ps-literal-tests", Guid.NewGuid().ToString("n") + ".ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        var script = "$ErrorActionPreference = 'Stop'\n"
            + $"$value = {PowerShellLiteral.SingleQuote(value)}\n"
            + "Write-Output ([Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($value)))\n";
        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        try
        {
            var startInfo = new ProcessStartInfo(powerShell!)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var errorTask = process.StandardError.ReadToEndAsync();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            var error = await errorTask;

            Assert.True(process.ExitCode == 0, error);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Single(lines);
            Assert.Equal(value, Encoding.UTF8.GetString(Convert.FromBase64String(lines[0])));
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }
}
