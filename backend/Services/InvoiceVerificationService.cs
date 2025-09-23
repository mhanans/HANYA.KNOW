using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Tesseract;
using UglyToad.PdfPig;

namespace backend.Services;

public class InvoiceVerificationService
{
    private readonly ILogger<InvoiceVerificationService> _logger;
    private readonly string _tessDataPath;

    public InvoiceVerificationService(ILogger<InvoiceVerificationService> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _tessDataPath = Path.Combine(environment.ContentRootPath, "tessdata");
    }

    public async Task<InvoiceVerificationResult> VerifyAsync(Stream pdfStream, string invoiceNumber, string purchaseOrderNumber, string totalAmount, CancellationToken cancellationToken = default)
    {
        var extracted = await ExtractTextAsync(pdfStream, cancellationToken);
        var normalized = NormalizeWhitespace(extracted);
        var comparisonText = NormalizeForComparison(extracted);

        var result = new InvoiceVerificationResult
        {
            ExtractedText = normalized
        };

        result.Fields["invoiceNumber"] = EvaluateToken("Invoice Number", invoiceNumber, extracted, comparisonText);
        result.Fields["purchaseOrderNumber"] = EvaluateToken("PO Number", purchaseOrderNumber, extracted, comparisonText);
        result.Fields["totalAmount"] = EvaluateAmount("Total Amount", totalAmount, extracted, comparisonText);

        result.Success = result.Fields.Values.All(f => f.Matched);
        result.Message = result.Success
            ? "Invoice details match the uploaded PDF."
            : "One or more invoice fields did not match the PDF content.";
        return result;
    }

    private async Task<string> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await pdfStream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var sb = new StringBuilder();
        var pagesNeedingOcr = new List<int>();

        try
        {
            using var document = PdfDocument.Open(new MemoryStream(bytes));
            var pageIndex = 0;
            foreach (var page in document.GetPages())
            {
                var text = page.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text.Trim());
                }
                else
                {
                    pagesNeedingOcr.Add(pageIndex);
                }
                pageIndex++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract embedded text from PDF.");
        }

        if (pagesNeedingOcr.Count == 0)
        {
            return sb.ToString();
        }

        try
        {
            if (!TryCreateEngine(out var engine))
            {
                return sb.ToString();
            }

            using (engine)
            using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(2480, 3508));
            foreach (var pageIndex in pagesNeedingOcr)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var pageReader = docReader.GetPageReader(pageIndex);
                var width = pageReader.GetPageWidth();
                var height = pageReader.GetPageHeight();
                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                var rawBytes = pageReader.GetImage();
                if (rawBytes.Length == 0)
                {
                    continue;
                }

                using var image = Image.LoadPixelData<Bgra32>(rawBytes, width, height);
                using var imageStream = new MemoryStream();
                image.Save(imageStream, new PngEncoder());
                using var pix = Pix.LoadFromMemory(imageStream.ToArray());
                using var ocrPage = engine.Process(pix);
                var text = ocrPage.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run OCR on PDF pages.");
        }

        return sb.ToString();
    }

    private bool TryCreateEngine(out TesseractEngine? engine)
    {
        engine = null;

        if (!Directory.Exists(_tessDataPath))
        {
            _logger.LogWarning("tessdata folder not found at '{TessDataPath}'. OCR will be skipped.", _tessDataPath);
            return false;
        }

        var engFile = Path.Combine(_tessDataPath, "eng.traineddata");
        if (!File.Exists(engFile))
        {
            _logger.LogWarning("Tesseract language data not found at '{EngFile}'. OCR will be skipped.", engFile);
            return false;
        }

        try
        {
            engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.LstmOnly);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Tesseract OCR engine. OCR will be skipped.");
            engine = null;
            return false;
        }
    }

    private static InvoiceFieldResult EvaluateToken(string label, string provided, string originalText, string normalizedText)
    {
        var result = new InvoiceFieldResult
        {
            Label = label,
            Provided = string.IsNullOrWhiteSpace(provided) ? null : provided.Trim()
        };

        if (string.IsNullOrWhiteSpace(provided))
        {
            return result;
        }

        var normalizedProvided = NormalizeForComparison(provided);
        if (string.IsNullOrEmpty(normalizedProvided))
        {
            return result;
        }

        result.Matched = normalizedText.Contains(normalizedProvided, StringComparison.OrdinalIgnoreCase);
        result.Found = FindLineContaining(originalText, provided, normalizedProvided);
        if (result.Matched && string.IsNullOrWhiteSpace(result.Found))
        {
            result.Found = provided.Trim();
        }

        return result;
    }

    private static InvoiceFieldResult EvaluateAmount(string label, string provided, string originalText, string normalizedText)
    {
        var result = new InvoiceFieldResult
        {
            Label = label,
            Provided = string.IsNullOrWhiteSpace(provided) ? null : provided.Trim()
        };

        if (string.IsNullOrWhiteSpace(provided))
        {
            return result;
        }

        var providedAmount = ParseAmount(provided);
        if (providedAmount == null)
        {
            return result;
        }

        var foundToken = FindAmountToken(originalText, providedAmount.Value);
        if (foundToken != null)
        {
            result.Matched = true;
            result.Found = foundToken;
            return result;
        }

        // fallback to simple token search when parsing fails on OCR noise
        var normalizedProvided = NormalizeForComparison(providedAmount.Value.ToString(CultureInfo.InvariantCulture));
        result.Matched = normalizedText.Contains(normalizedProvided, StringComparison.OrdinalIgnoreCase);
        if (result.Matched)
        {
            result.Found = providedAmount.Value.ToString("F2", CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static decimal? ParseAmount(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = Regex.Replace(value, "[^0-9,.-]", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return null;
        }

        if (decimal.TryParse(sanitized, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount))
        {
            return amount;
        }

        var swapped = sanitized.Replace(".", string.Empty).Replace(",", ".");
        if (decimal.TryParse(swapped, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount))
        {
            return amount;
        }

        var removed = sanitized.Replace(",", string.Empty);
        if (decimal.TryParse(removed, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount))
        {
            return amount;
        }

        return null;
    }

    private static string? FindAmountToken(string text, decimal target)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        const decimal tolerance = 0.01m;
        foreach (Match match in Regex.Matches(text, @"[-+]?[0-9][0-9.,]*"))
        {
            var candidate = ParseAmount(match.Value);
            if (candidate == null)
            {
                continue;
            }

            if (Math.Abs(candidate.Value - target) <= tolerance)
            {
                return match.Value.Trim();
            }
        }

        return null;
    }

    private static string? FindLineContaining(string text, string provided, string normalizedProvided)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(normalizedProvided))
        {
            return null;
        }

        var lines = text.Split('\n', '\r');
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var normalizedLine = NormalizeForComparison(line);
            if (normalizedLine.Contains(normalizedProvided, StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return null;
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalizedLineEndings = Regex.Replace(value, "\r\n?", "\n");
        var lines = normalizedLineEndings
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
        return string.Join('\n', lines);
    }

    private static string NormalizeForComparison(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var alphanumeric = Regex.Replace(value, "[^A-Za-z0-9]", string.Empty);
        return alphanumeric.ToLowerInvariant();
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
}
