namespace Zelo.Api.Domain.Enums;

/// <summary>
/// Valores de domínio em MAIÚSCULAS_COM_UNDERSCORE conforme Contrato Compartilhado do Zelo.
/// </summary>
public static class PapeisUsuario
{
    public const string Idoso = "IDOSO";
    public const string Familiar = "FAMILIAR";
    public const string Guardiao = "GUARDIAO";
    public const string Admin = "ADMIN";
    public const string Servico = "SERVICO";

    public static readonly string[] Todos = [Idoso, Familiar, Guardiao, Admin, Servico];
    public static readonly string[] ContatosApoio = [Familiar, Guardiao];

    public static bool EhValido(string? papel) =>
        !string.IsNullOrWhiteSpace(papel) && Todos.Contains(papel);

    public static bool EhPapelContatoValido(string? papel) =>
        !string.IsNullOrWhiteSpace(papel) && ContatosApoio.Contains(papel);
}

public static class ComodosSensor
{
    public const string Quarto = "QUARTO";
    public const string Banheiro = "BANHEIRO";
    public const string Cozinha = "COZINHA";
    public const string Geladeira = "GELADEIRA";
    public const string Entrada = "ENTRADA";

    public static readonly string[] Todos = [Quarto, Banheiro, Cozinha, Geladeira, Entrada];

    public static bool EhValido(string? comodo) =>
        !string.IsNullOrWhiteSpace(comodo) && Todos.Contains(comodo);
}

public static class TiposSensor
{
    public const string Presenca = "PRESENCA";
    public const string Porta = "PORTA";

    public static readonly string[] Todos = [Presenca, Porta];

    public static bool EhValido(string? tipo) =>
        !string.IsNullOrWhiteSpace(tipo) && Todos.Contains(tipo);

    public static bool CompatibelComComodo(string comodo, string tipoSensor) =>
        comodo switch
        {
            ComodosSensor.Quarto or ComodosSensor.Banheiro or ComodosSensor.Cozinha => tipoSensor == Presenca,
            ComodosSensor.Geladeira or ComodosSensor.Entrada => tipoSensor == Porta,
            _ => false
        };
}

public static class StatusLembreteRegistro
{
    public const string Pendente = "PENDENTE";
    public const string Confirmado = "CONFIRMADO";
    public const string NaoConfirmado = "NAO_CONFIRMADO";

    public static readonly string[] Todos = [Pendente, Confirmado, NaoConfirmado];
}
