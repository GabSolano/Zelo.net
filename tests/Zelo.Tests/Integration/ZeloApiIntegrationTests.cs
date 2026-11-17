using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zelo.Api.Contracts;
using Zelo.Api.Infrastructure.Data;

namespace Zelo.Tests.Integration;

public class CustomZeloWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"ZeloTestDb_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "InMemory",
                ["ConnectionStrings:Default"] = _dbName,
                ["RateLimiting:AuthPermitLimit"] = "4",
                ["RateLimiting:AuthWindowSeconds"] = "60",
                ["RateLimiting:GeneralPermitLimit"] = "200",
                ["RateLimiting:GeneralWindowSeconds"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ZeloDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ZeloDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, StreamBodyStartupFilter>();
        });
    }

    private sealed class StreamBodyStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (ctx, nextMiddleware) =>
                {
                    var original = ctx.Response.Body;
                    using var buffer = new MemoryStream();
                    ctx.Response.Body = buffer;
                    try
                    {
                        await nextMiddleware();
                        buffer.Position = 0;
                        await buffer.CopyToAsync(original);
                    }
                    finally
                    {
                        ctx.Response.Body = original;
                    }
                });
                next(app);
            };
    }
}

public class ZeloApiIntegrationTests
{
    private static async Task<string> AuthenticateAsync(HttpClient client, string email, string senha = ZeloSeedConstants.SenhaPadraoTeste)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, senha));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));
        return auth.Token;
    }

    [Fact]
    public async Task Login_ComDadosFixosDonaMaria_DeveRetornar200ETokenJwtValido()
    {
        await using var factory = new CustomZeloWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("maria@zelo.teste", ZeloSeedConstants.SenhaPadraoTeste));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.Equal(ZeloSeedConstants.UsuarioDonaMariaId, body!.Usuario.Id);
        Assert.Equal("IDOSO", body.Usuario.Papel);
        Assert.Equal(ZeloSeedConstants.IdosoDonaMariaId, body.Usuario.IdosoId);
    }

    [Fact]
    public async Task EndpointProtegido_SemTokenJwt_DeveRetornar401Unauthorized()
    {
        await using var factory = new CustomZeloWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/casas/{ZeloSeedConstants.CasaDonaMariaId}/rede-apoio");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RedeApoio_CasaDonaMaria_DeveRetornarContratoExatoParaOJava()
    {
        await using var factory = new CustomZeloWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AuthenticateAsync(client, "servico@zelo.teste");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/casas/{ZeloSeedConstants.CasaDonaMariaId}/rede-apoio");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rede = await response.Content.ReadFromJsonAsync<RedeApoioCasaResponse>();
        Assert.NotNull(rede);
        Assert.Equal(ZeloSeedConstants.CasaDonaMariaId, rede!.CasaId);
        Assert.True(rede.ConsentimentoAtivo);
        Assert.Equal(ZeloSeedConstants.IdosoDonaMariaId, rede.Idoso.IdosoId);
        Assert.Equal("Dona Maria", rede.Idoso.Nome);
        Assert.Equal(2, rede.Contatos.Count);
        Assert.Equal("Ana (Filha)", rede.Contatos[0].Nome);
        Assert.Equal("FAMILIAR", rede.Contatos[0].Papel);
        Assert.Equal(1, rede.Contatos[0].Ordem);
        Assert.Equal("Seu José (Vizinho)", rede.Contatos[1].Nome);
        Assert.Equal("GUARDIAO", rede.Contatos[1].Papel);
        Assert.Equal(2, rede.Contatos[1].Ordem);
    }

    [Fact]
    public async Task Idempotencia_MesmaChaveEMesmoCorpo_NaoDuplicaLembrete_ECorpoDiferenteRetorna409()
    {
        await using var factory = new CustomZeloWebApplicationFactory();
        using var client = factory.CreateClient();

        var tokenAna = await AuthenticateAsync(client, "ana@zelo.teste");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenAna);

        var idempotencyKey = $"idem-test-{Guid.NewGuid():N}";
        var url = $"/api/v1/idosos/{ZeloSeedConstants.IdosoDonaMariaId}/lembretes";

        // 1ª chamada com a chave
        using var req1 = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new CriarLembreteRequest("Colírio dos olhos", "14:30"))
        };
        req1.Headers.Add("Idempotency-Key", idempotencyKey);
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
        var lembrete1 = await resp1.Content.ReadFromJsonAsync<LembreteResponse>();

        // 2ª chamada repetida com a MESMA chave e MESMO corpo -> devolve a mesma resposta sem duplicar
        using var req2 = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new CriarLembreteRequest("Colírio dos olhos", "14:30"))
        };
        req2.Headers.Add("Idempotency-Key", idempotencyKey);
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
        Assert.True(resp2.Headers.Contains("X-Idempotent-Replay"));
        var lembrete2 = await resp2.Content.ReadFromJsonAsync<LembreteResponse>();
        Assert.Equal(lembrete1!.Id, lembrete2!.Id);

        // 3ª chamada com a MESMA chave mas CORPO DIFERENTE -> devolve 409 Conflict
        using var req3 = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new CriarLembreteRequest("Outro lembrete diferente", "18:00"))
        };
        req3.Headers.Add("Idempotency-Key", idempotencyKey);
        var resp3 = await client.SendAsync(req3);
        Assert.Equal(HttpStatusCode.Conflict, resp3.StatusCode);
    }

    [Fact]
    public async Task FluxoLembretes_ConfirmacaoPeloIdosoEExpiracaoApos60Minutos()
    {
        await using var factory = new CustomZeloWebApplicationFactory();
        using var client = factory.CreateClient();

        var tokenMaria = await AuthenticateAsync(client, "maria@zelo.teste");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenMaria);

        // Ajusta o relógio simulado para 08:15 de hoje (lembrete das 08:00 ainda está PENDENTE)
        var hoje = DateTime.UtcNow.Date;
        await client.PostAsJsonAsync(
            "/api/v1/simulacao/relogio",
            new RelogioSimuladoRequest(hoje.AddHours(8).AddMinutes(15)));

        var hojeResp = await client.GetAsync($"/api/v1/idosos/{ZeloSeedConstants.IdosoDonaMariaId}/lembretes/hoje");
        Assert.Equal(HttpStatusCode.OK, hojeResp.StatusCode);
        var listaHoje = await hojeResp.Content.ReadFromJsonAsync<List<LembreteHojeDto>>();
        Assert.NotNull(listaHoje);
        Assert.Contains(listaHoje!, l => l.LembreteId == ZeloSeedConstants.LembretePressaoId && l.Status == "PENDENTE");

        // Avança o relógio simulado para 09:05 (>60 min após 08:00 sem confirmação)
        await client.PostAsJsonAsync(
            "/api/v1/simulacao/relogio",
            new RelogioSimuladoRequest(hoje.AddHours(9).AddMinutes(5)));

        // Consulta o endpoint consumido pelo Java: GET /api/v1/lembretes/nao-confirmados
        var naoConfResp = await client.GetAsync("/api/v1/lembretes/nao-confirmados");
        Assert.Equal(HttpStatusCode.OK, naoConfResp.StatusCode);
        var naoConfirmados = await naoConfResp.Content.ReadFromJsonAsync<List<LembreteNaoConfirmadoDto>>();
        Assert.NotNull(naoConfirmados);
        Assert.Contains(naoConfirmados!, l =>
            l.LembreteId == ZeloSeedConstants.LembretePressaoId &&
            l.Status == "NAO_CONFIRMADO" &&
            l.CasaId == ZeloSeedConstants.CasaDonaMariaId);
    }

    [Fact]
    public async Task RateLimiting_QuandoExcedeLimiteEmAuth_DeveRetornar429TooManyRequestsComRetryAfter()
    {
        await using var factory = new CustomZeloWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", "rate-limit-test-client");

        HttpResponseMessage? ultimaResposta = null;
        for (int i = 0; i < 12; i++)
        {
            ultimaResposta = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LoginRequest("maria@zelo.teste", ZeloSeedConstants.SenhaPadraoTeste));

            if (ultimaResposta.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.NotNull(ultimaResposta);
        Assert.Equal(HttpStatusCode.TooManyRequests, ultimaResposta!.StatusCode);
        Assert.True(ultimaResposta.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task HealthCheck_ESwagger_DevemResponder200OK()
    {
        await using var factory = new CustomZeloWebApplicationFactory();
        using var client = factory.CreateClient();

        var healthResp = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);

        var healthJson = await healthResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(healthJson);
        Assert.True(doc.RootElement.TryGetProperty("componentes", out var componentes));
        Assert.True(componentes.TryGetProperty("api", out _));
        Assert.True(componentes.TryGetProperty("database", out _));
        Assert.True(componentes.TryGetProperty("zelo-monitor", out _));

        var swaggerResp = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swaggerResp.StatusCode);
    }
}
