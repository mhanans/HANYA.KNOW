using backend.Services;
using Microsoft.AspNetCore.Mvc;

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
        return await _store.ListAsync();
    }
}

