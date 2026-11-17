using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Enums;
using Zelo.Api.Domain.Services;
using Zelo.Api.Infrastructure.Data;
using Zelo.Api.Infrastructure.Security;

namespace Zelo.Api.Infrastructure.Background;

public interface IMonitorAlertClient
{
    Task NotificarAlertaR5Async(string casaId, string textoLembrete, string horario, CancellationToken cancellationToken = default);
}

public class MonitorAlertHttpClient(
    IHttpClientFactory httpClientFactory,
    JwtTokenService jwtTokenService,
    IConfiguration configuration,
    ILogger<MonitorAlertHttpClient> logger) : IMonitorAlertClient
{
    public async Task NotificarAlertaR5Async(string casaId, string textoLembrete, string horario, CancellationToken cancellationToken = default)
    {
        var baseUrl = Environment.GetEnvironmentVariable("MONITOR_URL")
                      ?? configuration["MonitorService:BaseUrl"]
                      ?? "http://localhost:8080";

        var url = $"{baseUrl.TrimEnd('/')}/api/v1/alertas";
        var (serviceToken, _) = jwtTokenService.GenerateServiceToken();

        var payload = new
        {
            casaId,
            regra = "R5",
            gravidade = "ATENCAO",
            origem = "LEMBRETE",
            contexto = $"Lembrete '{textoLembrete}' das {horario} não confirmado após 60 min"
        };

        try
        {
            var client = httpClientFactory.CreateClient("MonitorAlertClient");
            client.Timeout = TimeSpan.FromSeconds(3);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

            using var response = await client.SendAsync(request, cancellationToken);
            logger.LogInformation(
                "Alerta R5 enviado ao zelo-monitor para casa {CasaId} (status HTTP {StatusCode}).",
                casaId,
                (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Não foi possível contatar zelo-monitor em {Url} para abrir alerta R5 da casa {CasaId}: {Message}",
                url,
                casaId,
                ex.Message);
        }
    }
}

public class LembreteExpirationProcessor(
    ZeloDbContext db,
    IClock clock,
    IMonitorAlertClient monitorAlertClient,
    ILogger<LembreteExpirationProcessor> logger)
{
    public async Task<int> ProcessarExpiradosAsync(CancellationToken cancellationToken = default)
    {
        var agora = clock.UtcNow;

        var pendentes = await db.LembreteRegistros
            .Include(r => r.Lembrete)
                .ThenInclude(l => l!.Idoso)
                    .ThenInclude(i => i!.Casas)
            .Include(r => r.Lembrete)
                .ThenInclude(l => l!.Idoso)
                    .ThenInclude(i => i!.Consentimentos)
            .Where(r => r.Status == StatusLembreteRegistro.Pendente)
            .ToListAsync(cancellationToken);

        int marcados = 0;

        foreach (var registro in pendentes)
        {
            if (!ZeloBusinessRules.DeveMarcarComoNaoConfirmado(registro, agora))
            {
                continue;
            }

            registro.Status = StatusLembreteRegistro.NaoConfirmado;
            marcados++;

            var idoso = registro.Lembrete?.Idoso;
            var consentimentoAtivo = idoso is not null && ZeloBusinessRules.PossuiConsentimentoAtivo(idoso.Consentimentos);
            var casaId = idoso?.Casas.FirstOrDefault()?.Id;

            if (consentimentoAtivo && !string.IsNullOrWhiteSpace(casaId) && registro.Lembrete is not null)
            {
                await monitorAlertClient.NotificarAlertaR5Async(
                    casaId,
                    registro.Lembrete.Texto,
                    registro.Lembrete.Horario,
                    cancellationToken);
            }
        }

        if (marcados > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Count} registro(s) de lembrete marcados como NAO_CONFIRMADO.", marcados);
        }

        return marcados;
    }
}

/// <summary>
/// Serviço em segundo plano que verifica periodicamente lembretes PENDENTE há mais de 60 minutos
/// e marca como NAO_CONFIRMADO (Regra R5).
/// </summary>
public class LembreteExpirationWorker(
    IServiceProvider serviceProvider,
    ILogger<LembreteExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<LembreteExpirationProcessor>();
                await processor.ProcessarExpiradosAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao processar expiração de lembretes em segundo plano.");
            }
        }
    }
}
