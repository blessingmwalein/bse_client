# Quick Update Instructions

## What Changed
- Added verbose logging to track TCP connection and Logon process
- Added diagnostic script to check connection status
- Enhanced error handling for Logon responses

## How to Update on Linux Server

### Option 1: Transfer Only Changed Files
```bash
# From Windows
cd "C:\Users\Blessing Mwale\Documents\Projects\BSE\BseFastClient"

# Transfer updated files
scp Session/SnapshotSession.cs your_username@svr-bse-esc:~/BseFastClient/Session/
scp diagnose.sh your_username@svr-bse-esc:~/BseFastClient/

# SSH to server and rebuild
ssh your_username@svr-bse-esc
cd ~/BseFastClient
dotnet build --configuration Release
```

### Option 2: Transfer Entire Project
```bash
# From Windows
cd "C:\Users\Blessing Mwale\Documents\Projects\BSE"
scp -r BseFastClient your_username@svr-bse-esc:~/

# SSH to server and rebuild
ssh your_username@svr-bse-esc
cd ~/BseFastClient
dotnet build --configuration Release
```

## Running the Updated Client

```bash
cd ~/BseFastClient

# Stop current client if running (Ctrl+C)

# Run with verbose logging
./run_full_test.sh
```

## New Diagnostic Tool

```bash
# Make executable
chmod +x diagnose.sh

# Run diagnostics while client is running
./diagnose.sh
```

This will show:
- TCP connection status
- UDP multicast membership
- Packet reception
- Log file contents

## What to Look For

### Successful Connection
You should see:
```
[INFO] Starting Snapshot Session...
[INFO] TCP connection established. Sending Logon...
[FIX OUT] 35=A|52=...|108=30|553=bseesc01|554=***|
[INFO] Waiting for Logon response...
[INFO] Received X bytes from server
[FIX IN] 35=A|1409=0|...
[SUCCESS] Logon successful! Session is active.
```

### If Still Hanging
Check:
1. Is TCP connection ESTABLISHED? (run `./diagnose.sh`)
2. Are bytes being received? (look for "Received X bytes" in logs)
3. Is the Logon message format correct? (check FIX OUT message)
4. Contact BSE team to verify:
   - Credentials are correct
   - Your server IP is whitelisted
   - UAT environment is operational
