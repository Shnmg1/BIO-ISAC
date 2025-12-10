using MyApp.Namespace.Services;
using api.DataAccess;
using api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add memory cache for temporary 2FA secret storage
builder.Services.AddMemoryCache();

// Register Database class as singleton (connection info doesn't change)
builder.Services.AddSingleton<Database>();

// Register database service
builder.Services.AddScoped<DatabaseService>();

// Register auth service
builder.Services.AddScoped<api.Services.AuthService>();

// Register TOTP service for 2FA
builder.Services.AddScoped<ITotpService, TotpService>();

// Register file storage service for document uploads
builder.Services.AddScoped<IFileStorageService, FileStorageService>();

// Register Gemini document verification service
builder.Services.AddHttpClient<IDocumentVerificationService, GeminiDocumentVerificationService>();

// Register HttpClient for OTX service
builder.Services.AddHttpClient<OTXService>();

// Register HttpClient for NIST service
builder.Services.AddHttpClient<NISTService>();

// Register HttpClient for CISA service
builder.Services.AddHttpClient<CISAService>();

// Register AI service with HttpClient
builder.Services.AddHttpClient<api.Services.AIService>();

// Register API validation service
builder.Services.AddScoped<ApiValidationService>();

// Register threat ingestion services
builder.Services.AddScoped<ThreatNormalizationService>();
builder.Services.AddScoped<ThreatDeduplicationService>();
builder.Services.AddScoped<ApiSourceService>();
builder.Services.AddScoped<AuditLogService>();

// Register background service for threat ingestion and AI rating
builder.Services.AddSingleton<ThreatIngestionBackgroundService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ThreatIngestionBackgroundService>());

// Register VideoAsk service for user screening (singleton to persist in-memory data across requests)
builder.Services.AddSingleton<api.Services.VideoAskService>();

// Add CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrEmpty(origin) || origin == "null")
                return true;
            return origin.StartsWith("http://localhost") ||
                   origin.StartsWith("http://127.0.0.1");
        })
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseCors("AllowFrontend");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
