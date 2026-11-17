namespace Zelo.Api.Contracts;

public record ErroResponse(string Codigo, string Mensagem);

// ============================================================================
// Auth (/api/v1/auth)
// ============================================================================
public record SignupRequest(
    string Nome,
    string Email,
    string Telefone,
    string Senha,
    string Papel = "FAMILIAR");

public record LoginRequest(string Email, string Senha);

public record UsuarioResumoDto(
    string Id,
    string Nome,
    string Email,
    string Telefone,
    string Papel,
    string? IdosoId = null);

public record AuthResponse(
    string Token,
    DateTime ExpiraEm,
    UsuarioResumoDto Usuario);

// ============================================================================
// Dispositivos (/api/v1/usuarios/{id}/dispositivos)
// ============================================================================
public record RegistrarDispositivoRequest(string PushToken);

public record DispositivoResponse(string Id, string UsuarioId, string PushToken);

// ============================================================================
// Idosos e Consentimentos (/api/v1/idosos)
// ============================================================================
public record CriarIdosoRequest(
    string UsuarioId,
    string Nome,
    string DataNascimento,
    string? Observacoes = null,
    bool AceitarConsentimentoInicial = true);

public record AtualizarIdosoRequest(
    string Nome,
    string DataNascimento,
    string? Observacoes = null);

public record IdosoResponse(
    string Id,
    string UsuarioId,
    string Nome,
    string DataNascimento,
    string? Observacoes,
    bool ConsentimentoAtivo);

public record RegistrarConsentimentoRequest(
    string Acao, // "ACEITAR" ou "REVOGAR"
    string VersaoTermo = "1.0");

public record ConsentimentoResponse(
    string Id,
    string IdosoId,
    string VersaoTermo,
    DateTime AceitoEm,
    DateTime? RevogadoEm,
    bool ConsentimentoAtivo);

// ============================================================================
// Casas e Sensores (/api/v1/casas)
// ============================================================================
public record CriarCasaRequest(
    string IdosoId,
    string Endereco,
    string Bairro,
    string Cidade);

public record AtualizarCasaRequest(
    string Endereco,
    string Bairro,
    string Cidade);

public record SensorDto(
    string Id,
    string CasaId,
    string Comodo,
    string TipoSensor,
    bool Ativo);

public record CasaResponse(
    string Id,
    string IdosoId,
    string Endereco,
    string Bairro,
    string Cidade,
    IReadOnlyList<SensorDto> Sensores);

public record CriarSensorRequest(
    string Comodo,
    string TipoSensor,
    bool Ativo = true);

// ============================================================================
// Rede de Apoio (/api/v1/idosos/{id}/contatos, /api/v1/contatos/{id}, /api/v1/casas/{id}/rede-apoio)
// ============================================================================
public record CriarContatoApoioRequest(
    string UsuarioId,
    string Papel, // "FAMILIAR" ou "GUARDIAO"
    int Ordem);

public record AtualizarContatoApoioRequest(
    string Papel,
    int Ordem);

public record ContatoApoioDto(
    string ContatoId,
    string IdosoId,
    string UsuarioId,
    string Nome,
    string Papel,
    int Ordem,
    string Telefone,
    IReadOnlyList<string> PushTokens);

public record IdosoRedeApoioDto(
    string IdosoId,
    string UsuarioId,
    string Nome,
    string Telefone,
    IReadOnlyList<string> PushTokens);

/// <summary>
/// Contrato compartilhado com o zelo-monitor (Java): GET /api/v1/casas/{id}/rede-apoio
/// </summary>
public record RedeApoioCasaResponse(
    string CasaId,
    bool ConsentimentoAtivo,
    IdosoRedeApoioDto Idoso,
    IReadOnlyList<ContatoApoioDto> Contatos);

// ============================================================================
// Lembretes e Registros (/api/v1/idosos/{id}/lembretes, /api/v1/lembretes/...)
// ============================================================================
public record CriarLembreteRequest(
    string Texto,
    string Horario,
    string DiasSemana = "SEG,TER,QUA,QUI,SEX,SAB,DOM",
    bool Ativo = true);

public record AtualizarLembreteRequest(
    string Texto,
    string Horario,
    string DiasSemana = "SEG,TER,QUA,QUI,SEX,SAB,DOM",
    bool Ativo = true);

public record LembreteResponse(
    string Id,
    string IdosoId,
    string Texto,
    string Horario,
    string DiasSemana,
    bool Ativo,
    string CriadoPor);

public record LembreteHojeDto(
    string RegistroId,
    string LembreteId,
    string IdosoId,
    string Texto,
    string Horario,
    DateTime PrevistoPara,
    string Status,
    DateTime? ConfirmadoEm);

public record LembreteNaoConfirmadoDto(
    string RegistroId,
    string LembreteId,
    string IdosoId,
    string? CasaId,
    string Texto,
    string Horario,
    DateTime PrevistoPara,
    string Status);

// ============================================================================
// Simulação de Relógio (Suporte a MODO_SIMULACAO=true em demos e testes)
// ============================================================================
public record RelogioSimuladoRequest(DateTime? Agora);
