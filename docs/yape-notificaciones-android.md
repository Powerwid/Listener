# Listener de notificaciones Yape

## Flujo implementado

1. La app C#/.NET `android-listener` se instala en el mismo Android donde esta Yape.
2. Android entrega a `NotificationListenerService` las notificaciones publicadas.
3. La app ignora todos los paquetes excepto `com.bcp.innovacxion.yapeapp`.
4. El evento se envia por HTTPS a `POST /api/v1/yape/notifications`.
5. Listener extrae monto y codigo de seguridad de tres digitos y guarda el evento.
6. TonysGo envia monto, conductor, cuenta receptora y codigo a
   `POST /api/v1/yape/verifications`.
7. Listener aprueba solo si hay exactamente una notificacion no utilizada que
   coincide en codigo, monto, moneda, cuenta y ventana de tiempo.

El orden no importa: la solicitud de TonysGo puede llegar antes o despues de la
notificacion. Las referencias de TonysGo y los eventos Android son idempotentes.

## Configuracion del servidor

Para desarrollo local, `Listener.Api` carga automáticamente
`src/Listener.Api/.env`. El repositorio incluye `.env.example` como plantilla y
el `.env` real permanece ignorado por Git. Las variables del sistema siempre
tienen prioridad sobre el archivo local.

No guardes claves reales en `appsettings.json`. Configura secretos de al menos 32
caracteres mediante variables del servidor o User Secrets:

```powershell
dotnet user-secrets set "ListenerSecurity:CollectorApiKey" "CLAVE-ALEATORIA-COLECTOR-DE-32-O-MAS" --project src/Listener.Api
dotnet user-secrets set "ListenerSecurity:TonysGoApiKey" "CLAVE-ALEATORIA-TONYSGO-DE-32-O-MAS" --project src/Listener.Api
dotnet user-secrets set "Yape:CuentaReceptora" "TONYSGO-YAPE-PRINCIPAL" --project src/Listener.Api
dotnet user-secrets set "TonysGo:CallbackUrl" "https://api.tonysgo.pe/api/internal/yape/approvals" --project src/Listener.Api
dotnet user-secrets set "TonysGo:CallbackApiKey" "CLAVE-ALEATORIA-CALLBACK-DE-32-O-MAS" --project src/Listener.Api
```

En produccion usa las variables:

- `ListenerSecurity__CollectorApiKey`
- `ListenerSecurity__TonysGoApiKey`
- `Yape__CuentaReceptora`
- `TonysGo__CallbackUrl`
- `TonysGo__CallbackApiKey`
- `ConnectionStrings__ListenerDb`

Los endpoints devuelven `503 SEGURIDAD_NO_CONFIGURADA` mientras falte la clave
correspondiente. Produccion requiere HTTPS con certificado valido.

## Instalar el capturador Android

Abre `Listener.sln` en Visual Studio, selecciona
`TonysGo.YapeListener.Android` como proyecto de inicio, conecta el Android
dedicado por USB y pulsa `F5`. En la pantalla configura:

- URL publica HTTPS de Listener, sin la ruta `/api`.
- Identificador estable del equipo, por ejemplo `yape-caja-01`.
- La misma clave configurada como `CollectorApiKey`.

Pulsa **Abrir acceso a notificaciones** y permite el acceso solamente a la app
Tony's Go Yape Listener. Android puede suspender procesos en segundo plano; excluye
esta app de optimizacion de bateria en el telefono dedicado.

## Contrato que usa TonysGo

Peticion autenticada con el header `X-Listener-Api-Key`:

```http
POST /api/v1/yape/verifications
Content-Type: application/json
X-Listener-Api-Key: CLAVE_TONYSGO

{
  "referenciaExterna": "RECARGA-8d41a4ed",
  "referenciaConductor": "6b7ce0e7-4f55-4779-8070-a4717ed04c82",
  "montoEsperado": 25.50,
  "moneda": "PEN",
  "medioPago": "YAPE",
  "referenciaCuentaReceptora": "TONYSGO-YAPE-PRINCIPAL",
  "codigoSeguridad": "837"
}
```

Respuesta verificada:

```json
{
  "id": "3df7902c-a29b-4276-8c06-86e31ad26fbf",
  "referenciaExterna": "RECARGA-8d41a4ed",
  "referenciaConductor": "6b7ce0e7-4f55-4779-8070-a4717ed04c82",
  "montoEsperado": 25.50,
  "moneda": "PEN",
  "medioPago": "YAPE",
  "codigoSeguridad": "837",
  "estado": "Verificada",
  "codigoMotivo": "CODIGO_MONTO_CUENTA_COINCIDEN"
}
```

Consulta posterior, con la misma clave:

```http
GET /api/v1/yape/verifications/RECARGA-8d41a4ed
X-Listener-Api-Key: CLAVE_TONYSGO
```

Estados esperados:

- `Pendiente`: aun no llego la notificacion con ese codigo.
- `Verificada`: existe una coincidencia exacta y la evidencia quedo consumida.
- `RequiereRevision`: existe conflicto, datos distintos o mas de una coincidencia.
- `Expirada`: termino la vigencia de la solicitud.

TonysGo debe acreditar saldo solamente ante `estado = Verificada`, dentro de una
transaccion local y con `referenciaExterna` unica. Repetir una consulta o peticion
no debe volver a acreditar la recarga.

## Conexion incluida en Tony's Go

`AppTaxi.Api` ya incluye dos extremos:

- `POST /api/recargas/yape`, autenticado con el JWT del conductor. Recibe
  `solicitudId`, `monto` y `codigoSeguridad`, obtiene el conductor del token y crea
  la solicitud en Listener.
- `POST /api/internal/yape/approvals`, llamado automaticamente por Listener cuando
  encuentra la coincidencia. El movimiento de cartera usa `verificationId` como
  clave primaria, por lo que un reintento no duplica el saldo.

Configura en el servidor de Tony's Go:

- `YapeListener__BaseUrl`: URL HTTPS base de Listener.
- `YapeListener__TonysGoApiKey`: igual a `ListenerSecurity__TonysGoApiKey`.
- `YapeListener__CallbackApiKey`: igual a `TonysGo__CallbackApiKey`.
- `YapeListener__CuentaReceptora`: igual a `Yape__CuentaReceptora`.

Ejemplo desde la app movil, usando un `solicitudId` nuevo que debe conservarse para
reintentos:

```http
POST /api/recargas/yape
Authorization: Bearer JWT_DEL_CONDUCTOR
Content-Type: application/json

{
  "solicitudId": "8d41a4ed-4c2d-46f4-8ced-e93acf76ac75",
  "monto": 25.50,
  "codigoSeguridad": "837"
}
```

Cuando se acredita, Tony's Go agrega un movimiento `Recarga`, actualiza el saldo y
crea una notificacion para el conductor. Listener conserva y reintenta el callback
cada cinco segundos hasta recibir una respuesta HTTP exitosa.

La pantalla `CarteraPage` incluye el boton **Recargar con Yape**. Solicita monto y
codigo de tres digitos, conserva un identificador unico durante los reintentos y
consulta el estado mientras espera la notificacion del telefono receptor.

## Prueba inicial obligatoria

El texto exacto de las notificaciones puede cambiar entre versiones de Yape. Antes
de usar dinero real, realiza yapeos controlados de S/ 0.10 y comprueba que la API
devuelva `NOTIFICACION_LISTA_PARA_COMPARAR`, el monto correcto y el mismo codigo de
tres digitos mostrado al pagador. Si devuelve `CODIGO_SEGURIDAD_NO_EXTRAIDO`, captura
los campos `title`, `text` y `bigText` ocultando nombres y envialos para ajustar el
parser.

Este mecanismo confirma que Android recibio una notificacion publicada por el
paquete instalado con el identificador de Yape; no es una API bancaria. El equipo
debe estar bloqueado, actualizado, sin depuracion USB y bajo control de Tony's Go.
