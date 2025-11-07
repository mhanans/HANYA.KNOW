using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using backend.Middleware;
using backend.Models;
using backend.Models.Configuration;
using backend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Threading;

var builder = WebApplication.CreateBuilder(args);

const string CombinedScheme = "Combined";

builder.Services.AddControllers(options =>
{
    var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.Filters.Add(new AuthorizeFilter(policy));
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API key required",
        Name = "X-API-KEY",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey",
                },
                In = ParameterLocation.Header,
            },
            Array.Empty<string>()
        }
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer",
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("ConnectionStrings"));
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection("Embedding"));
builder.Services.AddSingleton<EmbeddingClient>();
builder.Services.AddSingleton<VectorStore>();
builder.Services.Configure<KnowledgeBaseOptions>(builder.Configuration.GetSection("KnowledgeBase"));
builder.Services.AddSingleton<KnowledgeBaseStore>();
builder.Services.AddSingleton<KnowledgeBaseIngestionService>();
builder.Services.AddSingleton<CodeEmbeddingStore>();
builder.Services.AddSingleton<CategoryStore>();
builder.Services.AddSingleton<RoleStore>();
builder.Services.AddSingleton<StatsStore>();
builder.Services.AddSingleton<RecommendationStore>();
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<UiStore>();
builder.Services.AddSingleton<SourceCodeSyncService>();
builder.Services.AddSingleton<TicketCategoryStore>();
builder.Services.AddSingleton<PicStore>();
builder.Services.AddSingleton<TicketStore>();
builder.Services.AddSingleton<TicketAssignmentResultStore>();
builder.Services.AddSingleton<TicketAssignmentService>();
builder.Services.AddSingleton<InvoiceVerificationService>();
builder.Services.AddSingleton<ProjectTemplateStore>();
builder.Services.AddSingleton<ProjectAssessmentStore>();
builder.Services.AddSingleton<AssessmentJobStore>();
builder.Services.AddSingleton<PresalesConfigurationStore>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<PresalesConfigurationStore>().GetEffectiveEstimationPolicy());
builder.Services.AddSingleton<ProjectAssessmentAnalysisService>();
builder.Services.AddSingleton<TimelineStore>();
builder.Services.AddSingleton<TimelineEstimationStore>();
builder.Services.AddSingleton<TimelineEstimationReferenceStore>();
builder.Services.AddSingleton<TimelineEstimatorService>();
builder.Services.AddSingleton<TimelineGenerationService>();
builder.Services.AddSingleton<CostEstimationConfigurationStore>();
builder.Services.AddSingleton<CostEstimationStore>();
builder.Services.AddSingleton<CostEstimationService>();
builder.Services.AddSingleton<AssessmentBundleExportService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<KnowledgeBaseIngestionService>());
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.AddHttpClient<LlmClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    client.Timeout = options.TimeoutSeconds > 0
        ? TimeSpan.FromSeconds(options.TimeoutSeconds)
        : Timeout.InfiniteTimeSpan;
});
builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection("Chat"));
builder.Services.Configure<RecommendationOptions>(builder.Configuration.GetSection("Recommendation"));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ConversationStore>();
builder.Services.Configure<SourceCodeOptions>(builder.Configuration.GetSection("SourceCode"));
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.AddSingleton<GitHubTokenStore>();
builder.Services.AddSingleton<GitHubIntegrationService>();
builder.Services.Configure<AccelistSsoOptions>(builder.Configuration.GetSection(AccelistSsoOptions.SectionName));
builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("HANYA.KNOW/1.0");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
});
builder.Services.AddHttpClient("GitHubOAuth", client =>
{
    client.BaseAddress = new Uri("https://github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("HANYA.KNOW/1.0");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CombinedScheme;
        options.DefaultChallengeScheme = CombinedScheme;
    })
    .AddPolicyScheme(CombinedScheme, CombinedScheme, options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey("Authorization")
                ? JwtBearerDefaults.AuthenticationScheme
                : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "HanyaKnow.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? string.Empty);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
    });
builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
        else
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var provider = scope.ServiceProvider;
    var embeddings = provider.GetRequiredService<EmbeddingClient>();
    try
    {
        await embeddings.TestConnectionAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to connect to embedding service on startup");
    }

    var uiStore = provider.GetRequiredService<UiStore>();
    try
    {
        await uiStore.EnsureDefaultPagesAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to initialize default UI access mappings");
    }
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.UseRequestInterceptor("(req) => { req.credentials = 'include'; return req; }");
});
app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapPost("/api/auth/sso-login", async (
    SsoLoginRequest request,
    IOptions<AccelistSsoOptions> ssoOptions,
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptionsMonitor,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.IdToken))
    {
        return Results.BadRequest(new { message = "ID token is required." });
    }

    var ssoConfig = ssoOptions.Value;
    var host = ssoConfig.Host?.TrimEnd('/') ?? string.Empty;
    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(ssoConfig.AppId))
    {
        logger.LogError("Accelist SSO configuration is incomplete. Host: {Host}, AppId: {AppId}", host, ssoConfig.AppId);
        return Results.Problem("SSO configuration is incomplete.", statusCode: StatusCodes.Status500InternalServerError);
    }

    var metadataAddress = $"{host}/.well-known/openid-configuration";
    var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
        metadataAddress,
        new OpenIdConnectConfigurationRetriever());

    OpenIdConnectConfiguration discoveryDocument;
    try
    {
        discoveryDocument = await configManager.GetConfigurationAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to download OIDC metadata from {MetadataAddress}", metadataAddress);
        return Results.Problem("Failed to reach SSO provider.", statusCode: StatusCodes.Status502BadGateway);
    }

    var validationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = host,
        ValidateAudience = true,
        ValidAudience = ssoConfig.AppId,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = discoveryDocument.SigningKeys,
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    try
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(request.IdToken, validationParameters, out _);

        var claimsIdentity = new ClaimsIdentity(principal.Claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var cookieOptions = cookieOptionsMonitor.Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(cookieOptions.ExpireTimeSpan)
        };

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        return Results.Ok(new { message = "Authentication successful." });
    }
    catch (SecurityTokenException ex)
    {
        logger.LogWarning(ex, "Invalid SSO token received");
        return Results.Json(
            new { message = "Invalid token.", details = ex.Message },
            statusCode: StatusCodes.Status401Unauthorized);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error while validating SSO token");
        return Results.Problem("An unexpected error occurred during login.", statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/user/profile", (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        Email = user.FindFirstValue(ClaimTypes.Email),
        Name = user.FindFirstValue(ClaimTypes.Name)
    });
}).RequireAuthorization();

app.MapControllers();
app.Run();

public record SsoLoginRequest(string IdToken);
