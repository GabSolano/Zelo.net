using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Zelo.Api.Infrastructure.Health;

/// <summary>
/// Health Check do serviço externo utilizado pela API (zelo-monitor em Java).
/// </summary>
public class MonitorExternalHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = Environment.GetEnvironmentVariable("MONITOR_URL")
                      ?? configuration["MonitorService:BaseUrl"]
                      ?? "http://localhost:8080";

        var healthUrl = $"{baseUrl.TrimEnd('/')}/actuator/health";

        try
        {
            var client = httpClientFactory.CreateClient("MonitorHealthClient");
            client.Timeout = TimeSpan.FromSeconds(2);

            using var response = await client.GetAsync(healthUrl, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy($"Serviço externo zelo-monitor acessível em {healthUrl}.");
            }

            return HealthCheckResult.Degraded(
                $"Serviço externo zelo-monitor respondeu HTTP {(int)response.StatusCode} em {healthUrl}.");
        }
        catch (Exception ex)
        {
            // Retorna Degraded quando o serviço Java externo está desligado para não derrubar o container da API .NET no Azure
            return HealthCheckResult.Degraded(
                $"Serviço externo zelo-monitor offline em {healthUrl} ({ex.GetType().Name}). API .NET operando normalmente.");
        }
    }
}
