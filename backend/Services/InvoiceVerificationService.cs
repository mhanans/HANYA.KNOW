using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services;

public class InvoiceVerificationService
{
    private const string DefaultModel = "models/gemini-1.5-flash";
    private const string PdfMimeType = "application/pdf";
    private const string ExtractionPrompt = """
You are an AI expert tasked with extracting structured data from the attached invoice.

RULES:
- Analyze the provided invoice file.
- Respond ONLY with valid JSON. Do not include any additional commentary.
- For each field, return the most precise value found in the document.
- If a field is missing, return null.
- For totalAmount, return the grand total as a number without currency symbols or thousand separators. Use a dot as the decimal separator.

EXPECTED JSON STRUCTURE:
{
  "invoiceNumber": "value",
  "purchaseOrderNumber": "value",
  "totalAmount": number
}
""";

    private static readonly Uri GeminiBaseUri = new("https://generativelanguage.googleapis.com/v1beta/");

    private readonly ILogger<InvoiceVerificationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly JsonSerializerOptions _deserializationOptions;
    private readonly JsonSerializerOptions _serializationOptions;
    private readonly TimeSpan _fileStatusPollInterval;
    private readonly TimeSpan _fileProcessingTimeout;

    public InvoiceVerificationService(ILogger<InvoiceVerificationService> logger, IConfiguration configuration)
    {
        _logger = logger;

        _apiKey = configuration["Gemini:ApiKey"]
                  ?? configuration["GoogleAI:ApiKey"]
                  ?? configuration["Google:ApiKey"]
                  ?? throw new InvalidOperationException("Gemini API key is not configured. Set 'Gemini:ApiKey' in configuration.");

        var configuredModel = configuration["Gemini:Model"];
        _model = NormalizeModelName(configuredModel);
        var pollSeconds = Math.Max(1, configuration.GetValue<int?>("Gemini:FileStatusPollSeconds") ?? 2);
        var timeoutSeconds = Math.Max(pollSeconds, configuration.GetValue<int?>("Gemini:FileProcessingTimeoutSeconds") ?? 300);

        _httpClient = new HttpClient
        {
            BaseAddress = GeminiBaseUri,
            Timeout = TimeSpan.FromSeconds(Math.Max(timeoutSeconds + 30, 330))
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _deserializationOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            PropertyNameCaseInsensitive = true
        };

        _serializationOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        _fileStatusPollInterval = TimeSpan.FromSeconds(pollSeconds);
        _fileProcessingTimeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public async Task<InvoiceVerificationResult> VerifyAsync(Stream pdfStream, string invoiceNumber, string purchaseOrderNumber,
        string totalAmount, CancellationToken cancellationToken = default)
    {
        var result = new InvoiceVerificationResult();

        try
        {
            var activeFile = await UploadAndActivateAsync(pdfStream, cancellationToken).ConfigureAwait(false);
            var extraction = await ExtractInvoiceDataAsync(activeFile, cancellationToken).ConfigureAwait(false);

            result.ExtractedText = extraction.Data != null
                ? JsonSerializer.Serialize(extraction.Data, _serializationOptions)
                : extraction.RawResponse;

            result.Fields["invoiceNumber"] = CreateStringField("Invoice Number", invoiceNumber, extraction.Data?.InvoiceNumber);
            result.Fields["purchaseOrderNumber"] = CreateStringField("PO Number", purchaseOrderNumber, extraction.Data?.PurchaseOrderNumber);
            result.Fields["totalAmount"] = CreateAmountField("Total Amount", totalAmount, extraction.Data?.TotalAmount);

            FinalizeResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify invoice with Gemini.");
            result.Success = false;
            result.Message = "AI could not process the uploaded invoice.";
            result.Explanations.Add("AI encountered an unexpected error while analyzing the invoice. Please try again or verify the document manually.");
        }

        return result;
    }

    private async Task<GeminiFileMetadata> UploadAndActivateAsync(Stream pdfStream, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await pdfStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        var initRequest = new UploadFileRequest
        {
            File = new UploadFileSpecification
            {
                DisplayName = $"invoice-{Guid.NewGuid():N}.pdf",
                MimeType = PdfMimeType
            }
        };

        var initResponse = await SendGeminiRequestAsync<UploadFileResponse>(HttpMethod.Post, "files:upload", initRequest, cancellationToken)
            .ConfigureAwait(false);

        if (initResponse?.File == null || string.IsNullOrWhiteSpace(initResponse.UploadUri))
        {
            throw new InvalidOperationException("Gemini did not return an upload URI for the provided invoice.");
        }

        buffer.Position = 0;
        using var uploadContent = new StreamContent(buffer, 81920);
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue(PdfMimeType);

        using (var uploadRequest = new HttpRequestMessage(HttpMethod.Put, initResponse.UploadUri)
        {
            Content = uploadContent
        })
        {
            using var uploadResponse = await _httpClient.SendAsync(uploadRequest, cancellationToken).ConfigureAwait(false);
            var uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var error = TryExtractErrorMessage(uploadBody) ?? uploadResponse.ReasonPhrase ?? "Unknown upload failure";
                throw new InvalidOperationException($"Gemini failed to accept the uploaded invoice: {error}");
            }

            if (!string.IsNullOrWhiteSpace(uploadBody))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<GeminiFileMetadata>(uploadBody, _deserializationOptions);
                    if (parsed != null)
                    {
                        initResponse.File = parsed;
                    }
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogDebug(jsonEx, "Gemini upload response was not standard JSON: {Body}", uploadBody);
                }
            }
        }

        var fileName = EnsureFileName(initResponse.File.Name);
        var deadline = DateTime.UtcNow + _fileProcessingTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = await FetchFileMetadataAsync(fileName, cancellationToken).ConfigureAwait(false);
            switch (metadata.State?.ToUpperInvariant())
            {
                case "ACTIVE":
                    return metadata;
                case "FAILED":
                    throw new InvalidOperationException($"Gemini failed to process the uploaded invoice: {metadata.Error?.Message ?? "Unknown error"}.");
                case "PROCESSING":
                case null:
                    break;
                default:
                    _logger.LogDebug("Gemini returned intermediate file state {State} for {File}", metadata.State, metadata.Name);
                    break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out while waiting for Gemini to process the uploaded invoice.");
            }

            await Task.Delay(_fileStatusPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<GeminiExtractionResult> ExtractInvoiceDataAsync(GeminiFileMetadata file, CancellationToken cancellationToken)
    {
        var fileUri = !string.IsNullOrWhiteSpace(file.Uri)
            ? file.Uri!
            : new Uri(_httpClient.BaseAddress!, EnsureFileName(file.Name)).ToString();

        var request = new GenerateContentPayload
        {
            Contents =
            {
                new GeminiContent
                {
                    Role = "user",
                    Parts =
                    {
                        new GeminiPart { Text = ExtractionPrompt },
                        new GeminiPart
                        {
                            FileData = new GeminiFileData
                            {
                                MimeType = PdfMimeType,
                                FileUri = fileUri
                            }
                        }
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                ResponseMimeType = "application/json"
            }
        };

        var response = await SendGeminiRequestAsync<GenerateContentResponse>(HttpMethod.Post, $"models/{_model}:generateContent", request, cancellationToken)
            .ConfigureAwait(false);

        if (response == null)
        {
            return new GeminiExtractionResult(null, string.Empty);
        }

        var raw = ExtractTextResponse(response)?.Trim() ?? string.Empty;
        GeminiInvoiceExtraction? extraction = null;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                extraction = JsonSerializer.Deserialize<GeminiInvoiceExtraction>(raw, _deserializationOptions);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "Failed to parse Gemini response as JSON: {Response}", raw);
            }
        }

        return new GeminiExtractionResult(extraction, raw);
    }

    private static InvoiceFieldResult CreateStringField(string label, string provided, string? extracted)
    {
        var result = new InvoiceFieldResult
        {
            Label = label,
            Provided = string.IsNullOrWhiteSpace(provided) ? null : provided.Trim(),
            Found = string.IsNullOrWhiteSpace(extracted) ? null : extracted?.Trim()
        };

        if (result.Provided == null || result.Found == null)
        {
            return result;
        }

        result.Matched = string.Equals(NormalizeForComparison(result.Provided), NormalizeForComparison(result.Found), StringComparison.Ordinal);
        return result;
    }

    private static InvoiceFieldResult CreateAmountField(string label, string provided, decimal? extracted)
    {
        var result = new InvoiceFieldResult
        {
            Label = label,
            Provided = string.IsNullOrWhiteSpace(provided) ? null : provided.Trim(),
            Found = extracted.HasValue ? extracted.Value.ToString("F2", CultureInfo.InvariantCulture) : null
        };

        if (result.Provided == null || !extracted.HasValue)
        {
            return result;
        }

        var providedAmount = ParseAmount(result.Provided);
        if (!providedAmount.HasValue)
        {
            return result;
        }

        result.Matched = Math.Abs(providedAmount.Value - extracted.Value) <= 0.01m;
        return result;
    }

    private static string NormalizeForComparison(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static decimal? ParseAmount(string value)
    {
        var filtered = new string(value.Where(ch => char.IsDigit(ch) || ch is '.' or ',' or '-' or '+').ToArray());
        if (string.IsNullOrWhiteSpace(filtered))
        {
            return null;
        }

        if (decimal.TryParse(filtered, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount))
        {
            return amount;
        }

        var swapped = filtered.Replace(".", string.Empty).Replace(",", ".");
        if (decimal.TryParse(swapped, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount))
        {
            return amount;
        }

        var stripped = filtered.Replace(",", string.Empty);
        if (decimal.TryParse(stripped, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount))
        {
            return amount;
        }

        return null;
    }

    private void FinalizeResult(InvoiceVerificationResult result)
    {
        foreach (var field in result.Fields.Values)
        {
            var explanation = BuildFieldExplanation(field);
            field.Explanation = explanation;
            if (!field.Matched && !string.IsNullOrWhiteSpace(explanation))
            {
                result.Explanations.Add(explanation);
            }
        }

        result.Success = result.Fields.Values.All(f => f.Matched);
        if (result.Success)
        {
            result.Message = "Invoice details match the uploaded PDF.";
            return;
        }

        var mismatchedLabels = result.Fields.Values
            .Where(f => !f.Matched)
            .Select(f => f.Label)
            .ToList();

        if (mismatchedLabels.Count == 1)
        {
            result.Message = $"AI found a mismatch in {mismatchedLabels[0]}.";
        }
        else if (mismatchedLabels.Count > 1)
        {
            var labelSummary = string.Join(", ", mismatchedLabels);
            result.Message = $"AI found mismatches in {labelSummary}.";
        }
        else
        {
            result.Message = "AI could not confirm all invoice details against the PDF.";
        }

        if (result.Explanations.Count == 0)
        {
            result.Explanations.Add("AI could not extract enough details from the invoice to explain the discrepancies.");
        }
    }

    private static string? BuildFieldExplanation(InvoiceFieldResult field)
    {
        if (field.Matched)
        {
            return $"AI confirmed {field.Label} matches the document.";
        }

        if (string.IsNullOrWhiteSpace(field.Provided))
        {
            return $"No {field.Label} was provided, so AI could not compare this field.";
        }

        if (string.IsNullOrWhiteSpace(field.Found))
        {
            return $"AI could not find the value '{field.Provided}' for {field.Label} anywhere in the PDF.";
        }

        return $"You entered '{field.Provided}', but AI detected '{field.Found}' for {field.Label}.";
    }

    private static string NormalizeModelName(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return TrimModelPrefix(DefaultModel);
        }

        return TrimModelPrefix(model);

        static string TrimModelPrefix(string value)
        {
            return value.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? value[7..]
                : value;
        }
    }

    private async Task<GeminiFileMetadata> FetchFileMetadataAsync(string fileName, CancellationToken cancellationToken)
    {
        var path = EnsureFileName(fileName);
        var response = await SendGeminiRequestAsync<GeminiFileMetadata>(HttpMethod.Get, path, body: null, cancellationToken)
            .ConfigureAwait(false);

        if (response == null)
        {
            throw new InvalidOperationException($"Gemini did not return metadata for file '{fileName}'.");
        }

        if (string.IsNullOrWhiteSpace(response.Uri))
        {
            response.Uri = new Uri(_httpClient.BaseAddress!, EnsureFileName(response.Name)).ToString();
        }

        return response;
    }

    private async Task<T?> SendGeminiRequestAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var uriBuilder = new StringBuilder(path);
        if (!path.Contains('?', StringComparison.Ordinal))
        {
            uriBuilder.Append('?');
        }
        else
        {
            uriBuilder.Append('&');
        }

        uriBuilder.Append("key=");
        uriBuilder.Append(Uri.EscapeDataString(_apiKey));

        using var request = new HttpRequestMessage(method, uriBuilder.ToString());

        if (body != null)
        {
            var payload = JsonSerializer.Serialize(body, _serializationOptions);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryExtractErrorMessage(content) ?? response.ReasonPhrase ?? "Unknown Gemini API error";
            throw new InvalidOperationException($"Gemini API request to '{path}' failed with status {(int)response.StatusCode}: {errorMessage}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, _deserializationOptions);
        }
        catch (JsonException jsonEx)
        {
            throw new InvalidOperationException($"Gemini API returned unexpected payload for '{path}': {content}", jsonEx);
        }
    }

    private static string EnsureFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Gemini did not return a file name for the uploaded invoice.");
        }

        return name.StartsWith("files/", StringComparison.OrdinalIgnoreCase) ? name : $"files/{name}";
    }

    private static string? ExtractTextResponse(GenerateContentResponse response)
    {
        if (response.Candidates == null || response.Candidates.Count == 0)
        {
            return response.PromptFeedback?.BlockReason;
        }

        foreach (var candidate in response.Candidates)
        {
            if (candidate?.Content?.Parts == null)
            {
                continue;
            }

            foreach (var part in candidate.Content.Parts)
            {
                if (!string.IsNullOrWhiteSpace(part?.Text))
                {
                    return part.Text;
                }

                if (!string.IsNullOrWhiteSpace(part?.InlineData?.Data))
                {
                    try
                    {
                        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(part.InlineData.Data));
                        if (!string.IsNullOrWhiteSpace(decoded))
                        {
                            return decoded;
                        }
                    }
                    catch (FormatException)
                    {
                        // Ignore invalid base64 payloads and continue searching for usable content.
                    }
                }
            }
        }

        return null;
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
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.TryGetProperty("message", out var messageElement))
                {
                    return messageElement.GetString();
                }

                if (errorElement.TryGetProperty("status", out var statusElement))
                {
                    return statusElement.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private sealed class UploadFileRequest
    {
        [JsonPropertyName("file")]
        public UploadFileSpecification File { get; set; } = new();
    }

    private sealed class UploadFileSpecification
    {
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; } = PdfMimeType;
    }

    private sealed class UploadFileResponse
    {
        [JsonPropertyName("file")]
        public GeminiFileMetadata File { get; set; } = new();

        [JsonPropertyName("upload_uri")]
        public string? UploadUri { get; set; }
    }

    private sealed class GeminiFileMetadata
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("mime_type")]
        public string? MimeType { get; set; }

        [JsonPropertyName("uri")]
        public string? Uri { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("error")]
        public GeminiError? Error { get; set; }
    }

    private sealed class GeminiError
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class GenerateContentPayload
    {
        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; } = new();

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
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; } = new();
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("file_data")]
        public GeminiFileData? FileData { get; set; }

        [JsonPropertyName("inline_data")]
        public GeminiInlineData? InlineData { get; set; }
    }

    private sealed class GeminiFileData
    {
        [JsonPropertyName("mime_type")]
        public string? MimeType { get; set; }

        [JsonPropertyName("file_uri")]
        public string? FileUri { get; set; }
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

        [JsonPropertyName("prompt_feedback")]
        public GeminiPromptFeedback? PromptFeedback { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private sealed class GeminiPromptFeedback
    {
        [JsonPropertyName("block_reason")]
        public string? BlockReason { get; set; }

        [JsonPropertyName("safety_ratings")]
        public List<GeminiSafetyRating>? SafetyRatings { get; set; }
    }

    private sealed class GeminiSafetyRating
    {
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("probability")]
        public string? Probability { get; set; }

        [JsonPropertyName("blocked")]
        public bool? Blocked { get; set; }
    }

    private sealed record GeminiExtractionResult(GeminiInvoiceExtraction? Data, string RawResponse);

    private sealed class GeminiInvoiceExtraction
    {
        [JsonPropertyName("invoiceNumber")]
        public string? InvoiceNumber { get; set; }

        [JsonPropertyName("purchaseOrderNumber")]
        public string? PurchaseOrderNumber { get; set; }

        [JsonPropertyName("totalAmount")]
        public decimal? TotalAmount { get; set; }
    }
}

public class InvoiceVerificationResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("status")]
    public string Status => Success ? "pass" : "fail";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("explanations")]
    public List<string> Explanations { get; } = new();

    [JsonPropertyName("fields")]
    public Dictionary<string, InvoiceFieldResult> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("extractedText")]
    public string ExtractedText { get; set; } = string.Empty;
}

public class InvoiceFieldResult
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("matched")]
    public bool Matched { get; set; }

    [JsonPropertyName("provided")]
    public string? Provided { get; set; }

    [JsonPropertyName("found")]
    public string? Found { get; set; }

    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; }
}
