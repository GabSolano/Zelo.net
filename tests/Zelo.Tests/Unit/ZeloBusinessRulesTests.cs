using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Enums;
using Zelo.Api.Domain.Services;

namespace Zelo.Tests.Unit;

public class ZeloBusinessRulesTests
{
    [Fact]
    public void ValidarAcaoConsentimento_DevePermitir_QuandoProprioIdosoAceitaOuRevoga()
    {
        var idoso = new Idoso
        {
            Id = "00000000-0000-0000-0000-000000000010",
            UsuarioId = "00000000-0000-0000-0000-000000000001",
            Nome = "Dona Maria"
        };

        var resultado = ZeloBusinessRules.ValidarAcaoConsentimento(
            idoso,
            usuarioAutenticadoId: "00000000-0000-0000-0000-000000000001",
            papelAutenticado: PapeisUsuario.Idoso,
            acao: "REVOGAR");

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void ValidarAcaoConsentimento_DeveRetornar403_QuandoFamiliarTentaRevogarPeloIdoso()
    {
        var idoso = new Idoso
        {
            Id = "00000000-0000-0000-0000-000000000010",
            UsuarioId = "00000000-0000-0000-0000-000000000001",
            Nome = "Dona Maria"
        };

        var resultado = ZeloBusinessRules.ValidarAcaoConsentimento(
            idoso,
            usuarioAutenticadoId: "00000000-0000-0000-0000-000000000002",
            papelAutenticado: PapeisUsuario.Familiar,
            acao: "REVOGAR");

        Assert.False(resultado.IsValid);
        Assert.Equal(403, resultado.HttpStatus);
        Assert.Equal("CONSENTIMENTO_APENAS_IDOSO", resultado.ErrorCode);
    }

    [Fact]
    public void AplicarRevogacaoConsentimento_DeveDesligarLembretesEMarcarRevogadoEm()
    {
        var consentimento = new Consentimento
        {
            Id = "c1",
            IdosoId = "i1",
            AceitoEm = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc),
            RevogadoEm = null
        };

        var lembretes = new List<Lembrete>
        {
            new() { Id = "l1", Texto = "Remédio da pressão", Ativo = true },
            new() { Id = "l2", Texto = "Vitamina D", Ativo = true }
        };

        var agora = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        ZeloBusinessRules.AplicarRevogacaoConsentimento(consentimento, lembretes, agora);

        Assert.Equal(agora, consentimento.RevogadoEm);
        Assert.All(lembretes, l => Assert.False(l.Ativo));
        Assert.False(ZeloBusinessRules.PossuiConsentimentoAtivo([consentimento]));
    }

    [Fact]
    public void ValidarNovoSensor_DeveRetornar409_QuandoComodoJaPossuiSensorNaCasa()
    {
        var existentes = new List<Sensor>
        {
            new() { Id = "s1", CasaId = "casa1", Comodo = ComodosSensor.Banheiro, TipoSensor = TiposSensor.Presenca }
        };

        var resultado = ZeloBusinessRules.ValidarNovoSensor(
            existentes,
            ComodosSensor.Banheiro,
            TiposSensor.Presenca);

        Assert.False(resultado.IsValid);
        Assert.Equal(409, resultado.HttpStatus);
        Assert.Equal("SENSOR_COMODO_DUPLICADO", resultado.ErrorCode);
    }

    [Fact]
    public void ValidarNovoSensor_DeveRetornar400_QuandoComodoIncompativelComTipoSensor()
    {
        var resultado = ZeloBusinessRules.ValidarNovoSensor(
            [],
            ComodosSensor.Geladeira,
            TiposSensor.Presenca); // Geladeira exige PORTA

        Assert.False(resultado.IsValid);
        Assert.Equal(400, resultado.HttpStatus);
        Assert.Equal("SENSOR_COMODO_INCOMPATIVEL", resultado.ErrorCode);
    }

    [Fact]
    public void ValidarContatoApoio_DeveRetornar409_QuandoOrdemJaExisteParaMesmoIdoso()
    {
        var contatos = new List<ContatoApoio>
        {
            new() { Id = "ca1", IdosoId = "idoso1", UsuarioId = "u2", Papel = PapeisUsuario.Familiar, Ordem = 1 }
        };

        var resultado = ZeloBusinessRules.ValidarContatoApoio(
            contatos,
            papel: PapeisUsuario.Guardiao,
            ordem: 1);

        Assert.False(resultado.IsValid);
        Assert.Equal(409, resultado.HttpStatus);
        Assert.Equal("ORDEM_CONTATO_DUPLICADA", resultado.ErrorCode);
    }

    [Theory]
    [InlineData("Remédio da pressão", "08:00", true, true)]
    [InlineData("Tomar água", "23:59", true, true)]
    [InlineData("", "08:00", true, false)]
    [InlineData("Remédio da pressão", "25:00", true, false)]
    [InlineData("Remédio da pressão", "8h", true, false)]
    [InlineData("Remédio da pressão", "08:00", false, false)]
    public void ValidarLembrete_DeveValidarTextoLivreHorarioEConsentimento(
        string texto,
        string horario,
        bool consentimentoAtivo,
        bool esperadoValido)
    {
        var resultado = ZeloBusinessRules.ValidarLembrete(texto, horario, consentimentoAtivo);
        Assert.Equal(esperadoValido, resultado.IsValid);
    }

    [Fact]
    public void DeveMarcarComoNaoConfirmado_DeveRespeitarLimiteExatoDe60Minutos()
    {
        var previsto = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);
        var registro = new LembreteRegistro
        {
            Id = "reg1",
            LembreteId = "lem1",
            PrevistoPara = previsto,
            Status = StatusLembreteRegistro.Pendente
        };

        // Aos 59 minutos: ainda PENDENTE
        Assert.False(ZeloBusinessRules.DeveMarcarComoNaoConfirmado(registro, previsto.AddMinutes(59)));

        // Aos 60 minutos exatos: vira NAO_CONFIRMADO (Regra R5)
        Assert.True(ZeloBusinessRules.DeveMarcarComoNaoConfirmado(registro, previsto.AddMinutes(60)));
    }
}
