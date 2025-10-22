using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace backend.Services;

public class JobQueue : IJobQueue
{
    private readonly Channel<int> _queue;

    public JobQueue()
    {
        _queue = Channel.CreateUnbounded<int>();
    }

    public void EnqueueJob(int jobId)
    {
        if (!_queue.Writer.TryWrite(jobId))
        {
            throw new InvalidOperationException("Unable to enqueue assessment job.");
        }
    }

    public async Task<int> DequeueJobAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }
}
