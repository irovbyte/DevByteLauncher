#!/usr/bin/env bash
set -euo pipefail

ARCH="$(uname -m)"
if [ "$ARCH" != "x86_64" ] && [ "$ARCH" != "amd64" ]; then
    echo "DevByte Launcher is currently compiled for x86_64 Linux architecture (detected: $ARCH)."
    exit 1
fi

INSTALL_DIR="$HOME/.local/share/DevByteLauncher"
BIN_DIR="$HOME/.local/bin"
APPS_DIR="$HOME/.local/share/applications"
ICONS_DIR="$HOME/.local/share/icons/hicolor/128x128/apps"

mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$APPS_DIR" "$ICONS_DIR"

echo "==> Downloading DevByte Launcher for Linux x64..."
curl -fsSL "https://github.com/irovbyte/DevByteLauncher/releases/latest/download/DevByteLauncher-linux-x64" -o "$INSTALL_DIR/DevByteLauncher"
chmod +x "$INSTALL_DIR/DevByteLauncher"

ln -sf "$INSTALL_DIR/DevByteLauncher" "$BIN_DIR/devbyte-launcher"

echo "==> Downloading launcher icon..."
curl -fsSL "https://raw.githubusercontent.com/irovbyte/DevByteLauncher/master/Assets/app-icon.png" -o "$ICONS_DIR/devbyte-launcher.png"

echo "==> Creating desktop entry..."
cat << 'EOF' > "$APPS_DIR/devbyte-launcher.desktop"
[Desktop Entry]
Name=DevByte Launcher
Comment=DevByte Homelab Minecraft Fabric Launcher
Exec=$HOME/.local/bin/devbyte-launcher
Icon=devbyte-launcher
Terminal=false
Type=Application
Categories=Game;
Keywords=minecraft;fabric;devbyte;homelab;
StartupWMClass=DevByteLauncher
EOF

sed -i "s|\$HOME|$HOME|g" "$APPS_DIR/devbyte-launcher.desktop"

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "$HOME/.local/share/icons/hicolor" >/dev/null 2>&1 || true
fi

echo "==> Installation complete!"
echo "==> DevByte Launcher is installed to: $INSTALL_DIR/DevByteLauncher"
echo "==> You can run it via desktop application menu or command: devbyte-launcher"
if [[ ":$PATH:" != *":$HOME/.local/bin:"* ]]; then
    echo "==> Note: Make sure $HOME/.local/bin is in your PATH."
fi
