# BSE Market Data Client

A .NET 6.0 client for receiving market data from the Botswana Stock Exchange (BSE) via the FIX/FAST protocol over TCP and UDP.

## 🎯 Project Status

**Current Phase:** UAT Connectivity Testing  
**Goal:** Capture raw market data from BSE UAT environment for analysis

### What Works
- ✅ TCP connection to Snapshot Channel
- ✅ UDP multicast reception
- ✅ FAST transfer encoding (framing)
- ✅ FIX session management (Logon/Logout/Heartbeat)
- ✅ Raw data logging to files

### In Progress
- 🔄 FAST field-level decoding
- 🔄 Market data message parsing
- 🔄 Sequence tracking and recovery

## 🚀 Quick Start

### For Linux Server Deployment (UAT Testing)

1. **Transfer code to Linux server:**
   ```bash
   scp -r BseFastClient your_username@svr-bse-esc:~/
   ```

2. **Deploy and test:**
   ```bash
   ssh your_username@svr-bse-esc
   cd ~/BseFastClient
   chmod +x deploy_to_linux.sh
   ./deploy_to_linux.sh
   ```

3. **Run tests:**
   ```bash
   ./run_tcp_test.sh    # TCP only
   ./run_udp_test.sh    # UDP only
   ./run_full_test.sh   # Both
   ```

📖 **See [commands.md](commands.md) for detailed instructions**  
📖 **See [linux_deployment_guide.md](linux_deployment_guide.md) for full deployment guide**

## 📋 Requirements

- .NET 6.0 SDK
- Network access to BSE UAT environment
- BSE credentials (for TCP authentication)

## 🏗️ Architecture

```
BseFastClient/
├── Fast/                    # FAST encoding/decoding
│   ├── FastFrameDecoder.cs # Transfer encoding decoder
│   └── FastFrameEncoder.cs # Transfer encoding encoder
├── Fix/                     # FIX message handling
│   ├── FixMessage.cs       # FIX message structure
│   └── FixParser.cs        # FIX message parser
├── Networking/              # Network communication
│   ├── TcpSession.cs       # TCP connection handler
│   └── UdpMulticastSession.cs # UDP multicast receiver
├── Session/                 # Session management
│   └── SnapshotSession.cs  # Snapshot channel handler
├── Logging/                 # Logging utilities
│   └── ConsoleLogger.cs    # Console output formatter
└── Program.cs              # Main entry point
```

## 📊 Data Capture

All market data is automatically logged to `market_data_logs/`:

- **Raw UDP packets:** `raw_udp_YYYYMMDD_HHMMSS.bin`
- **Decoded messages:** `decoded_messages_YYYYMMDD_HHMMSS.txt`

## 🔍 Monitoring

```bash
# View live decoded messages
tail -f market_data_logs/decoded_messages_*.txt

# Check network connectivity
sudo tcpdump -i any host 239.255.190.100 and udp port 540 -X
```

## 📚 Documentation

- **[commands.md](commands.md)** - Quick reference guide
- **[linux_deployment_guide.md](linux_deployment_guide.md)** - Full deployment instructions
- **[bse_implementation_analysis.md](bse_implementation_analysis.md)** - Implementation compliance analysis

## 🤝 Contributing

This is an internal project for BSE market data integration.

## 📄 License

Proprietary - Botswana Stock Exchange

## 📞 Support

For issues or questions, contact the development team.

---

**Last Updated:** 2026-01-20  
**Version:** 0.1.0 (UAT Testing Phase)
