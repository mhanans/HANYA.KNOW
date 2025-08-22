using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly UserStore _users;

    public UsersController(UserStore users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<ActionResult<List<User>>> Get()
    {
        return await _users.ListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<User>> Post(UserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Username and password required");
        var user = new User { Username = request.Username, Password = request.Password, RoleIds = request.RoleIds ?? new List<int>() };
        var id = await _users.CreateAsync(user);
        user.Id = id;
        user.Password = string.Empty;
        return CreatedAtAction(nameof(Get), new { id }, user);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(int id, UserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Username and password required");
        var user = new User { Id = id, Username = request.Username, Password = request.Password, RoleIds = request.RoleIds ?? new List<int>() };
        try
        {
            await _users.UpdateAsync(user);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _users.DeleteAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

public class UserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<int> RoleIds { get; set; } = new();
}
