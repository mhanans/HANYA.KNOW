using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("ConnectionStrings"));
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection("Embedding"));
builder.Services.AddSingleton<EmbeddingClient>();
builder.Services.AddSingleton<VectorStore>();
builder.Services.AddSingleton<CategoryStore>();
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.AddHttpClient<LlmClient>();
builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection("Chat"));
builder.Services.AddMemoryCache();

// Add CORS policy using origins from configuration
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (allowedOrigins.Length == 0)
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        else
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// Test embedding service connectivity at startup
using (var scope = app.Services.CreateScope())
{
    var embeddings = scope.ServiceProvider.GetRequiredService<EmbeddingClient>();
    try
    {
        await embeddings.TestConnectionAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to connect to embedding service on startup");
    }
}

app.UseSwagger();
app.UseSwaggerUI();
// Use the CORS policy
app.UseCors("AllowFrontend");
app.MapControllers();
app.Run();
