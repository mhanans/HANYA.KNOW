using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using backend.Configuration;
using backend.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class PrototypeGenerationService
{
    private readonly ProjectAssessmentStore _store;
    private readonly PrototypeStore _prototypeStore;
    private readonly ILogger<PrototypeGenerationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    
    // Relative path assuming the app runs from the 'backend' folder
    private const string FrontendPublicPath = @"..\frontend\public\demos";

    private readonly IConfiguration _configuration;

    public PrototypeGenerationService(
        ProjectAssessmentStore store,
        PrototypeStore prototypeStore,
        IConfiguration configuration,
        IOptions<LlmOptions> llmOptions,
        ILogger<PrototypeGenerationService> logger)
    {
        _store = store;
        _prototypeStore = prototypeStore;
        _logger = logger;
        _httpClient = new HttpClient();
        _configuration = configuration;
        
        _apiKey = configuration["Gemini:ApiKey"]
                 ?? configuration["GoogleAI:ApiKey"]
                 ?? configuration["Google:ApiKey"]
                 ?? configuration["Llm:ApiKey"]
                 ?? configuration["ApiKey"]
                 ?? string.Empty;

        _model = configuration["Gemini:Model"]
                 ?? configuration["GoogleAI:Model"]
                 ?? configuration["Google:Model"]
                 ?? configuration["Llm:Model"]
                 ?? configuration["Model"]
                 ?? llmOptions.Value.Model
                 ?? "gemini-pro";
    }

    private string GetPrototypeStoragePath()
    {
        var path = _configuration["PrototypeStoragePath"];
        if (string.IsNullOrWhiteSpace(path))
        {
             // Fallback 1: Dev environment ..\frontend\public\demos
             path = Path.Combine(Directory.GetParent(AppContext.BaseDirectory)?.FullName ?? AppContext.BaseDirectory, "..", "frontend", "public", "demos");
             
             // Fallback 2: If fail, use local 'demos'
             if (!Directory.Exists(path) && !path.Contains("frontend")) // Check simple existence or path logic
             {
                 path = Path.Combine(AppContext.BaseDirectory, "demos");
             }
             else if (!Directory.Exists(Path.GetDirectoryName(path))) // check parent of demos
             {
                  // If frontend/public doesn't exist, we can't create demos there safely
                  path = Path.Combine(AppContext.BaseDirectory, "demos");
             }
        }
        return path;
    }

    public async Task StartGenerationAsync(int assessmentId, List<string>? itemIds = null, Dictionary<string, string>? itemFeedback = null)
    {
        var assessment = await _store.GetAsync(assessmentId);
        if (assessment == null) throw new ArgumentException($"Assessment {assessmentId} not found");

        // Validate items synchronously BEFORE starting background task
        // This prevents "Processing" state for invalid requests
        var itemsToGenerate = GetItemsToGenerate(assessment, itemIds);

        if (!itemsToGenerate.Any())
        {
             _logger.LogWarning("No item found with [WEB] or [MOBILE] tag for Assessment {Id}.", assessmentId);
             throw new InvalidOperationException("No items found to generate. Ensure your assessment items have [WEB] or [MOBILE] in their details or category.");
        }

        // Set status to Processing immediately
        await RecordGenerationStatusAsync(assessmentId, assessment.ProjectName, "Processing");

        // Run generation in background
        _ = Task.Run(async () =>
        {
            try
            {
                await InternalGenerateDemoAsync(assessmentId, assessment, itemsToGenerate, itemIds, itemFeedback);
                await RecordGenerationStatusAsync(assessmentId, assessment.ProjectName, "Completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background prototype generation failed for Assessment {Id}", assessmentId);
                await RecordGenerationStatusAsync(assessmentId, assessment.ProjectName, "Failed");
            }
        });
    }

    private List<AssessmentItem> GetItemsToGenerate(ProjectAssessment assessment, List<string>? itemIds)
    {
        List<AssessmentItem> items;

        if (itemIds != null && itemIds.Any())
        {
             var requestedIds = itemIds.Select(id => id.Trim()).ToList();
             var allItems = assessment.Sections.SelectMany(s => s.Items).ToList();
             
             // Explicit Generation: Robust Matching (Trim + IgnoreCase)
             items = allItems
                 .Where(i => requestedIds.Any(req => req.Equals(i.ItemId?.Trim(), StringComparison.OrdinalIgnoreCase)))
                 .ToList();
        }
        else
        {
             // Bulk Generation: Standard Logic
             items = assessment.Sections
                .Where(s => s.SectionName.Contains("Item Development", StringComparison.OrdinalIgnoreCase))
                .SelectMany(s => s.Items)
                .Where(i => i.IsNeeded)
                .ToList();

            // Fallback: if no items found in strict sections, grab all items marked needed from anywhere
            if (!items.Any()) 
            {
                 items = assessment.Sections.SelectMany(s => s.Items).Where(i => i.IsNeeded).ToList();
            }

            // STRICT FILTER for Bulk: Only generate Web or Mobile items
            items = items.Where(i => IsWebItem(i) || IsMobileItem(i)).ToList();
        }
        return items;
    }

    private async Task InternalGenerateDemoAsync(int assessmentId, ProjectAssessment assessment, List<AssessmentItem> itemsToGenerate, List<string>? itemIds, Dictionary<string, string>? itemFeedback = null)
    {
        var storageBase = GetPrototypeStoragePath();
        var outputDir = Path.Combine(storageBase, assessmentId.ToString());
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        _logger.LogInformation("Starting prototype generation for Assessment {Id} with {Count} items.", assessmentId, itemsToGenerate.Count);

        // Build Consistent Navigation
        var navigationHtml = BuildNavigationHtml(assessment);
        
        // ... (rest of method continues, removed item selection logic)

        // Write the shared CSS file to the output directory
        var cssPath = Path.Combine(AppContext.BaseDirectory, "Services", "standard_styles.css");
        var cssContent = await File.ReadAllTextAsync(cssPath);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "style.css"), cssContent);

        // Use ConcurrentBag for thread-safe collection during parallel processing
        var generatedPagesBag = new System.Collections.Concurrent.ConcurrentBag<(string Filename, string Title)>();

        // Limit concurrency to avoid rate limits, but speed up processing
        var options = new ParallelOptions { MaxDegreeOfParallelism = 8 }; 
        
        int processed = 0;
        await Parallel.ForEachAsync(itemsToGenerate, options, async (item, token) =>
        {
            var currentCount = System.Threading.Interlocked.Increment(ref processed);
            _logger.LogInformation("[{Current}/{Total}] Generating demo page for item: {ItemName}", currentCount, itemsToGenerate.Count, item.ItemName);
            try
            {
                var filename = $"{SanitizeFilename(item.ItemName)}_{item.ItemId}.html";
                
                string? specificFeedback = null;
                if (itemFeedback != null && itemFeedback.TryGetValue(item.ItemId, out var fb)) 
                {
                    specificFeedback = fb;
                }

                string? existingHtml = null;
                var existingPath = Path.Combine(outputDir, filename);
                if (File.Exists(existingPath))
                {
                    // Read file to pass as context for revision
                    existingHtml = await File.ReadAllTextAsync(existingPath, token);
                }

                // Pass cssContent for context, but we won't inline it
                var pageContent = await GeneratePageContentAsync(item, assessment.ProjectName, navigationHtml, cssContent, specificFeedback, existingHtml);
                
                await File.WriteAllTextAsync(Path.Combine(outputDir, filename), pageContent, token);
                generatedPagesBag.Add((filename, item.ItemName));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate demo page for item {ItemName}", item.ItemName);
            }
        });

        var generatedPages = generatedPagesBag.ToList();
        
        _logger.LogInformation("Finished generating pages. Creating index.html...");
        
        var allPages = new List<(string Filename, string Title)>();
        if (itemIds != null && itemIds.Any())
        {
             // Partial update: Scan directory for existing files to rebuild complete index
             var existingFiles = Directory.GetFiles(outputDir, "*.html")
                                          .Where(f => !Path.GetFileName(f).Equals("index.html", StringComparison.OrdinalIgnoreCase));
             
             foreach(var file in existingFiles)
             {
                 var name = Path.GetFileNameWithoutExtension(file);
                 // Format: {SanitizedName}_{ItemId}.html
                 var parts = name.Split('_');
                 var title = parts.Length > 1 ? string.Join(" ", parts.Take(parts.Length - 1)) : name; 
                 allPages.Add((Path.GetFileName(file), title));
             }
        }
        else 
        {
            allPages = generatedPages;
        }

        var indexContent = GenerateIndexHtml(assessment.ProjectName, allPages);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "index.html"), indexContent);

        _logger.LogInformation("Prototype generation complete for Assessment {Id}", assessmentId);
    }

    private string BuildNavigationHtml(ProjectAssessment assessment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ul class=\"app-nav\">");
        sb.AppendLine("<li><a href=\"index.html\"><i class=\"fa fa-th-large\"></i>UI Home</a></li>");

        var relevantSections = assessment.Sections
            .Where(s => s.SectionName.Contains("Item Development", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!relevantSections.Any())
        {
            relevantSections = assessment.Sections;
        }

        foreach (var section in relevantSections)
        {
            bool hasSectionHeader = false;
            foreach (var item in section.Items.Where(i => i.IsNeeded))
            {
                if (IsMobileItem(item)) continue; // Mobile takes precedence in generation, so exclude from Web Sidebar
                if (!IsWebItem(item)) continue; // Only Web items in Web Sidebar

                if (!hasSectionHeader)
                {
                    sb.AppendLine($"<li class=\"app-nav-section\">{section.SectionName}</li>");
                    hasSectionHeader = true;
                }
                var filename = $"{SanitizeFilename(item.ItemName)}_{item.ItemId}.html";
                sb.AppendLine($"<li><a href=\"{filename}\"><i class=\"fa fa-file-o\"></i>{item.ItemName}</a></li>");
            }
        }
        sb.AppendLine("</ul>");
        return sb.ToString();
    }

    private async Task RecordGenerationStatusAsync(int assessmentId, string projectName, string status)
    {
        var record = new PrototypeRecord
        {
            AssessmentId = assessmentId,
            ProjectName = projectName,
            GeneratedAt = DateTime.UtcNow,
            StoragePath = $"demos/{assessmentId}",
            Status = status
        };
        await _prototypeStore.SaveAsync(record);
    }

    private string SanitizeFilename(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var validName = new string(name.Where(ch => !invalidChars.Contains(ch)).ToArray());
        return validName.Replace(" ", "_").ToLowerInvariant();
    }

    private bool IsMobileItem(AssessmentItem item)
    {
        return item.ItemDetail != null && 
               (item.ItemDetail.Contains("[MOBILE]", StringComparison.OrdinalIgnoreCase) ||
                item.Category?.Contains("Mobile", StringComparison.OrdinalIgnoreCase) == true);
    }

    private bool IsWebItem(AssessmentItem item)
    {
        return item.ItemDetail != null && 
               (item.ItemDetail.Contains("[WEB]", StringComparison.OrdinalIgnoreCase) ||
                item.Category?.Contains("Web", StringComparison.OrdinalIgnoreCase) == true);
    }

    private async Task<string> GeneratePageContentAsync(AssessmentItem item, string projectName, string navigationHtml, string cssContent, string? feedback = null, string? existingHtml = null)
    {
        bool isMobile = IsMobileItem(item);
        string templateFilename = isMobile ? "template_mobile.html" : "template_structure.html";
        string templatePath = Path.Combine(AppContext.BaseDirectory, "Services", templateFilename);
        
        // Fallback if mobile template missing
        if (!File.Exists(templatePath)) 
        {
             templatePath = Path.Combine(AppContext.BaseDirectory, "Services", "template_structure.html");
             isMobile = false; 
        }

        var htmlTemplate = await File.ReadAllTextAsync(templatePath);

        // INJECT NAVIGATION (Only for Web, Mobile uses static Nav)
        if (!isMobile)
        {
            var navRegex = new System.Text.RegularExpressions.Regex(@"<ul class=""app-nav"">[\s\S]*?</ul>");
            htmlTemplate = navRegex.Replace(htmlTemplate, navigationHtml);
        }

        // INJECT METADATA
        htmlTemplate = htmlTemplate.Replace("<!-- PAGE_TITLE_PLACEHOLDER -->", item.ItemName);
        var subtitle = item.ItemDetail?.Length > 100 ? item.ItemDetail.Substring(0, 97) + "..." : item.ItemDetail;
        htmlTemplate = htmlTemplate.Replace("<!-- PAGE_SUBTITLE_PLACEHOLDER -->", subtitle ?? "");

        var feedbackPrompt = string.IsNullOrWhiteSpace(feedback) ? "" : $"\nIMPORTANT USER FEEDBACK/INSTRUCTIONS: {feedback}\n";
        
        string existingContext = "";
        if (!string.IsNullOrWhiteSpace(existingHtml))
        {
            // Try to extract just the previous body content to save tokens, or pass full file
            // Since we want robust revision, passing full context is safer but expensive. 
            // Let's pass the text specifically.
            existingContext = $@"
CURRENT IMPLEMENTATION:
Below is the EXISTING HTML content for this feature that was previously generated.
Your task is to REVISE and IMPROVE this existing code based on the new user feedback.
Do NOT completely rewrite it unless the feedback implies a redesign.
Maintain the existing structure where possible.

--- EXISTING HTML START ---
{existingHtml}
--- EXISTING HTML END ---
";
        }

        var prompt = $@"
You are a frontend developer expert.
{(string.IsNullOrWhiteSpace(existingHtml) ? "Generate" : "Revise")} the HTML CONTENT for a {(isMobile ? "Mobile App Screen" : "Web Dashboard Feature")} in the project '{projectName}'.

Feature Name: {item.ItemName}
Feature Details: {item.ItemDetail}
{feedbackPrompt}

{existingContext}

Instructions:
1. OUTPUT ONLY the HTML code that goes inside the main content area (e.g. inside <main> or the content card).
   - Do NOT output <html>, <head>, <body>, scripts, or CSS.
   - Do NOT output the navigation sidebar or header (these are already provided).
   - START DIRECTLY with your <div> elements.

2. Styling:
   - Use Bootstrap 5 classes (row, col, card, btn, etc).
   - Use the following CUSTOM CSS classes provided in the system (for context only):
     {cssContent}
   - Use 'app-card' for containers.

3. Content Requirements:
   - Implement the feature described in 'Feature Details'.
   - Make it look premium and professional.
   - {(isMobile ? "Fit content for a 375px mobile screen." : "Use responsive grid for dashboard layout.")}
";

        var aiContent = await SendGeminiRequestAsync(prompt);

        // INJECT AI CONTENT
        htmlTemplate = htmlTemplate.Replace("<!-- CONTENT_PLACEHOLDER -->", aiContent);

        return htmlTemplate;
    }

    private string GenerateIndexHtml(string projectName, List<(string Filename, string Title)> pages)
    {
        var linksHtml = string.Join("\n", pages.Select(p => 
            $"<a href=\"{p.Filename}\" class=\"demo-link\">{p.Title}</a>"));

        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Demo: {projectName}</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%);
            min-height: 100vh;
            margin: 0;
            padding: 40px;
            display: flex;
            justify-content: center;
            align-items: flex-start;
        }}
        .container {{
            background: white;
            padding: 40px;
            border-radius: 16px;
            box-shadow: 0 10px 25px rgba(0,0,0,0.1);
            max-width: 600px;
            width: 100%;
        }}
        h1 {{
            color: #333;
            text-align: center;
            margin-bottom: 30px;
        }}
        .demo-link {{
            display: block;
            padding: 15px 20px;
            margin-bottom: 10px;
            background: white;
            border: 1px solid #eee;
            border-radius: 8px;
            text-decoration: none;
            color: #555;
            font-weight: 500;
            transition: all 0.2s ease;
            box-shadow: 0 2px 5px rgba(0,0,0,0.05);
        }}
        .demo-link:hover {{
            transform: translateY(-2px);
            box-shadow: 0 5px 15px rgba(0,0,0,0.1);
            border-color: #007bff;
            color: #007bff;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Prototype: {projectName}</h1>
        <div class=""links"">
            {linksHtml}
        </div>
    </div>
</body>
</html>";
    }

    public bool HasPrototype(int assessmentId)
    {
        var outputDir = Path.Combine(GetPrototypeStoragePath(), assessmentId.ToString());
        return Directory.Exists(outputDir) && File.Exists(Path.Combine(outputDir, "index.html"));
    }

    public async Task<byte[]> GetZipBytesAsync(int assessmentId, string outputDir)
    {
        _logger.LogInformation("Preparing zip for assessment {Id} from path {Path}", assessmentId, outputDir);
        
        if (!Directory.Exists(outputDir))
        {
             _logger.LogError("Prototype directory not found: {Path}", outputDir);
             throw new DirectoryNotFoundException($"Prototype for assessment {assessmentId} not found at {outputDir}");
        }
        
        using var memoryStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var file in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(outputDir, file);
                var entry = archive.CreateEntry(relativePath);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream);
            }
        }
        
        return memoryStream.ToArray();
    }

    private async Task<string> SendGeminiRequestAsync(string prompt)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.7,
                maxOutputTokens = 8192
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);
        
        try 
        {
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            // Clean up markdown blocks if present, despite instruction
            if (text.StartsWith("```html")) text = text.Substring(7);
            if (text.StartsWith("```")) text = text.Substring(3);
            if (text.EndsWith("```")) text = text.Substring(0, text.Length - 3);

            return text.Trim();
        }
        catch
        {
            return "<!-- Failed to generate content -->";
        }
    }
}
