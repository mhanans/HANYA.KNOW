using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using backend.Configuration;
using backend.Models;
using backend.Services.Estimation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class ProjectAssessmentAnalysisService
{
    private static readonly Uri GeminiBaseUri = new("https://generativelanguage.googleapis.com/");
    private static readonly Uri MiniMaxBaseUri = new("https://api.minimax.io/v1/");
    private const string MiniMaxSystemPrompt = "You are a senior business analyst that responds strictly with valid JSON and follows the provided instructions.";
    private static readonly string[] AllowedCategories =
    {
        "New UI",
        "New Interface",
        "New Backgrounder",
        "Adjust Existing UI",
        "Adjust Existing Logic"
    };

    private static readonly Dictionary<string, string> CategoryLookup = AllowedCategories
        .ToDictionary(category => category, category => category, StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<ProjectAssessmentAnalysisService> _logger;
    private readonly AssessmentJobStore _jobStore;
    private readonly EffectiveEstimationPolicy _estimationPolicy;
    private readonly HttpClient _httpClient;
    private readonly SettingsStore _settingsStore;
    private readonly LlmOptions _defaultLlmOptions;
    private readonly string? _configuredApiKey;
    private readonly string? _configuredModel;
    private readonly string _configuredProvider;
    private readonly JsonSerializerOptions _deserializationOptions;
    private readonly JsonSerializerOptions _serializationOptions;
    private readonly string _storageRoot;

    public ProjectAssessmentAnalysisService(
        IConfiguration configuration,
        ILogger<ProjectAssessmentAnalysisService> logger,
        AssessmentJobStore jobStore,
        EffectiveEstimationPolicy estimationPolicy,
        SettingsStore settingsStore,
        IOptions<LlmOptions> llmOptions)
    {
        _logger = logger;
        _jobStore = jobStore;
        _estimationPolicy = estimationPolicy;
        _settingsStore = settingsStore;
        _defaultLlmOptions = llmOptions.Value;

        var configuredProvider = configuration["Gemini:Provider"]
                               ?? configuration["GoogleAI:Provider"]
                               ?? configuration["Google:Provider"]
                               ?? configuration["Llm:Provider"]
                               ?? configuration["Provider"]
                               ?? _defaultLlmOptions.Provider;

        _configuredApiKey = configuration["Gemini:ApiKey"]
                            ?? configuration["GoogleAI:ApiKey"]
                            ?? configuration["Google:ApiKey"]
                            ?? configuration["Llm:ApiKey"]
                            ?? configuration["ApiKey"];

        var configuredModel = configuration["Gemini:Model"]
                              ?? configuration["GoogleAI:Model"]
                              ?? configuration["Google:Model"]
                              ?? configuration["Llm:Model"]
                              ?? configuration["Model"]
                              ?? _defaultLlmOptions.Model
                              ?? "gemini-pro-vision";

        if (string.IsNullOrWhiteSpace(configuredModel))
        {
            configuredModel = "gemini-pro-vision";
        }

        _configuredModel = configuredModel;
        _configuredProvider = string.IsNullOrWhiteSpace(configuredProvider)
            ? "gemini"
            : configuredProvider.Trim();

        if (!string.IsNullOrWhiteSpace(_configuredModel))
        {
            _logger.LogInformation(
                "Default LLM configuration for project assessment analysis set to provider {Provider} model {Model}",
                _configuredProvider,
                _configuredModel);
        }

        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _deserializationOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        _serializationOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString
        };

        _storageRoot = Path.Combine(AppContext.BaseDirectory, "App_Data", "assessment-jobs");
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<AssessmentJob> AnalyzeAsync(
        ProjectTemplate template,
        int requestedTemplateId,
        string projectName,
        IFormFile scopeDocument,
        AssessmentAnalysisMode analysisMode,
        AssessmentLanguage outputLanguage,
        IReadOnlyList<ProjectAssessment>? referenceAssessments,
        IReadOnlyList<AssessmentReferenceDocument>? referenceDocuments,
        CancellationToken cancellationToken)
    {
        if (scopeDocument == null)
        {
            throw new ArgumentNullException(nameof(scopeDocument));
        }

        var (storedPath, mimeType) = await StoreDocumentAsync(scopeDocument, cancellationToken).ConfigureAwait(false);

        var job = new AssessmentJob
        {
            ProjectName = projectName?.Trim() ?? string.Empty,
            TemplateId = requestedTemplateId,
            TemplateName = template?.TemplateName ?? string.Empty,
            AnalysisMode = analysisMode,
            OutputLanguage = outputLanguage,
            Status = JobStatus.Pending,
            ScopeDocumentPath = storedPath,
            ScopeDocumentMimeType = mimeType,
            OriginalTemplateJson = JsonSerializer.Serialize(template, _serializationOptions),
            ReferenceAssessmentsJson = referenceAssessments?.Count > 0
                ? JsonSerializer.Serialize(referenceAssessments, _serializationOptions)
                : null,
            ReferenceDocumentsJson = referenceDocuments?.Count > 0
                ? JsonSerializer.Serialize(referenceDocuments, _serializationOptions)
                : null
        };

        await _jobStore.InsertAsync(job, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Created job {JobId}. Starting synchronous pipeline.", job.Id);

        await ExecuteFullPipelineAsync(job.Id, cancellationToken).ConfigureAwait(false);

        return await _jobStore.GetAsync(job.Id, cancellationToken).ConfigureAwait(false) ?? job;
    }

    private async Task ExecuteFullPipelineAsync(int jobId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting processing pipeline for assessment job {JobId}.", jobId);

        var job = await _jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job == null)
        {
            _logger.LogWarning("Assessment job {JobId} could not be loaded before pipeline execution.", jobId);
            return;
        }

        if (ShouldRunGeneration(job))
        {
            await ExecuteItemGenerationStepAsync(jobId, cancellationToken).ConfigureAwait(false);
            job = await _jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (job == null)
            {
                _logger.LogWarning("Assessment job {JobId} could not be loaded after generation step.", jobId);
                return;
            }
        }
        else
        {
            _logger.LogDebug(
                "Skipping generation step for job {JobId} because status is {Status} at step {Step}.",
                jobId,
                job.Status,
                job.Step);
        }

        if (ShouldRunEstimation(job))
        {
            await ExecuteEffortEstimationStepAsync(jobId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogDebug(
                "Skipping estimation step for job {JobId} because status is {Status} at step {Step}.",
                jobId,
                job.Status,
                job.Step);
        }

        var finalJob = await _jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (finalJob == null)
        {
            _logger.LogWarning("Assessment job {JobId} could not be loaded after pipeline execution.", jobId);
            return;
        }

        _logger.LogInformation(
            "Finished processing pipeline for assessment job {JobId} with status {Status} at step {Step}.",
            jobId,
            finalJob.Status,
            finalJob.Step);
    }

    public Task<AssessmentJob?> GetJobAsync(int jobId, CancellationToken cancellationToken)
    {
        return _jobStore.GetAsync(jobId, cancellationToken);
    }

    public Task<IReadOnlyList<AssessmentJobSummary>> ListJobsAsync(CancellationToken cancellationToken)
    {
        return _jobStore.ListAsync(cancellationToken);
    }

    public async Task<ProjectAssessment?> TryBuildAssessmentAsync(int jobId, CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job == null || string.IsNullOrWhiteSpace(job.FinalAnalysisJson))
        {
            return null;
        }

        var template = JsonSerializer.Deserialize<ProjectTemplate>(job.OriginalTemplateJson, _deserializationOptions) ?? new ProjectTemplate();
        var generatedItems = DeserializeGeneratedItems(job.GeneratedItemsJson);
        var augmentedTemplate = BuildAugmentedTemplate(template, generatedItems);
        var analysis = JsonSerializer.Deserialize<AnalysisResult>(job.FinalAnalysisJson, _deserializationOptions);
        if (analysis?.Items == null || analysis.Items.Count == 0)
        {
            return null;
        }

        return BuildAssessmentFromAnalysis(augmentedTemplate, job.TemplateId, job.ProjectName, analysis.Items);
    }

    public async Task<AssessmentJob?> RepairAndResumeFailedStepAsync(int jobId, CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(job.LastError))
        {
            return job;
        }

        try
        {
            var repairedJson = await RepairJsonAsync(job.LastError, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(repairedJson))
            {
                return job;
            }

            if (job.Status == JobStatus.FailedGeneration)
            {
                if (TryDeserializeGeneratedItems(repairedJson, job, out var serializedItems))
                {
                    job.RawGenerationResponse = repairedJson;
                    job.GeneratedItemsJson = serializedItems;
                    job.LastError = null;
                    job.Status = JobStatus.GenerationComplete;
                    job.SyncStepWithStatus();
                    await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
                    await ExecuteFullPipelineAsync(job.Id, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Repaired job {JobId} after generation failure and resumed estimation.", job.Id);
                }
                else
                {
                    job.LastError = repairedJson;
                    job.SyncStepWithStatus();
                    await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (job.Status == JobStatus.FailedEstimation)
            {
                var template = JsonSerializer.Deserialize<ProjectTemplate>(job.OriginalTemplateJson, _deserializationOptions)
                               ?? new ProjectTemplate();
                var generatedItems = DeserializeGeneratedItems(job.GeneratedItemsJson);
                var augmentedTemplate = BuildAugmentedTemplate(template, generatedItems);
                var references = DeserializeReferences(job.ReferenceAssessmentsJson, _deserializationOptions);

                if (TryDeserializeAnalysis(repairedJson, augmentedTemplate, references, out var serializedResult))
                {
                    job.RawEstimationResponse = repairedJson;
                    job.FinalAnalysisJson = serializedResult;
                    job.LastError = null;
                    job.Status = JobStatus.Complete;
                    job.SyncStepWithStatus();
                    await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    job.LastError = repairedJson;
                    job.SyncStepWithStatus();
                    await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to repair JSON for job {JobId}", jobId);
        }

        return await _jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> DeleteJobAsync(int jobId, CancellationToken cancellationToken)
    {
        return _jobStore.DeleteAsync(jobId, cancellationToken);
    }

    private static bool ShouldRunGeneration(AssessmentJob job)
    {
        if (job == null)
        {
            return false;
        }

        if (job.Status == JobStatus.FailedGeneration)
        {
            return true;
        }

        var generationInProgressStep = AssessmentJob.GetStepForStatus(JobStatus.GenerationInProgress);
        return job.Step <= generationInProgressStep;
    }

    private static bool ShouldRunEstimation(AssessmentJob job)
    {
        if (job == null)
        {
            return false;
        }

        if (job.Status == JobStatus.Complete)
        {
            return false;
        }

        if (job.Status == JobStatus.FailedGeneration
            || job.Status == JobStatus.Pending
            || job.Status == JobStatus.GenerationInProgress)
        {
            return false;
        }

        var generationCompleteStep = AssessmentJob.GetStepForStatus(JobStatus.GenerationComplete);
        var failedEstimationStep = AssessmentJob.GetStepForStatus(JobStatus.FailedEstimation);

        return job.Step >= generationCompleteStep && job.Step <= failedEstimationStep;
    }
    private async Task ExecuteItemGenerationStepAsync(int jobId, CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job == null)
        {
            _logger.LogWarning("Skipping generation step because job {JobId} could not be found.", jobId);
            return;
        }

        if (job.Status != JobStatus.Pending && job.Status != JobStatus.FailedGeneration)
        {
            _logger.LogDebug("Skipping generation step for job {JobId} with status {Status}.", jobId, job.Status);
            return;
        }

        try
        {
            job.Status = JobStatus.GenerationInProgress;
            job.LastError = null;
            await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);

            var template = JsonSerializer.Deserialize<ProjectTemplate>(job.OriginalTemplateJson, _deserializationOptions) ?? new ProjectTemplate();
            var referenceDocuments = DeserializeReferenceDocuments(job.ReferenceDocumentsJson, _deserializationOptions);
            var prompt = BuildItemGenerationPrompt(template, job.ProjectName, referenceDocuments, job.AnalysisMode, job.OutputLanguage);

            _logger.LogInformation("Starting item generation step for job {JobId}", jobId);
            var llmConfig = await ResolveLlmConfigurationAsync().ConfigureAwait(false);
            string rawResponse;

            if (string.Equals(llmConfig.Provider, "minimax", StringComparison.OrdinalIgnoreCase))
            {
                var documentText = await LoadDocumentForPromptAsync(job.ScopeDocumentPath, job.ScopeDocumentMimeType, cancellationToken).ConfigureAwait(false);
                var userPrompt = BuildMiniMaxUserPrompt(prompt, documentText);
                rawResponse = await SendMiniMaxRequestAsync(userPrompt, llmConfig, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var documentPart = await CreateDocumentPartFromPath(job.ScopeDocumentPath, job.ScopeDocumentMimeType, cancellationToken).ConfigureAwait(false);
                var request = BuildItemGenerationRequest(template, job.ProjectName, referenceDocuments, job.AnalysisMode, job.OutputLanguage, documentPart);
                var response = await SendGeminiRequestAsync<GenerateContentResponse>($"v1beta/models/{llmConfig.Model}:generateContent", request, llmConfig.ApiKey, cancellationToken).ConfigureAwait(false);
                rawResponse = ExtractTextResponse(response)?.Trim() ?? string.Empty;
            }
            job.RawGenerationResponse = rawResponse;

            if (TryDeserializeGeneratedItems(rawResponse, job, out var serializedItems))
            {
                job.GeneratedItemsJson = serializedItems;
                job.Status = JobStatus.GenerationComplete;
                job.LastError = null;
            }
            else
            {
                job.GeneratedItemsJson = null;
                job.Status = JobStatus.FailedGeneration;
                job.LastError = rawResponse;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LlmApiException llmEx)
        {
            _logger.LogError(llmEx, "Unhandled exception during generation step for job {JobId}", jobId);
            job.Status = JobStatus.FailedGeneration;
            job.LastError = llmEx.UserMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during generation step for job {JobId}", jobId);
            job.Status = JobStatus.FailedGeneration;
            job.LastError = ex.Message;
        }
        finally
        {
            await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteEffortEstimationStepAsync(int jobId, CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job == null)
        {
            _logger.LogWarning("Skipping estimation step because job {JobId} could not be found.", jobId);
            return;
        }

        if (job.Status != JobStatus.GenerationComplete && job.Status != JobStatus.FailedEstimation)
        {
            _logger.LogDebug("Skipping estimation step for job {JobId} with status {Status}.", jobId, job.Status);
            return;
        }

        try
        {
            job.Status = JobStatus.EstimationInProgress;
            job.LastError = null;
            await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);

            var template = JsonSerializer.Deserialize<ProjectTemplate>(job.OriginalTemplateJson, _deserializationOptions) ?? new ProjectTemplate();
            var generatedItems = DeserializeGeneratedItems(job.GeneratedItemsJson);
            var augmentedTemplate = BuildAugmentedTemplate(template, generatedItems);
            var references = DeserializeReferences(job.ReferenceAssessmentsJson, _deserializationOptions);
            var referenceDocuments = DeserializeReferenceDocuments(job.ReferenceDocumentsJson, _deserializationOptions);
            var prompt = BuildEffortEstimationPrompt(augmentedTemplate, job.ProjectName, references, referenceDocuments, job.AnalysisMode, job.OutputLanguage);

            _logger.LogInformation("Starting effort estimation step for job {JobId}", jobId);
            var llmConfig = await ResolveLlmConfigurationAsync().ConfigureAwait(false);
            string rawResponse;

            if (string.Equals(llmConfig.Provider, "minimax", StringComparison.OrdinalIgnoreCase))
            {
                var documentText = await LoadDocumentForPromptAsync(job.ScopeDocumentPath, job.ScopeDocumentMimeType, cancellationToken).ConfigureAwait(false);
                var userPrompt = BuildMiniMaxUserPrompt(prompt, documentText);
                rawResponse = await SendMiniMaxRequestAsync(userPrompt, llmConfig, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var documentPart = await CreateDocumentPartFromPath(job.ScopeDocumentPath, job.ScopeDocumentMimeType, cancellationToken).ConfigureAwait(false);
                var request = BuildEffortEstimationRequest(augmentedTemplate, job.ProjectName, references, referenceDocuments, job.AnalysisMode, job.OutputLanguage, documentPart);
                var response = await SendGeminiRequestAsync<GenerateContentResponse>($"v1beta/models/{llmConfig.Model}:generateContent", request, llmConfig.ApiKey, cancellationToken).ConfigureAwait(false);
                rawResponse = ExtractTextResponse(response)?.Trim() ?? string.Empty;
            }
            job.RawEstimationResponse = rawResponse;

            if (TryDeserializeAnalysis(rawResponse, augmentedTemplate, references, out var serializedResult))
            {
                job.FinalAnalysisJson = serializedResult;
                job.Status = JobStatus.Complete;
                job.LastError = null;
            }
            else
            {
                job.FinalAnalysisJson = null;
                job.Status = JobStatus.FailedEstimation;
                job.LastError = rawResponse;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LlmApiException llmEx)
        {
            _logger.LogError(llmEx, "Unhandled exception during estimation step for job {JobId}", jobId);
            job.Status = JobStatus.FailedEstimation;
            job.LastError = llmEx.UserMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during estimation step for job {JobId}", jobId);
            job.Status = JobStatus.FailedEstimation;
            job.LastError = ex.Message;
        }
        finally
        {
            await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool TryDeserializeGeneratedItems(string response, AssessmentJob job, out string serializedItems)
    {
        serializedItems = string.Empty;
        if (string.IsNullOrWhiteSpace(response))
        {
            serializedItems = JsonSerializer.Serialize(new List<GeneratedAssessmentItem>(), _serializationOptions);
            return true;
        }

        try
        {
            var template = JsonSerializer.Deserialize<ProjectTemplate>(job.OriginalTemplateJson, _deserializationOptions) ?? new ProjectTemplate();
            var clean = CleanResponse(response);
            List<GeneratedItemResponse>? parsed = null;
            if (clean.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(clean);
                if (document.RootElement.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                {
                    parsed = JsonSerializer.Deserialize<List<GeneratedItemResponse>>(itemsElement.GetRawText(), _deserializationOptions);
                }
            }
            else
            {
                parsed = JsonSerializer.Deserialize<List<GeneratedItemResponse>>(clean, _deserializationOptions);
            }

            if (parsed == null)
            {
                return false;
            }

            var mapped = MapGeneratedItems(parsed, template);
            serializedItems = JsonSerializer.Serialize(mapped, _serializationOptions);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize generated items for job {JobId}", job.Id);
            return false;
        }
    }

    private bool TryDeserializeAnalysis(
        string response,
        ProjectTemplate template,
        IReadOnlyList<ProjectAssessment> references,
        out string serializedResult)
    {
        serializedResult = string.Empty;
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        try
        {
            var clean = CleanResponse(response);
            if (!clean.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return false;
            }

            var analysis = JsonSerializer.Deserialize<AnalysisResult>(clean, _deserializationOptions);
            if (analysis?.Items == null || analysis.Items.Count == 0)
            {
                return false;
            }

            var templateItems = template.Sections
                .SelectMany(section => section.Items)
                .Where(item => !string.IsNullOrWhiteSpace(item.ItemId))
                .ToDictionary(item => item.ItemId, item => item, StringComparer.OrdinalIgnoreCase);

            if (templateItems.Count == 0)
            {
                return false;
            }

            var columns = CollectEstimationColumns(template, analysis);
            var normalizedItems = new List<AnalyzedItem>();
            foreach (var item in analysis.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ItemId))
                {
                    continue;
                }

                if (!templateItems.TryGetValue(item.ItemId, out var templateItem))
                {
                    continue;
                }

                if (TryNormalizeAnalyzedItem(item, templateItem, columns, references))
                {
                    normalizedItems.Add(item);
                }
            }

            if (normalizedItems.Count == 0)
            {
                return false;
            }

            foreach (var normalized in normalizedItems)
            {
                normalized.Diagnostics = null;
                normalized.IsNeeded ??= true;
            }

            analysis.Items = normalizedItems;
            serializedResult = JsonSerializer.Serialize(analysis, _serializationOptions);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize estimation analysis");
            return false;
        }
    }


    private IReadOnlyList<string> CollectEstimationColumns(ProjectTemplate template, AnalysisResult analysis)
    {
        if (template.EstimationColumns != null && template.EstimationColumns.Count > 0)
        {
            return template.EstimationColumns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var analyzedItem in analysis.Items ?? new List<AnalyzedItem>())
        {
            if (analyzedItem?.Estimates == null)
            {
                continue;
            }

            foreach (var key in analyzedItem.Estimates.Keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    columns.Add(key);
                }
            }
        }

        if (columns.Count == 0)
        {
            columns.Add("EffortHours");
        }

        return columns.ToList();
    }

    private bool TryNormalizeAnalyzedItem(
        AnalyzedItem item,
        TemplateItem templateItem,
        IReadOnlyList<string> columns,
        IReadOnlyList<ProjectAssessment> references)
    {
        if (columns.Count == 0)
        {
            return false;
        }

        item.IsNeeded ??= true;
        item.Estimates ??= new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);

        var diagnostics = item.Diagnostics ?? new ItemDiagnostics();
        item.Diagnostics = diagnostics;

        var signals = ComplexityScorer.ExtractSignals(templateItem.ItemDetail);
        diagnostics.Signals = new ItemDiagnosticsSignals
        {
            Fields = signals.fields,
            Integrations = signals.integrations,
            WorkflowSteps = signals.workflowSteps,
            HasUpload = signals.hasUpload,
            HasAuthRole = signals.hasAuthRole,
            Crud = BuildCrudString(signals)
        };

        var rawComplexity = CalculateComplexityScore(signals);
        diagnostics.ComplexityScore = Math.Round(Math.Min(100, rawComplexity), 2, MidpointRounding.AwayFromZero);
        diagnostics.ScopeFit = item.IsNeeded == true ? "in" : "out";
        diagnostics.Confidence = Clamp01(diagnostics.Confidence ?? 0.55);
        diagnostics.JustificationScore = Clamp01(diagnostics.JustificationScore ?? 0);
        if (!string.IsNullOrEmpty(diagnostics.Justification) && diagnostics.Justification.Length > 120)
        {
            diagnostics.Justification = diagnostics.Justification[..120];
        }

        var category = NormalizeCategory(templateItem.Category);
        var bands = ResolveBands(category);
        var requestedSize = NormalizeSizeClass(diagnostics.SizeClass);
        var finalSize = DetermineSizeClass(category, bands, requestedSize, diagnostics.JustificationScore.Value, signals, rawComplexity);
        diagnostics.SizeClass = finalSize;

        var crudMultiplier = CalculateCrudMultiplier(signals);
        var baseHours = GetBandMidpoint(bands, finalSize);
        var baseWithCrud = baseHours * crudMultiplier;

        var fieldAdd = signals.fields * _estimationPolicy.PerFieldHours;
        var integrationAdd = signals.integrations * _estimationPolicy.PerIntegrationHours;
        var uploadAdd = signals.hasUpload ? _estimationPolicy.FileUploadHours : 0;
        var authAdd = signals.hasAuthRole ? _estimationPolicy.AuthRolesHours : 0;
        var workflowAdd = signals.workflowSteps * _estimationPolicy.WorkflowStepHours;

        var rawHours = baseWithCrud + fieldAdd + integrationAdd + uploadAdd + authAdd + workflowAdd;
        var baseForRatio = Math.Max(baseWithCrud, 1e-6);

        diagnostics.Multipliers = new ItemDiagnosticsMultipliers
        {
            Crud = Math.Round(crudMultiplier, 3, MidpointRounding.AwayFromZero),
            Fields = Math.Round(fieldAdd == 0 ? 1 : 1 + fieldAdd / baseForRatio, 3, MidpointRounding.AwayFromZero),
            Integrations = Math.Round(integrationAdd == 0 ? 1 : 1 + integrationAdd / baseForRatio, 3, MidpointRounding.AwayFromZero),
            Upload = Math.Round(uploadAdd == 0 ? 1 : 1 + uploadAdd / baseForRatio, 3, MidpointRounding.AwayFromZero),
            Auth = Math.Round(authAdd == 0 ? 1 : 1 + authAdd / baseForRatio, 3, MidpointRounding.AwayFromZero),
            Workflow = Math.Round(workflowAdd == 0 ? 1 : 1 + workflowAdd / baseForRatio, 3, MidpointRounding.AwayFromZero)
        };

        var normalizedEstimates = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        double? referenceBaselineForDiagnostics = null;

        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column))
            {
                continue;
            }

            if (item.IsNeeded != true)
            {
                normalizedEstimates[column] = 0;
                continue;
            }

            var referenceStats = CalculateReferenceStats(references, templateItem.ItemId, category, column);
            var referenceBaseline = ChooseReferenceBaseline(referenceStats.median, referenceStats.geoMean);
            if (referenceBaselineForDiagnostics == null && referenceBaseline != null)
            {
                referenceBaselineForDiagnostics = referenceBaseline;
            }

            double finalValue = rawHours;
            if (item.Estimates.TryGetValue(column, out var provided) && provided.HasValue && !double.IsNaN(provided.Value) && !double.IsInfinity(provided.Value))
            {
                finalValue = Math.Min(finalValue, provided.Value);
            }

            var shrunk = EstimationNormalizer.ApplyReferenceShrinkage(finalValue, referenceBaseline, _estimationPolicy);
            var clamped = EstimationNormalizer.Clamp(shrunk, _estimationPolicy);
            var rounded = EstimationNormalizer.Round(clamped, _estimationPolicy);

            normalizedEstimates[column] = rounded;
        }

        if (normalizedEstimates.Count == 0)
        {
            return false;
        }

        diagnostics.ReferenceMedian = referenceBaselineForDiagnostics;
        item.Estimates = normalizedEstimates;

        var estimateSummary = string.Join(", ", normalizedEstimates.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        _logger.LogDebug("Item {ItemId} normalized with size {SizeClass} (category {Category}) => {Estimates}", item.ItemId, diagnostics.SizeClass, category, estimateSummary);

        return true;
    }

    private (double xs, double s, double m, double l, double xl) ResolveBands(string category)
    {
        if (_estimationPolicy.BaseHoursByCategory.TryGetValue(category, out var bands))
        {
            return bands;
        }

        return (4, 8, 16, 32, 56);
    }

    private static double GetBandMidpoint((double xs, double s, double m, double l, double xl) bands, string sizeClass)
    {
        return sizeClass switch
        {
            "XS" => bands.xs,
            "S" => (bands.xs + bands.s) / 2.0,
            "M" => (bands.s + bands.m) / 2.0,
            "L" => (bands.m + bands.l) / 2.0,
            "XL" => (bands.l + bands.xl) / 2.0,
            _ => bands.s
        };
    }

    private string DetermineSizeClass(
        string category,
        (double xs, double s, double m, double l, double xl) bands,
        string requested,
        double justificationScore,
        (int fields, int integrations, int workflowSteps, bool hasUpload, bool hasAuthRole, bool hasCrudC, bool hasCrudR, bool hasCrudU, bool hasCrudD) signals,
        double rawComplexity)
    {
        var size = NormalizeSizeClass(requested);
        var guardActive = category.StartsWith("Adjust Existing", StringComparison.OrdinalIgnoreCase)
            && _estimationPolicy.CapAdjustCategoriesToMaxM
            && justificationScore < _estimationPolicy.JustificationScoreThreshold;

        if (string.IsNullOrEmpty(size))
        {
            size = MapScoreToSizeClass(rawComplexity, signals);
        }

        if (guardActive && SizeClassRank(size) > SizeClassRank("M"))
        {
            size = "M";
        }

        if (string.IsNullOrEmpty(size))
        {
            size = "S";
        }

        return size;
    }

    private static string MapScoreToSizeClass(double complexityScore, (int fields, int integrations, int workflowSteps, bool hasUpload, bool hasAuthRole, bool hasCrudC, bool hasCrudR, bool hasCrudU, bool hasCrudD) signals)
    {
        string size = complexityScore switch
        {
            <= 8 => "XS",
            <= 18 => "S",
            <= 32 => "M",
            <= 55 => "L",
            _ => "XL"
        };

        if (signals.integrations >= 2 && SizeClassRank(size) < SizeClassRank("M"))
        {
            size = "M";
        }

        if (signals.integrations >= 3 && SizeClassRank(size) < SizeClassRank("L"))
        {
            size = "L";
        }

        if (signals.fields >= 25 && SizeClassRank(size) < SizeClassRank("L"))
        {
            size = "L";
        }

        if (signals.fields <= 3 && SizeClassRank(size) > SizeClassRank("S"))
        {
            size = "S";
        }

        return size;
    }

    private static string NormalizeSizeClass(string? sizeClass)
    {
        return sizeClass?.Trim().ToUpperInvariant() switch
        {
            "XS" => "XS",
            "S" => "S",
            "M" => "M",
            "L" => "L",
            "XL" => "XL",
            _ => string.Empty
        };
    }

    private static int SizeClassRank(string sizeClass)
    {
        return sizeClass switch
        {
            "XS" => 0,
            "S" => 1,
            "M" => 2,
            "L" => 3,
            "XL" => 4,
            _ => 1
        };
    }

    private static double CalculateComplexityScore((int fields, int integrations, int workflowSteps, bool hasUpload, bool hasAuthRole, bool hasCrudC, bool hasCrudR, bool hasCrudU, bool hasCrudD) signals)
    {
        double score = signals.fields * 1.8 + signals.integrations * 15 + signals.workflowSteps * 6;
        if (signals.hasUpload)
        {
            score += 6;
        }

        if (signals.hasAuthRole)
        {
            score += 10;
        }

        var crudCount = 0;
        if (signals.hasCrudC) crudCount++;
        if (signals.hasCrudR) crudCount++;
        if (signals.hasCrudU) crudCount++;
        if (signals.hasCrudD) crudCount++;
        score += crudCount * 4;

        return Math.Min(100, score);
    }

    private double CalculateCrudMultiplier((int fields, int integrations, int workflowSteps, bool hasUpload, bool hasAuthRole, bool hasCrudC, bool hasCrudR, bool hasCrudU, bool hasCrudD) signals)
    {
        double multiplier = 1.0;
        if (signals.hasCrudC)
        {
            multiplier *= _estimationPolicy.CrudCreateMultiplier;
        }

        if (signals.hasCrudR)
        {
            multiplier *= _estimationPolicy.CrudReadMultiplier;
        }

        if (signals.hasCrudU)
        {
            multiplier *= _estimationPolicy.CrudUpdateMultiplier;
        }

        if (signals.hasCrudD)
        {
            multiplier *= _estimationPolicy.CrudDeleteMultiplier;
        }

        return multiplier;
    }

    private static string BuildCrudString((int fields, int integrations, int workflowSteps, bool hasUpload, bool hasAuthRole, bool hasCrudC, bool hasCrudR, bool hasCrudU, bool hasCrudD) signals)
    {
        var builder = new StringBuilder();
        if (signals.hasCrudC)
        {
            builder.Append('C');
        }

        if (signals.hasCrudR)
        {
            builder.Append('R');
        }

        if (signals.hasCrudU)
        {
            builder.Append('U');
        }

        if (signals.hasCrudD)
        {
            builder.Append('D');
        }

        return builder.Length == 0 ? "-" : builder.ToString();
    }

    private (double? median, double? geoMean) CalculateReferenceStats(
        IReadOnlyList<ProjectAssessment> references,
        string itemId,
        string category,
        string column)
    {
        var perItem = new List<double>();
        var perCategory = new List<double>();

        foreach (var assessment in references ?? Array.Empty<ProjectAssessment>())
        {
            foreach (var section in assessment.Sections ?? new List<AssessmentSection>())
            {
                foreach (var refItem in section.Items ?? new List<AssessmentItem>())
                {
                    if (refItem?.Estimates == null)
                    {
                        continue;
                    }

                    if (!refItem.Estimates.TryGetValue(column, out var value) || !value.HasValue || value.Value <= 0)
                    {
                        continue;
                    }

                    if (string.Equals(refItem.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                    {
                        perItem.Add(value.Value);
                    }
                    else if (string.Equals(NormalizeCategory(refItem.Category), category, StringComparison.OrdinalIgnoreCase))
                    {
                        perCategory.Add(value.Value);
                    }
                }
            }
        }

        var source = perItem.Count > 0 ? perItem : perCategory;
        if (source.Count == 0)
        {
            return (null, null);
        }

        source.Sort();
        double? median;
        var mid = source.Count / 2;
        if (source.Count % 2 == 0)
        {
            median = (source[mid - 1] + source[mid]) / 2.0;
        }
        else
        {
            median = source[mid];
        }

        double? geoMean = null;
        var positive = source.Where(v => v > 0).ToList();
        if (positive.Count > 0)
        {
            geoMean = Math.Exp(positive.Sum(Math.Log) / positive.Count);
        }

        return (median, geoMean);
    }

    private static double? ChooseReferenceBaseline(double? median, double? geoMean)
    {
        if (median.HasValue && geoMean.HasValue)
        {
            return Math.Min(median.Value, geoMean.Value);
        }

        return median ?? geoMean;
    }

    private static double Clamp01(double value)
    {
        return Math.Max(0, Math.Min(1, value));
    }

    private static List<GeneratedAssessmentItem> MapGeneratedItems(IReadOnlyList<GeneratedItemResponse> responses, ProjectTemplate template)
    {
        var result = new List<GeneratedAssessmentItem>();
        var aiSections = template.Sections
            .Where(s => string.Equals(s.Type, "AI-Generated", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (aiSections.Count == 0)
        {
            return result;
        }

        for (var index = 0; index < responses.Count; index++)
        {
            var response = responses[index];
            if (string.IsNullOrWhiteSpace(response.ItemName))
            {
                continue;
            }

            var section = aiSections[index % aiSections.Count];
            result.Add(new GeneratedAssessmentItem
            {
                ItemId = $"ai-{Guid.NewGuid():N}",
                SectionName = section.SectionName,
                ItemName = response.ItemName.Trim(),
                ItemDetail = response.ItemDetail?.Trim() ?? string.Empty,
                Category = NormalizeCategory(response.Category)
            });
        }

        return result;
    }

    private static List<GeneratedAssessmentItem> DeserializeGeneratedItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<GeneratedAssessmentItem>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<GeneratedAssessmentItem>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? new List<GeneratedAssessmentItem>();
        }
        catch
        {
            return new List<GeneratedAssessmentItem>();
        }
    }

    private static ProjectTemplate BuildAugmentedTemplate(ProjectTemplate template, IReadOnlyList<GeneratedAssessmentItem> generatedItems)
    {
        var augmented = new ProjectTemplate
        {
            Id = template.Id,
            TemplateName = template.TemplateName,
            EstimationColumns = template.EstimationColumns?.ToList() ?? new List<string>(),
            Sections = new List<TemplateSection>()
        };

        foreach (var section in template.Sections)
        {
            var clonedSection = new TemplateSection
            {
                SectionName = section.SectionName,
                Type = section.Type,
                Items = section.Items.Select(item => new TemplateItem
                {
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    ItemDetail = item.ItemDetail,
                    Category = NormalizeCategory(item.Category)
                }).ToList()
            };

            if (string.Equals(section.Type, "AI-Generated", StringComparison.OrdinalIgnoreCase))
            {
                var additions = generatedItems
                    .Where(item => string.Equals(item.SectionName, section.SectionName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var addition in additions)
                {
                    clonedSection.Items.Add(new TemplateItem
                    {
                        ItemId = addition.ItemId,
                        ItemName = addition.ItemName,
                        ItemDetail = addition.ItemDetail,
                        Category = NormalizeCategory(addition.Category)
                    });
                }
            }

            augmented.Sections.Add(clonedSection);
        }

        return augmented;
    }

    private static List<ProjectAssessment> DeserializeReferences(string? json, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ProjectAssessment>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ProjectAssessment>>(json, options) ?? new List<ProjectAssessment>();
        }
        catch
        {
            return new List<ProjectAssessment>();
        }
    }

    private static List<AssessmentReferenceDocument> DeserializeReferenceDocuments(
        string? json,
        JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<AssessmentReferenceDocument>();
        }

        try
        {
            var documents = JsonSerializer.Deserialize<List<AssessmentReferenceDocument>>(json, options)
                           ?? new List<AssessmentReferenceDocument>();
            return documents
                .Where(document =>
                    document != null &&
                    !string.IsNullOrWhiteSpace(document.Source) &&
                    !string.IsNullOrWhiteSpace(document.Summary))
                .Select(document => new AssessmentReferenceDocument
                {
                    Source = document.Source!.Trim(),
                    Summary = document.Summary!.Trim()
                })
                .ToList();
        }
        catch
        {
            return new List<AssessmentReferenceDocument>();
        }
    }

    private async Task<(string Path, string MimeType)> StoreDocumentAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(_storageRoot, fileName);

        await using var stream = File.Create(filePath);
        await file.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);

        var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        return (filePath, mimeType);
    }

    private async Task<string> LoadDocumentForPromptAsync(string path, string mimeType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        await using var stream = File.OpenRead(path);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var bytes = memory.ToArray();

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var decoded = TryDecodeUtf8(bytes);
        if (!string.IsNullOrEmpty(decoded))
        {
            return decoded;
        }

        var fileName = Path.GetFileName(path);
        var effectiveMime = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;
        _logger.LogWarning(
            "Unable to inline scope document {FileName} ({MimeType}) as text for MiniMax request. Using placeholder metadata.",
            fileName,
            effectiveMime);

        return $"[Scope document: {fileName} ({effectiveMime}), {bytes.Length} bytes. Binary content could not be inlined. Focus on instructions and context.]";
    }

    private static async Task<GeminiPart> CreateDocumentPartFromPath(string path, string mimeType, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var data = Convert.ToBase64String(memory.ToArray());
        return new GeminiPart
        {
            InlineData = new GeminiInlineData
            {
                MimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType,
                Data = data
            }
        };
    }

    private static string? TryDecodeUtf8(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }

        var text = Encoding.UTF8.GetString(bytes);
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (text.IndexOf('\uFFFD') >= 0)
        {
            return null;
        }

        var controlCount = 0;
        foreach (var ch in text)
        {
            if (char.IsControl(ch) && !char.IsWhiteSpace(ch))
            {
                controlCount++;
                if (controlCount > Math.Max(4, text.Length / 40))
                {
                    return null;
                }
            }
        }

        return text;
    }

    private GenerateContentPayload BuildItemGenerationRequest(
        ProjectTemplate template,
        string projectName,
        IReadOnlyList<AssessmentReferenceDocument> referenceDocuments,
        AssessmentAnalysisMode analysisMode,
        AssessmentLanguage outputLanguage,
        GeminiPart documentPart)
    {
        var prompt = BuildItemGenerationPrompt(template, projectName, referenceDocuments, analysisMode, outputLanguage);
        return new GenerateContentPayload
        {
            Contents = new List<GeminiContent>
            {
                new()
                {
                    Parts = new List<GeminiPart>
                    {
                        new() { Text = prompt },
                        documentPart
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                ResponseMimeType = "application/json"
            }
        };
    }

    private GenerateContentPayload BuildEffortEstimationRequest(
        ProjectTemplate template,
        string projectName,
        IReadOnlyList<ProjectAssessment> references,
        IReadOnlyList<AssessmentReferenceDocument> referenceDocuments,
        AssessmentAnalysisMode analysisMode,
        AssessmentLanguage outputLanguage,
        GeminiPart documentPart)
    {
        var prompt = BuildEffortEstimationPrompt(template, projectName, references, referenceDocuments, analysisMode, outputLanguage);
        return new GenerateContentPayload
        {
            Contents = new List<GeminiContent>
            {
                new()
                {
                    Parts = new List<GeminiPart>
                    {
                        new() { Text = prompt },
                        documentPart
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                ResponseMimeType = "application/json"
            }
        };
    }

    private string BuildItemGenerationPrompt(
        ProjectTemplate template,
        string projectName,
        IReadOnlyList<AssessmentReferenceDocument> referenceDocuments,
        AssessmentAnalysisMode analysisMode,
        AssessmentLanguage outputLanguage)
    {
        var sections = template.Sections
            .Where(s => string.Equals(s.Type, "AI-Generated", StringComparison.OrdinalIgnoreCase))
            .Select(s => new GenerationPromptSection
            {
                SectionName = s.SectionName,
                ExistingItems = s.Items.Select(item => new GenerationPromptItem
                {
                    ItemName = item.ItemName,
                    ItemDetail = item.ItemDetail,
                    Category = NormalizeCategory(item.Category)
                }).ToList()
            })
            .ToList();

        var context = new GenerationPromptContext
        {
            ProjectName = projectName,
            Sections = sections,
            ReferenceDocuments = BuildGenerationPromptDocuments(referenceDocuments)
        };

        var categoryGuidance = string.Join(", ", AllowedCategories);
        var instructionsBuilder = new StringBuilder();
        if (analysisMode == AssessmentAnalysisMode.Strict)
        {
            instructionsBuilder.Append("You are a meticulous business analyst reviewing the attached scope document. Translate every explicitly described requirement into backlog items for the sections marked as AI-Generated. Do not invent new scope beyond what the document states.");
        }
        else
        {
            instructionsBuilder.Append("You are a senior business analyst reviewing the attached scope document. Identify additional backlog items that should be considered for the sections marked as AI-Generated.");
        }

        instructionsBuilder.Append(' ');
        instructionsBuilder.Append("You are a senior BA. Extract only in-scope backlog items. Prefer merging trivial UI fragments that don’t materially change estimates. If a function is clearly reusable from references, include [REUSE] tag in itemDetail. Category must be one of {{AllowedCategories}}. Return ONLY JSON array of {itemName,itemDetail,category}.");

        var languageInstruction = outputLanguage == AssessmentLanguage.Indonesian
            ? "Return itemName and itemDetail written in Bahasa Indonesia."
            : "Return itemName and itemDetail written in English.";
        instructionsBuilder.Append(' ');
        instructionsBuilder.Append(languageInstruction);

        if (context.ReferenceDocuments.Count > 0)
        {
            instructionsBuilder.Append(analysisMode == AssessmentAnalysisMode.Strict
                ? " Use the provided knowledge base summaries only to clarify terminology; never introduce functionality that is absent from the scope document."
                : " Leverage the provided knowledge base summaries when they clarify requirements or provide helpful precedents.");
        }

        var instructions = instructionsBuilder.ToString().Replace("{{AllowedCategories}}", categoryGuidance);
        var outputRules = "Return ONLY JSON array of {itemName,itemDetail,category}. Avoid splitting trivial variants; prefer merge unless estimation impact > S. If similar component exists in references, append tag [REUSE] in itemDetail.";
        return $"{instructions}\n\nProject Context:\n{JsonSerializer.Serialize(context, _serializationOptions)}\n\n{outputRules}";
    }

    private static List<GenerationPromptDocument> BuildGenerationPromptDocuments(
        IReadOnlyList<AssessmentReferenceDocument> referenceDocuments)
    {
        if (referenceDocuments == null || referenceDocuments.Count == 0)
        {
            return new List<GenerationPromptDocument>();
        }

        var results = new List<GenerationPromptDocument>(referenceDocuments.Count);
        foreach (var document in referenceDocuments)
        {
            if (document == null)
            {
                continue;
            }

            var source = document.Source?.Trim();
            var summary = document.Summary?.Trim();
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(summary))
            {
                continue;
            }

            results.Add(new GenerationPromptDocument
            {
                Source = source,
                Summary = summary
            });
        }

        return results;
    }


    private string BuildEffortEstimationPrompt(
        ProjectTemplate template,
        string projectName,
        IReadOnlyList<ProjectAssessment> references,
        IReadOnlyList<AssessmentReferenceDocument> referenceDocuments,
        AssessmentAnalysisMode analysisMode,
        AssessmentLanguage outputLanguage)
    {
        var payload = new PromptPayload
        {
            ProjectName = projectName,
            EstimationColumns = template.EstimationColumns ?? new List<string>(),
            Sections = template.Sections.Select(section => new PromptSection
            {
                SectionName = section.SectionName,
                Items = section.Items.Select(item => new PromptItem
                {
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    ItemDetail = item.ItemDetail,
                    Category = NormalizeCategory(item.Category)
                }).ToList()
            }).ToList(),
            SimilarAssessments = BuildPromptReferences(references, template.EstimationColumns ?? new List<string>()),
            ReferenceDocuments = BuildPromptDocuments(referenceDocuments),
            Policy = BuildPromptPolicy()
        };

        var instructionsBuilder = new StringBuilder();
        if (analysisMode == AssessmentAnalysisMode.Strict)
        {
            instructionsBuilder.Append("You are an experienced software project estimator. The backlog items were transcribed directly from the uploaded scope document. Evaluate each item exactly as written and determine whether it remains in scope for this project.");
        }
        else
        {
            instructionsBuilder.Append("You are an experienced software project estimator. Review every template item provided in the context and decide if it is needed for the uploaded scope document.");
        }

        if (payload.ReferenceDocuments.Count > 0)
        {
            instructionsBuilder.Append(analysisMode == AssessmentAnalysisMode.Strict
                ? " Use the supplied knowledge base summaries only for clarification; do not broaden the scope beyond the document."
                : " Consider the supplied knowledge base summaries when they add relevant background or precedent.");
        }

        if (payload.SimilarAssessments.Count > 0)
        {
            instructionsBuilder.Append(" Use the similar assessment history to calibrate whether items are typically in scope and the scale of effort required for comparable projects.");
        }

        instructionsBuilder.Append(" Apply the effective estimation policy provided in the context and keep estimates conservative and auditable.");
        instructionsBuilder.AppendLine();
        instructionsBuilder.AppendLine("Critical Flaw to Correct: Do not calculate a single total effort and apply it to every column. Instead, analyze the work required for each item and distribute effort only among the roles that actually contribute to that work.");
        instructionsBuilder.AppendLine("New Estimation Thought Process (follow these steps for each item):");
        instructionsBuilder.AppendLine("1. Analyze the Task: Review itemName and itemDetail to understand the nature and scope of the work.");
        instructionsBuilder.AppendLine("2. Identify Involved Roles: Determine which estimation columns correspond to the roles needed (e.g., UI or frontend implies FE and Requirement; database or API implies BE, Requirement, and possibly Architect or Setup; UAT or acceptance scripts imply Business Analyst or QA).");
        instructionsBuilder.AppendLine("3. Distribute Effort: Estimate the total hours required based on complexity, then allocate those hours proportionally across the relevant columns you identified.");
        instructionsBuilder.AppendLine("4. Set Irrelevant Columns to Zero: Any column without an involved role must be assigned 0 hours for that item.");

        instructionsBuilder.Append(' ');
        instructionsBuilder.Append(outputLanguage == AssessmentLanguage.Indonesian
            ? "Provide justification, signals, and textual diagnostics in Bahasa Indonesia."
            : "Provide justification, signals, and textual diagnostics in English.");

        var rules = string.Join(Environment.NewLine, new[]
        {
            "Rules:",
            "- (CRITICAL RULE) Your primary task is to distribute effort. For each item, determine the relevant EstimationColumns based on the work described. Assign effort hours ONLY to these relevant columns. All other columns for that item MUST be 0.",
            "- The sum of the hours across all columns for an item should reflect its total complexity.",
            "- Analyze ItemName and ItemDetail to infer roles. 'UI' implies FE Developer. 'Database' or 'API' implies BE Developer. 'Requirement' implies Business Analyst. 'Test Scenario' implies QA/Analyst.",
            "- Assign sizeClass ∈ {XS,S,M,L,XL} based on scope; prefer smaller class when ambiguous.",
            "- Use band hours by category (XS,S,M,L,XL) and proposed multipliers. Do NOT exceed referenceMedian×1.10 unless strong justification with justificationScore ≥ 0.7.",
            "- For ‘Adjust Existing UI’ or ‘Adjust Existing Logic’, cap size at M unless justificationScore ≥ 0.7 with explicit reason.",
            "- Classify scopeFit as 'in' or 'out' and set isNeeded accordingly. Provide diagnostics for each item: sizeClass, complexityScore (0..100), signals, multipliers, referenceMedian, justification, justificationScore, confidence.",
            "- Respond ONLY JSON object: { \"items\":[ ... ] } with numbers in hours (decimals allowed). No markdown.",
        });

        var examples = string.Join(Environment.NewLine, new[]
        {
            "Examples of Correct vs. Incorrect Output:",
            "Example 1: UI-heavy Task",
            "Item: ItemId: \"2A.1\", ItemName: \"User Registration UI\"",
            "Estimation Columns: [\"Requirement\", \"SIT\", \"Setup\", \"BE\", \"FE\"]",
            "INCORRECT (Current Behavior): {\"Requirement\": 12, \"SIT\": 12, \"Setup\": 12, \"BE\": 12, \"FE\": 12}",
            "CORRECT (Desired Behavior): {\"Requirement\": 1, \"SIT\": 2, \"Setup\": 0, \"BE\": 4, \"FE\": 8}",
            "Justification: 1 hour for BA requirement, 8 hours for FE to build the UI, 4 hours for BE to create/adjust the endpoint, 2 hours for QA to test in SIT. Setup is not involved, so it's 0.",
            "Example 2: Architect-specific Task",
            "Item: ItemId: \"1.1\", ItemName: \"System Setup\"",
            "Estimation Columns: [\"Requirement\", \"SIT\", \"Setup\", \"BE\", \"FE\"]",
            "INCORRECT (Current Behavior): {\"Requirement\": 4, \"SIT\": 4, \"Setup\": 4, \"BE\": 4, \"FE\": 4}",
            "CORRECT (Desired Behavior): {\"Requirement\": 0, \"SIT\": 0, \"Setup\": 4, \"BE\": 0, \"FE\": 0}",
            "Justification: This is a pure setup task performed by an Architect. No other roles are involved.",
            "Example 3: Backend-heavy Task",
            "Item: ItemId: \"custom-1\", ItemName: \"Payment Gateway Integration\"",
            "Estimation Columns: [\"Requirement\", \"SIT\", \"Setup\", \"BE\", \"FE\"]",
            "CORRECT (Desired Behavior): {\"Requirement\": 4, \"SIT\": 8, \"Setup\": 0, \"BE\": 24, \"FE\": 2}",
            "Justification: This is a complex backend task. Significant time for BA requirements and QA testing (SIT). Minimal FE work is needed to connect the UI."
        });

        var estimationGuidance = "Proposed hours = Band(sizeClass) × crudMultiplier × (1 + fields×perField/BandBase) + integrations×perIntegration + extras(upload/auth/workflow). Server may normalize.";

        return $"{instructionsBuilder}{Environment.NewLine}{Environment.NewLine}Project Context:{Environment.NewLine}{JsonSerializer.Serialize(payload, _serializationOptions)}{Environment.NewLine}{Environment.NewLine}{rules}{Environment.NewLine}{examples}{Environment.NewLine}{estimationGuidance}";
    }

    private static string BuildMiniMaxUserPrompt(string instructions, string? additionalContent, string label = "Scope Document")
    {
        var trimmedInstructions = string.IsNullOrWhiteSpace(instructions) ? string.Empty : instructions.Trim();
        if (string.IsNullOrWhiteSpace(additionalContent))
        {
            return trimmedInstructions;
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(trimmedInstructions))
        {
            builder.AppendLine(trimmedInstructions);
            builder.AppendLine();
        }

        var effectiveLabel = string.IsNullOrWhiteSpace(label) ? "Additional Context" : label.Trim();
        builder.AppendLine($"{effectiveLabel}:");
        builder.AppendLine(additionalContent.Trim());
        return builder.ToString();
    }

    private async Task<LlmConfiguration> ResolveLlmConfigurationAsync()
    {
        var settings = await _settingsStore.GetAsync().ConfigureAwait(false);

        var provider = string.IsNullOrWhiteSpace(settings.LlmProvider)
            ? null
            : settings.LlmProvider;
        var apiKey = string.IsNullOrWhiteSpace(settings.LlmApiKey)
            ? null
            : settings.LlmApiKey;
        var model = string.IsNullOrWhiteSpace(settings.LlmModel)
            ? null
            : settings.LlmModel;

        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = string.IsNullOrWhiteSpace(_defaultLlmOptions.Provider)
                ? _configuredProvider
                : _defaultLlmOptions.Provider;
        }

        provider = string.IsNullOrWhiteSpace(provider)
            ? "gemini"
            : provider.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = string.IsNullOrWhiteSpace(_defaultLlmOptions.ApiKey)
                ? _configuredApiKey
                : _defaultLlmOptions.ApiKey;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            model = string.IsNullOrWhiteSpace(_defaultLlmOptions.Model)
                ? _configuredModel
                : _defaultLlmOptions.Model;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("LLM API key is not configured. Please update the LLM settings.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            model = string.Equals(provider, "minimax", StringComparison.OrdinalIgnoreCase)
                ? "MiniMax-M2"
                : "gemini-pro-vision";
        }

        return new LlmConfiguration(provider, apiKey!, model!);
    }

    private async Task<string> SendMiniMaxRequestAsync(string userPrompt, LlmConfiguration config, CancellationToken cancellationToken)
    {
        var path = "chat/completions";
        var requestUri = new Uri(MiniMaxBaseUri, path);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        var sanitizedPrompt = string.IsNullOrWhiteSpace(userPrompt) ? string.Empty : userPrompt.Trim();

        var payload = new MiniMaxChatCompletionRequest
        {
            Model = config.Model,
            Messages = new List<MiniMaxChatMessage>
            {
                new() { Role = "system", Content = MiniMaxSystemPrompt },
                new() { Role = "user", Content = sanitizedPrompt }
            },
            Temperature = 0.2,
            MaxTokens = 4096,
            ResponseFormat = new MiniMaxResponseFormat { Type = "json_object" }
        };

        var payloadJson = JsonSerializer.Serialize(payload, _serializationOptions);
        request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        _logger.LogInformation("[Presales AI] Request to {Path}: {Payload}", $"minimax/{path}", payloadJson);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("[Presales AI] Response from {Path}: {Content}", $"minimax/{path}", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryExtractErrorMessage(content) ?? response.ReasonPhrase ?? "MiniMax API request failed.";
            throw new LlmApiException("MiniMax", path, response.StatusCode, errorMessage, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("MiniMax API returned an empty response for path {Path}", path);
            return string.Empty;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MiniMaxChatCompletionResponse>(content, _deserializationOptions);
            var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            return text?.Trim() ?? string.Empty;
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to deserialize MiniMax response for path {Path}. Raw content: {Content}", path, content);
            throw new InvalidOperationException($"MiniMax API returned an unexpected payload for '{path}': {content}", jsonEx);
        }
    }

    private async Task<string> RepairJsonAsync(string invalidJson, CancellationToken cancellationToken)
    {
        var prompt = "Correct any syntax errors in the following text to make it a perfectly valid JSON object. Do not alter the data or content within the JSON structure. Respond ONLY with the corrected JSON object.";
        var request = new GenerateContentPayload
        {
            Contents = new List<GeminiContent>
            {
                new()
                {
                    Parts = new List<GeminiPart>
                    {
                        new() { Text = prompt },
                        new() { Text = invalidJson }
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                ResponseMimeType = "application/json"
            }
        };

        var llmConfig = await ResolveLlmConfigurationAsync().ConfigureAwait(false);

        if (string.Equals(llmConfig.Provider, "minimax", StringComparison.OrdinalIgnoreCase))
        {
            var userPrompt = BuildMiniMaxUserPrompt(prompt, invalidJson, "Invalid JSON");
            var response = await SendMiniMaxRequestAsync(userPrompt, llmConfig, cancellationToken).ConfigureAwait(false);
            return CleanResponse(response);
        }

        var geminiResponse = await SendGeminiRequestAsync<GenerateContentResponse>($"v1beta/models/{llmConfig.Model}:generateContent", request, llmConfig.ApiKey, cancellationToken).ConfigureAwait(false);
        var raw = ExtractTextResponse(geminiResponse)?.Trim() ?? string.Empty;
        return CleanResponse(raw);
    }

    private async Task<T?> SendGeminiRequestAsync<T>(string path, object body, string apiKey, CancellationToken cancellationToken)
    {
        var fullUri = new Uri(GeminiBaseUri, $"{path}?key={apiKey}");
        using var request = new HttpRequestMessage(HttpMethod.Post, fullUri);
        var payload = JsonSerializer.Serialize(body, _serializationOptions);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        // DEBUG: Presales AI request logging - remove when debugging is complete.
        _logger.LogInformation("[Presales AI] Request to {Path}: {Payload}", path, payload);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // DEBUG: Presales AI response logging - remove when debugging is complete.
        _logger.LogInformation("[Presales AI] Response from {Path}: {Content}", path, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryExtractErrorMessage(content) ?? response.ReasonPhrase ?? "Gemini API request failed.";
            _logger.LogError("Gemini API request failed. Path: {Path}, Status: {StatusCode}, Body: {Content}", path, response.StatusCode, content);
            throw new LlmApiException("Gemini", path, response.StatusCode, errorMessage, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("Gemini API returned an empty response for path {Path}", path);
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, _deserializationOptions);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to deserialize Gemini response for path {Path}. Raw content: {Content}", path, content);
            throw new InvalidOperationException($"Gemini API returned an unexpected payload for '{path}': {content}", jsonEx);
        }
    }

    private static string? ExtractTextResponse(GenerateContentResponse? response)
    {
        return response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault(p => p.Text != null)?.Text;
    }

    private static string? TryExtractErrorMessage(string? responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseContent);
            if (document.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return responseContent;
    }

    private static string CleanResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return string.Empty;
        }

        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..];
                var closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (closing >= 0)
                {
                    trimmed = trimmed[..closing];
                }
            }
        }

        return trimmed.Trim();
    }

    private static string NormalizeCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AllowedCategories[0];
        }

        var trimmed = value.Trim();
        if (CategoryLookup.TryGetValue(trimmed, out var normalized))
        {
            return normalized;
        }

        return AllowedCategories[0];
    }

    private static ProjectAssessment BuildAssessmentFromAnalysis(
        ProjectTemplate template,
        int requestedTemplateId,
        string projectName,
        IReadOnlyCollection<AnalyzedItem> items)
    {
        var itemLookup = items
            .Where(i => !string.IsNullOrWhiteSpace(i.ItemId))
            .GroupBy(i => i.ItemId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var columns = template.EstimationColumns ?? new List<string>();
        var sections = new List<AssessmentSection>();

        foreach (var section in template.Sections)
        {
            var assessmentSection = new AssessmentSection
            {
                SectionName = section.SectionName,
                Items = new List<AssessmentItem>()
            };

            foreach (var templateItem in section.Items)
            {
                itemLookup.TryGetValue(templateItem.ItemId, out var analyzedItem);
                var estimates = new Dictionary<string, double?>();
                foreach (var column in columns)
                {
                    double? value = null;
                    if (analyzedItem?.Estimates != null && analyzedItem.Estimates.Count > 0)
                    {
                        if (analyzedItem.Estimates.TryGetValue(column, out var directValue))
                        {
                            value = NormalizeEstimate(directValue);
                        }
                        else
                        {
                            var match = analyzedItem.Estimates.FirstOrDefault(kvp => string.Equals(kvp.Key, column, StringComparison.OrdinalIgnoreCase));
                            if (!match.Equals(default(KeyValuePair<string, double?>)))
                            {
                                value = NormalizeEstimate(match.Value);
                            }
                        }
                    }
                    estimates[column] = value;
                }

                var assessmentItem = new AssessmentItem
                {
                    ItemId = templateItem.ItemId,
                    ItemName = templateItem.ItemName,
                    ItemDetail = templateItem.ItemDetail,
                    Category = NormalizeCategory(templateItem.Category),
                    IsNeeded = true,
                    Estimates = estimates
                };

                assessmentSection.Items.Add(assessmentItem);
            }

            sections.Add(assessmentSection);
        }

        var templateId = template.Id ?? requestedTemplateId;
        return new ProjectAssessment
        {
            TemplateId = templateId,
            TemplateName = template.TemplateName,
            ProjectName = projectName,
            Status = "Draft",
            Step = 1,
            Sections = sections
        };
    }


    private PromptEstimationPolicy BuildPromptPolicy()
    {
        var promptPolicy = new PromptEstimationPolicy
        {
            CrudCreateMultiplier = _estimationPolicy.CrudCreateMultiplier,
            CrudReadMultiplier = _estimationPolicy.CrudReadMultiplier,
            CrudUpdateMultiplier = _estimationPolicy.CrudUpdateMultiplier,
            CrudDeleteMultiplier = _estimationPolicy.CrudDeleteMultiplier,
            PerFieldHours = _estimationPolicy.PerFieldHours,
            PerIntegrationHours = _estimationPolicy.PerIntegrationHours,
            FileUploadHours = _estimationPolicy.FileUploadHours,
            AuthRolesHours = _estimationPolicy.AuthRolesHours,
            WorkflowStepHours = _estimationPolicy.WorkflowStepHours,
            ReferenceMedianCapMultiplier = _estimationPolicy.ReferenceMedianCapMultiplier,
            GlobalShrinkageToMedian = _estimationPolicy.GlobalShrinkageToMedian,
            HardMaxPerItemHours = _estimationPolicy.HardMaxPerItemHours,
            HardMinPerItemHours = _estimationPolicy.HardMinPerItemHours,
            RoundToNearestHours = _estimationPolicy.RoundToNearestHours,
            CapAdjustCategoriesToMaxM = _estimationPolicy.CapAdjustCategoriesToMaxM,
            JustificationScoreThreshold = _estimationPolicy.JustificationScoreThreshold
        };

        foreach (var (category, bands) in _estimationPolicy.BaseHoursByCategory)
        {
            promptPolicy.BaseHoursByCategory[category] = new List<double>
            {
                bands.xs,
                bands.s,
                bands.m,
                bands.l,
                bands.xl
            };
        }

        return promptPolicy;
    }

    private static List<PromptDocumentContext> BuildPromptDocuments(IReadOnlyList<AssessmentReferenceDocument> documents)
    {
        if (documents == null || documents.Count == 0)
        {
            return new List<PromptDocumentContext>();
        }

        var results = new List<PromptDocumentContext>(documents.Count);
        foreach (var document in documents)
        {
            if (document == null)
            {
                continue;
            }

            var source = document.Source?.Trim();
            var summary = document.Summary?.Trim();
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(summary))
            {
                continue;
            }

            results.Add(new PromptDocumentContext
            {
                Source = source,
                Summary = summary
            });
        }

        return results;
    }

    private static List<PromptReference> BuildPromptReferences(IReadOnlyList<ProjectAssessment> references, IReadOnlyList<string> estimationColumns)
    {
        if (references == null || references.Count == 0)
        {
            return new List<PromptReference>();
        }

        var columns = estimationColumns ?? Array.Empty<string>();
        var result = new List<PromptReference>(references.Count);
        foreach (var assessment in references)
        {
            var reference = new PromptReference
            {
                ProjectName = assessment.ProjectName ?? string.Empty,
                Status = string.IsNullOrWhiteSpace(assessment.Status) ? "Draft" : assessment.Status,
                TotalHours = CalculateTotalHours(assessment)
            };

            foreach (var section in assessment.Sections ?? new List<AssessmentSection>())
            {
                var referenceSection = new PromptReferenceSection { SectionName = section.SectionName ?? string.Empty };
                foreach (var item in section.Items ?? new List<AssessmentItem>())
                {
                    var estimates = new Dictionary<string, double?>();
                    if (columns.Count > 0)
                    {
                        foreach (var column in columns)
                        {
                            double? value = null;
                            if (item.Estimates != null && item.Estimates.TryGetValue(column, out var foundValue))
                            {
                                value = foundValue;
                            }
                            estimates[column] = value;
                        }
                    }
                    else if (item.Estimates != null)
                    {
                        foreach (var kvp in item.Estimates)
                        {
                            estimates[kvp.Key] = kvp.Value;
                        }
                    }

                    referenceSection.Items.Add(new PromptReferenceItem
                    {
                        ItemId = item.ItemId ?? string.Empty,
                        ItemName = item.ItemName ?? string.Empty,
                        ItemDetail = item.ItemDetail ?? string.Empty,
                        Category = NormalizeCategory(item.Category),
                        IsNeeded = item.IsNeeded,
                        Estimates = estimates
                    });
                }

                reference.Sections.Add(referenceSection);
            }

            result.Add(reference);
        }

        return result;
    }

    private static double CalculateTotalHours(ProjectAssessment assessment)
    {
        double total = 0;
        foreach (var section in assessment.Sections ?? new List<AssessmentSection>())
        {
            foreach (var item in section.Items ?? new List<AssessmentItem>())
            {
                if (item.Estimates == null)
                {
                    continue;
                }

                foreach (var value in item.Estimates.Values)
                {
                    if (value.HasValue)
                    {
                        total += value.Value;
                    }
                }
            }
        }

        return total;
    }

    private static double? NormalizeEstimate(double? value)
    {
        if (value == null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return null;
        }

        return Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
    }

    private sealed class GeneratedItemResponse
    {
        [JsonPropertyName("itemName")]
        public string ItemName { get; set; } = string.Empty;

        [JsonPropertyName("itemDetail")]
        public string? ItemDetail { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }

    private sealed class GeneratedAssessmentItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemDetail { get; set; } = string.Empty;
        public string Category { get; set; } = AllowedCategories[0];
    }

    private sealed class GenerationPromptContext
    {
        public string ProjectName { get; set; } = string.Empty;
        public List<GenerationPromptSection> Sections { get; set; } = new();
        public List<GenerationPromptDocument> ReferenceDocuments { get; set; } = new();
    }

    private sealed class GenerationPromptSection
    {
        public string SectionName { get; set; } = string.Empty;
        public List<GenerationPromptItem> ExistingItems { get; set; } = new();
    }

    private sealed class GenerationPromptDocument
    {
        public string Source { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    private sealed class GenerationPromptItem
    {
        public string ItemName { get; set; } = string.Empty;
        public string ItemDetail { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    private sealed record LlmConfiguration(string Provider, string ApiKey, string Model);

    private sealed class MiniMaxChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<MiniMaxChatMessage> Messages { get; set; } = new();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
            = 0.2;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
            = 4096;

        [JsonPropertyName("response_format")]
        public MiniMaxResponseFormat? ResponseFormat { get; set; }
            = new();
    }

    private sealed class MiniMaxChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class MiniMaxResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }

    private sealed class MiniMaxChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<MiniMaxChoice>? Choices { get; set; }
            = new();
    }

    private sealed class MiniMaxChoice
    {
        [JsonPropertyName("message")]
        public MiniMaxMessage? Message { get; set; }
            = new();
    }

    private sealed class MiniMaxMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
            = string.Empty;
    }

    private sealed class GenerateContentPayload
    {
        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = new();

        [JsonPropertyName("generation_config")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("response_mime_type")]
        public string? ResponseMimeType { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = new();
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("inline_data")]
        public GeminiInlineData? InlineData { get; set; }
    }

    private sealed class GeminiInlineData
    {
        [JsonPropertyName("mime_type")]
        public string? MimeType { get; set; }

        [JsonPropertyName("data")]
        public string? Data { get; set; }
    }

    private sealed class GenerateContentResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private sealed class PromptPayload
    {
        public string ProjectName { get; set; } = string.Empty;
        public List<string> EstimationColumns { get; set; } = new();
        public List<PromptSection> Sections { get; set; } = new();
        public List<PromptReference> SimilarAssessments { get; set; } = new();
        public List<PromptDocumentContext> ReferenceDocuments { get; set; } = new();
        public PromptEstimationPolicy Policy { get; set; } = new();
    }

    private sealed class PromptSection
    {
        public string SectionName { get; set; } = string.Empty;
        public List<PromptItem> Items { get; set; } = new();
    }

    private sealed class PromptItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemDetail { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    private sealed class PromptReference
    {
        public string ProjectName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public double TotalHours { get; set; }
        public List<PromptReferenceSection> Sections { get; set; } = new();
    }

    private sealed class PromptDocumentContext
    {
        public string Source { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    private sealed class PromptReferenceSection
    {
        public string SectionName { get; set; } = string.Empty;
        public List<PromptReferenceItem> Items { get; set; } = new();
    }


    private sealed class PromptEstimationPolicy
    {
        public Dictionary<string, List<double>> BaseHoursByCategory { get; set; } = new();
        public double CrudCreateMultiplier { get; set; }
        public double CrudReadMultiplier { get; set; }
        public double CrudUpdateMultiplier { get; set; }
        public double CrudDeleteMultiplier { get; set; }
        public double PerFieldHours { get; set; }
        public double PerIntegrationHours { get; set; }
        public double FileUploadHours { get; set; }
        public double AuthRolesHours { get; set; }
        public double WorkflowStepHours { get; set; }
        public double ReferenceMedianCapMultiplier { get; set; }
        public double GlobalShrinkageToMedian { get; set; }
        public double HardMaxPerItemHours { get; set; }
        public double HardMinPerItemHours { get; set; }
        public double RoundToNearestHours { get; set; }
        public bool CapAdjustCategoriesToMaxM { get; set; }
        public double JustificationScoreThreshold { get; set; }
    }

    private sealed class PromptReferenceItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemDetail { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsNeeded { get; set; }
        public Dictionary<string, double?> Estimates { get; set; } = new();
    }

    private sealed class AnalysisResult
    {
        public List<AnalyzedItem> Items { get; set; } = new();
    }

    private sealed class AnalyzedItem
    {
        public string ItemId { get; set; } = string.Empty;
        public bool? IsNeeded { get; set; }
        public Dictionary<string, double?>? Estimates { get; set; }
        public ItemDiagnostics? Diagnostics { get; set; }
    }

    private sealed class ItemDiagnostics
    {
        public string? SizeClass { get; set; }
        public double? ComplexityScore { get; set; }
        public ItemDiagnosticsSignals? Signals { get; set; }
        public ItemDiagnosticsMultipliers? Multipliers { get; set; }
        public double? ReferenceMedian { get; set; }
        public string? Justification { get; set; }
        public double? JustificationScore { get; set; }
        public double? Confidence { get; set; }
        public string? ScopeFit { get; set; }
    }

    private sealed class ItemDiagnosticsSignals
    {
        public int? Fields { get; set; }
        public int? Integrations { get; set; }
        public int? WorkflowSteps { get; set; }
        public bool? HasUpload { get; set; }
        public bool? HasAuthRole { get; set; }
        public string Crud { get; set; } = "-";
    }

    private sealed class ItemDiagnosticsMultipliers
    {
        public double Crud { get; set; } = 1;
        public double Fields { get; set; } = 1;
        public double Integrations { get; set; } = 1;
        public double Upload { get; set; } = 1;
        public double Auth { get; set; } = 1;
        public double Workflow { get; set; } = 1;
    }
}
