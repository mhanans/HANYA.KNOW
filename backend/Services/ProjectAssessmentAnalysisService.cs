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
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services;

public class ProjectAssessmentAnalysisService
{
    private static readonly Uri GeminiBaseUri = new("https://generativelanguage.googleapis.com/");

    private readonly ILogger<ProjectAssessmentAnalysisService> _logger;
    private readonly AssessmentJobStore _jobStore;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly JsonSerializerOptions _deserializationOptions;
    private readonly JsonSerializerOptions _serializationOptions;
    private readonly string _storageRoot;

    public ProjectAssessmentAnalysisService(
        IConfiguration configuration,
        ILogger<ProjectAssessmentAnalysisService> logger,
        AssessmentJobStore jobStore)
    {
        _logger = logger;
        _jobStore = jobStore;

        _apiKey = configuration["Gemini:ApiKey"]
                  ?? configuration["GoogleAI:ApiKey"]
                  ?? configuration["Google:ApiKey"]
                  ?? configuration["Llm:ApiKey"]
                  ?? configuration["ApiKey"]
                  ?? throw new InvalidOperationException("Gemini API key is not configured.");

        _model = configuration["Gemini:Model"]
                 ?? configuration["GoogleAI:Model"]
                 ?? configuration["Google:Model"]
                 ?? configuration["Llm:Model"]
                 ?? configuration["Model"]
                 ?? "gemini-pro-vision";

        _logger.LogInformation("Using Gemini model {Model} for project assessment analysis", _model);

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
        IReadOnlyList<ProjectAssessment>? referenceAssessments,
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
            Status = JobStatus.Pending,
            ScopeDocumentPath = storedPath,
            ScopeDocumentMimeType = mimeType,
            OriginalTemplateJson = JsonSerializer.Serialize(template, _serializationOptions),
            ReferenceAssessmentsJson = referenceAssessments?.Count > 0
                ? JsonSerializer.Serialize(referenceAssessments, _serializationOptions)
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

        await ExecuteItemGenerationStepAsync(jobId, cancellationToken).ConfigureAwait(false);

        var jobAfterGeneration = await _jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (jobAfterGeneration?.Status != JobStatus.GenerationComplete)
        {
            _logger.LogDebug("Skipping estimation step for job {JobId} because status is {Status}.", jobId, jobAfterGeneration?.Status);
            return;
        }

        await ExecuteEffortEstimationStepAsync(jobId, cancellationToken).ConfigureAwait(false);

        var finalJob = await _jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Finished processing pipeline for assessment job {JobId} with status {Status}.", jobId, finalJob?.Status);
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
                    await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
                    await ExecuteFullPipelineAsync(job.Id, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Repaired job {JobId} after generation failure and resumed estimation.", job.Id);
                }
                else
                {
                    job.LastError = repairedJson;
                    await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (job.Status == JobStatus.FailedEstimation)
            {
                if (TryDeserializeAnalysis(repairedJson, out var serializedResult))
                {
                    job.RawEstimationResponse = repairedJson;
                    job.FinalAnalysisJson = serializedResult;
                    job.LastError = null;
                    job.Status = JobStatus.Complete;
                    await _jobStore.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    job.LastError = repairedJson;
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
            var documentPart = await CreateDocumentPartFromPath(job.ScopeDocumentPath, job.ScopeDocumentMimeType, cancellationToken).ConfigureAwait(false);
            var request = BuildItemGenerationRequest(template, job.ProjectName, documentPart);

            _logger.LogInformation("Starting item generation step for job {JobId}", jobId);
            var response = await SendGeminiRequestAsync<GenerateContentResponse>($"v1beta/models/{_model}:generateContent", request, cancellationToken).ConfigureAwait(false);
            var rawResponse = ExtractTextResponse(response)?.Trim() ?? string.Empty;
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
            var documentPart = await CreateDocumentPartFromPath(job.ScopeDocumentPath, job.ScopeDocumentMimeType, cancellationToken).ConfigureAwait(false);
            var request = BuildEffortEstimationRequest(augmentedTemplate, job.ProjectName, references, documentPart);

            _logger.LogInformation("Starting effort estimation step for job {JobId}", jobId);
            var response = await SendGeminiRequestAsync<GenerateContentResponse>($"v1beta/models/{_model}:generateContent", request, cancellationToken).ConfigureAwait(false);
            var rawResponse = ExtractTextResponse(response)?.Trim() ?? string.Empty;
            job.RawEstimationResponse = rawResponse;

            if (TryDeserializeAnalysis(rawResponse, out var serializedResult))
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

    private bool TryDeserializeAnalysis(string response, out string serializedResult)
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

            serializedResult = JsonSerializer.Serialize(analysis, _serializationOptions);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize estimation analysis");
            return false;
        }
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
                ItemDetail = response.ItemDetail?.Trim() ?? string.Empty
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
                    ItemDetail = item.ItemDetail
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
                        ItemDetail = addition.ItemDetail
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

    private GenerateContentPayload BuildItemGenerationRequest(ProjectTemplate template, string projectName, GeminiPart documentPart)
    {
        var prompt = BuildItemGenerationPrompt(template, projectName);
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

    private GenerateContentPayload BuildEffortEstimationRequest(ProjectTemplate template, string projectName, IReadOnlyList<ProjectAssessment> references, GeminiPart documentPart)
    {
        var prompt = BuildEffortEstimationPrompt(template, projectName, references);
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

    private string BuildItemGenerationPrompt(ProjectTemplate template, string projectName)
    {
        var sections = template.Sections
            .Where(s => string.Equals(s.Type, "AI-Generated", StringComparison.OrdinalIgnoreCase))
            .Select(s => new GenerationPromptSection
            {
                SectionName = s.SectionName,
                ExistingItems = s.Items.Select(item => new GenerationPromptItem
                {
                    ItemName = item.ItemName,
                    ItemDetail = item.ItemDetail
                }).ToList()
            })
            .ToList();

        var context = new GenerationPromptContext
        {
            ProjectName = projectName,
            Sections = sections
        };

        var instructions =
            "You are a senior business analyst reviewing the attached scope document. Identify additional backlog items that should be considered for the sections marked as AI-Generated.";
        var outputRules =
            """Return ONLY a valid JSON array using the schema [{"itemName":"...","itemDetail":"..."}]. Each itemName must be a concise feature title and itemDetail should be a short sentence describing the expected work. Do not include markdown fences or commentary. Avoid duplicating any items listed in the context.""";

        return $"{instructions}\\n\\nProject Context:\\n{JsonSerializer.Serialize(context, _serializationOptions)}\\n\\n{outputRules}";
    }

    private string BuildEffortEstimationPrompt(ProjectTemplate template, string projectName, IReadOnlyList<ProjectAssessment> references)
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
                    ItemDetail = item.ItemDetail
                }).ToList()
            }).ToList(),
            SimilarAssessments = BuildPromptReferences(references, template.EstimationColumns ?? new List<string>())
        };

        var instructions =
            "You are an experienced software project estimator. Review every template item provided in the context and decide if it is needed for the uploaded scope document.";
        var outputRules =
            """Respond ONLY with a JSON object using the schema {"items":[{"itemId":string,"isNeeded":bool,"estimates":{"<column>":number|null}}]}.""";
        var estimationGuidance =
            "Set isNeeded to true when the scope clearly requires the capability. Provide numeric hour estimates for each column or null when there is insufficient information. Evaluate every item exactly once and do not introduce new items.";

        return $"{instructions}\\n\\nProject Context:\\n{JsonSerializer.Serialize(payload, _serializationOptions)}\\n\\n{outputRules}\\n{estimationGuidance}";
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

        var response = await SendGeminiRequestAsync<GenerateContentResponse>($"v1beta/models/{_model}:generateContent", request, cancellationToken).ConfigureAwait(false);
        var raw = ExtractTextResponse(response)?.Trim() ?? string.Empty;
        return CleanResponse(raw);
    }

    private async Task<T?> SendGeminiRequestAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        var fullUri = new Uri(GeminiBaseUri, $"{path}?key={_apiKey}");
        using var request = new HttpRequestMessage(HttpMethod.Post, fullUri);
        var payload = JsonSerializer.Serialize(body, _serializationOptions);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryExtractErrorMessage(content) ?? response.ReasonPhrase;
            _logger.LogError("Gemini API request failed. Path: {Path}, Status: {StatusCode}, Body: {Content}", path, response.StatusCode, content);
            throw new InvalidOperationException($"Gemini API request to '{path}' failed with status {(int)response.StatusCode}: {errorMessage}");
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
                    IsNeeded = analyzedItem?.IsNeeded ?? false,
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
            Sections = sections
        };
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
                if (!item.IsNeeded || item.Estimates == null)
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
    }

    private sealed class GeneratedAssessmentItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemDetail { get; set; } = string.Empty;
    }

    private sealed class GenerationPromptContext
    {
        public string ProjectName { get; set; } = string.Empty;
        public List<GenerationPromptSection> Sections { get; set; } = new();
    }

    private sealed class GenerationPromptSection
    {
        public string SectionName { get; set; } = string.Empty;
        public List<GenerationPromptItem> ExistingItems { get; set; } = new();
    }

    private sealed class GenerationPromptItem
    {
        public string ItemName { get; set; } = string.Empty;
        public string ItemDetail { get; set; } = string.Empty;
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
    }

    private sealed class PromptReference
    {
        public string ProjectName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public double TotalHours { get; set; }
        public List<PromptReferenceSection> Sections { get; set; } = new();
    }

    private sealed class PromptReferenceSection
    {
        public string SectionName { get; set; } = string.Empty;
        public List<PromptReferenceItem> Items { get; set; } = new();
    }

    private sealed class PromptReferenceItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemDetail { get; set; } = string.Empty;
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
    }
}
