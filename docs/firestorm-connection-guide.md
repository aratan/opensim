# Connecting Firestorm to an OpenSim Standalone Grid

This guide covers how to configure the Firestorm viewer to connect to a local
OpenSim standalone server.

## Required Values

| Field | Value |
|-------|-------|
| Login URI | `http://127.0.0.1:9000` |
| Grid name | `OpenSim Local` (or any name you choose) |

The default OpenSim standalone listens on port **9000** for the login service.
If you changed `HttpListenerPort` in `OpenSim.ini`, use that port instead.

---

## Method 1: Add via the Viewer UI

The UI path to the grid manager varies by Firestorm version:

| Version | Path |
|---------|------|
| **Legacy (pre-6.x)** | `Firestorm → Login Manager...` or `Me → Grid Manager` |
| **Modern (7.x+)** | Click the **gear/wrench icon** next to the grid selector dropdown on the login screen |
| **Any** | `Ctrl+Shift+G` (keyboard shortcut, may need enabling in preferences) |

Once in the **Grid Manager**:

1. Click **Add New Grid** (or the **+** button).
2. Enter the **Grid Name** (e.g. `OpenSim Local`).
3. Enter the **Login URI**: `http://127.0.0.1:9000`
4. Click **OK** or **Apply**.
5. Select your new grid from the grid dropdown on the login screen.
6. Log in with your OpenSim user account.

---

## Method 2: Direct XML Configuration

Firestorm stores grid definitions in an XML file. You can edit it directly:

| OS | Path |
|----|------|
| **Linux** | `~/.firestorm/grids.xml` |
| **Windows** | `%APPDATA%/Firestorm/grids.xml` |
| **macOS** | `~/Library/Application Support/Firestorm/grids.xml` |

Add a `<grid>` entry inside `<grids>`:

```xml
<grids>
  <grid>
    <name>OpenSim Local</name>
    <gridnick>OpenSim Local</gridnick>
    <loginuri>http://127.0.0.1:9000</loginuri>
    <loginpage>http://127.0.0.1:9000/?method=login</loginpage>
    <helperuri>http://127.0.0.1:9000</helperuri>
    <website></website>
    <support></support>
    <gridtype>0</gridtype>
  </grid>
</grids>
```

Restart Firestorm after editing. Your grid should appear in the grid selector.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| **"Could not connect to host"** | OpenSim not running or wrong port | Verify `OpenSim.exe` is running and the port matches `HttpListenerPort` |
| **Login URI is greyed out / can't edit** | Viewer using a predefined grid list | Use XML config (Method 2) instead |
| **Grid doesn't appear after XML edit** | Firestorm caches grid list on startup | Restart the viewer completely |
| **"401 Unauthorized"** | Wrong user/password or account doesn't exist | Create the account first: `create user` in the OpenSim console |
| **Connection refused on `127.0.0.1`** | Firestorm on a different machine | Replace `127.0.0.1` with the server's LAN IP (e.g. `http://192.168.1.10:9000`) |
