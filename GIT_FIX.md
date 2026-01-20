# Git Merge Conflict Fix

## Problem
You committed build artifacts (`bin/`, `obj/`) to Git, causing merge conflicts.

## Quick Fix (On Linux Server)

```bash
cd ~/bse_client

# 1. Abort the current merge
git merge --abort

# 2. Clean build artifacts
dotnet clean
rm -rf bin/ obj/

# 3. Pull the .gitignore file first
git pull origin dev

# 4. If still conflicts, force pull (WARNING: loses local changes)
git fetch origin dev
git reset --hard origin/dev

# 5. Rebuild
dotnet build --configuration Release
```

## Proper Workflow Going Forward

### On Windows (Before Committing)
```bash
cd "C:\Users\Blessing Mwale\Documents\Projects\BSE\BseFastClient"

# Clean build artifacts
dotnet clean

# Check what will be committed
git status

# Add only source files
git add *.cs *.csproj *.md *.sh .gitignore
git commit -m "Your commit message"
git push origin dev
```

### On Linux Server (Pulling Updates)
```bash
cd ~/bse_client

# Clean before pulling
dotnet clean

# Pull updates
git pull origin dev

# Rebuild
dotnet build --configuration Release
```

## What NOT to Commit
- ❌ `bin/` folder
- ❌ `obj/` folder
- ❌ `*.dll` files
- ❌ `*.pdb` files
- ❌ `market_data_logs/` folder
- ❌ `.vs/` or `.vscode/` folders

## What TO Commit
- ✅ `*.cs` source files
- ✅ `*.csproj` project file
- ✅ `*.md` documentation
- ✅ `*.sh` scripts
- ✅ `.gitignore` file

## Alternative: Start Fresh (If Conflicts Persist)

```bash
# On Linux server
cd ~
mv bse_client bse_client_backup
git clone https://github.com/blessingmwalein/bse_client.git
cd bse_client
git checkout dev
dotnet build --configuration Release
```

Then copy any local changes from `bse_client_backup` if needed.
