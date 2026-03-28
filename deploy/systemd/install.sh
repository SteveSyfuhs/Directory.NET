#!/bin/bash
set -euo pipefail

# Directory.NET Linux Installation Script
# Installs Directory.Server and Directory.Web as systemd services

INSTALL_DIR="/opt/directory-net"
SERVICE_USER="directorynet"
SERVICE_GROUP="directorynet"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_info()  { echo -e "${GREEN}[INFO]${NC} $1"; }
log_warn()  { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# Check for root
if [[ $EUID -ne 0 ]]; then
    log_error "This script must be run as root (use sudo)."
    exit 1
fi

# Parse arguments
SERVER_PACKAGE=""
WEB_PACKAGE=""
COSMOS_CONNECTION=""

usage() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Options:"
    echo "  --server-package PATH    Path to Directory.Server published output"
    echo "  --web-package PATH       Path to Directory.Web published output"
    echo "  --cosmos-connection STR   Cosmos DB connection string"
    echo "  --help                   Show this help message"
    echo ""
    echo "Example:"
    echo "  $0 --server-package ./server-publish --web-package ./web-publish \\"
    echo "     --cosmos-connection 'AccountEndpoint=https://...'"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --server-package) SERVER_PACKAGE="$2"; shift 2 ;;
        --web-package)    WEB_PACKAGE="$2"; shift 2 ;;
        --cosmos-connection) COSMOS_CONNECTION="$2"; shift 2 ;;
        --help) usage; exit 0 ;;
        *) log_error "Unknown option: $1"; usage; exit 1 ;;
    esac
done

if [[ -z "$SERVER_PACKAGE" || -z "$WEB_PACKAGE" ]]; then
    log_error "Both --server-package and --web-package are required."
    usage
    exit 1
fi

if [[ ! -d "$SERVER_PACKAGE" ]]; then
    log_error "Server package directory not found: $SERVER_PACKAGE"
    exit 1
fi

if [[ ! -d "$WEB_PACKAGE" ]]; then
    log_error "Web package directory not found: $WEB_PACKAGE"
    exit 1
fi

# Step 1: Create service user and group
log_info "Creating service user and group: $SERVICE_USER"
if ! getent group "$SERVICE_GROUP" > /dev/null 2>&1; then
    groupadd --system "$SERVICE_GROUP"
fi

if ! getent passwd "$SERVICE_USER" > /dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin \
        -g "$SERVICE_GROUP" "$SERVICE_USER"
fi

# Step 2: Create installation directories
log_info "Creating installation directories under $INSTALL_DIR"
mkdir -p "$INSTALL_DIR/server"
mkdir -p "$INSTALL_DIR/web"
mkdir -p "$INSTALL_DIR/data"
mkdir -p "$INSTALL_DIR/certs"
mkdir -p /var/log/directory-net

# Step 3: Copy application binaries
log_info "Copying Directory.Server binaries..."
cp -r "$SERVER_PACKAGE"/. "$INSTALL_DIR/server/"

log_info "Copying Directory.Web binaries..."
cp -r "$WEB_PACKAGE"/. "$INSTALL_DIR/web/"

# Step 4: Set up configuration with Cosmos DB connection string
if [[ -n "$COSMOS_CONNECTION" ]]; then
    log_info "Configuring Cosmos DB connection string..."

    for component in server web; do
        SETTINGS_FILE="$INSTALL_DIR/$component/appsettings.Production.json"
        if [[ -f "$SETTINGS_FILE" ]]; then
            # Use a temporary file to avoid issues with in-place editing
            if command -v jq > /dev/null 2>&1; then
                jq --arg cs "$COSMOS_CONNECTION" '.CosmosDb.ConnectionString = $cs' \
                    "$SETTINGS_FILE" > "$SETTINGS_FILE.tmp" && \
                    mv "$SETTINGS_FILE.tmp" "$SETTINGS_FILE"
            else
                log_warn "jq not found; set CosmosDb__ConnectionString environment variable manually."
            fi
        fi
    done
fi

# Step 5: Set file permissions
log_info "Setting file ownership and permissions..."
chown -R "$SERVICE_USER:$SERVICE_GROUP" "$INSTALL_DIR"
chown -R "$SERVICE_USER:$SERVICE_GROUP" /var/log/directory-net
chmod -R 750 "$INSTALL_DIR"
chmod +x "$INSTALL_DIR/server/Directory.Server"
chmod +x "$INSTALL_DIR/web/Directory.Web"

# Step 6: Install systemd service files
log_info "Installing systemd service files..."
cp "$SCRIPT_DIR/directory-server.service" /etc/systemd/system/
cp "$SCRIPT_DIR/directory-web.service" /etc/systemd/system/
systemctl daemon-reload

# Step 7: Enable services
log_info "Enabling services..."
systemctl enable directory-server.service
systemctl enable directory-web.service

# Step 8: Start services
log_info "Starting Directory.Server..."
systemctl start directory-server.service

log_info "Starting Directory.Web..."
systemctl start directory-web.service

# Step 9: Verify services are running
sleep 3

if systemctl is-active --quiet directory-server.service; then
    log_info "Directory.Server is running."
else
    log_error "Directory.Server failed to start. Check: journalctl -u directory-server.service"
fi

if systemctl is-active --quiet directory-web.service; then
    log_info "Directory.Web is running."
else
    log_error "Directory.Web failed to start. Check: journalctl -u directory-web.service"
fi

echo ""
log_info "Installation complete!"
echo ""
echo "  Install directory: $INSTALL_DIR"
echo "  Service user:      $SERVICE_USER"
echo ""
echo "  Manage services:"
echo "    systemctl status directory-server"
echo "    systemctl status directory-web"
echo "    journalctl -u directory-server -f"
echo "    journalctl -u directory-web -f"
echo ""
echo "  Ports:"
echo "    LDAP:     389 / 636 (TLS)"
echo "    Kerberos: 88"
echo "    DNS:      53"
echo "    GC:       3268 / 3269 (TLS)"
echo "    Web:      443 (HTTPS) / 80 (HTTP)"
