namespace Zelo.Api.Domain.Entities;

public class Usuario
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefone { get; set; } = string.Empty;
    public string Papel { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;

    public ICollection<UsuarioDispositivo> Dispositivos { get; set; } = new List<UsuarioDispositivo>();
}

public class UsuarioDispositivo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UsuarioId { get; set; } = string.Empty;
    public string PushToken { get; set; } = string.Empty;

    public Usuario? Usuario { get; set; }
}

public class Idoso
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UsuarioId { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string DataNascimento { get; set; } = string.Empty;
    public string? Observacoes { get; set; }

    public Usuario? Usuario { get; set; }
    public ICollection<Consentimento> Consentimentos { get; set; } = new List<Consentimento>();
    public ICollection<Casa> Casas { get; set; } = new List<Casa>();
    public ICollection<ContatoApoio> ContatosApoio { get; set; } = new List<ContatoApoio>();
    public ICollection<Lembrete> Lembretes { get; set; } = new List<Lembrete>();
}

public class Consentimento
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string IdosoId { get; set; } = string.Empty;
    public string VersaoTermo { get; set; } = "1.0";
    public DateTime AceitoEm { get; set; } = DateTime.UtcNow;
    public DateTime? RevogadoEm { get; set; }

    public Idoso? Idoso { get; set; }
}

public class Casa
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string IdosoId { get; set; } = string.Empty;
    public string Endereco { get; set; } = string.Empty;
    public string Bairro { get; set; } = string.Empty;
    public string Cidade { get; set; } = string.Empty;

    public Idoso? Idoso { get; set; }
    public ICollection<Sensor> Sensores { get; set; } = new List<Sensor>();
}

public class Sensor
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CasaId { get; set; } = string.Empty;
    public string Comodo { get; set; } = string.Empty;
    public string TipoSensor { get; set; } = string.Empty;
    public bool Ativo { get; set; } = true;

    public Casa? Casa { get; set; }
}

public class ContatoApoio
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string IdosoId { get; set; } = string.Empty;
    public string UsuarioId { get; set; } = string.Empty;
    public string Papel { get; set; } = string.Empty;
    public int Ordem { get; set; }

    public Idoso? Idoso { get; set; }
    public Usuario? Usuario { get; set; }
}

public class Lembrete
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string IdosoId { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public string Horario { get; set; } = "08:00";
    public string DiasSemana { get; set; } = "SEG,TER,QUA,QUI,SEX,SAB,DOM";
    public bool Ativo { get; set; } = true;
    public string CriadoPor { get; set; } = string.Empty;

    public Idoso? Idoso { get; set; }
    public ICollection<LembreteRegistro> Registros { get; set; } = new List<LembreteRegistro>();
}

public class LembreteRegistro
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string LembreteId { get; set; } = string.Empty;
    public DateTime PrevistoPara { get; set; }
    public string Status { get; set; } = "PENDENTE";
    public DateTime? ConfirmadoEm { get; set; }

    public Lembrete? Lembrete { get; set; }
}

public class IdempotencyRecord
{
    public string Chave { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
