using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;
using System.Security.Claims;

namespace backend.Middleware;

public class UiAuthorizeAttribute : TypeFilterAttribute
{
    public UiAuthorizeAttribute(params string[] uiKeys) : base(typeof(UiAuthorizeFilter))
    {
        Arguments = new object[] { uiKeys };
    }

    private class UiAuthorizeFilter : IAsyncAuthorizationFilter
    {
        private readonly string[] _uiKeys;
        private readonly UiStore _uiStore;

        public UiAuthorizeFilter(string[] uiKeys, UiStore uiStore)
        {
            _uiKeys = uiKeys ?? Array.Empty<string>();
            _uiStore = uiStore;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var roles = context.HttpContext.User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => int.TryParse(c.Value, out var id) ? (int?)id : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value);

            if (_uiKeys.Length == 0)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var allowed = await _uiStore.HasAccessAsync(roles, _uiKeys);
            if (!allowed)
            {
                context.Result = new UnauthorizedResult();
            }
        }
    }
}
