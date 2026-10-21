# Panel local de Listener

> Pantalla retirada: `/panel` ya no está disponible. Usa `/historial` y `/dispositivos`.
> Consulta la [guía actual](vinculacion-qr.md). Las instrucciones siguientes son históricas.

Actualizacion: el flujo principal ahora es **Activar lector**. Consulta
[lector-automatico.md](lector-automatico.md) para recepcion automatica, persistencia
y API JSON. Las consultas y cargas manuales descritas abajo son diagnosticos.

Desde la carpeta Listener:

```powershell
dotnet run --project src/Listener.Api --configuration Release --launch-profile http
```

Abrir http://localhost:5057/panel. Mantener el proceso abierto mientras se usa el
panel. No hay que volver a ejecutar comandos para cada lectura; los formularios
permiten consultar Gmail y analizar EML. Detener el servidor con Ctrl+C al acabar.

Navegacion: `/panel` muestra el lector y su configuracion;
`/panel?view=resultados` muestra una vista separada del historial, sin desplazamiento
hacia abajo. Admite volver/avanzar del navegador. El historial automatico se refresca
cada 10 segundos mientras esta visible, conservando el filtro y la busqueda. Las
respuestas de diagnostico se muestran al terminar la prueba y no se reemplazan por
el historial hasta seleccionarlo en la navegacion.

## Funciones

- Gmail: correo (por defecto Victorrmq35@gmail.com), contraseña de aplicación de
  esa cuenta, días (1–90) y máximo de correos (1–100). Probar conexión o leer avisos
  BCP. Cada consulta tiene un límite de 45 segundos; si expira, reducir el lote.
- Archivos: seleccionar entre 1 y 20 EML de hasta 1 MiB cada uno. Se procesan sin
  copiarlos al proyecto ni guardarlos en SQL Server.
- Resultados: contadores, tabla, filtro por clasificación y búsqueda por texto,
  detalle de cada correo y respuesta JSON descargable. El JSON conserva el informe
  completo aunque se aplique un filtro visual a la tabla.
- Un resultado extraído sigue pendiente de autenticidad y vinculación con una
  solicitud. No recarga saldos. Transferencias requieren una plantilla real.

Los resultados manuales permanecen solo en la pagina; el lector automatico guarda
su historial en SQL. La contraseña nunca se rellena en la respuesta ni se guarda
en disco; en modo automatico permanece en memoria hasta detenerlo o reiniciar.
Cerrar el navegador no detiene un lector automatico activo.

## Alcance de esta primera versión

El panel está habilitado únicamente en Development y para conexiones de loopback
con host localhost/127.0.0.1/::1. En Production devuelve 404. Tiene antiforgery en
los formularios, salida HTML escapada, límites de tamaño y cabeceras no-store/CSP.
No debe publicarse mediante un proxy o túnel: todavía no tiene usuarios ni login.
Para producción faltan autenticación administrativa, HTTPS, OAuth/gestión de
secretos y el contrato autenticado con TonysGo. El lector automático ya persiste
resultados y auditoría; el panel local no debe exponerse por un proxy o túnel.

Se conserva la API y todos los comandos de terminal. La página reutiliza los
mismos analizadores. No se añadieron dependencias de frontend, fuentes externas
ni scripts de CDN; los estilos y el JavaScript se sirven localmente.
