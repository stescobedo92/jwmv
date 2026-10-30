using System.Runtime.Versioning;
using Jwmv.Core.Exceptions;
using Jwmv.Infrastructure.Windows;
using Microsoft.Win32;

namespace Jwmv.Tests;

[SupportedOSPlatform("windows")]
public sealed class RegistryUserEnvironmentStoreTests : IDisposable
{
    private readonly string _keyPath = @"Software\jwmv-tests\" + Guid.NewGuid().ToString("n");
    private readonly RegistryUserEnvironmentStore _store;

    public RegistryUserEnvironmentStoreTests()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        key.SetValue("Path", @"%USERPROFILE%\bin;C:\Tools", RegistryValueKind.ExpandString);
        key.SetValue("PLAIN", @"C:\plain", RegistryValueKind.String);
        _store = new RegistryUserEnvironmentStore(_keyPath);
    }

    [Fact]
    public void Get_ReturnsNullForAVariableThatDoesNotExist()
    {
        Assert.Null(_store.Get("JWMV_TESTS_DOES_NOT_EXIST"));
    }

    [Fact]
    public void Get_ReturnsTheUnexpandedValueAndWhetherItIsExpandable()
    {
        var path = _store.Get("Path");
        var plain = _store.Get("PLAIN");

        Assert.NotNull(path);
        Assert.Equal(@"%USERPROFILE%\bin;C:\Tools", path!.Value);
        Assert.True(path.IsExpandable);
        Assert.NotNull(plain);
        Assert.Equal(@"C:\plain", plain!.Value);
        Assert.False(plain.IsExpandable);
    }

    [Fact]
    public void Set_WritesTheRequestedValueKind()
    {
        _store.Set("JWMV_TESTS_EXPAND", @"%USERPROFILE%\x", isExpandable: true);
        _store.Set("JWMV_TESTS_PLAIN", "x", isExpandable: false);

        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: false);
        Assert.NotNull(key);
        Assert.Equal(RegistryValueKind.ExpandString, key!.GetValueKind("JWMV_TESTS_EXPAND"));
        Assert.Equal(RegistryValueKind.String, key.GetValueKind("JWMV_TESTS_PLAIN"));
        Assert.Equal(@"%USERPROFILE%\x", _store.Get("JWMV_TESTS_EXPAND")?.Value);
    }

    [Fact]
    public void Remove_DeletesTheValueAndIgnoresMissingOnes()
    {
        _store.Remove("PLAIN");
        _store.Remove("PLAIN");

        Assert.Null(_store.Get("PLAIN"));
    }

    [Fact]
    public void Get_RejectsNamesThatAreNotEnvironmentVariableIdentifiers()
    {
        Assert.ThrowsAny<JwmvException>(() => _store.Get("BAD=NAME"));
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
    }
}
