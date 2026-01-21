#!/bin/bash

# BSE Multicast Port Testing Script
# This script tests all possible port combinations for the TEST environment

echo "=========================================="
echo "BSE Multicast Connection Test"
echo "=========================================="
echo ""

MULTICAST_IP="239.255.190.100"
PORTS=(540 541 30540 30541)

echo "Testing multicast group: $MULTICAST_IP"
echo "Ports to test: ${PORTS[@]}"
echo ""

# Check if we're on the RedHat server or local machine
if [ -f /etc/redhat-release ]; then
    echo "✓ Running on RedHat server"
    IS_REDHAT=true
else
    echo "⚠ Running on local machine (not RedHat server)"
    IS_REDHAT=false
fi

echo ""
echo "=========================================="
echo "Network Interface Check"
echo "=========================================="
ip addr show | grep -E "inet |UP"

echo ""
echo "=========================================="
echo "Multicast Group Membership"
echo "=========================================="
netstat -g 2>/dev/null || ip maddr show

echo ""
echo "=========================================="
echo "Testing Each Port (10 seconds each)"
echo "=========================================="

for PORT in "${PORTS[@]}"; do
    echo ""
    echo "--- Testing Port $PORT ---"
    
    # Test with tcpdump (requires root/sudo)
    if command -v tcpdump &> /dev/null; then
        echo "Listening for packets on $MULTICAST_IP:$PORT for 10 seconds..."
        timeout 10s sudo tcpdump -i any -n "host $MULTICAST_IP and port $PORT" -c 5 2>&1 | head -20
        
        if [ $? -eq 124 ]; then
            echo "⚠ Timeout - no packets received on port $PORT"
        elif [ $? -eq 0 ]; then
            echo "✓ Packets detected on port $PORT!"
        fi
    else
        echo "⚠ tcpdump not available, skipping packet capture"
    fi
    
    # Test with .NET client if available
    if [ -f "./bin/Debug/net8.0/BseFastClient.dll" ]; then
        echo ""
        echo "Testing with .NET client on port $PORT..."
        timeout 10s dotnet run -- --m-ip $MULTICAST_IP --m-port $PORT 2>&1 | grep -E "Joined|Received|Error" | head -10
    fi
    
    echo ""
done

echo ""
echo "=========================================="
echo "Routing Table Check"
echo "=========================================="
echo "Checking multicast routes..."
ip route show | grep -E "224.0.0.0|239.255"
netstat -rn | grep -E "224.0.0.0|239.255"

echo ""
echo "=========================================="
echo "Firewall Check"
echo "=========================================="
if command -v iptables &> /dev/null; then
    echo "Checking iptables rules for multicast..."
    sudo iptables -L -n | grep -E "239.255|multicast" || echo "No specific multicast rules found"
fi

echo ""
echo "=========================================="
echo "Recommendations"
echo "=========================================="
echo "1. Ensure you're testing during market hours: 09:55-14:00 Botswana time (UTC+2)"
echo "2. Current time: $(date)"
echo "3. Botswana time: $(TZ='Africa/Gaborone' date)"
echo "4. If on Windows, ensure Windows Firewall allows multicast"
echo "5. If no packets received, contact BSE to verify:"
echo "   - Multicast group is active"
echo "   - Your IP is whitelisted"
echo "   - Correct multicast IP and port"
echo ""
