using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Services;
using Zelo.Api.Infrastructure.Data;

namespace Zelo.Api.Infrastructure.Idempotency;

/// <summary>
/// Middleware de Idempotência (Requisito 03 do PDF Oficial - 15 pts):
/// Guarda {chave, hash do corpo, status, resposta} na tabela IDEMPOTENCY_RECORD por 24 h.
/// - Mesma chave com o mesmo corpo devolve a resposta salva (sem duplicar).
/// - Mesma chave com corpo diferente devolve 409 Conflict.
/// </summary>
public class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Idempotency-Key";

    public async Task InvokeAsync(HttpContext context, ZeloDbContext db, IClock clock)
    {
        if (!HttpMethods.IsPost(context.Request.Method) ||
            !context.Request.Headers.TryGetValue(HeaderName, out var keyValues) ||
            string.IsNullOrWhiteSpace(keyValues.FirstOrDefault()))
        {
            await next(context);
            return;
        }

        var rawKey = keyValues.First()!.Trim();
        var scopedKey = $"{context.Request.Path.ToString().ToLowerInvariant()}:{rawKey}";

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestBody)));
        var cutoff = clock.UtcNow.AddHours(-24);

        var existing = await db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Chave == scopedKey);

        if (existing is not null && existing.CriadoEm >= cutoff)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                context.Response.ContentType = "application/json";
                var conflictJson = JsonSerializer.Serialize(new
                {
                    codigo = "IDEMPOTENCY_CONFLICT",
                    mensagem = "A chave Idempotency-Key informada já foi utilizada com um corpo de requisição diferente."
                });
                await context.Response.WriteAsync(conflictJson);
                return;
            }

            context.Response.StatusCode = existing.StatusCode;
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Idempotent-Replay"] = "true";
            await context.Response.WriteAsync(existing.ResponseBody);
            return;
        }

        if (existing is not null && existing.CriadoEm < cutoff)
        {
            var expiredEntity = await db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Chave == scopedKey);
            if (expiredEntity is not null)
            {
                db.IdempotencyRecords.Remove(expiredEntity);
                await db.SaveChangesAsync();
            }
        }

        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            await next(context);

            memoryStream.Position = 0;
            var responseBody = await new StreamReader(memoryStream, Encoding.UTF8).ReadToEndAsync();

            // Armazena respostas bem-sucedidas (2xx) para garantir idempotência nas repetições
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    Chave = scopedKey,
                    RequestHash = requestHash,
                    StatusCode = context.Response.StatusCode,
                    ResponseBody = responseBody,
                    CriadoEm = clock.UtcNow
                });
                await db.SaveChangesAsync();
            }

            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalBodyStream);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }
}
