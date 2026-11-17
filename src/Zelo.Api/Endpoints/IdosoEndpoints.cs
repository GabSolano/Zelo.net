using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Zelo.Api.Contracts;
using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Enums;
using Zelo.Api.Domain.Services;
using Zelo.Api.Infrastructure.Data;
using Zelo.Api.Infrastructure.Security;

namespace Zelo.Api.Endpoints;

public static class IdosoEndpoints
{
    public static RouteGroupBuilder MapIdosoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/idosos")
            .WithTags("Idosos e Consentimento")
            .RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, ZeloDbContext db) =>
        {
            var usuarioId = user.GetUsuarioId();
            var papel = user.GetPapel();

            IQueryable<Idoso> query = db.Idosos.Include(i => i.Consentimentos);

            if (papel == PapeisUsuario.Idoso)
            {
                query = query.Where(i => i.UsuarioId == usuarioId);
            }
            else if (papel == PapeisUsuario.Familiar || papel == PapeisUsuario.Guardiao)
            {
                query = query.Where(i => i.ContatosApoio.Any(c => c.UsuarioId == usuarioId));
            }

            var idosos = await query.ToListAsync();
            var result = idosos.Select(i => new IdosoResponse(
                i.Id,
                i.UsuarioId,
                i.Nome,
                i.DataNascimento,
                i.Observacoes,
                ZeloBusinessRules.PossuiConsentimentoAtivo(i.Consentimentos))).ToList();

            return Results.Ok(result);
        })
        .WithName("ListarIdosos")
        .WithSummary("Lista os idosos visíveis conforme o papel do usuário autenticado.")
        .Produces<IReadOnlyList<IdosoResponse>>(StatusCodes.Status200OK)
        .WithOpenApi();

        group.MapGet("/{id}", async (string id, ZeloDbContext db) =>
        {
            var idoso = await db.Idosos
                .Include(i => i.Consentimentos)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (idoso is null)
            {
                return Results.NotFound(new ErroResponse("IDOSO_NAO_ENCONTRADO", $"Idoso '{id}' não encontrado."));
            }

            return Results.Ok(new IdosoResponse(
                idoso.Id,
                idoso.UsuarioId,
                idoso.Nome,
                idoso.DataNascimento,
                idoso.Observacoes,
                ZeloBusinessRules.PossuiConsentimentoAtivo(idoso.Consentimentos)));
        })
        .WithName("ObterIdosoPorId")
        .WithSummary("Busca os dados de um idoso pelo ID.")
        .Produces<IdosoResponse>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        group.MapPost("/", async (
            CriarIdosoRequest request,
            IClock clock,
            ZeloDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.UsuarioId) ||
                string.IsNullOrWhiteSpace(request.Nome) ||
                string.IsNullOrWhiteSpace(request.DataNascimento))
            {
                return Results.BadRequest(new ErroResponse(
                    "DADOS_IDOSO_INVALIDOS",
                    "Os campos usuarioId, nome e dataNascimento são obrigatórios."));
            }

            var usuarioExiste = await db.Usuarios.AnyAsync(u => u.Id == request.UsuarioId);
            if (!usuarioExiste)
            {
                return Results.NotFound(new ErroResponse(
                    "USUARIO_NAO_ENCONTRADO",
                    $"Usuário '{request.UsuarioId}' não encontrado para vínculo do idoso."));
            }

            var idoso = new Idoso
            {
                Id = Guid.NewGuid().ToString(),
                UsuarioId = request.UsuarioId,
                Nome = request.Nome.Trim(),
                DataNascimento = request.DataNascimento.Trim(),
                Observacoes = request.Observacoes?.Trim()
            };

            db.Idosos.Add(idoso);

            if (request.AceitarConsentimentoInicial)
            {
                db.Consentimentos.Add(new Consentimento
                {
                    Id = Guid.NewGuid().ToString(),
                    IdosoId = idoso.Id,
                    VersaoTermo = "1.0",
                    AceitoEm = clock.UtcNow,
                    RevogadoEm = null
                });
            }

            await db.SaveChangesAsync();

            var response = new IdosoResponse(
                idoso.Id,
                idoso.UsuarioId,
                idoso.Nome,
                idoso.DataNascimento,
                idoso.Observacoes,
                request.AceitarConsentimentoInicial);

            return Results.Created($"/api/v1/idosos/{idoso.Id}", response);
        })
        .WithName("CriarIdoso")
        .WithSummary("Cadastra um novo idoso monitorado.")
        .Produces<IdosoResponse>(StatusCodes.Status201Created)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        group.MapPut("/{id}", async (
            string id,
            AtualizarIdosoRequest request,
            ZeloDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Nome) || string.IsNullOrWhiteSpace(request.DataNascimento))
            {
                return Results.BadRequest(new ErroResponse(
                    "DADOS_IDOSO_INVALIDOS",
                    "Nome e dataNascimento são obrigatórios."));
            }

            var idoso = await db.Idosos
                .Include(i => i.Consentimentos)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (idoso is null)
            {
                return Results.NotFound(new ErroResponse("IDOSO_NAO_ENCONTRADO", $"Idoso '{id}' não encontrado."));
            }

            idoso.Nome = request.Nome.Trim();
            idoso.DataNascimento = request.DataNascimento.Trim();
            idoso.Observacoes = request.Observacoes?.Trim();

            await db.SaveChangesAsync();

            return Results.Ok(new IdosoResponse(
                idoso.Id,
                idoso.UsuarioId,
                idoso.Nome,
                idoso.DataNascimento,
                idoso.Observacoes,
                ZeloBusinessRules.PossuiConsentimentoAtivo(idoso.Consentimentos)));
        })
        .WithName("AtualizarIdoso")
        .WithSummary("Atualiza o cadastro de um idoso.")
        .Produces<IdosoResponse>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        group.MapDelete("/{id}", async (string id, ZeloDbContext db) =>
        {
            var idoso = await db.Idosos.FirstOrDefaultAsync(i => i.Id == id);
            if (idoso is null)
            {
                return Results.NotFound(new ErroResponse("IDOSO_NAO_ENCONTRADO", $"Idoso '{id}' não encontrado."));
            }

            db.Idosos.Remove(idoso);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("RemoverIdoso")
        .WithSummary("Remove o cadastro de um idoso.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        group.MapGet("/{id}/consentimentos", async (string id, ZeloDbContext db) =>
        {
            var idoso = await db.Idosos
                .Include(i => i.Consentimentos)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (idoso is null)
            {
                return Results.NotFound(new ErroResponse("IDOSO_NAO_ENCONTRADO", $"Idoso '{id}' não encontrado."));
            }

            var ativo = ZeloBusinessRules.PossuiConsentimentoAtivo(idoso.Consentimentos);
            var historico = idoso.Consentimentos
                .OrderByDescending(c => c.AceitoEm)
                .Select(c => new ConsentimentoResponse(
                    c.Id,
                    c.IdosoId,
                    c.VersaoTermo,
                    c.AceitoEm,
                    c.RevogadoEm,
                    c.RevogadoEm == null))
                .ToList();

            return Results.Ok(new
            {
                idosoId = id,
                consentimentoAtivo = ativo,
                consentimentos = historico
            });
        })
        .WithName("ListarConsentimentos")
        .WithSummary("Consulta o status atual e histórico de consentimento LGPD do idoso.")
        .WithOpenApi();

        group.MapPost("/{id}/consentimentos", async (
            string id,
            RegistrarConsentimentoRequest request,
            ClaimsPrincipal user,
            IClock clock,
            ZeloDbContext db) =>
        {
            var idoso = await db.Idosos
                .Include(i => i.Consentimentos)
                .Include(i => i.Lembretes)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (idoso is null)
            {
                return Results.NotFound(new ErroResponse("IDOSO_NAO_ENCONTRADO", $"Idoso '{id}' não encontrado."));
            }

            var acao = request.Acao?.Trim().ToUpperInvariant() ?? string.Empty;
            var validacao = ZeloBusinessRules.ValidarAcaoConsentimento(
                idoso,
                user.GetUsuarioId(),
                user.GetPapel(),
                acao);

            if (!validacao.IsValid)
            {
                return Results.Json(
                    new ErroResponse(validacao.ErrorCode!, validacao.ErrorMessage!),
                    statusCode: validacao.HttpStatus);
            }

            if (acao == "REVOGAR")
            {
                var ativos = idoso.Consentimentos.Where(c => c.RevogadoEm == null).ToList();
                if (ativos.Count == 0)
                {
                    return Results.BadRequest(new ErroResponse(
                        "CONSENTIMENTO_JA_REVOGADO",
                        "Não há consentimento ativo para revogar."));
                }

                foreach (var consentimento in ativos)
                {
                    ZeloBusinessRules.AplicarRevogacaoConsentimento(consentimento, idoso.Lembretes, clock.UtcNow);
                }

                await db.SaveChangesAsync();

                var ultimo = ativos.First();
                return Results.Ok(new ConsentimentoResponse(
                    ultimo.Id,
                    ultimo.IdosoId,
                    ultimo.VersaoTermo,
                    ultimo.AceitoEm,
                    ultimo.RevogadoEm,
                    false));
            }

            // Acao == "ACEITAR"
            foreach (var anterior in idoso.Consentimentos.Where(c => c.RevogadoEm == null))
            {
                anterior.RevogadoEm = clock.UtcNow;
            }

            var novo = new Consentimento
            {
                Id = Guid.NewGuid().ToString(),
                IdosoId = idoso.Id,
                VersaoTermo = string.IsNullOrWhiteSpace(request.VersaoTermo) ? "1.0" : request.VersaoTermo.Trim(),
                AceitoEm = clock.UtcNow,
                RevogadoEm = null
            };

            db.Consentimentos.Add(novo);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/api/v1/idosos/{id}/consentimentos",
                new ConsentimentoResponse(novo.Id, novo.IdosoId, novo.VersaoTermo, novo.AceitoEm, novo.RevogadoEm, true));
        })
        .WithName("RegistrarConsentimento")
        .WithSummary("Aceita ou revoga o termo de consentimento (exclusivo do próprio IDOSO; revogar desliga monitoramento e lembretes).")
        .Produces<ConsentimentoResponse>(StatusCodes.Status201Created)
        .Produces<ConsentimentoResponse>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        return group;
    }
}
