using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Zelo.Api.Contracts;
using Zelo.Api.Domain.Enums;
using Zelo.Api.Domain.Services;
using Zelo.Api.Endpoints;
using Zelo.Api.Infrastructure.Background;
using Zelo.Api.Infrastructure.Data;
using Zelo.Api.Infrastructure.Health;
using Zelo.Api.Infrastructure.Idempotency;
using Zelo.Api.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// ==============================================================================
// 1. Relógio (Sistema ou Simulado para Demo MODO_SIMULACAO=true) e Serviços
// ==============================================================================
builder.Services.AddSingleton<IClock, SystemOrSimulatedClock>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IMonitorAlertClient, MonitorAlertHttpClient>();
builder.Services.AddScoped<LembreteExpirationProcessor>();
builder.Services.AddHostedService<LembreteExpirationWorker>();

// ==============================================================================
// 2. Banco de Dados (EF Core: SqlServer no Azure, Sqlite local ou InMemory em Testes)
// ==============================================================================
var dbProvider = Environment.GetEnvironmentVariable("DB_PROVIDER")
                 ?? builder.Configuration["Database:Provider"]
                 ?? "Sqlite";

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                       ?? builder.Configuration.GetConnectionString("Default");

builder.Services.AddDbContext<ZeloDbContext>(options =>
{
    if (string.Equals(dbProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
    {
        var dbName = string.IsNullOrWhiteSpace(connectionString) ? "ZeloInMemoryDb" : connectionString;
        options.UseInMemoryDatabase(dbName);
    }
    else if (string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        var sqliteConn = string.IsNullOrWhiteSpace(connectionString)
            ? "Data Source=zelo_dev.db"
            : connectionString;
        options.UseSqlite(sqliteConn);
    }
});

// ==============================================================================
// 3. Autenticação e Autorização com JWT HS256 (Claims: sub e papel)
// ==============================================================================
var tempJwtService = new JwtTokenService(builder.Configuration);
var signingKeyBytes = Encoding.UTF8.GetBytes(tempJwtService.GetSigningKeyString());

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            ValidateIssuer = false,
            ValidateAudience = false,
            NameClaimType = "sub",
            RoleClaimType = "papel",
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                var payload = JsonSerializer.Serialize(new ErroResponse(
                    "NAO_AUTENTICADO",
                    "Token JWT ausente, inválido ou expirado."));
                await context.Response.WriteAsync(payload);
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                var payload = JsonSerializer.Serialize(new ErroResponse(
                    "ACESSO_NEGADO",
                    "Você não possui permissão (papel insuficiente) para acessar este recurso."));
                await context.Response.WriteAsync(payload);
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireClaim("papel", PapeisUsuario.Admin))
    .AddPolicy("ServicoOrAdmin", policy => policy.RequireClaim("papel", PapeisUsuario.Servico, PapeisUsuario.Admin))
    .AddPolicy("IdosoOnly", policy => policy.RequireClaim("papel", PapeisUsuario.Idoso));

// ==============================================================================
// 4. Rate Limiting com Microsoft.AspNetCore.RateLimiting (HTTP 429 + Retry-After)
// ==============================================================================
var generalPermitLimit = builder.Configuration.GetValue("RateLimiting:GeneralPermitLimit", 60);
var generalWindowSeconds = builder.Configuration.GetValue("RateLimiting:GeneralWindowSeconds", 60);
var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
var authWindowSeconds = builder.Configuration.GetValue("RateLimiting:AuthWindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : authWindowSeconds;

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        context.HttpContext.Response.ContentType = "application/json";

        var erro = JsonSerializer.Serialize(new ErroResponse(
            "TOO_MANY_REQUESTS",
            $"Limite de requisições excedido. Tente novamente após {retryAfterSeconds} segundos."));

        await context.HttpContext.Response.WriteAsync(erro, cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "global";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"global:{partitionKey}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = generalPermitLimit,
                Window = TimeSpan.FromSeconds(generalWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    options.AddPolicy("auth", httpContext =>
    {
        var cfg = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var limit = cfg.GetValue("RateLimiting:AuthPermitLimit", authPermitLimit);
        var windowSec = cfg.GetValue("RateLimiting:AuthWindowSeconds", authWindowSeconds);

        var clientKey = httpContext.Request.Headers["X-Client-Id"].FirstOrDefault()
                        ?? httpContext.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"auth:{clientKey}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromSeconds(windowSec),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

// ==============================================================================
// 5. Health Checks (API, Banco de Dados EF Core e Serviço Externo zelo-monitor)
// ==============================================================================
builder.Services.AddHealthChecks()
    .AddCheck("api", () => HealthCheckResult.Healthy("API zelo-cuidado (.NET 8) online."), tags: ["self"])
    .AddDbContextCheck<ZeloDbContext>("database", tags: ["db"])
    .AddCheck<MonitorExternalHealthCheck>("zelo-monitor", tags: ["external"]);

// ==============================================================================
// 6. OpenAPI / Swagger com Suporte a JWT Bearer e Header Idempotency-Key
// ==============================================================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Zelo API (.NET 8) — zelo-cuidado",
        Version = "v1",
        Description = "API principal do projeto Zelo (Global Solution 2026/2 FIAP - CitizenTech): autenticação JWT, idosos, consentimento LGPD, casas, sensores, rede de apoio e lembretes de remédio com idempotência, rate limiting e health checks."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Informe o token JWT HS256 emitido em /api/v1/auth/login."
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

var app = builder.Build();

// ==============================================================================
// 7. Inicialização do Banco e Seed com Dados Fixos (Dona Maria, Ana, Seu José)
// ==============================================================================
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ZeloDbContext>();
    var clock = scope.ServiceProvider.GetRequiredService<IClock>();
    await DbSeeder.EnsureSeededAsync(db, clock);
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Zelo API v1");
    c.RoutePrefix = "swagger";
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

// ==============================================================================
// 8. Mapeamento dos Endpoints Minimal APIs
// ==============================================================================
app.MapGet("/", () => Results.Ok(new
{
    servico = "zelo-api-dotnet",
    versao = "1.0.0",
    tema = "GS 2026/2 CitizenTech - Zelo (Cuidado que não vigia)",
    swagger = "/swagger",
    health = "/api/v1/health"
}))
.WithTags("Status")
.WithOpenApi();

var healthOptions = new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            verificadoEm = DateTime.UtcNow,
            duracaoTotalMs = report.TotalDuration.TotalMilliseconds,
            componentes = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    descricao = e.Value.Description,
                    duracaoMs = e.Value.Duration.TotalMilliseconds
                })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
};

app.MapHealthChecks("/health", healthOptions).WithTags("Health Checks");
app.MapHealthChecks("/api/v1/health", healthOptions).WithTags("Health Checks");

app.MapAuthEndpoints();
app.MapIdosoEndpoints();
app.MapCasaEndpoints();
app.MapRedeApoioEndpoints();
app.MapLembreteEndpoints();

app.Run();

public partial class Program { }
