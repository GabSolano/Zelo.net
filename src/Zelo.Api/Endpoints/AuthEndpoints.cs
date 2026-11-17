using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Zelo.Api.Contracts;
using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Enums;
using Zelo.Api.Domain.Services;
using Zelo.Api.Infrastructure.Data;
using Zelo.Api.Infrastructure.Security;

namespace Zelo.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Autenticação (JWT)")
            .RequireRateLimiting("auth");

        group.MapPost("/signup", async (
            SignupRequest request,
            ZeloDbContext db,
            JwtTokenService jwtService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Nome) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                !request.Email.Contains('@') ||
                string.IsNullOrWhiteSpace(request.Telefone) ||
                string.IsNullOrWhiteSpace(request.Senha) ||
                request.Senha.Length < 6)
            {
                return Results.BadRequest(new ErroResponse(
                    "DADOS_CADASTRO_INVALIDOS",
                    "Nome, email válido, telefone e senha (mínimo 6 caracteres) são obrigatórios."));
            }

            var papel = string.IsNullOrWhiteSpace(request.Papel)
                ? PapeisUsuario.Familiar
                : request.Papel.Trim().ToUpperInvariant();

            if (!PapeisUsuario.EhValido(papel))
            {
                return Results.BadRequest(new ErroResponse(
                    "PAPEL_INVALIDO",
                    $"Papel inválido '{request.Papel}'. Valores aceitos: {string.Join(", ", PapeisUsuario.Todos)}."));
            }

            var emailNormalizado = request.Email.Trim().ToLowerInvariant();
            if (await db.Usuarios.AnyAsync(u => u.Email.ToLower() == emailNormalizado))
            {
                return Results.Conflict(new ErroResponse(
                    "EMAIL_JA_CADASTRADO",
                    "Já existe um usuário cadastrado com este endereço de email."));
            }

            var usuario = new Usuario
            {
                Id = Guid.NewGuid().ToString(),
                Nome = request.Nome.Trim(),
                Email = emailNormalizado,
                Telefone = request.Telefone.Trim(),
                Papel = papel,
                SenhaHash = PasswordHasher.Hash(request.Senha)
            };

            db.Usuarios.Add(usuario);

            string? idosoId = null;
            if (papel == PapeisUsuario.Idoso)
            {
                var idoso = new Idoso
                {
                    Id = Guid.NewGuid().ToString(),
                    UsuarioId = usuario.Id,
                    Nome = usuario.Nome,
                    DataNascimento = "1950-01-01",
                    Observacoes = "Cadastro via signup"
                };
                db.Idosos.Add(idoso);
                db.Consentimentos.Add(new Consentimento
                {
                    Id = Guid.NewGuid().ToString(),
                    IdosoId = idoso.Id,
                    VersaoTermo = "1.0",
                    AceitoEm = DateTime.UtcNow
                });
                idosoId = idoso.Id;
            }

            await db.SaveChangesAsync();

            var (token, expiresAt) = jwtService.GenerateToken(usuario);
            var response = new AuthResponse(
                token,
                expiresAt,
                new UsuarioResumoDto(usuario.Id, usuario.Nome, usuario.Email, usuario.Telefone, usuario.Papel, idosoId));

            return Results.Created($"/api/v1/usuarios/{usuario.Id}", response);
        })
        .WithName("Signup")
        .WithSummary("Cadastra um novo usuário e emite token JWT HS256 (suporta Idempotency-Key).")
        .Produces<AuthResponse>(StatusCodes.Status201Created)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status409Conflict)
        .Produces<ErroResponse>(StatusCodes.Status429TooManyRequests)
        .WithOpenApi();

        group.MapPost("/login", async (
            LoginRequest request,
            ZeloDbContext db,
            JwtTokenService jwtService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Senha))
            {
                return Results.BadRequest(new ErroResponse(
                    "CREDENCIAIS_OBRIGATORIAS",
                    "Email e senha são obrigatórios."));
            }

            var emailNormalizado = request.Email.Trim().ToLowerInvariant();
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email.ToLower() == emailNormalizado);

            if (usuario is null || !PasswordHasher.Verify(request.Senha, usuario.SenhaHash))
            {
                return Results.Json(
                    new ErroResponse("CREDENCIAIS_INVALIDAS", "Email ou senha inválidos."),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var idosoVinculado = await db.Idosos
                .Where(i => i.UsuarioId == usuario.Id)
                .Select(i => i.Id)
                .FirstOrDefaultAsync();

            if (idosoVinculado is null && (usuario.Papel == PapeisUsuario.Familiar || usuario.Papel == PapeisUsuario.Guardiao))
            {
                idosoVinculado = await db.ContatosApoio
                    .Where(c => c.UsuarioId == usuario.Id)
                    .Select(c => c.IdosoId)
                    .FirstOrDefaultAsync();
            }

            var (token, expiresAt) = jwtService.GenerateToken(usuario);
            return Results.Ok(new AuthResponse(
                token,
                expiresAt,
                new UsuarioResumoDto(usuario.Id, usuario.Nome, usuario.Email, usuario.Telefone, usuario.Papel, idosoVinculado)));
        })
        .WithName("Login")
        .WithSummary("Autentica o usuário e retorna um JWT HS256 com as claims sub e papel.")
        .Produces<AuthResponse>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErroResponse>(StatusCodes.Status429TooManyRequests)
        .WithOpenApi();

        app.MapPost("/api/v1/usuarios/{id}/dispositivos", async (
            string id,
            RegistrarDispositivoRequest request,
            ClaimsPrincipal user,
            ZeloDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.PushToken))
            {
                return Results.BadRequest(new ErroResponse(
                    "PUSH_TOKEN_OBRIGATORIO",
                    "O campo pushToken é obrigatório."));
            }

            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id);
            if (usuario is null)
            {
                return Results.NotFound(new ErroResponse(
                    "USUARIO_NAO_ENCONTRADO",
                    $"Usuário '{id}' não encontrado."));
            }

            var autenticadoId = user.GetUsuarioId();
            var papel = user.GetPapel();
            if (autenticadoId != id && papel != PapeisUsuario.Admin && papel != PapeisUsuario.Servico)
            {
                return Results.Json(
                    new ErroResponse("ACESSO_NEGADO", "Você só pode registrar dispositivos para a sua própria conta."),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var existente = await db.UsuarioDispositivos
                .FirstOrDefaultAsync(d => d.UsuarioId == id && d.PushToken == request.PushToken.Trim());

            if (existente is not null)
            {
                return Results.Ok(new DispositivoResponse(existente.Id, existente.UsuarioId, existente.PushToken));
            }

            var dispositivo = new UsuarioDispositivo
            {
                Id = Guid.NewGuid().ToString(),
                UsuarioId = id,
                PushToken = request.PushToken.Trim()
            };

            db.UsuarioDispositivos.Add(dispositivo);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/api/v1/usuarios/{id}/dispositivos/{dispositivo.Id}",
                new DispositivoResponse(dispositivo.Id, dispositivo.UsuarioId, dispositivo.PushToken));
        })
        .RequireAuthorization()
        .WithTags("Usuários e Dispositivos")
        .WithName("RegistrarDispositivo")
        .WithSummary("Registra o push token (Expo Push) do dispositivo do usuário.")
        .Produces<DispositivoResponse>(StatusCodes.Status201Created)
        .Produces<DispositivoResponse>(StatusCodes.Status200OK)
        .Produces<ErroResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErroResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErroResponse>(StatusCodes.Status404NotFound)
        .WithOpenApi();

        return group;
    }
}
