using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Linq;

namespace backend.Controllers;

[ApiController]
[Route("api/ui")]
public class UiController : ControllerBase
{
    private readonly UiStore _store;

    public UiController(UiStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<ActionResult<List<UiPage>>> Get()
    {
        var roles = User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => int.Parse(c.Value))
            .ToArray();
        var pages = await _store.ListForRolesAsync(roles);
        return pages;
    }
}

