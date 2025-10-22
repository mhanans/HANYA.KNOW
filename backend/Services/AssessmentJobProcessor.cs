using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace backend.Services;

public class AssessmentJobProcessor : BackgroundService
{
    private readonly ILogger<AssessmentJobProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IJobQueue _jobQueue;

    public AssessmentJobProcessor(
        ILogger<AssessmentJobProcessor> logger,
        IServiceProvider serviceProvider,
        IJobQueue jobQueue)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _jobQueue = jobQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Assessment job processor is running.");

        while (!stoppingToken.IsCancellationRequested)
        {
            int jobId;
            try
            {
                jobId = await _jobQueue.DequeueJobAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var analysisService = scope.ServiceProvider.GetRequiredService<ProjectAssessmentAnalysisService>();
                await analysisService.ExecuteFullPipelineAsync(jobId, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing assessment job {JobId}.", jobId);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Assessment job processor is stopping.");
    }
}
