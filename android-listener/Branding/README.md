# Icono de Listener

El icono identifica Listener con **>LS**, manteniendo la flecha amarilla, las
letras blancas inclinadas y el fondo morado de la familia visual de Tony’s Go.

- `listener-icon-approved.png`: concepto aprobado, generado con la herramienta
  integrada de imágenes a partir del icono de Tony’s Go. Se conserva como
  referencia; no se incluye como bitmap en los recursos de la aplicación.
- `listener-icon.svg`: versión vectorial editable del diseño implementado.
- `../Resources/drawable/listener_launcher_foreground.xml`: los mismos tres
  trazados, convertidos a VectorDrawable nativo y centrados en la zona segura.
- `../Resources/drawable/listener_launcher_background.xml`: degradado morado.
- `../Resources/mipmap-anydpi-v26/ic_launcher.xml`: icono adaptable de Android.
- `../Resources/mipmap-anydpi-v33/ic_launcher.xml`: añade la capa monocromática
  para los launchers que permiten iconos temáticos.

La aplicación y su actividad de inicio referencian `@mipmap/ic_launcher`.
El encabezado utiliza ese mismo recurso. La identidad del paquete, los permisos,
la captura y el envío de avisos no se modifican. El icono blanco de estado de las
notificaciones se conserva.

Los trazados principales quedan dentro del área central de 66 × 66 dp de un
lienzo de 108 × 108 dp. El sistema aplica su propia máscara (círculo, cuadrado
redondeado u otra); el recurso no incluye un borde blanco ni una sombra exterior.
Consulta las [especificaciones de iconos adaptables de Android](https://developer.android.com/develop/ui/compose/system/icon_design_adaptive).

## Prompt del concepto aprobado

Modo utilizado: herramienta integrada `image_gen`, edición de una imagen local.

> Change ONLY the two white letters GO to the exact letters LS (capital L followed
> by capital S), so the complete mark reads >LS, with > represented by the existing
> yellow chevron. Preserve the distinctive purple rounded-square background,
> yellow chevron on the left, bold white slightly forward-slanted geometric
> lettering, gentle depth and shadows, clean composition and polished style of
> the reference. Match the overall scale and visual weight of the original
> two-letter mark. Keep the icon centered and square. No additional text or symbols.

Para los recursos finales se reprodujo el diseño en vectores nativos. Las pruebas
de conversión automática a PNG transparente se descartaron por sus bordes
irregulares y no forman parte de los archivos de la aplicación.
