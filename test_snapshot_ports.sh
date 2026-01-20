#!/bin/bash
# Test Snapshot Channel - Request market data directly

cd "$(dirname "$0")"

echo "=========================================="
echo "BSE Snapshot Channel Test"
echo "=========================================="
echo ""
echo "This will connect to the Snapshot channel and request:"
echo "  1. Security definitions (instrument list)"
echo "  2. Market data snapshot (order book)"
echo ""

read -p "Enter your BSE username: " USERNAME
read -sp "Enter your BSE password: " PASSWORD
echo ""
echo ""

echo "Testing Snapshot Channel connections..."
echo ""

# Test both ports from the spec
PORTS=(540 541 5521)

for PORT in "${PORTS[@]}"; do
    echo "----------------------------------------"
    echo "Testing port $PORT..."
    echo "----------------------------------------"
    
    # Try 192.168.90.18 (from spec)
    echo "Attempting: 192.168.90.18:$PORT"
    if timeout 3 bash -c "cat < /dev/null > /dev/tcp/192.168.90.18/$PORT" 2>/dev/null; then
        echo "✓ Port $PORT is OPEN on 192.168.90.18"
        echo ""
        echo "Running client with this configuration..."
        dotnet run --configuration Release -- \
            --ip 192.168.90.18 \
            --port $PORT \
            --user "$USERNAME" \
            --pass "$PASSWORD"
        exit 0
    else
        echo "✗ Port $PORT is CLOSED or filtered on 192.168.90.18"
    fi
    echo ""
done

echo "=========================================="
echo "All ports tested - none responded"
echo "=========================================="
echo ""
echo "Possible issues:"
echo "  1. Firewall blocking connections"
echo "  2. Snapshot server not running"
echo "  3. Need to use different IP (192.168.50.22?)"
echo ""
echo "Contact BSE team to verify:"
echo "  - Correct Snapshot server IP and port"
echo "  - Your server IP is whitelisted"
echo "  - UAT environment is operational"
