#!/bin/sh
set -eu

REPO="ms-sk/sshocked"
BINARY="ssk"
INSTALL_DIR="/usr/local/bin"

info()  { printf "\033[1;34m==>\033[0m %s\n" "$*"; }
ok()    { printf "\033[1;32m ✓\033[0m %s\n" "$*"; }
die()   { printf "\033[1;31m!!\033[0m %s\n" "$*" >&2; exit 1; }

[ "$(id -u)" -ne 0 ] && die "Root permissions required to write to ${INSTALL_DIR}. Run with: curl -fsSL ... | sudo sh"

OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
ARCH="$(uname -m)"

case "$OS" in linux) ;; darwin) OS="osx" ;;
  *) die "Unsupported OS: $(uname -s). Supported: Linux, macOS." ;;
esac

case "$ARCH" in x86_64|amd64) ARCH="x64" ;; aarch64|arm64) ARCH="arm64" ;;
  *) die "Unsupported architecture: $(uname -m). Supported: x86_64, aarch64." ;;
esac

RID="${OS}-${ARCH}"
case "$RID" in
  linux-x64|linux-arm64|osx-x64) ;;
  *) die "No release package for ${RID}. Supported: linux-x64, linux-arm64, osx-x64." ;;
esac
info "Detected: ${RID}"

info "Fetching latest release..."
TAG="$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" | sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p')"
[ -z "$TAG" ] && die "Could not determine the latest release tag."

DOWNLOAD_URL="https://github.com/${REPO}/releases/download/${TAG}/sshocked-${RID}.tar.gz"
info "Downloading sshocked ${TAG}..."

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

curl -fsSL "$DOWNLOAD_URL" -o "${TMP_DIR}/sshocked.tar.gz" || die "Download failed for ${RID}."

tar -xzf "${TMP_DIR}/sshocked.tar.gz" -C "$TMP_DIR" || die "Extraction failed."

install -m 755 "${TMP_DIR}/${BINARY}" "${INSTALL_DIR}/${BINARY}" || die "Installation failed."

ok "sshocked ${TAG} installed to ${INSTALL_DIR}/${BINARY}"
info "Run 'ssk --help' to get started."
