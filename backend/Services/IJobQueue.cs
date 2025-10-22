using System.Threading;
using System.Threading.Tasks;

namespace backend.Services;

public interface IJobQueue
{
    void EnqueueJob(int jobId);
    Task<int> DequeueJobAsync(CancellationToken cancellationToken);
}
