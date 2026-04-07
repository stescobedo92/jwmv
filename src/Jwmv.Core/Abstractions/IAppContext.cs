using System.Runtime.InteropServices;

namespace Jwmv.Core.Abstractions;

public interface IAppContext
{
    string WorkingDirectory { get; }
    string UserProfileDirectory { get; }
    Architecture ProcessArchitecture { get; }
    string ExecutablePath { get; }
    string? GetEnvironmentVariable(string variableName);
}
