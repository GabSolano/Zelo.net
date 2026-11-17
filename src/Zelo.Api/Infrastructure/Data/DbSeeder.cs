using Microsoft.EntityFrameworkCore;
using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Enums;
using Zelo.Api.Domain.Services;

namespace Zelo.Api.Infrastructure.Data;

/// <summary>
/// Dados fixos de teste definidos no Documento-base do Zelo (Seção "Dados fixos de teste", pág. 20).
/// </summary>
public static class ZeloSeedConstants
{
    public const string UsuarioDonaMariaId = "00000000-0000-0000-0000-000000000001";
    public const string UsuarioAnaId = "00000000-0000-0000-0000-000000000002";
    public const string UsuarioSeuJoseId = "00000000-0000-0000-0000-000000000003";
    public const string UsuarioAdminId = "00000000-0000-0000-0000-000000000004";
    public const string UsuarioServicoId = "00000000-0000-0000-0000-000000000005";

    public const string IdosoDonaMariaId = "00000000-0000-0000-0000-000000000010";
    public const string ConsentimentoDonaMariaId = "00000000-0000-0000-0000-000000000020";
    public const string CasaDonaMariaId = "00000000-0000-0000-0000-000000000100";

    public const string SensorQuartoId = "00000000-0000-0000-0000-000000001001";
    public const string SensorBanheiroId = "00000000-0000-0000-0000-000000001002";
    public const string SensorCozinhaId = "00000000-0000-0000-0000-000000001003";
    public const string SensorGeladeiraId = "00000000-0000-0000-0000-000000001004";
    public const string SensorEntradaId = "00000000-0000-0000-0000-000000001005";

    public const string LembretePressaoId = "00000000-0000-0000-0000-000000002001";
    public const string LembreteRegistroHojeId = "00000000-0000-0000-0000-000000002101";

    public const string ContatoAnaId = "00000000-0000-0000-0000-000000003001";
    public const string ContatoSeuJoseId = "00000000-0000-0000-0000-000000003002";

    public const string SenhaPadraoTeste = "123456";
}

public static class DbSeeder
{
    public static async Task EnsureSeededAsync(ZeloDbContext db, IClock clock)
    {
        await db.Database.EnsureCreatedAsync();

        if (await db.Usuarios.AnyAsync(u => u.Id == ZeloSeedConstants.UsuarioDonaMariaId))
        {
            return;
        }

        var senhaHash = PasswordHasher.Hash(ZeloSeedConstants.SenhaPadraoTeste);

        db.Usuarios.AddRange(
            new Usuario
            {
                Id = ZeloSeedConstants.UsuarioDonaMariaId,
                Nome = "Dona Maria",
                Email = "maria@zelo.teste",
                Telefone = "+5511999990001",
                Papel = PapeisUsuario.Idoso,
                SenhaHash = senhaHash
            },
            new Usuario
            {
                Id = ZeloSeedConstants.UsuarioAnaId,
                Nome = "Ana (Filha)",
                Email = "ana@zelo.teste",
                Telefone = "+5511999990002",
                Papel = PapeisUsuario.Familiar,
                SenhaHash = senhaHash
            },
            new Usuario
            {
                Id = ZeloSeedConstants.UsuarioSeuJoseId,
                Nome = "Seu José (Vizinho)",
                Email = "jose@zelo.teste",
                Telefone = "+5511999990003",
                Papel = PapeisUsuario.Guardiao,
                SenhaHash = senhaHash
            },
            new Usuario
            {
                Id = ZeloSeedConstants.UsuarioAdminId,
                Nome = "Administrador Zelo",
                Email = "admin@zelo.teste",
                Telefone = "+5511999990004",
                Papel = PapeisUsuario.Admin,
                SenhaHash = senhaHash
            },
            new Usuario
            {
                Id = ZeloSeedConstants.UsuarioServicoId,
                Nome = "Serviço Interno Zelo",
                Email = "servico@zelo.teste",
                Telefone = "+5511999990005",
                Papel = PapeisUsuario.Servico,
                SenhaHash = senhaHash
            });

        db.Idosos.Add(new Idoso
        {
            Id = ZeloSeedConstants.IdosoDonaMariaId,
            UsuarioId = ZeloSeedConstants.UsuarioDonaMariaId,
            Nome = "Dona Maria",
            DataNascimento = "1948-05-12",
            Observacoes = "78 anos, mora sozinha. Rotina matinal habitual por volta das 07h20."
        });

        db.Consentimentos.Add(new Consentimento
        {
            Id = ZeloSeedConstants.ConsentimentoDonaMariaId,
            IdosoId = ZeloSeedConstants.IdosoDonaMariaId,
            VersaoTermo = "1.0",
            AceitoEm = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc),
            RevogadoEm = null
        });

        db.Casas.Add(new Casa
        {
            Id = ZeloSeedConstants.CasaDonaMariaId,
            IdosoId = ZeloSeedConstants.IdosoDonaMariaId,
            Endereco = "Rua das Flores, 120",
            Bairro = "Vila Mariana",
            Cidade = "São Paulo"
        });

        db.Sensores.AddRange(
            new Sensor
            {
                Id = ZeloSeedConstants.SensorQuartoId,
                CasaId = ZeloSeedConstants.CasaDonaMariaId,
                Comodo = ComodosSensor.Quarto,
                TipoSensor = TiposSensor.Presenca,
                Ativo = true
            },
            new Sensor
            {
                Id = ZeloSeedConstants.SensorBanheiroId,
                CasaId = ZeloSeedConstants.CasaDonaMariaId,
                Comodo = ComodosSensor.Banheiro,
                TipoSensor = TiposSensor.Presenca,
                Ativo = true
            },
            new Sensor
            {
                Id = ZeloSeedConstants.SensorCozinhaId,
                CasaId = ZeloSeedConstants.CasaDonaMariaId,
                Comodo = ComodosSensor.Cozinha,
                TipoSensor = TiposSensor.Presenca,
                Ativo = true
            },
            new Sensor
            {
                Id = ZeloSeedConstants.SensorGeladeiraId,
                CasaId = ZeloSeedConstants.CasaDonaMariaId,
                Comodo = ComodosSensor.Geladeira,
                TipoSensor = TiposSensor.Porta,
                Ativo = true
            },
            new Sensor
            {
                Id = ZeloSeedConstants.SensorEntradaId,
                CasaId = ZeloSeedConstants.CasaDonaMariaId,
                Comodo = ComodosSensor.Entrada,
                TipoSensor = TiposSensor.Porta,
                Ativo = true
            });

        db.ContatosApoio.AddRange(
            new ContatoApoio
            {
                Id = ZeloSeedConstants.ContatoAnaId,
                IdosoId = ZeloSeedConstants.IdosoDonaMariaId,
                UsuarioId = ZeloSeedConstants.UsuarioAnaId,
                Papel = PapeisUsuario.Familiar,
                Ordem = 1
            },
            new ContatoApoio
            {
                Id = ZeloSeedConstants.ContatoSeuJoseId,
                IdosoId = ZeloSeedConstants.IdosoDonaMariaId,
                UsuarioId = ZeloSeedConstants.UsuarioSeuJoseId,
                Papel = PapeisUsuario.Guardiao,
                Ordem = 2
            });

        db.Lembretes.Add(new Lembrete
        {
            Id = ZeloSeedConstants.LembretePressaoId,
            IdosoId = ZeloSeedConstants.IdosoDonaMariaId,
            Texto = "Remédio da pressão",
            Horario = "08:00",
            DiasSemana = "SEG,TER,QUA,QUI,SEX,SAB,DOM",
            Ativo = true,
            CriadoPor = ZeloSeedConstants.UsuarioAnaId
        });

        var hoje = clock.UtcNow.Date;
        var previstoHoje8h = new DateTime(hoje.Year, hoje.Month, hoje.Day, 8, 0, 0, DateTimeKind.Utc);

        db.LembreteRegistros.Add(new LembreteRegistro
        {
            Id = ZeloSeedConstants.LembreteRegistroHojeId,
            LembreteId = ZeloSeedConstants.LembretePressaoId,
            PrevistoPara = previstoHoje8h,
            Status = StatusLembreteRegistro.Pendente,
            ConfirmadoEm = null
        });

        await db.SaveChangesAsync();
    }
}
