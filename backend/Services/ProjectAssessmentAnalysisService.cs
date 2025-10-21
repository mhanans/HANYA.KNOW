using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace backend.Services;

public class ProjectAssessmentAnalysisService
{
    private const int MaxScopeCharacters = 20000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
        WriteIndented = false
    };

    private readonly LlmClient _llmClient;
    private readonly ILogger<ProjectAssessmentAnalysisService> _logger;

    public ProjectAssessmentAnalysisService(LlmClient llmClient, ILogger<ProjectAssessmentAnalysisService> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<ProjectAssessment> AnalyzeAsync(ProjectTemplate template, int requestedTemplateId, string projectName, IFormFile scopeDocument, CancellationToken cancellationToken)
    {
        if (scopeDocument == null)
        {
            throw new ArgumentNullException(nameof(scopeDocument));
        }

        var sanitizedProjectName = projectName?.Trim() ?? string.Empty;

        var scopeText = await ExtractScopeTextAsync(scopeDocument, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(scopeText))
        {
            throw new InvalidOperationException("The uploaded scope document did not contain readable text for AI analysis.");
        }

        if (scopeText.Length > MaxScopeCharacters)
        {
            _logger.LogInformation("Truncating scope document from {OriginalLength} to {MaxLength} characters for AI prompt size limits.", scopeText.Length, MaxScopeCharacters);
            scopeText = scopeText[..MaxScopeCharacters];
        }

        var promptPayload = BuildPromptPayload(template, sanitizedProjectName, scopeText);
        var messages = new[]
        {
            new ChatMessage("system", BuildSystemPrompt(template.EstimationColumns)),
            new ChatMessage("user", promptPayload)
        };

        string rawResponse;
        try
        {
            rawResponse = await _llmClient.GenerateAsync(messages).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI client failed to generate project assessment analysis for template {TemplateId}", template.Id);
            throw new InvalidOperationException("AI could not analyze the uploaded scope document. Please try again later.", ex);
        }

        var cleanedResponse = CleanResponse(rawResponse);
        AnalysisResult? analysis;
        try
        {
            analysis = JsonSerializer.Deserialize<AnalysisResult>(cleanedResponse, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse AI response for template {TemplateId}. Raw response: {Response}", template.Id, rawResponse);
            throw new InvalidOperationException("AI returned an unexpected response while analyzing the scope document. Please review the document and try again.", ex);
        }

        if (analysis?.Items == null || analysis.Items.Count == 0)
        {
            throw new InvalidOperationException("AI did not return assessment data for any template items.");
        }

        return BuildAssessmentFromAnalysis(template, requestedTemplateId, sanitizedProjectName, analysis.Items);
    }

    private static string BuildSystemPrompt(IReadOnlyCollection<string> estimationColumns)
    {
        var columnInstructions = estimationColumns.Count == 0
            ? ""
            : $"Populate the 'estimates' object using only these column keys: {string.Join(", ", estimationColumns)}.";

        return $"""
You are an expert software delivery estimator helping a pre-sales team. Analyse the provided project scope against the project template items.

OUTPUT RULES:
- Respond ONLY with compact JSON that matches the schema: {{ "items": [ {{ "itemId": string, "isNeeded": bool, "estimates": {{ <column>: number|null }} }} ] }}.
- Include every template item exactly once using its itemId.
- If information is missing, set the estimate value to null.
- Use numbers for man-hour estimates without units. {columnInstructions}
- Do not include additional text or explanations outside the JSON.
""";
    }

    private static string BuildPromptPayload(ProjectTemplate template, string projectName, string scopeText)
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
            ScopeDocument = scopeText
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string CleanResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return "{\"items\":[]}";
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

    private static ProjectAssessment BuildAssessmentFromAnalysis(ProjectTemplate template, int requestedTemplateId, string projectName, IReadOnlyCollection<AnalyzedItem> items)
    {
        var itemLookup = items
            .Where(i => !string.IsNullOrWhiteSpace(i.ItemId))
            .ToDictionary(i => i.ItemId, i => i, StringComparer.OrdinalIgnoreCase);

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
                            var match = analyzedItem.Estimates
                                .FirstOrDefault(kvp => string.Equals(kvp.Key, column, StringComparison.OrdinalIgnoreCase));
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

    private static double? NormalizeEstimate(double? value)
    {
        if (value == null)
        {
            return null;
        }

        if (double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return null;
        }

        return Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
    }

    private static async Task<string> ExtractScopeTextAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        if (IsPdf(file))
        {
            using var pdf = PdfDocument.Open(buffer);
            var builder = new StringBuilder();
            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = page.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text.Trim());
                }
            }

            return builder.ToString();
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
        return content;
    }

    private static bool IsPdf(IFormFile file)
    {
        if (file.ContentType?.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var extension = Path.GetExtension(file.FileName);
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PromptPayload
    {
        public string ProjectName { get; set; } = string.Empty;
        public List<string> EstimationColumns { get; set; } = new();
        public List<PromptSection> Sections { get; set; } = new();
        public string ScopeDocument { get; set; } = string.Empty;
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
