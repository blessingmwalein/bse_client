#!/bin/bash
# Quick diagnostic script for BSE UAT connection issues

echo "=========================================="
echo "BSE Client - Connection Diagnostics"
echo "=========================================="
echo ""

# Check if client is running
echo "1. Checking if client is running..."
if pgrep -f "dotnet.*BseFastClient" > /dev/null; then
    echo "   ✓ Client is running (PID: $(pgrep -f 'dotnet.*BseFastClient'))"
else
    echo "   ✗ Client is NOT running"
fi
echo ""

# Check TCP connection
echo "2. Checking TCP connection to 192.168.90.18:30540..."
if netstat -tn | grep "192.168.90.18:30540" | grep "ESTABLISHED" > /dev/null; then
    echo "   ✓ TCP connection ESTABLISHED"
    netstat -tn | grep "192.168.90.18:30540"
else
    echo "   ✗ TCP connection NOT established"
    netstat -tn | grep "192.168.90.18:30540" || echo "   (No connection found)"
fi
echo ""

# Check UDP multicast membership
echo "3. Checking UDP multicast membership..."
if netstat -g | grep "239.255.190.100" > /dev/null; then
    echo "   ✓ Joined multicast group 239.255.190.100"
else
    echo "   ✗ NOT joined to multicast group"
fi
echo ""

# Check for UDP packets
echo "4. Checking for UDP packets (5 second capture)..."
echo "   Running: sudo tcpdump -i any host 239.255.190.100 and udp port 540 -c 5 -q"
sudo timeout 5 tcpdump -i any host 239.255.190.100 and udp port 540 -c 5 -q 2>&1 | grep -v "listening" || echo "   No packets received"
echo ""

# Check log files
echo "5. Checking log files..."
LOG_DIR="$HOME/BseFastClient/market_data_logs"
if [ -d "$LOG_DIR" ]; then
    echo "   Log directory: $LOG_DIR"
    echo "   Files:"
    ls -lh "$LOG_DIR" 2>/dev/null || echo "   (empty)"
    echo ""
    echo "   Latest decoded messages (last 10 lines):"
    tail -10 "$LOG_DIR"/decoded_messages_*.txt 2>/dev/null || echo "   (no messages yet)"
else
    echo "   ✗ Log directory not found: $LOG_DIR"
fi
echo ""

# Check recent console output
echo "6. Recent application logs (if running in background):"
journalctl -u bse-client --no-pager -n 20 2>/dev/null || echo "   (not running as systemd service)"
echo ""

echo "=========================================="
echo "Diagnostics Complete"
echo "=========================================="
echo ""
echo "Troubleshooting tips:"
echo "  - If TCP is ESTABLISHED but no Logon response, check credentials"
echo "  - If no UDP packets, verify multicast routing and firewall"
echo "  - Check logs for FIX messages: tail -f $LOG_DIR/decoded_messages_*.txt"
echo "  - Monitor raw packets: sudo tcpdump -i any host 239.255.190.100 -X"
echo ""
