# RedHat Server Deployment Guide

## Prerequisites on RedHat Server

### 1. Check .NET Version
```bash
dotnet --version
```

If .NET 8.0 is not installed, install it:

```bash
# For RedHat/CentOS/Fedora
sudo dnf install dotnet-sdk-8.0

# Or download from Microsoft
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0
```

### 2. Transfer Files to RedHat Server

From your Windows machine:

```bash
# Using SCP (replace with your server details)
scp -r BseFastClient/ user@redhat-server:/home/user/

# Or using rsync
rsync -avz BseFastClient/ user@redhat-server:/home/user/BseFastClient/
```

### 3. Build on RedHat Server

```bash
ssh user@redhat-server
cd ~/BseFastClient
dotnet build
```

## Testing Multicast on RedHat Server

### Step 1: Check Network Configuration

```bash
# Check network interfaces
ip addr show

# Check multicast routing
ip route show | grep 224.0.0.0
netstat -rn | grep 224.0.0.0

# Check multicast group membership
netstat -g
```

### Step 2: Verify Multicast Traffic

```bash
# Capture multicast packets (requires root)
sudo tcpdump -i any -n 'host 239.255.190.100' -c 10 -X

# If you see packets, note the port number!
```

### Step 3: Run the Application

Try all port combinations:

```bash
# Port 540
dotnet run -- --m-ip 239.255.190.100 --m-port 540

# Port 541
dotnet run -- --m-ip 239.255.190.100 --m-port 541

# Port 30540
dotnet run -- --m-ip 239.255.190.100 --m-port 30540

# Port 30541
dotnet run -- --m-ip 239.255.190.100 --m-port 30541
```

### Step 4: Run with TCP Snapshot + UDP Multicast

```bash
# Full command with both TCP and UDP
dotnet run -- \
  --ip 192.168.90.18 \
  --port 30540 \
  --user YOUR_USERNAME \
  --pass YOUR_PASSWORD \
  --m-ip 239.255.190.100 \
  --m-port 540
```

## Automated Testing Script

Run the multicast port testing script:

```bash
chmod +x test_multicast_ports.sh
./test_multicast_ports.sh
```

## Troubleshooting on RedHat

### Issue: Permission Denied for Multicast

```bash
# Check if user has permission to join multicast groups
# May need to run with sudo or add user to appropriate group
sudo usermod -a -G network $USER
```

### Issue: Firewall Blocking

```bash
# Check firewall status
sudo firewall-cmd --state

# Allow multicast (if needed)
sudo firewall-cmd --permanent --add-rich-rule='rule family="ipv4" destination address="239.255.190.100" accept'
sudo firewall-cmd --reload

# Or temporarily disable for testing
sudo systemctl stop firewalld
```

### Issue: SELinux Blocking

```bash
# Check SELinux status
getenforce

# Temporarily set to permissive for testing
sudo setenforce 0

# Check logs
sudo ausearch -m avc -ts recent
```

## Production Deployment

### Create Systemd Service

Create `/etc/systemd/system/bse-client.service`:

```ini
[Unit]
Description=BSE Market Data Client
After=network.target

[Service]
Type=simple
User=bseuser
WorkingDirectory=/home/bseuser/BseFastClient
ExecStart=/usr/bin/dotnet run -- --ip 192.168.90.18 --port 30540 --m-ip 239.255.190.100 --m-port 540
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable bse-client
sudo systemctl start bse-client
sudo systemctl status bse-client
```

View logs:

```bash
sudo journalctl -u bse-client -f
```

## Market Hours Reminder

BSE market data feed operates:
- **Start:** 09:55:00 Botswana time (UTC+2)
- **End:** 14:00:00 Botswana time (UTC+2)

Check current Botswana time:
```bash
TZ='Africa/Gaborone' date
```

## Expected Output When Working

```
✓ BSE Market Data Client starting...
✓ Starting Snapshot Session...
✓ TCP connection established. Sending Logon...
✓ Joined multicast group 239.255.190.100:540 (bound to all interfaces)
✓ Received 1024 bytes from server
✓ [FIX IN] 35=X|55=ABC|270=123.45|...
✓ [UDP-INC] Symbol: ABC, LastPrice: 123.45
```

## Next Steps

1. SSH to RedHat server
2. Install .NET 8.0 if needed
3. Transfer project files
4. Run `test_multicast_ports.sh` to find correct port
5. Run application with correct parameters
6. Check logs in `market_data_logs/` directory
