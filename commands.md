## How to Deploy and Run

### Step 1: Transfer Code to the Server
Since the market data is likely only accessible from your Linux server, you need to copy the project there first. Use `scp` or `rsync`:
```bash
# Example from your local machine
scp -r ./BseFastClient user@svr-bse-esc:~/
```

### Step 2: Build on Server
Ensure you have the .NET SDK installed on the Linux server, then run:
```bash
cd BseFastClient
dotnet build
```

### Step 3: Run with Sample Commands
#### TCP Snapshot Only
Use this to connect to the snapshot server and perform the initial logon.
```bash
dotnet run -- --ip 192.168.90.18 --port 30540 --user YOUR_USERNAME --pass YOUR_PASSWORD
```

#### TCP + UDP Multicast (Incremental)
Use this on the server that receives the multicast feed (e.g., `svr-bse-esc`).
```bash
dotnet run -- --ip 192.168.90.18 --port 30540 --user YOUR_USERNAME --pass YOUR_PASSWORD --m-ip 233.1.1.1 --m-port 12345
```

---

## Testing Connectivity (Linux)

Before running the client, you can use these commands on your Linux server to ensure the ports and multicast feeds are reachable.

### Test TCP Snapshot Connectivity
Verify that you can reach the snapshot server:
```bash
# Using nc (netcat)
nc -zv 192.168.90.18 30540

# Using telnet
telnet 192.168.90.18 30540
```

### Test UDP Multicast Reception
Verify that the Linux server is actually receiving packets on the multicast group:
```bash
# Using tcpdump (requires sudo)
sudo tcpdump -i any host 233.1.1.1 and udp port 12345 -X

# Using ngrep
sudo ngrep -d any . udp port 12345
```

### Check Multicast Group Membership
Verify if the interface has joined the group (after starting the client):
```bash
netstat -g
# OR
ip maddr show
```

---

## Command Line Arguments Reference

| Argument | Description | Default |
| :--- | :--- | :--- |
| `--ip` | TCP Snapshot Server IP | `192.168.90.18` |
| `--port` | TCP Snapshot Server Port | `30540` |
| `--user` | FIX Logon Username | `YOUR_USERNAME` |
| `--pass` | FIX Logon Password | `YOUR_PASSWORD` |
| `--m-ip` | UDP Multicast Group IP | (None) |
| `--m-port` | UDP Multicast Port | `0` |

> [!TIP]
> Use `--help` or `-h` to see the usage instructions directly from the terminal.
