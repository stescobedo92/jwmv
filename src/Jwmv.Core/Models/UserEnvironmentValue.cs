namespace Jwmv.Core.Models;

/// <summary>
/// A persistent per-user environment variable as stored in the registry.
/// </summary>
/// <param name="Value">The stored, unexpanded value.</param>
/// <param name="IsExpandable">Whether the value is stored as REG_EXPAND_SZ (variables such as <c>%USERPROFILE%</c> are expanded on use).</param>
public sealed record UserEnvironmentValue(string Value, bool IsExpandable);
