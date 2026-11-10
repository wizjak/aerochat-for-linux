# Aerochat.Linux

Reimplementación de [Aerochat](https://github.com/obscure-labs/aerochat) para Linux usando
Avalonia. Mantiene la estética de Windows Live Messenger 2009 (contactos, ventanas de chat,
barra de título azul) pero usa Discord como backend.

Por ahora **solo chat de texto con archivos e imágenes**: no hay voz ni llamadas.

## Estado

- Inicio de sesión con token de usuario de Discord (sin WebView ni captcha).
- Lista de contactos con chats directos y servidores/canales.
- Ventana de chat con historial (últimos 100 mensajes), envío, edición, borrado,
  indicador de "está escribiendo" y avatares.
- **Multimedia**: las imágenes se muestran dentro del chat y se abren en un visor WLM con
  opciones de abrir y copiar; los demás archivos se descargan y abren con la aplicación
  predeterminada del escritorio. Se pueden adjuntar hasta 10 archivos por mensaje (25 MB
  cada uno) con el botón **Attach**, arrastrando archivos a la ventana o pegando una imagen
  del portapapeles. La franja de adjuntos muestra miniaturas antes de enviar.
- **Portapapeles**: pegar texto o imágenes en la entrada, copiar el texto de un mensaje o
  el enlace de un adjunto desde el menú contextual, y copiar imágenes desde el visor.
- **Permisos**: Aerochat comprueba `SendMessages`, `AttachFiles` y `ReadMessageHistory`.
  Si no puedes escribir o adjuntar, el compositor se desactiva y muestra el motivo.
- **Notificaciones**: avisos emergentes estilo WLM para mensajes directos, menciones y
  amigos que se conectan, con sonido (`Assets/notify.mp3`) y opciones en **Alerts**.
  Al hacer clic en el aviso se abre el chat correspondiente.
- **Bandeja del sistema**: cerrar la ventana de contactos la oculta en la bandeja en vez
  de cerrar el programa; el icono permite reabrirla o salir de verdad. Las notificaciones
  y los chats siguen funcionando en segundo plano.
- Escenas de Windows Live Messenger: la barra de título, cabecera, árbol, entrada de chat
  y burbujas se tiñen con el color de la escena activa.
- Selector de escenas (botón **Scenes**) con miniaturas; la elección se guarda localmente.
- Por defecto se usa la escena que coincide con el color del banner del perfil de Discord.
  Los chats 1:1 muestran la escena de la otra persona; grupos y canales usan la escena propia.
  Cambiar de escena **no** modifica el perfil de Discord.
- Estados: Disponible, Ocupado, Ausente, Aparecer desconectado.
- Recordar token entre ejecuciones.
- Interfaz WLM 2009 reconstruida con los recursos originales de Aerochat: barra de título
  con botones Vista, listas de contactos compactas con iconos de estado, marcos de foto de
  perfil (XS/S/M/L/XL), barras de desplazamiento de 17 px, botones y cajas de texto
  nine-slice, y burbuja de entrada con cola de diálogo.
- Los chats 1:1 usan el marco y el estado de la otra persona; los canales y grupos usan el
  icono del servidor y el estado propio.

## Requisitos

- Linux x64.
- Una cuenta de Discord y su **token de usuario**.
- Para compilar: .NET SDK 10.

## Ejecutar desde el código

Desde `Aerochat.Linux/`:

```bash
dotnet run --project Aerochat.Linux.csproj
```

## Compilar

Todo se hace desde `Aerochat.Linux/`:

```bash
./build.sh            # compila en Debug
./build.sh --release  # compila en Release
./build.sh --publish  # compila y genera el binario único
```

O directamente con `dotnet`:

```bash
dotnet build Aerochat.Linux.slnx -c Release
```

### Compilar un único binario

```bash
dotnet publish Aerochat.Linux.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

El resultado queda en:

```
Aerochat.Linux/bin/Release/net10.0/linux-x64/publish/Aerochat
```

Es un único archivo ejecutable. No necesita .NET instalado en la máquina destino.

## Generar un AppImage

Desde `Aerochat.Linux/`:

```bash
./make-appimage.sh
```

El script publica el binario autocontenido, monta el AppDir con el icono
`Assets/MainWnd.png` y genera:

```
Aerochat.Linux/dist/Aerochat-x86_64.AppImage
```

Si `appimagetool` no está instalado, se descarga automáticamente en
`$XDG_CACHE_HOME/aerochat/appimage-tools/`. El AppImage resultante se puede ejecutar
directamente (requiere FUSE) o con `APPIMAGE_EXTRACT_AND_RUN=1` en sistemas sin FUSE.

## Bandeja del sistema

- Cerrar la ventana principal (o pulsar el botón **Close**) **no** termina el programa:
  se oculta en la bandeja y sigue recibiendo mensajes y notificaciones.
- Clic en el icono: muestra u oculta la ventana principal.
- Menú del icono: **Open Windows Live Messenger** y **Exit Aerochat**.
- Cerrar la ventana de inicio de sesión sin haber entrado sí termina el programa.
- La bandeja usa StatusNotifierItem (KDE, GNOME con extensión, XFCE, Cinnamon, etc.).
  Si el escritorio no ofrece bandeja, cerrar la ventana principal sale de la aplicación
  para no dejarla inaccesible.

## Configuración

El token y las preferencias se guardan en:

```
$XDG_CONFIG_HOME/aerochat/config.json
```

o, si `XDG_CONFIG_HOME` no está definido:

```
~/.config/aerochat/config.json
```

El archivo se crea con permisos `0600`. El token se guarda en texto plano; si no quieres
que se guarde, desmarca "Remember me on this computer" al iniciar sesión. Las preferencias
de alertas (mensajes directos, menciones, amigos en línea y sonido) se guardan aquí.

Los adjuntos descargados y el sonido extraído se guardan en:

```
$XDG_CACHE_HOME/aerochat/
```

o, por defecto, `~/.cache/aerochat/`.

### Sonido de notificación

El MP3 se reproduce con el primer reproductor disponible en `PATH`: `mpv`, `ffplay`,
`paplay`, `pw-play`, `cvlc`, `gst-play-1.0`, `mpg123` o `aplay`. Si no hay ninguno, la
aplicación sigue funcionando en silencio.

## Cómo obtener el token

1. Abre Discord en el navegador e inicia sesión.
2. Abre las herramientas de desarrollador (F12) → pestaña Network/Red.
3. Recarga la página y busca una petición a `discord.com/api`.
4. Copia el valor de la cabecera `Authorization` (no lleva la palabra `Bearer`).
5. Pégalo en el campo "Discord token".

## Aviso

Usar un cliente no oficial con una cuenta de usuario **viola los Términos de Servicio de
Discord** y puede provocar la suspensión de la cuenta. Úsalo con una cuenta secundaria y
bajo tu responsabilidad.

## Estructura

```
Aerochat.Linux/
├── App.axaml(.cs)          # ciclo de vida, bandeja y ventanas
├── Services/               # Discord, ajustes, avatares, multimedia, sonido y escenas
├── ViewModels/             # home, chat, mensajes, adjuntos, avisos y escenas
├── Views/                  # Login, Home, Chat, visor de imágenes y avisos WLM
├── Styles/WlmTheme.axaml   # colores y estilos estilo WLM
├── Assets/                 # recursos copiados del Aerochat original
├── build.sh                # compilación y publicación
└── make-appimage.sh        # empaquetado AppImage
```
