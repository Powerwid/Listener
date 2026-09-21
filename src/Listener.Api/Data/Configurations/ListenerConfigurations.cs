using Listener.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listener.Api.Data.Configurations;

public sealed class SolicitudConfiguration : IEntityTypeConfiguration<SolicitudVerificacion>
{
    public void Configure(EntityTypeBuilder<SolicitudVerificacion> b)
    {
        b.ToTable("SolicitudesVerificacion", t =>
        {
            t.HasCheckConstraint("CK_Solicitudes_Monto", "[MontoEsperado] > 0");
            t.HasCheckConstraint("CK_Solicitudes_Simulacion", "[EsSimulacion] = 0 OR [Estado] <> 'Verificada'");
            t.HasCheckConstraint("CK_Solicitudes_Fechas", "[FechaExpiracion] > [FechaCreacion] AND ([FechaResolucion] IS NULL OR [FechaResolucion] >= [FechaCreacion])");
            t.HasCheckConstraint("CK_Solicitudes_Estado", "[Estado] IN ('Pendiente','Verificada','Rechazada','RequiereRevision','Expirada')");
            t.HasCheckConstraint("CK_Solicitudes_Resolucion", "([Estado] IN ('Pendiente','RequiereRevision') AND [FechaResolucion] IS NULL) OR ([Estado] IN ('Verificada','Rechazada','Expirada') AND [FechaResolucion] IS NOT NULL)");
            t.HasCheckConstraint("CK_Solicitudes_Moneda", "LEN([Moneda]) = 3");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ReferenciaExterna).HasMaxLength(100).IsRequired();
        b.Property(x => x.ReferenciaConductor).HasMaxLength(100).IsRequired();
        b.Property(x => x.MontoEsperado).HasPrecision(18, 2);
        b.Property(x => x.Moneda).HasMaxLength(3).IsUnicode(false).IsRequired();
        b.Property(x => x.MedioPago).HasMaxLength(30).IsRequired();
        b.Property(x => x.ReferenciaCuentaReceptora).HasMaxLength(120).IsRequired();
        b.Property(x => x.OperacionDeclarada).HasMaxLength(100);
        b.Property(x => x.Estado).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.CodigoMotivo).HasMaxLength(100);
        b.Property(x => x.VersionFila).IsRowVersion();
        b.HasIndex(x => x.ReferenciaExterna).IsUnique();
        b.HasIndex(x => new { x.Estado, x.FechaExpiracion });
    }
}

public sealed class SincronizacionConfiguration : IEntityTypeConfiguration<EstadoSincronizacionCorreo>
{
    public void Configure(EntityTypeBuilder<EstadoSincronizacionCorreo> b)
    {
        b.ToTable("EstadoSincronizacionCorreo", t =>
        {
            t.HasCheckConstraint("CK_Sincronizacion_UidValidity", "[UidValidity] IS NULL OR [UidValidity] BETWEEN 1 AND 4294967295");
            t.HasCheckConstraint("CK_Sincronizacion_UltimoUid", "[UltimoUidProcesado] BETWEEN 0 AND 4294967295 AND ([UidValidity] IS NOT NULL OR [UltimoUidProcesado] = 0)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Buzon).HasMaxLength(254).IsRequired();
        b.Property(x => x.Carpeta).HasMaxLength(128).IsRequired();
        b.Property(x => x.VersionFila).IsRowVersion();
        b.HasIndex(x => new { x.Buzon, x.Carpeta }).IsUnique();
    }
}

public sealed class CorreoConfiguration : IEntityTypeConfiguration<CorreoProcesado>
{
    public void Configure(EntityTypeBuilder<CorreoProcesado> b)
    {
        b.ToTable("CorreosProcesados", t =>
        {
            t.HasCheckConstraint("CK_Correos_Uids", "[UidValidity] BETWEEN 1 AND 4294967295 AND [Uid] BETWEEN 1 AND 4294967295");
            t.HasCheckConstraint("CK_Correos_Simulacion", "[EsSimulacion] = 0 OR [ResultadoAutenticidad] = 'NoVerificada'");
            t.HasCheckConstraint("CK_Correos_Hash", "LEN([HashContenido]) = 64 AND [HashContenido] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-fA-F]%'");
            t.HasCheckConstraint("CK_Correos_Autenticidad", "[ResultadoAutenticidad] IN ('NoVerificada','Valida','Invalida')");
            t.HasCheckConstraint("CK_Correos_Estado", "[EstadoProcesamiento] IN ('Pendiente','Procesado','Descartado','Error')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.MessageId).HasMaxLength(998);
        b.Property(x => x.Remitente).HasMaxLength(320);
        b.Property(x => x.Asunto).HasMaxLength(1000);
        b.Property(x => x.HashContenido).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.ReferenciaContenidoProtegido).HasMaxLength(1000);
        b.Property(x => x.ResultadoAutenticidad).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.EstadoProcesamiento).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.CodigoMotivo).HasMaxLength(100);
        b.Property(x => x.VersionExtractor).HasMaxLength(40);
        b.Property(x => x.ResultadoJson).HasMaxLength(16000);
        b.Property(x => x.VersionFila).IsRowVersion();
        b.HasOne(x => x.EstadoSincronizacionCorreo).WithMany(x => x.Correos)
            .HasForeignKey(x => x.EstadoSincronizacionCorreoId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.EstadoSincronizacionCorreoId, x.UidValidity, x.Uid }).IsUnique();
        b.HasIndex(x => new { x.EstadoProcesamiento, x.FechaRecepcion });
        // Hash helps identify repeat content but is not a payment identity.
        b.HasIndex(x => x.HashContenido);
    }
}

public sealed class EvidenciaConfiguration : IEntityTypeConfiguration<EvidenciaPago>
{
    public void Configure(EntityTypeBuilder<EvidenciaPago> b)
    {
        b.ToTable("EvidenciasPago", t =>
        {
            t.HasCheckConstraint("CK_Evidencias_Monto", "[Monto] IS NULL OR [Monto] > 0");
            t.HasCheckConstraint("CK_Evidencias_Simulacion", "[EsSimulacion] = 0 OR ([EstadoValidacion] <> 'Validada' AND [SolicitudVerificacionId] IS NULL)");
            t.HasCheckConstraint("CK_Evidencias_Moneda", "[Moneda] IS NULL OR LEN([Moneda]) = 3");
            t.HasCheckConstraint("CK_Evidencias_Estado", "[EstadoValidacion] IN ('Pendiente','Validada','Invalida','Duplicada','RequiereRevision')");
            t.HasCheckConstraint("CK_Evidencias_Direccion", "[DireccionMovimiento] IN ('Desconocida','Ingreso','Salida')");
            t.HasCheckConstraint("CK_Evidencias_EstadoPago", "[EstadoPagoInformado] IN ('Desconocido','Pendiente','Completado','Fallido','Reversado')");
            t.HasCheckConstraint("CK_Evidencias_ValidadaCompleta", "[EstadoValidacion] <> 'Validada' OR ([ProveedorPago] IS NOT NULL AND LEN([ProveedorPago]) > 0 AND [NumeroOperacion] IS NOT NULL AND LEN([NumeroOperacion]) > 0 AND [ReferenciaCuentaReceptora] IS NOT NULL AND LEN([ReferenciaCuentaReceptora]) > 0 AND [Monto] IS NOT NULL AND [Moneda] IS NOT NULL AND [FechaOperacion] IS NOT NULL AND [DireccionMovimiento] = 'Ingreso' AND [EstadoPagoInformado] = 'Completado')");
            t.HasCheckConstraint("CK_Evidencias_Asignacion", "[SolicitudVerificacionId] IS NULL OR [EstadoValidacion] = 'Validada'");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ProveedorPago).HasMaxLength(30);
        b.Property(x => x.ReferenciaSolicitudExtraida).HasMaxLength(100);
        b.Property(x => x.NumeroOperacion).HasMaxLength(100);
        b.Property(x => x.ReferenciaCuentaReceptora).HasMaxLength(120);
        b.Property(x => x.Monto).HasPrecision(18, 2);
        b.Property(x => x.Moneda).HasMaxLength(3).IsUnicode(false);
        b.Property(x => x.ReferenciaPagador).HasMaxLength(200);
        b.Property(x => x.DireccionMovimiento).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.EstadoPagoInformado).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.EstadoValidacion).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.CodigoMotivo).HasMaxLength(100);
        b.Property(x => x.VersionFila).IsRowVersion();
        b.HasOne(x => x.CorreoProcesado).WithMany(x => x.Evidencias)
            .HasForeignKey(x => x.CorreoProcesadoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SolicitudVerificacion).WithOne(x => x.EvidenciaAsignada)
            .HasForeignKey<EvidenciaPago>(x => x.SolicitudVerificacionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.SolicitudVerificacionId).IsUnique().HasFilter("[SolicitudVerificacionId] IS NOT NULL");
        // Untrusted emails must not reserve operation identities. The future verifier
        // sets Validada only after authenticity, direction and matching checks.
        b.HasIndex(x => new { x.ProveedorPago, x.ReferenciaCuentaReceptora, x.NumeroOperacion })
            .IsUnique().HasFilter("[EstadoValidacion] = 'Validada'");
        b.HasIndex(x => new { x.EstadoValidacion, x.FechaOperacion });
    }
}

public sealed class AuditoriaConfiguration : IEntityTypeConfiguration<EventoAuditoria>
{
    public void Configure(EntityTypeBuilder<EventoAuditoria> b)
    {
        b.ToTable("EventosAuditoria");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TipoEvento).HasMaxLength(80).IsRequired();
        b.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        b.Property(x => x.EstadoAnterior).HasMaxLength(32);
        b.Property(x => x.EstadoNuevo).HasMaxLength(32);
        b.Property(x => x.CodigoMotivo).HasMaxLength(100).IsRequired();
        b.Property(x => x.IdCorrelacion).HasMaxLength(100).IsRequired();
        b.HasOne(x => x.SolicitudVerificacion).WithMany()
            .HasForeignKey(x => x.SolicitudVerificacionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CorreoProcesado).WithMany()
            .HasForeignKey(x => x.CorreoProcesadoId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.IdCorrelacion);
        b.HasIndex(x => x.Fecha);
    }
}
