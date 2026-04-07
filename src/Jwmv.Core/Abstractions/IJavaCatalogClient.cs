using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface IJavaCatalogClient
{
    Task<IReadOnlyList<JavaDistributionPackage>> GetAvailablePackagesAsync(CancellationToken cancellationToken);
}
