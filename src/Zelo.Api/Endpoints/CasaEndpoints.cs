using Microsoft.EntityFrameworkCore;
using Zelo.Api.Contracts;
using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Services;
using Zelo.Api.Infrastructure.Data;

namespace Zelo.Api.Endpoints;

public static class CasaEndpoints
{
    public static RouteGroupBuilder MapCasaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/casas")
            .WithTags("Casas, Sensores e Rede de Apoio (Java)")
            .RequireAuthorization();

        group.MapGet("/", async (ZeloDbContext db) =>
        {
            var casas = await db.Casas
                .Include(c => c.Sensores)
                .ToListAsync();

            var response = casas.Select(MapCasaResponse).ToList();
            return Results.Ok(response);
        })
        .WithName("ListarCasas")
        .WithSummary("Lista todas as casas monitoradas com seus sensores.")
        .Produces<IReadOnlyList<CasaResponse>>(StatusCodes.Status200OK)
        .WithOpenApi();

        group.MapGet("/{id}", async (string id, ZeloDbContext db) =>
        {
            var casa = await db.Casas
                .Include(c => c.Sensores)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (casa is null)
            {
                return Results.NotFound(new ErroResponse("CASA_NAO_ENCONTRADA", $"Casa '{id}' não encontrada."));
            }

            return Results.Ok(MapCasaResponse(casa));
        })
        .WithName("ObterCasaPorId")
        .WithSummary("Obtém os detalhes de uma casa e seus sensores.")
        .Produces<CasaResponse>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        group.MapPost("/", async (CriarCasaRequest request, ZeloDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.IdosoId) ||
                string.IsNullOrWhiteSpace(request.Endereco) ||
                string.IsNullOrWhiteSpace(request.Bairro) ||
                string.IsNullOrWhiteSpace(request.Cidade))
            {
                return Results.BadRequest(new ErroResponse(
                    "DADOS_CASA_INVALIDOS",
                    "Os campos idosoId, endereco, bairro e cidade são obrigatórios."));
            }

            if (!await db.Idosos.AnyAsync(i => i.Id == request.IdosoId))
            {
                return Results.NotFound(new ErroResponse(
                    "IDOSO_NAO_ENCONTRADO",
                    $"Idoso '{request.IdosoId}' não encontrado."));
            }

            var casa = new Casa
            {
                Id = Guid.NewGuid().ToString(),
                IdosoId = request.IdosoId,
                Endereco = request.Endereco.Trim(),
                Bairro = request.Bairro.Trim(),
                Cidade = request.Cidade.Trim()
            };

            db.Casas.Add(casa);
            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/casas/{casa.Id}", MapCasaResponse(casa));
        })
        .WithName("CriarCasa")
        .WithSummary("Cadastra uma nova casa vinculada a um idoso.")
        .Produces<CasaResponse>(StatusCodes.Status201Created)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        group.MapPut("/{id}", async (string id, AtualizarCasaRequest request, ZeloDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Endereco) ||
                string.IsNullOrWhiteSpace(request.Bairro) ||
                string.IsNullOrWhiteSpace(request.Cidade))
            {
                return Results.BadRequest(new ErroResponse(
                    "DADOS_CASA_INVALIDOS",
                    "Os campos endereco, bairro e cidade são obrigatórios."));
            }

            var casa = await db.Casas
                .Include(c => c.Sensores)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (casa is null)
            {
                return Results.NotFound(new ErroResponse("CASA_NAO_ENCONTRADA", $"Casa '{id}' não encontrada."));
            }

            casa.Endereco = request.Endereco.Trim();
            casa.Bairro = request.Bairro.Trim();
            casa.Cidade = request.Cidade.Trim();
            await db.SaveChangesAsync();

            return Results.Ok(MapCasaResponse(casa));
        })
        .WithName("AtualizarCasa")
        .WithSummary("Atualiza o endereço de uma casa.")
        .Produces<CasaResponse>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        group.MapDelete("/{id}", async (string id, ZeloDbContext db) =>
        {
            var casa = await db.Casas.FirstOrDefaultAsync(c => c.Id == id);
            if (casa is null)
            {
                return Results.NotFound(new ErroResponse("CASA_NAO_ENCONTRADA", $"Casa '{id}' não encontrada."));
            }

            db.Casas.Remove(casa);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("RemoverCasa")
        .WithSummary("Remove uma casa pelo ID.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        group.MapGet("/{id}/sensores", async (string id, ZeloDbContext db) =>
        {
            if (!await db.Casas.AnyAsync(c => c.Id == id))
            {
                return Results.NotFound(new ErroResponse("CASA_NAO_ENCONTRADA", $"Casa '{id}' não encontrada."));
            }

            var sensores = await db.Sensores
                .Where(s => s.CasaId == id)
                .Select(s => new SensorDto(s.Id, s.CasaId, s.Comodo, s.TipoSensor, s.Ativo))
                .ToListAsync();

            return Results.Ok(sensores);
        })
        .WithName("ListarSensoresDaCasa")
        .WithSummary("Lista os sensores instalados na casa.")
        .Produces<IReadOnlyList<SensorDto>>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        group.MapPost("/{id}/sensores", async (string id, CriarSensorRequest request, ZeloDbContext db) =>
        {
            var casa = await db.Casas
                .Include(c => c.Sensores)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (casa is null)
            {
                return Results.NotFound(new ErroResponse("CASA_NAO_ENCONTRADA", $"Casa '{id}' não encontrada."));
            }

            var comodo = request.Comodo?.Trim().ToUpperInvariant() ?? string.Empty;
            var tipo = request.TipoSensor?.Trim().ToUpperInvariant() ?? string.Empty;

            var validacao = ZeloBusinessRules.ValidarNovoSensor(casa.Sensores, comodo, tipo);
            if (!validacao.IsValid)
            {
                return Results.Json(
                    new ErroResponse(validacao.ErrorCode!, validacao.ErrorMessage!),
                    statusCode: validacao.HttpStatus);
            }

            var sensor = new Sensor
            {
                Id = Guid.NewGuid().ToString(),
                CasaId = id,
                Comodo = comodo,
                TipoSensor = tipo,
                Ativo = request.Ativo
            };

            db.Sensores.Add(sensor);
            await db.SaveChangesAsync();

            var dto = new SensorDto(sensor.Id, sensor.CasaId, sensor.Comodo, sensor.TipoSensor, sensor.Ativo);
            return Results.Created($"/api/v1/casas/{id}/sensores/{sensor.Id}", dto);
        })
        .WithName("CriarSensorNaCasa")
        .WithSummary("Instala um novo sensor na casa (Regra: apenas 1 sensor de cada cômodo por casa).")
        .Produces<SensorDto>(StatusCodes.Status201Created)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .Produces<ErroResponse>(StatusCodes.Status409Conflict)
        .WithOpenApi();

        // Endpoint crítico para o Java (zelo-monitor): GET /api/v1/casas/{id}/rede-apoio
        group.MapGet("/{id}/rede-apoio", async (string id, ZeloDbContext db) =>
        {
            var casa = await db.Casas
                .Include(c => c.Idoso)
                    .ThenInclude(i => i!.Usuario)
                        .ThenInclude(u => u!.Dispositivos)
                .Include(c => c.Idoso)
                    .ThenInclude(i => i!.Consentimentos)
                .Include(c => c.Idoso)
                    .ThenInclude(i => i!.ContatosApoio)
                        .ThenInclude(ca => ca.Usuario)
                            .ThenInclude(u => u!.Dispositivos)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (casa is null || casa.Idoso is null)
            {
                return Results.NotFound(new ErroResponse("CASA_NAO_ENCONTRADA", $"Casa '{id}' não encontrada."));
            }

            var idoso = casa.Idoso;
            var consentimentoAtivo = ZeloBusinessRules.PossuiConsentimentoAtivo(idoso.Consentimentos);

            var idosoDto = new IdosoRedeApoioDto(
                idoso.Id,
                idoso.UsuarioId,
                idoso.Nome,
                idoso.Usuario?.Telefone ?? string.Empty,
                idoso.Usuario?.Dispositivos.Select(d => d.PushToken).ToList() ?? []);

            var contatosOrdenados = idoso.ContatosApoio
                .OrderBy(c => c.Ordem)
                .Select(c => new ContatoApoioDto(
                    c.Id,
                    c.IdosoId,
                    c.UsuarioId,
                    c.Usuario?.Nome ?? string.Empty,
                    c.Papel,
                    c.Ordem,
                    c.Usuario?.Telefone ?? string.Empty,
                    c.Usuario?.Dispositivos.Select(d => d.PushToken).ToList() ?? []))
                .ToList();

            var response = new RedeApoioCasaResponse(
                casa.Id,
                consentimentoAtivo,
                idosoDto,
                contatosOrdenados);

            return Results.Ok(response);
        })
        .WithName("ObterRedeApoioDaCasa")
        .WithSummary("Retorna a rede de apoio ordenada, pushTokens e consentimentoAtivo da casa (consumido pelo zelo-monitor em Java).")
        .Produces<RedeApoioCasaResponse>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        return group;
    }

    private static CasaResponse MapCasaResponse(Casa casa) =>
        new(
            casa.Id,
            casa.IdosoId,
            casa.Endereco,
            casa.Bairro,
            casa.Cidade,
            casa.Sensores.Select(s => new SensorDto(s.Id, s.CasaId, s.Comodo, s.TipoSensor, s.Ativo)).ToList());
}
