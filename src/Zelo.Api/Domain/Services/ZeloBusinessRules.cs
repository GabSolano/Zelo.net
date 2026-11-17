using System.Globalization;
using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Enums;

namespace Zelo.Api.Domain.Services;

public record RuleValidationResult(bool IsValid, string? ErrorCode = null, string? ErrorMessage = null, int HttpStatus = 400)
{
    public static RuleValidationResult Success() => new(true);
    public static RuleValidationResult Fail(string code, string message, int httpStatus = 400) =>
        new(false, code, message, httpStatus);
}

/// <summary>
/// Regras de negócio puras do domínio Zelo (testadas unitariamente com xUnit):
/// 1. O consentimento é do próprio idoso; revogar desliga o monitoramento e os lembretes.
/// 2. A ordem dos contatos de apoio é única por idoso.
/// 3. Só há um sensor de cada cômodo por casa (e tipo compatível com o cômodo).
/// 4. O lembrete tem texto livre (sem nome de medicamento nem dosagem estruturados) e horário HH:mm válido.
/// 5. Registro de lembrete PENDENTE sem confirmação após 60 minutos vira NAO_CONFIRMADO (Regra R5).
/// </summary>
public static class ZeloBusinessRules
{
    public static RuleValidationResult ValidarAcaoConsentimento(
        Idoso idoso,
        string usuarioAutenticadoId,
        string papelAutenticado,
        string acao)
    {
        if (string.IsNullOrWhiteSpace(acao) || (acao != "ACEITAR" && acao != "REVOGAR"))
        {
            return RuleValidationResult.Fail(
                "ACAO_CONSENTIMENTO_INVALIDA",
                "A ação de consentimento deve ser 'ACEITAR' ou 'REVOGAR'.",
                400);
        }

        // Regra 1: O consentimento é do próprio idoso, não da família.
        bool ehProprioIdoso = papelAutenticado == PapeisUsuario.Idoso && idoso.UsuarioId == usuarioAutenticadoId;
        if (!ehProprioIdoso)
        {
            return RuleValidationResult.Fail(
                "CONSENTIMENTO_APENAS_IDOSO",
                "O consentimento pertence exclusivamente ao próprio idoso monitorado e não pode ser alterado por familiares ou terceiros.",
                403);
        }

        return RuleValidationResult.Success();
    }

    public static void AplicarRevogacaoConsentimento(
        Consentimento consentimentoAtivo,
        IEnumerable<Lembrete> lembretesDoIdoso,
        DateTime revogadoEmUtc)
    {
        consentimentoAtivo.RevogadoEm = revogadoEmUtc;
        foreach (var lembrete in lembretesDoIdoso)
        {
            lembrete.Ativo = false;
        }
    }

    public static bool PossuiConsentimentoAtivo(IEnumerable<Consentimento> consentimentos) =>
        consentimentos.Any(c => c.RevogadoEm == null);

    public static RuleValidationResult ValidarNovoSensor(
        IEnumerable<Sensor> sensoresExistentesNaCasa,
        string comodo,
        string tipoSensor)
    {
        if (!ComodosSensor.EhValido(comodo))
        {
            return RuleValidationResult.Fail(
                "COMODO_INVALIDO",
                $"Cômodo inválido '{comodo}'. Valores aceitos: {string.Join(", ", ComodosSensor.Todos)}.",
                400);
        }

        if (!TiposSensor.EhValido(tipoSensor))
        {
            return RuleValidationResult.Fail(
                "TIPO_SENSOR_INVALIDO",
                $"Tipo de sensor inválido '{tipoSensor}'. Valores aceitos: {string.Join(", ", TiposSensor.Todos)}.",
                400);
        }

        if (!TiposSensor.CompatibelComComodo(comodo, tipoSensor))
        {
            return RuleValidationResult.Fail(
                "SENSOR_COMODO_INCOMPATIVEL",
                $"O cômodo '{comodo}' não é compatível com o tipo de sensor '{tipoSensor}'.",
                400);
        }

        // Regra 3: Só há um sensor de cada cômodo por casa.
        if (sensoresExistentesNaCasa.Any(s => s.Comodo == comodo))
        {
            return RuleValidationResult.Fail(
                "SENSOR_COMODO_DUPLICADO",
                $"Já existe um sensor cadastrado para o cômodo '{comodo}' nesta casa.",
                409);
        }

        return RuleValidationResult.Success();
    }

    public static RuleValidationResult ValidarContatoApoio(
        IEnumerable<ContatoApoio> contatosExistentesDoIdoso,
        string papel,
        int ordem,
        string? ignorarContatoId = null)
    {
        if (!PapeisUsuario.EhPapelContatoValido(papel))
        {
            return RuleValidationResult.Fail(
                "PAPEL_CONTATO_INVALIDO",
                "O papel do contato de apoio deve ser 'FAMILIAR' ou 'GUARDIAO'.",
                400);
        }

        if (ordem < 1)
        {
            return RuleValidationResult.Fail(
                "ORDEM_CONTATO_INVALIDA",
                "A ordem na cadeia de escalada deve ser maior ou igual a 1.",
                400);
        }

        // Regra 2: A ordem dos contatos de apoio é única por idoso.
        bool ordemJaExiste = contatosExistentesDoIdoso.Any(c =>
            c.Ordem == ordem && (ignorarContatoId == null || c.Id != ignorarContatoId));

        if (ordemJaExiste)
        {
            return RuleValidationResult.Fail(
                "ORDEM_CONTATO_DUPLICADA",
                $"Já existe um contato de apoio na ordem {ordem} para este idoso.",
                409);
        }

        return RuleValidationResult.Success();
    }

    public static RuleValidationResult ValidarLembrete(
        string? texto,
        string? horario,
        bool consentimentoAtivo)
    {
        if (!consentimentoAtivo)
        {
            return RuleValidationResult.Fail(
                "CONSENTIMENTO_INATIVO",
                "Não é possível ativar ou cadastrar lembretes para um idoso com consentimento revogado ou inativo.",
                400);
        }

        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length > 250)
        {
            return RuleValidationResult.Fail(
                "TEXTO_LEMBRETE_INVALIDO",
                "O texto livre do lembrete é obrigatório e deve ter até 250 caracteres.",
                400);
        }

        if (!HorarioValido(horario))
        {
            return RuleValidationResult.Fail(
                "HORARIO_LEMBRETE_INVALIDO",
                "O horário do lembrete deve estar no formato HH:mm (ex.: 08:00).",
                400);
        }

        return RuleValidationResult.Success();
    }

    public static bool HorarioValido(string? horario) =>
        !string.IsNullOrWhiteSpace(horario) &&
        TimeSpan.TryParseExact(horario, @"hh\:mm", CultureInfo.InvariantCulture, out var ts) &&
        ts >= TimeSpan.Zero &&
        ts < TimeSpan.FromHours(24);

    /// <summary>
    /// Regra R5: Registro PENDENTE sem confirmação 60 minutos após o horário previsto vira NAO_CONFIRMADO.
    /// </summary>
    public static bool DeveMarcarComoNaoConfirmado(LembreteRegistro registro, DateTime agoraUtc)
    {
        if (registro.Status != StatusLembreteRegistro.Pendente)
        {
            return false;
        }

        return agoraUtc >= registro.PrevistoPara.AddMinutes(60);
    }
}
