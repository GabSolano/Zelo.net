using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Zelo.Api.Contracts;
using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Enums;
using Zelo.Api.Domain.Services;
using Zelo.Api.Infrastructure.Background;
using Zelo.Api.Infrastructure.Data;
using Zelo.Api.Infrastructure.Security;

namespace Zelo.Api.Endpoints;

public static class LembreteEndpoints
{
    public static void MapLembreteEndpoints(this IEndpointRouteBuilder app)
    {
        var idosoLembretesGroup = app.MapGroup("/api/v1/idosos/{id}/lembretes")
            .WithTags("Lembretes de Remédio (App Mobile & Java)")
            .RequireAuthorization();

        idosoLembretesGroup.MapGet("/", async (string id, ZeloDbContext db) =>
        {
            if (!await db.Idosos.AnyAsync(i => i.Id == id))
            {
                return Results.NotFound(new ErroResponse("IDOSO_NAO_ENCONTRADO", $"Idoso '{id}' não encontrado."));
            }

            var lembretes = await db.Lembretes
                .Where(l => l.IdosoId == id)
                .OrderBy(l => l.Horario)
                .Select(l => new LembreteResponse(l.Id, l.IdosoId, l.Texto, l.Horario, l.DiasSemana, l.Ativo, l.CriadoPor))
                .ToListAsync();

            return Results.Ok(lembretes);
        })
        .WithName("ListarLembretesDoIdoso")
        .WithSummary("Lista os lembretes cadastrados para o idoso.")
        .Produces<IReadOnlyList<LembreteResponse>>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        idosoLembretesGroup.MapPost("/", async (
            string id,
            CriarLembreteRequest request,
            ClaimsPrincipal user,
            IClock clock,
            ZeloDbContext db) =>
        {
            var idoso = await db.Idosos
                .Include(i => i.Consentimentos)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (idoso is null)
            {
                return Results.NotFound(new ErroResponse("IDOSO_NAO_ENCONTRADO", $"Idoso '{id}' não encontrado."));
            }

            var consentimentoAtivo = ZeloBusinessRules.PossuiConsentimentoAtivo(idoso.Consentimentos);
            var validacao = ZeloBusinessRules.ValidarLembrete(request.Texto, request.Horario, consentimentoAtivo);
            if (!validacao.IsValid)
            {
                return Results.Json(
                    new ErroResponse(validacao.ErrorCode!, validacao.ErrorMessage!),
                    statusCode: validacao.HttpStatus);
            }

            var diasSemana = string.IsNullOrWhiteSpace(request.DiasSemana)
                ? "SEG,TER,QUA,QUI,SEX,SAB,DOM"
                : request.DiasSemana.Trim().ToUpperInvariant();

            var lembrete = new Lembrete
            {
                Id = Guid.NewGuid().ToString(),
                IdosoId = id,
                Texto = request.Texto.Trim(),
                Horario = request.Horario.Trim(),
                DiasSemana = diasSemana,
                Ativo = request.Ativo,
                CriadoPor = user.GetUsuarioId()
            };

            db.Lembretes.Add(lembrete);

            if (lembrete.Ativo && TimeSpan.TryParseExact(lembrete.Horario, @"hh\:mm", CultureInfo.InvariantCulture, out var horarioTs))
            {
                var hoje = clock.UtcNow.Date;
                var previstoHoje = new DateTime(hoje.Year, hoje.Month, hoje.Day, horarioTs.Hours, horarioTs.Minutes, 0, DateTimeKind.Utc);

                db.LembreteRegistros.Add(new LembreteRegistro
                {
                    Id = Guid.NewGuid().ToString(),
                    LembreteId = lembrete.Id,
                    PrevistoPara = previstoHoje,
                    Status = StatusLembreteRegistro.Pendente
                });
            }

            await db.SaveChangesAsync();

            var response = new LembreteResponse(
                lembrete.Id,
                lembrete.IdosoId,
                lembrete.Texto,
                lembrete.Horario,
                lembrete.DiasSemana,
                lembrete.Ativo,
                lembrete.CriadoPor);

            return Results.Created($"/api/v1/lembretes/{lembrete.Id}", response);
        })
        .WithName("CriarLembrete")
        .WithSummary("Cadastra um novo lembrete com texto livre e horário HH:mm (suporta Idempotency-Key).")
        .Produces<LembreteResponse>(StatusCodes.Status201Created)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .Produces<ErroResponse>(StatusCodes.Status409Conflict)
        .WithOpenApi();

        idosoLembretesGroup.MapGet("/hoje", async (
            string id,
            IClock clock,
            LembreteExpirationProcessor expirationProcessor,
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

            var hoje = clock.UtcNow.Date;
            var amanha = hoje.AddDays(1);

            // Garante que cada lembrete ativo tenha um registro para hoje se o consentimento estiver ativo
            if (ZeloBusinessRules.PossuiConsentimentoAtivo(idoso.Consentimentos))
            {
                foreach (var lembrete in idoso.Lembretes.Where(l => l.Ativo))
                {
                    if (!TimeSpan.TryParseExact(lembrete.Horario, @"hh\:mm", CultureInfo.InvariantCulture, out var ts))
                    {
                        continue;
                    }

                    var previstoHoje = new DateTime(hoje.Year, hoje.Month, hoje.Day, ts.Hours, ts.Minutes, 0, DateTimeKind.Utc);
                    var jaExiste = await db.LembreteRegistros.AnyAsync(r =>
                        r.LembreteId == lembrete.Id && r.PrevistoPara == previstoHoje);

                    if (!jaExiste)
                    {
                        db.LembreteRegistros.Add(new LembreteRegistro
                        {
                            Id = Guid.NewGuid().ToString(),
                            LembreteId = lembrete.Id,
                            PrevistoPara = previstoHoje,
                            Status = StatusLembreteRegistro.Pendente
                        });
                    }
                }

                await db.SaveChangesAsync();
            }

            // Atualiza eventuais registros expirados (>60 min) antes de devolver
            await expirationProcessor.ProcessarExpiradosAsync();

            var registrosHoje = await db.LembreteRegistros
                .Include(r => r.Lembrete)
                .Where(r => r.Lembrete != null &&
                            r.Lembrete.IdosoId == id &&
                            r.PrevistoPara >= hoje &&
                            r.PrevistoPara < amanha)
                .OrderBy(r => r.PrevistoPara)
                .Select(r => new LembreteHojeDto(
                    r.Id,
                    r.LembreteId,
                    r.Lembrete!.IdosoId,
                    r.Lembrete.Texto,
                    r.Lembrete.Horario,
                    r.PrevistoPara,
                    r.Status,
                    r.ConfirmadoEm))
                .ToListAsync();

            return Results.Ok(registrosHoje);
        })
        .WithName("ListarLembretesDeHoje")
        .WithSummary("Retorna as ocorrências de lembretes do dia atual com status (PENDENTE, CONFIRMADO, NAO_CONFIRMADO).")
        .Produces<IReadOnlyList<LembreteHojeDto>>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        var lembretesGroup = app.MapGroup("/api/v1/lembretes")
            .WithTags("Lembretes de Remédio (App Mobile & Java)")
            .RequireAuthorization();

        lembretesGroup.MapPut("/{id}", async (
            string id,
            AtualizarLembreteRequest request,
            ZeloDbContext db) =>
        {
            var lembrete = await db.Lembretes
                .Include(l => l.Idoso)
                    .ThenInclude(i => i!.Consentimentos)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (lembrete is null)
            {
                return Results.NotFound(new ErroResponse("LEMBRETE_NAO_ENCONTRADO", $"Lembrete '{id}' não encontrado."));
            }

            var consentimentoAtivo = lembrete.Idoso is not null &&
                                     ZeloBusinessRules.PossuiConsentimentoAtivo(lembrete.Idoso.Consentimentos);

            var validacao = ZeloBusinessRules.ValidarLembrete(request.Texto, request.Horario, consentimentoAtivo || !request.Ativo);
            if (!validacao.IsValid)
            {
                return Results.Json(
                    new ErroResponse(validacao.ErrorCode!, validacao.ErrorMessage!),
                    statusCode: validacao.HttpStatus);
            }

            lembrete.Texto = request.Texto.Trim();
            lembrete.Horario = request.Horario.Trim();
            lembrete.DiasSemana = string.IsNullOrWhiteSpace(request.DiasSemana)
                ? "SEG,TER,QUA,QUI,SEX,SAB,DOM"
                : request.DiasSemana.Trim().ToUpperInvariant();
            lembrete.Ativo = request.Ativo;

            await db.SaveChangesAsync();

            return Results.Ok(new LembreteResponse(
                lembrete.Id,
                lembrete.IdosoId,
                lembrete.Texto,
                lembrete.Horario,
                lembrete.DiasSemana,
                lembrete.Ativo,
                lembrete.CriadoPor));
        })
        .WithName("AtualizarLembrete")
        .WithSummary("Edita o texto livre, horário ou status de um lembrete.")
        .Produces<LembreteResponse>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        lembretesGroup.MapDelete("/{id}", async (string id, ZeloDbContext db) =>
        {
            var lembrete = await db.Lembretes.FirstOrDefaultAsync(l => l.Id == id);
            if (lembrete is null)
            {
                return Results.NotFound(new ErroResponse("LEMBRETE_NAO_ENCONTRADO", $"Lembrete '{id}' não encontrado."));
            }

            db.Lembretes.Remove(lembrete);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("RemoverLembrete")
        .WithSummary("Remove um lembrete cadastrado.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        lembretesGroup.MapPost("/registros/{id}/confirmacao", async (
            string id,
            ClaimsPrincipal user,
            IClock clock,
            ZeloDbContext db) =>
        {
            var registro = await db.LembreteRegistros
                .Include(r => r.Lembrete)
                    .ThenInclude(l => l!.Idoso)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (registro is null || registro.Lembrete is null)
            {
                return Results.NotFound(new ErroResponse(
                    "REGISTRO_LEMBRETE_NAO_ENCONTRADO",
                    $"Registro de lembrete '{id}' não encontrado."));
            }

            var papel = user.GetPapel();
            var usuarioId = user.GetUsuarioId();

            // Regra de autorização: IDOSO só confirma os próprios lembretes
            bool ehIdosoDono = papel == PapeisUsuario.Idoso && registro.Lembrete.Idoso?.UsuarioId == usuarioId;
            if (!ehIdosoDono && papel != PapeisUsuario.Admin)
            {
                return Results.Json(
                    new ErroResponse(
                        "CONFIRMACAO_APENAS_IDOSO",
                        "Apenas o próprio idoso pode confirmar que tomou o remédio ('Tomei')."),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (registro.Status != StatusLembreteRegistro.Confirmado)
            {
                registro.Status = StatusLembreteRegistro.Confirmado;
                registro.ConfirmadoEm = clock.UtcNow;
                await db.SaveChangesAsync();
            }

            return Results.Ok(new LembreteHojeDto(
                registro.Id,
                registro.LembreteId,
                registro.Lembrete.IdosoId,
                registro.Lembrete.Texto,
                registro.Lembrete.Horario,
                registro.PrevistoPara,
                registro.Status,
                registro.ConfirmadoEm));
        })
        .WithName("ConfirmarLembreteRegistro")
        .WithSummary("Confirma que o idoso tomou o remédio ('Tomei') - suporta Idempotency-Key.")
        .Produces<LembreteHojeDto>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .Produces<ErroResponse>(StatusCodes.Status409Conflict)
        .WithOpenApi();

        // Endpoint usado pelo Java: GET /api/v1/lembretes/nao-confirmados?desde=
        lembretesGroup.MapGet("/nao-confirmados", async (
            string? desde,
            LembreteExpirationProcessor expirationProcessor,
            ZeloDbContext db) =>
        {
            await expirationProcessor.ProcessarExpiradosAsync();

            DateTime? filtroDesde = null;
            if (!string.IsNullOrWhiteSpace(desde))
            {
                if (!DateTime.TryParse(desde, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return Results.BadRequest(new ErroResponse(
                        "DATA_DESDE_INVALIDA",
                        "O parâmetro 'desde' deve estar em formato ISO-8601 válido (ex.: 2026-11-16T00:00:00Z)."));
                }
                filtroDesde = parsed;
            }

            var query = db.LembreteRegistros
                .Include(r => r.Lembrete)
                    .ThenInclude(l => l!.Idoso)
                        .ThenInclude(i => i!.Casas)
                .Where(r => r.Status == StatusLembreteRegistro.NaoConfirmado);

            if (filtroDesde.HasValue)
            {
                query = query.Where(r => r.PrevistoPara >= filtroDesde.Value);
            }

            var lista = await query
                .OrderByDescending(r => r.PrevistoPara)
                .ToListAsync();

            var response = lista.Select(r => new LembreteNaoConfirmadoDto(
                r.Id,
                r.LembreteId,
                r.Lembrete!.IdosoId,
                r.Lembrete.Idoso?.Casas.FirstOrDefault()?.Id,
                r.Lembrete.Texto,
                r.Lembrete.Horario,
                r.PrevistoPara,
                r.Status)).ToList();

            return Results.Ok(response);
        })
        .WithName("ListarLembretesNaoConfirmados")
        .WithSummary("Retorna os registros de lembretes NAO_CONFIRMADO após 60 min (consumido pelo serviço Java).")
        .Produces<IReadOnlyList<LembreteNaoConfirmadoDto>>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .WithOpenApi();

        // Endpoint auxiliar de relógio simulado quando MODO_SIMULACAO=true (Roteiro 6 da Demo)
        app.MapPost("/api/v1/simulacao/relogio", async (
            RelogioSimuladoRequest request,
            IClock clock,
            LembreteExpirationProcessor expirationProcessor) =>
        {
            clock.SetSimulatedNow(request.Agora);
            var expirados = await expirationProcessor.ProcessarExpiradosAsync();
            return Results.Ok(new
            {
                agora = clock.UtcNow,
                lembretesExpiradosProcessados = expirados
            });
        })
        .RequireAuthorization()
        .WithTags("Simulação (Demo)")
        .WithName("AjustarRelogioSimulado")
        .WithSummary("Ajusta o relógio simulado (MODO_SIMULACAO=true) e processa lembretes expirados (>60 min).")
        .WithOpenApi();
    }
}
