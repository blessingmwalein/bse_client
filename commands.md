## BSE Market Data Client - Quick Start Guide

### For UAT Testing on Linux Server

## 🚀 Quick Deployment to Linux

### Step 1: Transfer Code to Linux Server
```bash
# From Windows (PowerShell or WSL)
cd "C:\Users\Blessing Mwale\Documents\Projects\BSE"
scp -r BseFastClient your_username@svr-bse-esc:~/

# Example:
scp -r BseFastClient bmwale@192.168.1.100:~/
```

### Step 2: Connect and Deploy
```bash
# SSH to Linux server
ssh your_username@svr-bse-esc

# Run deployment script
cd ~/BseFastClient
chmod +x deploy_to_linux.sh
./deploy_to_linux.sh
```

### Step 3: Test Connectivity

#### Option A: TCP Snapshot Only (with credentials)
```bash
./run_tcp_test.sh
```

#### Option B: UDP Multicast Only (no credentials needed)
```bash
./run_udp_test.sh
```

#### Option C: Full Test (TCP + UDP)
```bash
./run_full_test.sh
```

---

## 📋 UAT Environment Details

### TCP Snapshot Channel
- **IP:** 192.168.90.18
- **Port:** 30540
- **Requires:** Username and password from BSE

### UDP Multicast Channel
- **Multicast IP:** 239.255.190.100
- **Port:** 540
- **Requires:** Network access to multicast group

---

## 🔍 Monitoring and Debugging

### View Live Logs
```bash
# Monitor decoded messages
tail -f ~/BseFastClient/market_data_logs/decoded_messages_*.txt

# Monitor raw packets
tail -f ~/BseFastClient/market_data_logs/raw_udp_*.bin
```

### Check Network Connectivity

#### Test TCP Connection
```bash
# Using telnet
telnet 192.168.90.18 30540

# Using nc (netcat)
nc -zv 192.168.90.18 30540
```

#### Test UDP Multicast Reception
```bash
# Check if packets are arriving (requires sudo)
sudo tcpdump -i any host 239.255.190.100 and udp port 540 -X

# Check multicast group membership
netstat -g | grep 239.255.190.100
ip maddr show
```

#### Check Firewall
```bash
# View firewall rules
sudo firewall-cmd --list-all

# Add TCP port if needed
sudo firewall-cmd --add-port=30540/tcp --permanent
sudo firewall-cmd --reload

# Add multicast route if needed
sudo route add -net 239.255.190.0 netmask 255.255.255.0 dev eth0
```

---

## 💻 Local Development (Windows)

### Build and Run
```bash
# Build
dotnet build --configuration Release

# Run TCP only
dotnet run -- --ip 192.168.90.18 --port 30540 --user YOUR_USERNAME --pass YOUR_PASSWORD

# Run TCP + UDP
dotnet run -- --ip 192.168.90.18 --port 30540 --user YOUR_USERNAME --pass YOUR_PASSWORD --m-ip 239.255.190.100 --m-port 540
```

---

## 📊 Command Line Arguments Reference

| Argument | Description | Default | Required |
| :--- | :--- | :--- | :--- |
| `--ip` | TCP Snapshot Server IP | `192.168.90.18` | No |
| `--port` | TCP Snapshot Server Port | `30540` | No |
| `--user` | FIX Logon Username | - | For TCP auth |
| `--pass` | FIX Logon Password | - | For TCP auth |
| `--m-ip` | UDP Multicast Group IP | - | For UDP feed |
| `--m-port` | UDP Multicast Port | - | For UDP feed |
| `--help` | Show help message | - | No |

---

## 📁 Log Files

All logs are saved to: `~/BseFastClient/market_data_logs/`

### File Types
- `raw_udp_YYYYMMDD_HHMMSS.bin` - Raw UDP packets (hex dump)
- `decoded_messages_YYYYMMDD_HHMMSS.txt` - Decoded FIX messages

### Sample Log Output
```
=== Message 1 at 2026-01-20 07:30:15.123 ===
Raw FIX: 35=X|1180=1|1181=12345|268=1|279=0|55=ABC|270=123.45|271=1000|

=== Message 2 at 2026-01-20 07:30:15.456 ===
Raw FIX: 35=X|1180=1|1181=12346|268=1|279=1|55=XYZ|270=67.89|271=500|
```

---

## 🐛 Troubleshooting

### Issue: "Connection refused" on TCP
```bash
# Check if server is reachable
ping 192.168.90.18
telnet 192.168.90.18 30540

# Check firewall
sudo firewall-cmd --list-all
```

### Issue: No UDP multicast data
```bash
# Check if packets are arriving
sudo tcpdump -i any host 239.255.190.100 and udp port 540 -c 10

# Check multicast membership
netstat -g
ip maddr show

# Add multicast route
sudo route add -net 239.255.190.0 netmask 255.255.255.0 dev eth0
```

### Issue: "Logon failed"
- Verify credentials with BSE team
- Check if account is locked
- Check if password has expired

---

## 📚 Additional Resources

- **Full Deployment Guide:** See `linux_deployment_guide.md`
- **Implementation Analysis:** See `bse_implementation_analysis.md`
- **BSE Specification:** Botswana Stock Exchange Market Data Feed UDP FIX/FAST Specification v2.02

---

## ⚠️ Important Notes

1. **File Logging:** Raw data is automatically saved to `market_data_logs/` for later analysis
2. **Credentials:** Never commit credentials to Git - use environment variables
3. **Network:** Multicast requires proper network configuration and firewall rules
4. **Disk Space:** Monitor log file sizes - they can grow quickly

---

> [!TIP]
> For detailed deployment instructions, see `linux_deployment_guide.md`

> [!NOTE]
> This client currently captures raw market data. Full FAST decoding will be implemented in the next phase.
