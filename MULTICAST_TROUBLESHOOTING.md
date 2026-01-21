# BSE Multicast Troubleshooting Guide

## Quick Diagnosis Checklist

### 1. Verify Market Hours ⏰
The BSE market data feed operates from **09:55:00 to 14:00:00** Botswana time (UTC+2).

```bash
# Check current Botswana time
TZ='Africa/Gaborone' date
```

If outside market hours, **no data will be broadcast**.

### 2. Test Correct Port Combinations 🔌

The documentation is ambiguous. Try **all** these combinations:

```bash
# Option 1: Port 540
dotnet run -- --m-ip 239.255.190.100 --m-port 540

# Option 2: Port 541  
dotnet run -- --m-ip 239.255.190.100 --m-port 541

# Option 3: Port 30540 (BASE PORT from docs)
dotnet run -- --m-ip 239.255.190.100 --m-port 30540

# Option 4: Port 30541
dotnet run -- --m-ip 239.255.190.100 --m-port 30541
```

### 3. Test from RedHat Server First 🖥️

BSE mentioned they have a RedHat server connected to multicast. **Test there first**:

```bash
# SSH to RedHat server
ssh user@redhat-server-ip

# Check multicast group membership
netstat -g

# Capture multicast traffic (requires root)
sudo tcpdump -i any -n 'host 239.255.190.100' -c 10 -X

# If you see packets, note the port number!
```

### 4. Windows Firewall Configuration 🛡️

On Windows, multicast may be blocked:

```powershell
# Run PowerShell as Administrator

# Allow multicast through firewall
New-NetFirewallRule -DisplayName "BSE Multicast" -Direction Inbound -Protocol UDP -LocalPort 540,541,30540,30541 -Action Allow

# Check if rule was created
Get-NetFirewallRule -DisplayName "BSE Multicast"
```

### 5. Network Interface Selection 🌐

If you have multiple network interfaces, specify which one:

```bash
# List network interfaces
ipconfig /all  # Windows
ip addr show   # Linux

# The multicast traffic must come through the interface connected to BSE network
```

### 6. Verify Multicast Routing 🗺️

```bash
# Windows
route print | findstr "224.0.0.0"

# Linux
ip route show | grep 224.0.0.0
netstat -rn | grep 224.0.0.0
```

You should see a route for multicast (224.0.0.0/4).

## Common Issues & Solutions

### Issue 1: "Joined multicast group" but no packets received

**Causes:**
- Wrong port number
- Outside market hours
- Firewall blocking
- Not connected to BSE network

**Solution:**
1. Try all port combinations (540, 541, 30540, 30541)
2. Verify market hours
3. Disable firewall temporarily to test
4. Confirm network connectivity to BSE

### Issue 2: "Connection refused" or "Network unreachable"

**Causes:**
- Not on BSE network
- VPN not connected
- Wrong multicast IP

**Solution:**
1. Verify you're on the RedHat server or connected via VPN
2. Ping the snapshot server: `ping 192.168.90.18`
3. Test TCP snapshot connection first

### Issue 3: Receiving packets but no decoded messages

**Causes:**
- FAST decoding issue
- Wrong FAST templates
- Corrupted data

**Solution:**
1. Check raw packet logs in `market_data_logs/`
2. Verify FAST frame structure (length prefix)
3. Compare with FAST 1.1 specification

### Issue 4: "Socket bind failed"

**Causes:**
- Port already in use
- Insufficient permissions
- Another instance running

**Solution:**
```bash
# Windows - check what's using the port
netstat -ano | findstr :540

# Kill the process if needed
taskkill /PID <process_id> /F

# Linux
sudo lsof -i :540
sudo kill <process_id>
```

## Testing Commands

### Test 1: Verify UDP Socket Works
```bash
# Simple UDP listener test
dotnet run -- --m-ip 239.255.190.100 --m-port 540
# Watch for "Joined multicast group" message
```

### Test 2: Capture Raw Packets
```bash
# On RedHat server with tcpdump
sudo tcpdump -i any -n 'host 239.255.190.100' -w /tmp/bse_multicast.pcap

# Download and analyze with Wireshark
scp user@redhat:/tmp/bse_multicast.pcap .
```

### Test 3: Test All Ports Automatically
```bash
# Run the test script
chmod +x test_multicast_ports.sh
./test_multicast_ports.sh
```

## Expected Behavior

### When Working Correctly:
```
✓ Joined multicast group 239.255.190.100:540 (bound to all interfaces)
✓ Received 1024 bytes from server
✓ [FIX IN] 35=X|55=ABC|270=123.45|...
✓ [UDP-INC] Symbol: ABC, LastPrice: 123.45
```

### When Not Working:
```
✓ Joined multicast group 239.255.190.100:540 (bound to all interfaces)
... (no further messages)
```

## Contact BSE Support

If still not working after trying all above:

**Email:** marketdata@bse.co.bw

**Provide:**
1. Your IP address
2. Time of testing (Botswana time)
3. Port numbers tried
4. Network interface details
5. Logs from `market_data_logs/`

**Ask BSE to confirm:**
- Multicast IP: 239.255.190.100
- Port number for TEST environment
- If your IP is whitelisted
- If multicast is currently broadcasting

## Next Steps

1. ✅ Run `test_multicast_ports.sh` on RedHat server
2. ✅ Note which port (if any) receives data
3. ✅ Test during market hours (09:55-14:00 Botswana time)
4. ✅ Check raw packet logs in `market_data_logs/`
5. ✅ Contact BSE if no data on any port
