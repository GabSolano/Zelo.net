using Microsoft.EntityFrameworkCore;
using Zelo.Api.Domain.Entities;

namespace Zelo.Api.Infrastructure.Data;

public class ZeloDbContext(DbContextOptions<ZeloDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<UsuarioDispositivo> UsuarioDispositivos => Set<UsuarioDispositivo>();
    public DbSet<Idoso> Idosos => Set<Idoso>();
    public DbSet<Consentimento> Consentimentos => Set<Consentimento>();
    public DbSet<Casa> Casas => Set<Casa>();
    public DbSet<Sensor> Sensores => Set<Sensor>();
    public DbSet<ContatoApoio> ContatosApoio => Set<ContatoApoio>();
    public DbSet<Lembrete> Lembretes => Set<Lembrete>();
    public DbSet<LembreteRegistro> LembreteRegistros => Set<LembreteRegistro>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("USUARIO");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.Nome).HasColumnName("nome").HasMaxLength(150).IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(180).IsRequired();
            entity.Property(e => e.Telefone).HasColumnName("telefone").HasMaxLength(40).IsRequired();
            entity.Property(e => e.Papel).HasColumnName("papel").HasMaxLength(30).IsRequired();
            entity.Property(e => e.SenhaHash).HasColumnName("senha_hash").HasMaxLength(256).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<UsuarioDispositivo>(entity =>
        {
            entity.ToTable("USUARIO_DISPOSITIVO");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.PushToken).HasColumnName("push_token").HasMaxLength(256).IsRequired();
            entity.HasOne(e => e.Usuario)
                .WithMany(u => u.Dispositivos)
                .HasForeignKey(e => e.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Idoso>(entity =>
        {
            entity.ToTable("IDOSO");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Nome).HasColumnName("nome").HasMaxLength(150).IsRequired();
            entity.Property(e => e.DataNascimento).HasColumnName("data_nascimento").HasMaxLength(10).IsRequired();
            entity.Property(e => e.Observacoes).HasColumnName("observacoes").HasMaxLength(500);
            entity.HasOne(e => e.Usuario)
                .WithMany()
                .HasForeignKey(e => e.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Consentimento>(entity =>
        {
            entity.ToTable("CONSENTIMENTO");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.IdosoId).HasColumnName("idoso_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.VersaoTermo).HasColumnName("versao_termo").HasMaxLength(30).IsRequired();
            entity.Property(e => e.AceitoEm).HasColumnName("aceito_em").IsRequired();
            entity.Property(e => e.RevogadoEm).HasColumnName("revogado_em");
            entity.HasOne(e => e.Idoso)
                .WithMany(i => i.Consentimentos)
                .HasForeignKey(e => e.IdosoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Casa>(entity =>
        {
            entity.ToTable("CASA");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.IdosoId).HasColumnName("idoso_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Endereco).HasColumnName("endereco").HasMaxLength(250).IsRequired();
            entity.Property(e => e.Bairro).HasColumnName("bairro").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Cidade).HasColumnName("cidade").HasMaxLength(100).IsRequired();
            entity.HasOne(e => e.Idoso)
                .WithMany(i => i.Casas)
                .HasForeignKey(e => e.IdosoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Sensor>(entity =>
        {
            entity.ToTable("SENSOR");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.CasaId).HasColumnName("casa_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Comodo).HasColumnName("comodo").HasMaxLength(30).IsRequired();
            entity.Property(e => e.TipoSensor).HasColumnName("tipo_sensor").HasMaxLength(30).IsRequired();
            entity.Property(e => e.Ativo).HasColumnName("ativo").IsRequired();
            entity.HasIndex(e => new { e.CasaId, e.Comodo }).IsUnique();
            entity.HasOne(e => e.Casa)
                .WithMany(c => c.Sensores)
                .HasForeignKey(e => e.CasaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ContatoApoio>(entity =>
        {
            entity.ToTable("CONTATO_APOIO");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.IdosoId).HasColumnName("idoso_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Papel).HasColumnName("papel").HasMaxLength(30).IsRequired();
            entity.Property(e => e.Ordem).HasColumnName("ordem").IsRequired();
            entity.HasIndex(e => new { e.IdosoId, e.Ordem }).IsUnique();
            entity.HasOne(e => e.Idoso)
                .WithMany(i => i.ContatosApoio)
                .HasForeignKey(e => e.IdosoId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Usuario)
                .WithMany()
                .HasForeignKey(e => e.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Lembrete>(entity =>
        {
            entity.ToTable("LEMBRETE");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.IdosoId).HasColumnName("idoso_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Texto).HasColumnName("texto").HasMaxLength(250).IsRequired();
            entity.Property(e => e.Horario).HasColumnName("horario").HasMaxLength(5).IsRequired();
            entity.Property(e => e.DiasSemana).HasColumnName("dias_semana").HasMaxLength(60).IsRequired();
            entity.Property(e => e.Ativo).HasColumnName("ativo").IsRequired();
            entity.Property(e => e.CriadoPor).HasColumnName("criado_por").HasMaxLength(36).IsRequired();
            entity.HasOne(e => e.Idoso)
                .WithMany(i => i.Lembretes)
                .HasForeignKey(e => e.IdosoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LembreteRegistro>(entity =>
        {
            entity.ToTable("LEMBRETE_REGISTRO");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.LembreteId).HasColumnName("lembrete_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.PrevistoPara).HasColumnName("previsto_para").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
            entity.Property(e => e.ConfirmadoEm).HasColumnName("confirmado_em");
            entity.HasIndex(e => new { e.LembreteId, e.PrevistoPara }).IsUnique();
            entity.HasOne(e => e.Lembrete)
                .WithMany(l => l.Registros)
                .HasForeignKey(e => e.LembreteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("IDEMPOTENCY_RECORD");
            entity.HasKey(e => e.Chave);
            entity.Property(e => e.Chave).HasColumnName("chave").HasMaxLength(120);
            entity.Property(e => e.RequestHash).HasColumnName("request_hash").HasMaxLength(128).IsRequired();
            entity.Property(e => e.StatusCode).HasColumnName("status_code").IsRequired();
            entity.Property(e => e.ResponseBody).HasColumnName("response_body").IsRequired();
            entity.Property(e => e.CriadoEm).HasColumnName("criado_em").IsRequired();
        });
    }
}
