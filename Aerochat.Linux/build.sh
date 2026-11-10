#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

CONFIG="${CONFIG:-Debug}"
PUBLISH=0

for arg in "$@"; do
  case "$arg" in
    publish|--publish|-p)
      PUBLISH=1
      ;;
    release|--release|-r)
      CONFIG=Release
      ;;
    debug|--debug|-d)
      CONFIG=Debug
      ;;
    -h|--help)
      cat <<'EOF'
Uso: ./build.sh [opciones]

Opciones:
  --release, -r   Compila en Release (por defecto: Debug).
  --publish, -p   Además publica un único binario autocontenido para linux-x64.
  --help, -h      Muestra esta ayuda.

Variables:
  CONFIG=Release  Equivalente a --release.
EOF
      exit 0
      ;;
    *)
      echo "Opción desconocida: $arg" >&2
      exit 1
      ;;
  esac
done

echo "==> Compilando Aerochat.Linux ($CONFIG)..."
dotnet build Aerochat.Linux.slnx -c "$CONFIG" -v minimal

if [[ "$PUBLISH" -eq 1 ]]; then
  echo
  echo "==> Publicando binario único (linux-x64)..."

  dotnet publish Aerochat.Linux.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

  echo
  echo "Binario publicado:"
  ls -lh bin/Release/net10.0/linux-x64/publish/Aerochat
fi
