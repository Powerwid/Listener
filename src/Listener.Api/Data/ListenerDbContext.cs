using Listener.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Listener.Api.Data;

public sealed class ListenerDbContext(DbContextOptions<ListenerDbContext> options) : DbContext(options)
{
    public DbSet<SolicitudVerificacion> SolicitudesVerificacion => Set<SolicitudVerificacion>();
    public DbSet<CorreoProcesado> CorreosProcesados => Set<CorreoProcesado>();
    public DbSet<EvidenciaPago> EvidenciasPago => Set<EvidenciaPago>();
    public DbSet<EstadoSincronizacionCorreo> EstadoSincronizacionCorreo => Set<EstadoSincronizacionCorreo>();
    public DbSet<EventoAuditoria> EventosAuditoria => Set<EventoAuditoria>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ListenerDbContext).Assembly);
    }
}
