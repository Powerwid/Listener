namespace Listener.Api.Data.Entities;

public enum EstadoSolicitud { Pendiente, Verificada, Rechazada, RequiereRevision, Expirada }
public enum EstadoProcesamientoCorreo { Pendiente, Procesado, Descartado, Error }
public enum ResultadoAutenticidad { NoVerificada, Valida, Invalida }
public enum EstadoEvidencia { Pendiente, Validada, Invalida, Duplicada, RequiereRevision }
public enum DireccionMovimiento { Desconocida, Ingreso, Salida }
public enum EstadoPagoInformado { Desconocido, Pendiente, Completado, Fallido, Reversado }
