using System.Reflection;
using System.Runtime.InteropServices;
using Jwmv.Core.Abstractions;

namespace Jwmv.Infrastructure.Runtime;

public sealed class DefaultAppContext : IAppContext
{
    public string WorkingDirectory => Environment.CurrentDirectory;

    public string UserProfileDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;

    public string ExecutablePath =>
        Environment.ProcessPath
        ?? Assembly.GetEntryAssembly()?.Location
        ?? throw new InvalidOperationException("Unable to resolve the current executable path.");

    public string? GetEnvironmentVariable(string variableName) =>
        Environment.GetEnvironmentVariable(variableName);
}
