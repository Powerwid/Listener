# Tony's Go BBVA Listener para Android

Aplicación nativa C#/.NET 10 que captura únicamente avisos de BBVA Empresas.
Extrae nombre completo e importe de «Cobro con QR» y los envía a Listener.Api.
La comparación y acreditación de recargas corresponden a AppTaxi.

## Conectar mediante QR

1. Prepara el administrador y aplica las migraciones de vinculación en Listener.Api.
2. Abre `/dispositivos` en la web de Listener e inicia sesión.
3. Pulsa **Generar QR de vinculación**.
4. En Android abre **Conexión → Escanear QR de vinculación** y confirma el servidor.
5. Habilita acceso a notificaciones y pulsa **Probar conexión con Listener**.

No hay formularios de nombre/identificador ni clave. El identificador se genera
internamente y la credencial queda cifrada con Android Keystore. El QR vence en
cinco minutos y solo funciona una vez. La web permite revocar equipos vinculados.
El lector requiere Servicios de Google Play y puede descargar su módulo la primera vez.

## Historial en el teléfono

**Historial BBVA** conserva los últimos 200 avisos con nombre, importe, fecha y
estado de envío. Toca una fila para ver el mensaje original. Los pendientes se
conservan en una cola independiente, incluso sin conexión. **Recibido en Listener**
confirma recepción en el servidor, no acreditación de saldo.

## Ejecutar desde Visual Studio

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