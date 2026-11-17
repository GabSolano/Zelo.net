using Microsoft.EntityFrameworkCore;
using Zelo.Api.Contracts;
using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Services;
using Zelo.Api.Infrastructure.Data;

namespace Zelo.Api.Endpoints;

public static class RedeApoioEndpoints
{
    public static void MapRedeApoioEndpoints(this IEndpointRouteBuilder app)
    {
        var idosoContatosGroup = app.MapGroup("/api/v1/idosos/{id}/contatos")
            .WithTags("Rede de Apoio")
            .RequireAuthorization();

        idosoContatosGroup.MapGet("/", async (string id, ZeloDbContext db) =>
        {
            if (!await db.Idosos.AnyAsync(i => i.Id == id))
            {
                return Results.NotFound(new ErroResponse("IDOSO_NAO_ENCONTRADO", $"Idoso '{id}' não encontrado."));
            }

            var contatos = await db.ContatosApoio
                .Include(c => c.Usuario)
                    .ThenInclude(u => u!.Dispositivos)
                .Where(c => c.IdosoId == id)
                .OrderBy(c => c.Ordem)
                .ToListAsync();

            var dtos = contatos.Select(c => new ContatoApoioDto(
                c.Id,
                c.IdosoId,
                c.UsuarioId,
                c.Usuario?.Nome ?? string.Empty,
                c.Papel,
                c.Ordem,
                c.Usuario?.Telefone ?? string.Empty,
                c.Usuario?.Dispositivos.Select(d => d.PushToken).ToList() ?? [])).ToList();

            return Results.Ok(dtos);
        })
        .WithName("ListarContatosDoIdoso")
        .WithSummary("Lista os contatos da rede de apoio do idoso em ordem de escalada.")
        .Produces<IReadOnlyList<ContatoApoioDto>>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        idosoContatosGroup.MapPost("/", async (
            string id,
            CriarContatoApoioRequest request,
            ZeloDbContext db) =>
        {
            var idoso = await db.Idosos
                .Include(i => i.ContatosApoio)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (idoso is null)
            {
                return Results.NotFound(new ErroResponse("IDOSO_NAO_ENCONTRADO", $"Idoso '{id}' não encontrado."));
            }

            var usuario = await db.Usuarios
                .Include(u => u.Dispositivos)
                .FirstOrDefaultAsync(u => u.Id == request.UsuarioId);

            if (usuario is null)
            {
                return Results.NotFound(new ErroResponse(
                    "USUARIO_NAO_ENCONTRADO",
                    $"Usuário '{request.UsuarioId}' não encontrado."));
            }

            var papel = request.Papel?.Trim().ToUpperInvariant() ?? string.Empty;
            var validacao = ZeloBusinessRules.ValidarContatoApoio(idoso.ContatosApoio, papel, request.Ordem);
            if (!validacao.IsValid)
            {
                return Results.Json(
                    new ErroResponse(validacao.ErrorCode!, validacao.ErrorMessage!),
                    statusCode: validacao.HttpStatus);
            }

            var contato = new ContatoApoio
            {
                Id = Guid.NewGuid().ToString(),
                IdosoId = id,
                UsuarioId = usuario.Id,
                Papel = papel,
                Ordem = request.Ordem
            };

            db.ContatosApoio.Add(contato);
            await db.SaveChangesAsync();

            var dto = new ContatoApoioDto(
                contato.Id,
                contato.IdosoId,
                contato.UsuarioId,
                usuario.Nome,
                contato.Papel,
                contato.Ordem,
                usuario.Telefone,
                usuario.Dispositivos.Select(d => d.PushToken).ToList());

            return Results.Created($"/api/v1/contatos/{contato.Id}", dto);
        })
        .WithName("CriarContatoApoio")
        .WithSummary("Adiciona um contato na rede de apoio do idoso (Regra: ordem única por idoso).")
        .Produces<ContatoApoioDto>(StatusCodes.Status201Created)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .Produces<ErroResponse>(StatusCodes.Status409Conflict)
        .WithOpenApi();

        var contatosGroup = app.MapGroup("/api/v1/contatos")
            .WithTags("Rede de Apoio")
            .RequireAuthorization();

        contatosGroup.MapPut("/{id}", async (
            string id,
            AtualizarContatoApoioRequest request,
            ZeloDbContext db) =>
        {
            var contato = await db.ContatosApoio
                .Include(c => c.Usuario)
                    .ThenInclude(u => u!.Dispositivos)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (contato is null)
            {
                return Results.NotFound(new ErroResponse("CONTATO_NAO_ENCONTRADO", $"Contato '{id}' não encontrado."));
            }

            var contatosDoIdoso = await db.ContatosApoio
                .Where(c => c.IdosoId == contato.IdosoId)
                .ToListAsync();

            var papel = request.Papel?.Trim().ToUpperInvariant() ?? string.Empty;
            var validacao = ZeloBusinessRules.ValidarContatoApoio(contatosDoIdoso, papel, request.Ordem, ignorarContatoId: id);
            if (!validacao.IsValid)
            {
                return Results.Json(
                    new ErroResponse(validacao.ErrorCode!, validacao.ErrorMessage!),
                    statusCode: validacao.HttpStatus);
            }

            contato.Papel = papel;
            contato.Ordem = request.Ordem;
            await db.SaveChangesAsync();

            return Results.Ok(new ContatoApoioDto(
                contato.Id,
                contato.IdosoId,
                contato.UsuarioId,
                contato.Usuario?.Nome ?? string.Empty,
                contato.Papel,
                contato.Ordem,
                contato.Usuario?.Telefone ?? string.Empty,
                contato.Usuario?.Dispositivos.Select(d => d.PushToken).ToList() ?? []));
        })
        .WithName("AtualizarContatoApoio")
        .WithSummary("Atualiza o papel ou a ordem de escalada de um contato de apoio.")
        .Produces<ContatoApoioDto>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .Produces<ErroResponse>(StatusCodes.Status409Conflict)
        .WithOpenApi();

        contatosGroup.MapDelete("/{id}", async (string id, ZeloDbContext db) =>
        {
            var contato = await db.ContatosApoio.FirstOrDefaultAsync(c => c.Id == id);
            if (contato is null)
            {
                return Results.NotFound(new ErroResponse("CONTATO_NAO_ENCONTRADO", $"Contato '{id}' não encontrado."));
            }

            db.ContatosApoio.Remove(contato);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("RemoverContatoApoio")
        .WithSummary("Remove um contato da rede de apoio.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();
    }
}
