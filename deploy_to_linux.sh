#!/bin/bash

# BSE Market Data Client - UAT Deployment and Testing Script
# This script helps you deploy and test the client on the Linux server

set -e  # Exit on error

echo "=========================================="
echo "BSE Market Data Client - UAT Deployment"
echo "=========================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuration
PROJECT_DIR="$HOME/BseFastClient"
LOG_DIR="$PROJECT_DIR/market_data_logs"

# Function to print colored messages
print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Step 1: Check prerequisites
echo "Step 1: Checking prerequisites..."
echo "-----------------------------------"

# Check if .NET SDK is installed
if ! command -v dotnet &> /dev/null; then
    print_error ".NET SDK not found!"
    echo ""
    echo "Please install .NET 6.0 SDK:"
    echo "  sudo dnf install dotnet-sdk-6.0"
    echo "  OR"
    echo "  sudo yum install dotnet-sdk-6.0"
    echo ""
    echo "For manual installation, visit:"
    echo "  https://dotnet.microsoft.com/download/dotnet/6.0"
    exit 1
else
    DOTNET_VERSION=$(dotnet --version)
    print_info ".NET SDK found: $DOTNET_VERSION"
fi

# Check network tools
if ! command -v netstat &> /dev/null; then
    print_warn "netstat not found. Installing net-tools..."
    sudo yum install -y net-tools || sudo dnf install -y net-tools
fi

if ! command -v tcpdump &> /dev/null; then
    print_warn "tcpdump not found. You may want to install it for network debugging:"
    echo "  sudo yum install -y tcpdump"
fi

echo ""

# Step 2: Build the project
echo "Step 2: Building the project..."
echo "-----------------------------------"

if [ ! -d "$PROJECT_DIR" ]; then
    print_error "Project directory not found: $PROJECT_DIR"
    echo "Please transfer the code to the server first."
    exit 1
fi

cd "$PROJECT_DIR"
print_info "Building in $(pwd)..."

dotnet build --configuration Release

if [ $? -eq 0 ]; then
    print_info "Build successful!"
else
    print_error "Build failed!"
    exit 1
fi

echo ""

# Step 3: Network connectivity tests
echo "Step 3: Testing network connectivity..."
echo "-----------------------------------"

# Get UAT connection details from user
read -p "Enter TCP Snapshot Server IP [192.168.90.18]: " TCP_IP
TCP_IP=${TCP_IP:-192.168.90.18}

read -p "Enter TCP Snapshot Server Port [30540]: " TCP_PORT
TCP_PORT=${TCP_PORT:-30540}

read -p "Enter UDP Multicast IP [239.255.190.100]: " UDP_IP
UDP_IP=${UDP_IP:-239.255.190.100}

read -p "Enter UDP Multicast Port [540]: " UDP_PORT
UDP_PORT=${UDP_PORT:-540}

# Test TCP connectivity
print_info "Testing TCP connection to $TCP_IP:$TCP_PORT..."
if timeout 5 bash -c "cat < /dev/null > /dev/tcp/$TCP_IP/$TCP_PORT" 2>/dev/null; then
    print_info "TCP connection successful!"
else
    print_warn "TCP connection failed or timed out. This may be normal if authentication is required."
fi

# Check if we can bind to UDP port
print_info "Checking UDP port $UDP_PORT availability..."
if netstat -tuln | grep -q ":$UDP_PORT "; then
    print_warn "Port $UDP_PORT is already in use. This may cause issues."
else
    print_info "UDP port $UDP_PORT is available."
fi

# Check multicast routing
print_info "Checking multicast routes..."
ip maddr show

echo ""

# Step 4: Create run script
echo "Step 4: Creating run scripts..."
echo "-----------------------------------"

# Create directory for logs
mkdir -p "$LOG_DIR"

# Create TCP-only test script
cat > "$PROJECT_DIR/run_tcp_test.sh" << 'EOF'
#!/bin/bash
# Test TCP Snapshot Channel only (no multicast)

cd "$(dirname "$0")"

echo "Starting BSE Client - TCP Snapshot Channel Test"
echo "================================================"
echo ""

read -p "Enter your BSE username: " USERNAME
read -sp "Enter your BSE password: " PASSWORD
echo ""

dotnet run --configuration Release -- \
    --ip 192.168.90.18 \
    --port 30540 \
    --user "$USERNAME" \
    --pass "$PASSWORD"
EOF

chmod +x "$PROJECT_DIR/run_tcp_test.sh"
print_info "Created: run_tcp_test.sh"

# Create UDP-only test script
cat > "$PROJECT_DIR/run_udp_test.sh" << 'EOF'
#!/bin/bash
# Test UDP Multicast Channel only (no authentication)

cd "$(dirname "$0")"

echo "Starting BSE Client - UDP Multicast Test"
echo "========================================="
echo ""
echo "This will listen for multicast market data."
echo "Press Ctrl+C to stop."
echo ""

# Note: TCP connection is still required for session management
# but we won't send credentials
dotnet run --configuration Release -- \
    --ip 192.168.90.18 \
    --port 30540 \
    --m-ip 239.255.190.100 \
    --m-port 540
EOF

chmod +x "$PROJECT_DIR/run_udp_test.sh"
print_info "Created: run_udp_test.sh"

# Create full test script
cat > "$PROJECT_DIR/run_full_test.sh" << 'EOF'
#!/bin/bash
# Test both TCP and UDP channels

cd "$(dirname "$0")"

echo "Starting BSE Client - Full Test (TCP + UDP)"
echo "============================================"
echo ""

read -p "Enter your BSE username: " USERNAME
read -sp "Enter your BSE password: " PASSWORD
echo ""

dotnet run --configuration Release -- \
    --ip 192.168.90.18 \
    --port 30540 \
    --user "$USERNAME" \
    --pass "$PASSWORD" \
    --m-ip 239.255.190.100 \
    --m-port 540
EOF

chmod +x "$PROJECT_DIR/run_full_test.sh"
print_info "Created: run_full_test.sh"

echo ""

# Step 5: Network debugging commands
echo "Step 5: Network debugging commands"
echo "-----------------------------------"
echo ""
echo "If you encounter issues, try these commands:"
echo ""
echo "1. Check if multicast packets are arriving:"
echo "   sudo tcpdump -i any host $UDP_IP and udp port $UDP_PORT -X"
echo ""
echo "2. Check multicast group membership:"
echo "   netstat -g"
echo "   ip maddr show"
echo ""
echo "3. Check firewall rules:"
echo "   sudo firewall-cmd --list-all"
echo "   sudo iptables -L -n"
echo ""
echo "4. Monitor application logs:"
echo "   tail -f $LOG_DIR/decoded_messages_*.txt"
echo ""
echo "5. View raw packet data:"
echo "   tail -f $LOG_DIR/raw_udp_*.bin"
echo ""

# Step 6: Final instructions
echo ""
echo "=========================================="
echo "Deployment Complete!"
echo "=========================================="
echo ""
print_info "You can now run the client using:"
echo ""
echo "  For TCP-only test (with authentication):"
echo "    ./run_tcp_test.sh"
echo ""
echo "  For UDP-only test (multicast listening):"
echo "    ./run_udp_test.sh"
echo ""
echo "  For full test (TCP + UDP):"
echo "    ./run_full_test.sh"
echo ""
print_info "Logs will be saved to: $LOG_DIR"
echo ""
print_warn "Note: Make sure the server has network access to the BSE UAT environment!"
echo ""
