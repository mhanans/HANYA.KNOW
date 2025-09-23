using System.Threading;
using System.Threading.Tasks;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/invoices")]
[UiAuthorize("invoice-verification")]
public class InvoiceController : ControllerBase
{
    private readonly InvoiceVerificationService _verificationService;
    private readonly ILogger<InvoiceController> _logger;

    public InvoiceController(InvoiceVerificationService verificationService, ILogger<InvoiceController> logger)
    {
        _verificationService = verificationService;
        _logger = logger;
    }

    public class InvoiceVerificationRequest
    {
        public IFormFile? File { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? PurchaseOrderNumber { get; set; }
        public string? TotalAmount { get; set; }
    }

    [HttpPost("verify")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Verify([FromForm] InvoiceVerificationRequest request, CancellationToken cancellationToken)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest(new { success = false, message = "A PDF invoice file is required." });
        }

        if (!request.File.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { success = false, message = "Only PDF files are supported." });
        }

        try
        {
            await using var stream = request.File.OpenReadStream();
            var result = await _verificationService.VerifyAsync(
                stream,
                request.InvoiceNumber ?? string.Empty,
                request.PurchaseOrderNumber ?? string.Empty,
                request.TotalAmount ?? string.Empty,
                cancellationToken);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { success = false, message = "Invoice verification was cancelled." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify invoice details.");
            return StatusCode(500, new
            {
                success = false,
                message = "Failed to verify invoice details.",
                technicalDetails = ex.Message
            });
        }
    }
}
