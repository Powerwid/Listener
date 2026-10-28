# Tony's Go BBVA Listener para Android

Aplicación nativa C#/.NET 10 que captura únicamente avisos de BBVA Empresas.
Extrae nombre completo e importe de «Cobro con QR» y los envía a Listener.Api.
La comparación y acreditación de recargas corresponden a AppTaxi.

## Icono de Listener (1.4.1)

El icono del launcher y del encabezado muestra **>LS**, con la flecha amarilla,
letras blancas y fondo morado de Tony’s Go. Utiliza vectores adaptables para
mantener la nitidez y soportar las máscaras de cada launcher, incluidos los
iconos temáticos de Android 13 o posterior. El diseño editable y su referencia
se conservan en [Branding](Branding/README.md). No cambia la identidad del paquete
ni los datos de las instalaciones existentes.

## Interfaz guiada (1.4)

La navegación se organiza en **Inicio**, **Historial** y **Ajustes**. Inicio muestra
el estado real de la lectura de avisos BBVA, internet y la conexión con la web,
con iconos, colores y explicaciones. El indicador general solo aparece en verde
cuando la lectura y el servicio están activos y existe una respuesta válida y
reciente del endpoint autenticado de conexión. Estar vinculado por QR no basta.

La comprobación de la web reutiliza el endpoint existente, al abrir la pantalla
y como máximo una vez cada 30 segundos mientras permanece visible. Se cancela
al salir. Las comprobaciones automáticas no modifican credenciales ni la cola.
El botón de prueba manual mantiene la gestión anterior de accesos revocados.

**Ajustes** conserva todas las acciones anteriores, agrupadas en **Vinculación
con QR**, **Permisos, batería y avisos** y **Ayuda y detalles técnicos**. El
diagnóstico se puede consultar y limpiar. Los mensajes originales, intentos e
identificadores siguen accesibles al abrir el detalle de un aviso del historial.
La captura, persistencia, entrega, reintentos y notificaciones locales mantienen
su comportamiento. El rediseño no introduce reglas de acumulación de pagos.

## Conectar mediante QR

1. Prepara el administrador y aplica las migraciones de vinculación en Listener.Api.
2. Abre `/dispositivos` en la web de Listener e inicia sesión.
3. Pulsa **Generar QR de vinculación**.
4. En Android abre **Ajustes → Vinculación con QR → Escanear QR de vinculación** y confirma el servidor.
5. Habilita acceso a notificaciones y pulsa **Probar conexión con Listener**.

No hay formularios de nombre/identificador ni clave. El identificador se genera
internamente y la credencial queda cifrada con Android Keystore. El QR vence en
cinco minutos y solo funciona una vez. La web permite revocar equipos vinculados.
El lector requiere Servicios de Google Play y puede descargar su módulo la primera vez.

## Historial en el teléfono

**Historial** muestra los últimos 200 avisos con nombre, importe, fecha y estado
de envío. El archivo SQLite privado conserva todos los avisos y los pendientes,
sin el límite de 200 de la lista visible. Toca una fila para ver el mensaje original.
**Recibido en la web de Listener** confirma recepción en el servidor, no acreditación
de saldo. No se utiliza la carpeta de caché, que Android puede eliminar.

## Servicio y avisos locales (1.3)

En **Inicio** puedes comprobar la lectura y la conexión. En **Ajustes → Permisos,
batería y avisos** puedes reactivar la lectura, permitir avisos locales,
probarlos y abrir los ajustes de batería. El estado técnico detallado permanece
en **Ajustes → Ayuda y detalles técnicos**.

1. Concede el acceso a notificaciones de BBVA y el permiso para mostrar avisos de Listener.
2. Pulsa **Permitir batería sin restricciones** y acepta la exclusión si Android la solicita.
3. Si el fabricante ofrece **Inicio automático**, actívalo para Listener desde los ajustes del equipo.
4. Mantén la vinculación QR activa. La escucha continúa sin conexión; el envío se recupera al volver Internet.

El servicio visible usa `specialUse` y no se detiene al retirar la actividad de
recientes. Solicita reconexión si Android desconecta la escucha. Tras un reinicio
o una actualización se recupera mediante los eventos del sistema; después de
encender el equipo hay que desbloquearlo una vez para habilitar sus datos privados.

Cada aviso se confirma primero en SQLite, junto con su estado de envío. La
captura no hace peticiones de red ni reescribe el historial completo en el hilo
de notificaciones. El servicio intenta la entrega inmediatamente y WorkManager
mantiene un respaldo persistente con restricciones de red y reintentos. Los
fallos transitorios esperan progresivamente de 5 segundos a 5 minutos; los
rechazos definitivos quedan visibles para revisión. Los identificadores se
conservan para que recuperar avisos activos o repetir envíos no duplique abonos.

Los avisos propios distinguen **BBVA capturado en Listener** (guardado en el
teléfono) de **Recibido en la web de Listener** (confirmado por la API).
Solo los abonos reconocidos generan este aviso; las notificaciones de la propia
app no se capturan de nuevo. Los demás avisos de BBVA permanecen en el historial local.

El historial y la cola JSON de versiones anteriores se importan una vez a SQLite
sin borrar los archivos originales ni cambiar la identidad de los eventos.
Un archivo de importación dañado no impide recuperar los otros ni captar avisos nuevos.

Android conserva el control del proceso: **Forzar detención**, retirar permisos,
desinstalar o las restricciones del fabricante pueden interrumpir la escucha.
No se puede recuperar un aviso borrado o reemplazado antes de capturarlo.
No se usan alarmas exactas, bucles de sondeo continuos ni un bloqueo de CPU permanente.

## Recuperación de lectura (1.4.2)

El estado verde de lectura depende de `OnListenerConnected`, no solo del permiso
concedido o del servicio visible. Si Android deja la conexión inactiva se reintenta
con espera progresiva. Después de 20 segundos se solicita revisar el registro del
componente, manteniéndolo habilitado y sin retirar el permiso; esta operación tiene
un intervalo mínimo de 15 minutos. A los 45 segundos sin confirmación, Inicio y
el aviso del servicio muestran que la lectura requiere atención, con instrucciones
para reparar el acceso. Los reintentos siguen activos y el estado se recupera al
confirmarse la conexión real.

La recuperación se comprueba también al abrir la app, retirar su pantalla de
recientes, desbloquear el equipo y recibir el arranque o la actualización del
paquete. Un trabajo único de WorkManager comprueba el servicio y los envíos
pendientes como respaldo, con un intervalo mínimo de 15 minutos. Android puede
aplazarlo; no garantiza un reinicio inmediato ni evita las restricciones del sistema.
No se abre el historial ni la credencial antes de desbloquear el almacenamiento
privado. Al conectarse la escucha se recuperan los avisos BBVA que aún estén activos.

En Huawei: **Ajustes → Batería → Inicio de aplicaciones → Tony’s Go BBVA Listener**.
Desactiva **Gestionar automáticamente** y permite **Inicio automático**, **Inicio
secundario** y **Ejecutar en segundo plano**. La app incluye esta guía y abre los
ajustes públicos, ya que algunas pantallas privadas de Huawei requieren permisos
reservados al fabricante. Si el permiso figura activo pero la lectura no se
recupera, desactiva y vuelve a activar solo Listener en Acceso a notificaciones.

## Ejecutar desde Visual Studio

Para generar el APK instalable desde la raíz del proyecto:

```powershell
dotnet build android-listener/TonysGo.YapeListener.Android.csproj -c Release
```

El archivo para compartir se genera automáticamente como
`android-listener/bin/Release/net10.0-android/TonysGo-BBVA-Listener.apk`.
Es una copia del APK firmado; el identificador interno se conserva para actualizar
las instalaciones existentes. En una publicación también se incluye en `publish/`.

Abre `Listener.sln`, selecciona `TonysGo.YapeListener.Android` como inicio y conecta
el teléfono con depuración USB. Selecciona Debug y pulsa F5. Se mantiene el
ApplicationId anterior para actualizar la instalación y conservar el historial.
Se requiere Android 8.0/API 26 o posterior y el workload Android de .NET 10.

Para pruebas locales por USB inicia la API con el perfil `http` y ejecuta:

```powershell
adb reverse tcp:5057 tcp:5057
```

Genera el QR desde `http://localhost:5057/dispositivos`; el APK Debug acepta
loopback HTTP mediante ese puente. Release exige un servidor HTTPS.

[Preparación del servidor y vinculación QR](../docs/vinculacion-qr.md)

[Contrato de entrega de abonos a AppTaxi](../docs/bbva-notificaciones-android.md)
