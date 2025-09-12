using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using System.Linq;

namespace backend.Middleware;

public class UiAuthorizeAttribute : TypeFilterAttribute
{
    public UiAuthorizeAttribute(string uiKey) : base(typeof(UiAuthorizeFilter))
    {
        Arguments = new object[] { uiKey };
    }

    private class UiAuthorizeFilter : IAsyncAuthorizationFilter
    {
        private readonly string _uiKey;
        private readonly UiStore _uiStore;

        public UiAuthorizeFilter(string uiKey, UiStore uiStore)
        {
            _uiKey = uiKey;
            _uiStore = uiStore;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var roles = context.HttpContext.User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => int.Parse(c.Value));
            var allowed = await _uiStore.HasAccessAsync(roles, _uiKey);
            if (!allowed)
            {
                context.Result = new UnauthorizedResult();
            }
        }
    }
}
