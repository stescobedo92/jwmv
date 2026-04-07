using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface ISelfUpdateService
{
    Task<SelfUpdateCheckResult> CheckForUpdateAsync(SelfUpdateRequest request, CancellationToken cancellationToken);
    Task<SelfUpdateResult> ApplyUpdateAsync(SelfUpdateCheckResult checkResult, bool restartAfterUpdate, IProgress<SelfUpdateProgressUpdate>? progress, CancellationToken cancellationToken);
}
