using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenerativeAI;
using GenerativeAI.Clients;
using GenerativeAI.Types;
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

    private readonly ILogger<InvoiceVerificationService> _logger;
    private readonly GeminiModel _geminiModel;
    private readonly FileClient _fileClient;
    private readonly JsonSerializerOptions _deserializationOptions;
    private readonly JsonSerializerOptions _serializationOptions;
    private readonly TimeSpan _fileStatusPollInterval;
    private readonly TimeSpan _fileProcessingTimeout;

    public InvoiceVerificationService(ILogger<InvoiceVerificationService> logger, IConfiguration configuration)
    {
        _logger = logger;

        var apiKey = configuration["Gemini:ApiKey"]
                     ?? configuration["GoogleAI:ApiKey"]
                     ?? configuration["Google:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured. Set 'Gemini:ApiKey' in configuration.");
        }

        var modelName = configuration["Gemini:Model"] ?? DefaultModel;
        var pollSeconds = Math.Max(1, configuration.GetValue<int?>("Gemini:FileStatusPollSeconds") ?? 2);
        var timeoutSeconds = Math.Max(pollSeconds, configuration.GetValue<int?>("Gemini:FileProcessingTimeoutSeconds") ?? 300);

        _geminiModel = new GeminiModel(apiKey, modelName);
        _geminiModel.TimeoutForFileStateCheck = timeoutSeconds;

        _fileClient = _geminiModel.Files ?? throw new InvalidOperationException("Gemini file client could not be initialized.");

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

    private async Task<RemoteFile> UploadAndActivateAsync(Stream pdfStream, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await pdfStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        var displayName = $"invoice-{Guid.NewGuid():N}";
        var uploaded = await _fileClient.UploadStreamAsync(buffer, displayName, PdfMimeType, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(uploaded.Name))
        {
            throw new InvalidOperationException("Gemini did not return a file identifier after upload.");
        }

        var deadline = DateTime.UtcNow + _fileProcessingTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = await _fileClient.GetFileAsync(uploaded.Name, cancellationToken).ConfigureAwait(false);
            switch (current.State)
            {
                case FileState.ACTIVE:
                    return current;
                case FileState.FAILED:
                    throw new InvalidOperationException($"Gemini failed to process the uploaded invoice: {current.Error?.Message ?? "Unknown error"}.");
                case FileState.PROCESSING:
                case FileState.STATE_UNSPECIFIED:
                    break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out while waiting for Gemini to process the uploaded invoice.");
            }

            await Task.Delay(_fileStatusPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<GeminiExtractionResult> ExtractInvoiceDataAsync(RemoteFile file, CancellationToken cancellationToken)
    {
        var request = new GenerateContentRequest();
        request.UseJsonMode<GeminiInvoiceExtraction>(_deserializationOptions);
        request.AddRemoteFile(file);
        request.AddText(ExtractionPrompt);

        var response = await _geminiModel.GenerateContentAsync(request, cancellationToken).ConfigureAwait(false);
        if (response == null)
        {
            return new GeminiExtractionResult(null, string.Empty);
        }

        var extraction = response.ToObject<GeminiInvoiceExtraction>(_deserializationOptions);
        var raw = response.Text()?.Trim() ?? string.Empty;

        if (extraction == null && !string.IsNullOrEmpty(raw))
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
