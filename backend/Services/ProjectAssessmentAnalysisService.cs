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
using backend.Models; // Pastikan namespace ini cocok dengan model Anda
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration; // Diperlukan untuk IConfiguration
using Microsoft.Extensions.Logging;

namespace backend.Services;

public class ProjectAssessmentAnalysisService
{
    private static readonly Uri GeminiBaseUri = new("https://generativelanguage.googleapis.com/");

    private readonly ILogger<ProjectAssessmentAnalysisService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly JsonSerializerOptions _deserializationOptions;
    private readonly JsonSerializerOptions _serializationOptions;

    public ProjectAssessmentAnalysisService(IConfiguration configuration, ILogger<ProjectAssessmentAnalysisService> logger)
    {
        _logger = logger;

        _apiKey = configuration["Gemini:ApiKey"]
                  ?? configuration["GoogleAI:ApiKey"]
                  ?? configuration["Google:ApiKey"]
                  ?? configuration["Llm:ApiKey"]
                  ?? configuration["ApiKey"]
                  ?? throw new InvalidOperationException("Kunci API Gemini tidak dikonfigurasi.");

        // Gunakan gemini-pro-vision secara default jika tidak ada yang ditentukan, karena ini yang terbaik untuk dokumen.
        _model = configuration["Gemini:Model"]
                 ?? configuration["GoogleAI:Model"]
                 ?? configuration["Google:Model"]
                 ?? configuration["Llm:Model"]
                 ?? configuration["Model"]
                 ?? "gemini-pro-vision";
        
        _logger.LogInformation("Menggunakan model Gemini untuk Analisis Penilaian Proyek: {Model}", _model);

        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; // Timeout lebih lama untuk file besar/analisis kompleks
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _deserializationOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        _serializationOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false, // Buat compact untuk payload yang lebih kecil
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString
        };
    }

    public async Task<ProjectAssessment> AnalyzeAsync(ProjectTemplate template, int requestedTemplateId, string projectName, IFormFile scopeDocument, IReadOnlyList<ProjectAssessment>? referenceAssessments, CancellationToken cancellationToken)
    {
        if (scopeDocument == null)
        {
            throw new ArgumentNullException(nameof(scopeDocument));
        }

        // 1. Ubah dokumen menjadi Base64 untuk inline data
        await using var buffer = new MemoryStream();
        await scopeDocument.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var documentBytes = buffer.ToArray();
        
        var documentPart = new GeminiPart
        {
            InlineData = new GeminiInlineData
            {
                MimeType = scopeDocument.ContentType, // Gunakan tipe konten dari file yang diunggah
                Data = Convert.ToBase64String(documentBytes)
            }
        };

        // 2. Buat prompt komprehensif
        var promptPayload = BuildPromptPayload(template, projectName?.Trim() ?? string.Empty, referenceAssessments);
        var systemPrompt = BuildSystemPrompt(template.EstimationColumns);
        var finalPrompt = $"{systemPrompt}\n\nBerikut adalah detail proyek yang perlu dianalisis:\n{promptPayload}";

        var request = new GenerateContentPayload
        {
            Contents = new List<GeminiContent>
            {
                new()
                {
                    Parts = new List<GeminiPart>
                    {
                        new() { Text = finalPrompt },
                        documentPart
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                ResponseMimeType = "application/json"
            }
        };

        // 3. Lakukan satu panggilan API
        string rawResponse;
        try
        {
            _logger.LogInformation("Mengirim permintaan analisis proyek ke model {Model}.", _model);
            _logger.LogInformation(
                "Payload permintaan analisis proyek: {Request}",
                JsonSerializer.Serialize(CreateLoggableRequestSnapshot(request), _serializationOptions));
            var response = await SendGeminiRequestAsync<GenerateContentResponse>($"v1beta/models/{_model}:generateContent", request, cancellationToken).ConfigureAwait(false);
            rawResponse = ExtractTextResponse(response)?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Klien AI gagal menghasilkan analisis penilaian proyek untuk template {TemplateId}", template.Id);
            throw new InvalidOperationException("AI tidak dapat menganalisis dokumen lingkup yang diunggah. Silakan coba lagi nanti.", ex);
        }

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            throw new InvalidOperationException("AI mengembalikan respons kosong setelah menganalisis dokumen lingkup.");
        }

        _logger.LogInformation("Respons AI mentah untuk analisis proyek: {Response}", rawResponse);

        var cleanedResponse = CleanResponse(rawResponse);
        if (!cleanedResponse.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("AI tidak mengembalikan JSON yang valid untuk analisis dokumen lingkup.");
        }
        AnalysisResult? analysis;
        try
        {
            analysis = JsonSerializer.Deserialize<AnalysisResult>(cleanedResponse, _deserializationOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mem-parsing respons AI untuk template {TemplateId}. Respons mentah: {Response}", template.Id, rawResponse);
            throw new InvalidOperationException("AI mengembalikan respons tak terduga saat menganalisis dokumen lingkup. Harap tinjau dokumen dan coba lagi.", ex);
        }

        if (analysis?.Items == null || analysis.Items.Count == 0)
        {
            throw new InvalidOperationException("AI tidak mengembalikan data penilaian untuk item template mana pun.");
        }

        return BuildAssessmentFromAnalysis(template, requestedTemplateId, projectName?.Trim() ?? string.Empty, analysis.Items);
    }

    private static string BuildSystemPrompt(IReadOnlyCollection<string> estimationColumns)
    {
        var columnInstructionLine = estimationColumns.Count == 0
            ? string.Empty
            : $"\n- Isi objek 'estimates' hanya menggunakan kunci kolom berikut: {string.Join(", ", estimationColumns)}.";

        return $@"Anda adalah estimator pengiriman perangkat lunak ahli yang membantu tim pra-penjualan. Analisis dokumen lingkup proyek yang disediakan terhadap item template proyek. Dokumen lingkup terlampir sebagai data inline.

ATURAN OUTPUT:
- Tanggapi HANYA dengan JSON ringkas yang cocok dengan skema: {{""items"": [ {{""itemId"": string, ""isNeeded"": bool, ""estimates"": {{""<column>"": number|null}} }} ] }}.
- Sertakan setiap item template tepat satu kali menggunakan itemId-nya.
- Jika informasi tidak ada untuk estimasi, atur nilainya menjadi null.
- Gunakan angka untuk estimasi jam kerja tanpa unit.{columnInstructionLine}
- Gunakan penilaian serupa yang disediakan sebagai inspirasi untuk rentang estimasi, tetapi sesuaikan estimasi akhir dengan dokumen lingkup yang diunggah.
- Jangan sertakan teks atau penjelasan tambahan di luar JSON.";
    }

    private string BuildPromptPayload(
        ProjectTemplate template,
        string projectName,
        IReadOnlyList<ProjectAssessment>? referenceAssessments)
    {
        var columns = template.EstimationColumns ?? new List<string>();
        var payload = new PromptPayload
        {
            ProjectName = projectName,
            EstimationColumns = columns,
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
            // ScopeDocument sekarang dikirim sebagai data inline, bukan teks
            SimilarAssessments = BuildPromptReferences(referenceAssessments, columns)
        };

        return JsonSerializer.Serialize(payload, _serializationOptions);
    }

    // Metode `BuildPromptReferences` dan `CalculateTotalHours` tetap sama
    private static List<PromptReference> BuildPromptReferences(IReadOnlyList<ProjectAssessment>? references, IReadOnlyList<string> estimationColumns)
    {
        if (references == null || references.Count == 0) return new List<PromptReference>();
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
                        foreach (var kvp in item.Estimates) { estimates[kvp.Key] = kvp.Value; }
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
                if (!item.IsNeeded || item.Estimates == null) continue;
                foreach (var value in item.Estimates.Values)
                {
                    if (value.HasValue) total += value.Value;
                }
            }
        }
        return total;
    }


    private static string CleanResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return "{\"items\":[]}";
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..];
                var closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (closing >= 0) trimmed = trimmed[..closing];
            }
        }
        return trimmed.Trim();
    }

    private static ProjectAssessment BuildAssessmentFromAnalysis(ProjectTemplate template, int requestedTemplateId, string projectName, IReadOnlyCollection<AnalyzedItem> items)
    {
        var itemLookup = items
            .Where(i => !string.IsNullOrWhiteSpace(i.ItemId))
            .GroupBy(i => i.ItemId, StringComparer.OrdinalIgnoreCase)
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

    private static object CreateLoggableRequestSnapshot(GenerateContentPayload request)
    {
        return new
        {
            request.GenerationConfig,
            Contents = request.Contents?.Select(content => new
            {
                Parts = content.Parts?.Select(part => new
                {
                    part.Text,
                    InlineData = part.InlineData == null
                        ? null
                        : new
                        {
                            part.InlineData.MimeType,
                            DataLength = part.InlineData.Data?.Length ?? 0
                        }
                }).ToList()
            }).ToList()
        };
    }

    private static double? NormalizeEstimate(double? value)
    {
        if (value == null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return null;
        return Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
    }
    
    // Metode `ExtractScopeTextAsync` dan `IsPdf` tidak lagi diperlukan
    // karena kita mengirim file biner secara langsung.

    // --- Model Data Internal untuk Panggilan API ---
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
            _logger.LogError("Permintaan API Gemini gagal. Path: {Path}, Status: {StatusCode}, Body: {Content}", path, response.StatusCode, content);
            throw new InvalidOperationException($"Permintaan API Gemini ke '{path}' gagal dengan status {(int)response.StatusCode}: {errorMessage}");
        }
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("API Gemini mengembalikan respons kosong untuk path {Path}", path);
            return default;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(content, _deserializationOptions);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Gagal mendeserialisasi respons API Gemini untuk path {Path}. Konten mentah: {Content}", path, content);
            throw new InvalidOperationException($"API Gemini mengembalikan payload tak terduga untuk '{path}': {content}", jsonEx);
        }
    }

    private static string? ExtractTextResponse(GenerateContentResponse? response)
    {
        return response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault(p => p.Text != null)?.Text;
    }

    private static string? TryExtractErrorMessage(string? responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent)) return null;
        try
        {
            using var document = JsonDocument.Parse(responseContent);
            if (document.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString();
            }
        }
        catch (JsonException) { /* Lanjutkan untuk mengembalikan konten asli */ }
        return responseContent;
    }

    // --- Model Data Gemini ---
    private sealed class GenerateContentPayload { [JsonPropertyName("contents")] public List<GeminiContent> Contents { get; set; } = new(); [JsonPropertyName("generation_config")] public GeminiGenerationConfig? GenerationConfig { get; set; } }
    private sealed class GeminiGenerationConfig { [JsonPropertyName("response_mime_type")] public string? ResponseMimeType { get; set; } }
    private sealed class GeminiContent { [JsonPropertyName("parts")] public List<GeminiPart> Parts { get; set; } = new(); }
    private sealed class GeminiPart { [JsonPropertyName("text")] public string? Text { get; set; } [JsonPropertyName("inline_data")] public GeminiInlineData? InlineData { get; set; } }
    private sealed class GeminiInlineData { [JsonPropertyName("mime_type")] public string? MimeType { get; set; } [JsonPropertyName("data")] public string? Data { get; set; } }
    private sealed class GenerateContentResponse { [JsonPropertyName("candidates")] public List<GeminiCandidate>? Candidates { get; set; } }
    private sealed class GeminiCandidate { [JsonPropertyName("content")] public GeminiContent? Content { get; set; } }

    // --- Model Data untuk Prompt ---
    private sealed class PromptPayload
    {
        public string ProjectName { get; set; } = string.Empty;
        public List<string> EstimationColumns { get; set; } = new();
        public List<PromptSection> Sections { get; set; } = new();
        // ScopeDocument dihilangkan karena sekarang menjadi data inline
        public List<PromptReference> SimilarAssessments { get; set; } = new();
    }
    private sealed class PromptSection { public string SectionName { get; set; } = string.Empty; public List<PromptItem> Items { get; set; } = new(); }
    private sealed class PromptItem { public string ItemId { get; set; } = string.Empty; public string ItemName { get; set; } = string.Empty; public string ItemDetail { get; set; } = string.Empty; }
    private sealed class PromptReference { public string ProjectName { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public double TotalHours { get; set; } public List<PromptReferenceSection> Sections { get; set; } = new(); }
    private sealed class PromptReferenceSection { public string SectionName { get; set; } = string.Empty; public List<PromptReferenceItem> Items { get; set; } = new(); }
    private sealed class PromptReferenceItem { public string ItemId { get; set; } = string.Empty; public string ItemName { get; set; } = string.Empty; public string ItemDetail { get; set; } = string.Empty; public bool IsNeeded { get; set; } public Dictionary<string, double?> Estimates { get; set; } = new(); }

    // --- Model Data untuk Deserialisasi Respons AI ---
    private sealed class AnalysisResult { public List<AnalyzedItem> Items { get; set; } = new(); }
    private sealed class AnalyzedItem { public string ItemId { get; set; } = string.Empty; public bool? IsNeeded { get; set; } public Dictionary<string, double?>? Estimates { get; set; } }
}