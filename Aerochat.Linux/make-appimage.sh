#!/usr/bin/env bash
#
# Crea un AppImage autocontenido de Aerochat.
#
# El binario se publica con los mismos flags que ./build.sh --publish y se empaqueta
# con appimagetool. Si appimagetool no está instalado se descarga en la caché del
# usuario. El icono del AppImage es Assets/MainWnd.png.
#
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

DIST_DIR="dist"
APPDIR="$DIST_DIR/Aerochat.AppDir"
PUBLISH_DIR="$DIST_DIR/publish"
OUTPUT="$DIST_DIR/Aerochat-x86_64.AppImage"
ICON_SOURCE="Assets/MainWnd.png"

for arg in "$@"; do
  case "$arg" in
    -h|--help)
      cat <<'EOF'
Uso: ./make-appimage.sh

Genera dist/Aerochat-x86_64.AppImage a partir de un binario autocontenido
para linux-x64. Descarga appimagetool si no está en PATH.

Variables:
  APPIMAGETOOL   Ruta a un appimagetool concreto (opcional).
  ARCH           Arquitectura destino (por defecto: x86_64).
EOF
      exit 0
      ;;
    *)
      echo "Opción desconocida: $arg" >&2
      exit 1
      ;;
  esac
done

if [[ ! -f "$ICON_SOURCE" ]]; then
  echo "No se encontró el icono $ICON_SOURCE" >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet no está instalado o no está en PATH." >&2
  exit 1
fi

echo "==> Publicando Aerochat (linux-x64, binario único)..."
rm -rf "$PUBLISH_DIR"
dotnet publish Aerochat.Linux.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$PUBLISH_DIR" \
  -v minimal

if [[ ! -x "$PUBLISH_DIR/Aerochat" ]]; then
  echo "La publicación no generó $PUBLISH_DIR/Aerochat" >&2
  exit 1
fi

echo
echo "==> Creando AppDir..."
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" \
         "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/256x256/apps"

cp "$PUBLISH_DIR/Aerochat" "$APPDIR/usr/bin/Aerochat"
chmod +x "$APPDIR/usr/bin/Aerochat"

# Icono: raíz del AppDir, .DirIcon y tema hicolor.
cp "$ICON_SOURCE" "$APPDIR/aerochat.png"
cp "$ICON_SOURCE" "$APPDIR/.DirIcon"
cp "$ICON_SOURCE" "$APPDIR/usr/share/icons/hicolor/256x256/apps/aerochat.png"

cat > "$APPDIR/aerochat.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Aerochat
GenericName=Instant Messenger
Comment=Windows Live Messenger style Discord client
Exec=Aerochat
Icon=aerochat
Terminal=false
Categories=Network;InstantMessaging;
StartupWMClass=Aerochat
EOF
cp "$APPDIR/aerochat.desktop" "$APPDIR/usr/share/applications/aerochat.desktop"

cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/Aerochat" "$@"
EOF
chmod +x "$APPDIR/AppRun"

find_appimagetool() {
  if [[ -n "${APPIMAGETOOL:-}" ]]; then
    echo "$APPIMAGETOOL"
    return
  fi

  if command -v appimagetool >/dev/null 2>&1; then
    command -v appimagetool
    return
  fi

  local cache_dir="${XDG_CACHE_HOME:-$HOME/.cache}/aerochat/appimage-tools"
  local tool="$cache_dir/appimagetool-x86_64.AppImage"

  if [[ ! -x "$tool" ]]; then
    mkdir -p "$cache_dir"
    echo "==> Descargando appimagetool en $cache_dir..." >&2
    curl -fL --retry 3 --connect-timeout 20 \
      -o "$tool" \
      "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "$tool"
  fi

  echo "$tool"
}

APPIMAGETOOL_BIN="$(find_appimagetool)"

echo
echo "==> Empaquetando $OUTPUT..."
rm -f "$OUTPUT"

# APPIMAGE_EXTRACT_AND_RUN permite usar appimagetool en sistemas sin FUSE.
export APPIMAGE_EXTRACT_AND_RUN=1
export ARCH="${ARCH:-x86_64}"

"$APPIMAGETOOL_BIN" --no-appstream "$APPDIR" "$OUTPUT"

chmod +x "$OUTPUT"

echo
echo "AppImage creado:"
ls -lh "$OUTPUT"
