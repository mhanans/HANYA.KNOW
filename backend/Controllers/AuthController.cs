using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace backend.Controllers;

[ApiController]
[Route("api")]
public class AuthController : ControllerBase
{
    private readonly UserStore _users;
    private readonly IConfiguration _config;
    private readonly AccelistSsoAuthenticator _ssoAuthenticator;

    public AuthController(UserStore users, IConfiguration config, AccelistSsoAuthenticator ssoAuthenticator)
    {
        _users = users;
        _config = config;
        _ssoAuthenticator = ssoAuthenticator;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _users.AuthenticateAsync(request.Username, request.Password);
        if (user == null) return Unauthorized();

        var tokenString = GenerateToken(user);

        return Ok(new { user.Id, user.Username, Roles = user.RoleIds, Token = tokenString });
    }

    [AllowAnonymous]
    [HttpPost("login/sso")]
    public async Task<IActionResult> LoginWithAccelistSso(AccelistSsoLoginRequest request)
    {
        var email = await _ssoAuthenticator.GetEmailFromTokenAsync(request.TamSignOnToken ?? string.Empty);
        if (string.IsNullOrWhiteSpace(email))
        {
            return Unauthorized(new { message = "SSO authentication failed" });
        }

        var user = await _users.FindByUsernameAsync(email);
        if (user == null || user.RoleIds.Count == 0)
        {
            return Unauthorized(new { message = "Login failed: User not found in database or no roles assigned" });
        }

        var tokenString = GenerateToken(user);
        return Ok(new { user.Id, user.Username, Roles = user.RoleIds, Token = tokenString });
    }

    [HttpPost("logout")]
    public IActionResult Logout() => Ok();

    [HttpGet("me")]
    public IActionResult Me()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = User.Identity?.Name ?? string.Empty;
        var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => int.Parse(c.Value)).ToList();
        return Ok(new { Id = id, Username = name, Roles = roles });
    }

    private string GenerateToken(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        };
        foreach (var rid in user.RoleIds)
            claims.Add(new Claim(ClaimTypes.Role, rid.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? string.Empty));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AccelistSsoLoginRequest
{
    public string? TamSignOnToken { get; set; }
}
