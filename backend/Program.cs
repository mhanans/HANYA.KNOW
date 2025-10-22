using backend.Services;
using backend.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http.Headers;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.Filters.Add(new AuthorizeFilter(policy));
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
                    Id = "ApiKey"
                },
                In = ParameterLocation.Header
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
                    Id = "Bearer"
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
builder.Services.AddSingleton<ProjectAssessmentAnalysisService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<KnowledgeBaseIngestionService>());
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.AddHttpClient<LlmClient>();
builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection("Chat"));
builder.Services.Configure<RecommendationOptions>(builder.Configuration.GetSection("Recommendation"));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ConversationStore>();
builder.Services.Configure<SourceCodeOptions>(builder.Configuration.GetSection("SourceCode"));
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.AddSingleton<GitHubTokenStore>();
builder.Services.AddSingleton<GitHubIntegrationService>();
builder.Services.Configure<AccelistSsoOptions>(builder.Configuration.GetSection("AccelistSso"));
builder.Services.AddHttpClient("AccelistSso", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<AccelistSsoOptions>>().Value;
    var host = (options.Host ?? string.Empty).TrimEnd('/');
    if (!string.IsNullOrEmpty(host))
    {
        client.BaseAddress = new Uri(host + "/");
    }
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});
builder.Services.AddSingleton<AccelistSsoAuthenticator>();
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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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

// Add CORS policy using origins from configuration
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

// Test embedding service connectivity at startup
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
    // Allow Swagger "try it out" requests to include cookies so
    // authenticated endpoints can be tested from the UI.
    options.UseRequestInterceptor("(req) => { req.credentials = 'include'; return req; }");
});
// Use the CORS policy
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();
app.Run();
