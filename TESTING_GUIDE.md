# Testing BSE Snapshot Channel

## What Changed

The client now **automatically requests market data** after successful login:
1. Security Definitions (instrument list)
2. Market Data Snapshot (order book)

This will help us verify if the Snapshot channel is working.

## Deploy to Linux Server

```bash
# From Windows
cd "C:\Users\Blessing Mwale\Documents\Projects\BSE\BseFastClient"

# Option 1: Use Git (recommended)
git add Session/SnapshotSession.cs test_snapshot_ports.sh diagnose.sh
git commit -m "Add automatic market data request after login"
git push origin dev

# Then on Linux server
cd ~/projects/bse_client
git pull origin dev
dotnet build --configuration Release

# Option 2: Direct file transfer
scp Session/SnapshotSession.cs your_username@svr-bse-esc:~/projects/bse_client/Session/
scp test_snapshot_ports.sh your_username@svr-bse-esc:~/projects/bse_client/
scp diagnose.sh your_username@svr-bse-esc:~/projects/bse_client/

# Then rebuild on server
ssh your_username@svr-bse-esc
cd ~/projects/bse_client
dotnet build --configuration Release
```

## Test 1: Try Different Ports

Based on the BSE spec, there are multiple ports. Let's test them:

```bash
cd ~/projects/bse_client
chmod +x test_snapshot_ports.sh
./test_snapshot_ports.sh
```

This will automatically try:
- Port 540 (base port from spec)
- Port 541 (alternate)
- Port 5521 (snapshot channel from spec)

## Test 2: Run Full Client

```bash
./run_full_test.sh
```

### What to Look For

**Successful Connection:**
```
[INFO] Starting Snapshot Session...
[INFO] TCP connection established. Sending Logon...
[FIX OUT] 35=A|52=...|553=bseesc01|...
[INFO] Waiting for Logon response...
[INFO] Received X bytes from server
[FIX IN] 35=A|1409=0|...
[SUCCESS] Logon successful! Session is active.
[INFO] Logon successful! Requesting market data...
[INFO] Requested Security Definitions (instrument list)
[FIX OUT] 35=c|320=...|321=8|...
[INFO] Requested Market Data Snapshot (order book)
[FIX OUT] 35=V|262=...|263=0|...
[INFO] Received X bytes from server
[FIX IN] 35=d|...  ← Security Definition response
[FIX IN] 35=W|...  ← Market Data Snapshot response
```

**If Still Hanging:**
```
[INFO] TCP connection established. Sending Logon...
[FIX OUT] 35=A|...
[INFO] Waiting for Logon response...
(nothing more)
```

This means:
- TCP connection works
- Logon message sent
- Server not responding

## Test 3: Check Diagnostics

While client is running, open another terminal:

```bash
cd ~/projects/bse_client
./diagnose.sh
```

Look for:
- ✓ TCP connection ESTABLISHED
- ✓ Bytes being received
- ✓ Log files being created

## Test 4: Monitor Network Traffic

```bash
# Watch for ANY TCP traffic from BSE server
sudo tcpdump -i any host 192.168.90.18 -X -vv

# Watch for UDP multicast
sudo tcpdump -i any host 239.255.190.100 and udp port 540 -X
```

## Troubleshooting

### Issue: No Response from Server

**Possible Causes:**
1. **Wrong Port** - Try test_snapshot_ports.sh to test all ports
2. **Firewall** - Server blocking outbound or inbound
3. **Credentials** - Username/password incorrect
4. **IP Whitelist** - Your server IP not whitelisted
5. **UAT Down** - BSE UAT environment not running

**Actions:**
```bash
# Check firewall
sudo firewall-cmd --list-all

# Check if packets leaving your server
sudo tcpdump -i any host 192.168.90.18 -c 10

# Try telnet to verify basic connectivity
telnet 192.168.90.18 540
```

### Issue: Connection Refused

**Possible Causes:**
1. **Wrong IP/Port** - Double-check with BSE team
2. **Firewall** - Port blocked
3. **Server Down** - BSE server not running

**Actions:**
```bash
# Test connectivity
ping 192.168.90.18
telnet 192.168.90.18 540
nc -zv 192.168.90.18 540
```

### Issue: Connection Established but No Data

**Possible Causes:**
1. **FAST Encoding Issue** - Server sending data but we can't decode
2. **Credentials Wrong** - Server rejecting login silently
3. **Protocol Mismatch** - Server expecting different message format

**Actions:**
```bash
# Capture raw TCP data
sudo tcpdump -i any host 192.168.90.18 -X -w capture.pcap

# Check logs
tail -f market_data_logs/decoded_messages_*.txt
tail -f market_data_logs/raw_udp_*.bin
```

## Next Steps

Once you see data coming in:

1. **Share logs** with development team:
   ```bash
   cd ~/projects/bse_client/market_data_logs
   tar -czf logs_$(date +%Y%m%d_%H%M%S).tar.gz *.txt *.bin
   ```

2. **Analyze message types**:
   - Look for `35=d` (Security Definition)
   - Look for `35=W` (Market Data Snapshot)
   - Look for `35=X` (Market Data Incremental Refresh)

3. **Contact BSE** if no data after all tests:
   - Provide your server IP
   - Share connection logs
   - Ask them to verify:
     - Correct IP and port for Snapshot channel
     - Your IP is whitelisted
     - UAT environment is operational
     - Credentials are correct

## Quick Reference

```bash
# Test all ports
./test_snapshot_ports.sh

# Run full client
./run_full_test.sh

# Check diagnostics
./diagnose.sh

# Monitor network
sudo tcpdump -i any host 192.168.90.18 -X
```
