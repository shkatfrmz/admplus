#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
export DEBIAN_FRONTEND=noninteractive

need_cmd() {
  command -v "$1" >/dev/null 2>&1
}

apt_install() {
  if [ "$(id -u)" -ne 0 ] || ! need_cmd apt-get; then
    return 1
  fi
  apt-get update -y
  apt-get install -y "$@"
}

echo "==> Admplus bootstrap"

if ! need_cmd curl; then
  echo "==> Installing curl"
  apt_install curl ca-certificates || {
    echo "Install curl, then re-run ./start.sh"
    exit 1
  }
fi

if ! need_cmd node || ! need_cmd npm; then
  echo "==> Installing Node.js 20 LTS (user-local)"
  NODE_VERSION="v20.18.1"
  NODE_DIR="${HOME}/.local/node"
  ARCH="$(uname -m)"
  case "$ARCH" in
    x86_64) NODE_ARCH="x64" ;;
    aarch64|arm64) NODE_ARCH="arm64" ;;
    *)
      echo "Unsupported CPU architecture: $ARCH"
      exit 1
      ;;
  esac
  mkdir -p "$NODE_DIR"
  curl -fsSL "https://nodejs.org/dist/${NODE_VERSION}/node-${NODE_VERSION}-linux-${NODE_ARCH}.tar.xz" \
    | tar -xJ -C "$NODE_DIR" --strip-components=1
fi

if [ -d "${HOME}/.local/node/bin" ]; then
  export PATH="${HOME}/.local/node/bin:$PATH"
fi

if ! need_cmd node || ! need_cmd npm; then
  echo "Node.js is required. Install Node 20+ and re-run ./start.sh"
  exit 1
fi

echo "==> Node $(node -v) / npm $(npm -v)"

if [ "$(id -u)" -eq 0 ] && need_cmd apt-get; then
  if ! ldconfig -p 2>/dev/null | grep -q 'libicu'; then
    echo "==> Installing ICU (required by .NET)"
    apt_install libicu-dev libicu72 || apt_install libicu-dev || true
  fi
fi

DOTNET_INSTALL_DIR="${DOTNET_ROOT:-${HOME}/.dotnet}"
need_dotnet8=1
if need_cmd dotnet && dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
  need_dotnet8=0
elif [ -x "${DOTNET_INSTALL_DIR}/dotnet" ] && "${DOTNET_INSTALL_DIR}/dotnet" --list-sdks 2>/dev/null | grep -q '^8\.'; then
  need_dotnet8=0
elif [ -x /usr/share/dotnet/dotnet ] && /usr/share/dotnet/dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
  DOTNET_INSTALL_DIR=/usr/share/dotnet
  need_dotnet8=0
fi

if [ "$need_dotnet8" -eq 1 ]; then
  echo "==> Installing .NET 8 SDK (user-local)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$DOTNET_INSTALL_DIR"
fi

export DOTNET_ROOT="${DOTNET_INSTALL_DIR}"
export PATH="${DOTNET_ROOT}:${DOTNET_ROOT}/tools:${PATH}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

if ! need_cmd dotnet; then
  echo ".NET SDK is required. Install .NET 8 and re-run ./start.sh"
  exit 1
fi

echo "==> .NET $(dotnet --version)"

echo "==> Installing frontend packages"
cd "$ROOT/frontend"
npm install --no-fund --no-audit

echo "==> Restoring .NET packages"
cd "$ROOT/src/Admplus.Api"
dotnet restore
dotnet build --no-restore -v q

echo "==> Starting API on http://0.0.0.0:3001"
cd "$ROOT/src/Admplus.Api"
dotnet run --no-build --urls http://0.0.0.0:3001 &
BACKEND_PID=$!

cleanup() {
  kill "$BACKEND_PID" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

echo "==> Starting UI on http://0.0.0.0:5173"
cd "$ROOT/frontend"
npm run dev
