# .NET Cross-Platform Apps

Cross-platform .NET applications that compile on Mac and run on both Windows and Mac.

## Projects

| Project              | Type           | Description                                      |
| -------------------- | -------------- | ------------------------------------------------ |
| `HelloWorld`         | Console        | Simple "Hello, World!" console app               |
| `HelloWorldGUI`      | GUI (Avalonia) | Interactive GUI with button and click counter    |
| `MachineIntegration` | Service        | Pathology machine connector (Serial/Network/TCP) |

---

## Prerequisites

### Install .NET SDK on Mac

```bash
brew install dotnet
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
```

---

## Machine Integration Service

Bidirectional communication service for pathology lab machines supporting:
- **Serial port** connections (COM ports)
- **Network/TCP** connections (Server or Client mode)
- **ASTM/LIS2-A2** protocol
- **MySQL** database integration

### Configuration

Create a `config.json` file:

```json
{
  "user": "root",
  "password": "admin",
  "host": "localhost",
  "db_port": 3306,
  "database": "flabs_db",
  "machine_name": "em200",
  "run_port": 5100,
  "type": "serial",
  "network_type": "server",
  "network_ip": "192.168.1.15",
  "network_port": 5200,
  "network_ack": false,
  "serial_port": "COM7",
  "serial_baudrate": 9600,
  "serial_parity": "none",
  "serial_data_bits": 8,
  "serial_stop_bits": 1
}
```

### Config Options

| Field             | Description                                      |
| ----------------- | ------------------------------------------------ |
| `type`            | `"serial"` or `"network"`                        |
| `network_type`    | `"server"` (listen) or `"client"` (connect)      |
| `network_ack`     | Send ACK for each control character              |
| `serial_parity`   | `"none"`, `"odd"`, `"even"`, `"mark"`, `"space"` |
| `serial_stop_bits`| `1` or `2`                                       |

### Database Tables

The service uses these tables:

```sql
-- Store machine readings
CREATE TABLE flabs_machinedata (
  LabNo VARCHAR(50),
  Machine_ID VARCHAR(50),
  Machine_Param VARCHAR(100),
  Reading VARCHAR(50),
  isImage BOOLEAN,
  imageType VARCHAR(20),
  ImageUrl VARCHAR(255)
);

-- Patient data for bidirectional communication
CREATE TABLE flabs_data (
  LabNo VARCHAR(50),
  LabObservation_ID VARCHAR(50),
  PName VARCHAR(100),
  Age VARCHAR(10),
  Gender VARCHAR(10)
);

-- Machine parameter mapping
CREATE TABLE flabs_host_mapping (
  LabObservation_ID VARCHAR(50),
  MachineID VARCHAR(50),
  AssayNo VARCHAR(50)
);
```

### Build Machine Integration

**Build for Windows:**

```bash
cd MachineIntegration
dotnet publish -r win-x64 -c Release --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ../publish-machine/windows
```

**Build for Mac (ARM64):**

```bash
cd MachineIntegration
dotnet publish -r osx-arm64 -c Release --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ../publish-machine/mac
```

### Run Machine Integration

**On Mac:**

```bash
./publish-machine/mac/MachineIntegration config.json
```

**On Windows:**

```cmd
publish-machine\windows\MachineIntegration.exe config.json
```

Or run multiple machines with different configs:

```bash
./MachineIntegration em200_config.json
./MachineIntegration sysmex_config.json
```

---

## Build Commands

### Console App (HelloWorld)

**Build for Windows:**

```bash
cd HelloWorld
dotnet publish -r win-x64 -c Release --self-contained true -o ../publish/windows
```

**Build for Mac (ARM64 - Apple Silicon):**

```bash
cd HelloWorld
dotnet publish -r osx-arm64 -c Release --self-contained true -o ../publish/mac
```

**Build for Mac (Intel):**

```bash
cd HelloWorld
dotnet publish -r osx-x64 -c Release --self-contained true -o ../publish/mac-intel
```

---

### GUI App (HelloWorldGUI)

**Build for Windows (single .exe file):**

```bash
cd HelloWorldGUI
dotnet publish -r win-x64 -c Release --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ../publish-gui/windows
```

**Build for Mac (ARM64 - Apple Silicon):**

```bash
cd HelloWorldGUI
dotnet publish -r osx-arm64 -c Release --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ../publish-gui/mac
```

**Build for Mac (Intel):**

```bash
cd HelloWorldGUI
dotnet publish -r osx-x64 -c Release --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ../publish-gui/mac-intel
```

---

## Run Commands

### Console App

**On Mac:**

```bash
./publish/mac/HelloWorld
```

**On Windows:**

```cmd
publish\windows\HelloWorld.exe
```

---

### GUI App

**On Mac:**

```bash
./publish-gui/mac/HelloWorldGUI
```

**On Windows:**

```cmd
publish-gui\windows\HelloWorldGUI.exe
```

---

## Distribution

| Platform    | Console App                           | GUI App                              | Machine Integration                              |
| ----------- | ------------------------------------- | ------------------------------------ | ------------------------------------------------ |
| **Windows** | Send entire `publish/windows/` folder | Send `HelloWorldGUI.exe` only        | Send `MachineIntegration.exe` + `config.json`    |
| **Mac**     | Send entire `publish/mac/` folder     | Send entire `publish-gui/mac/` folder| Send `MachineIntegration` + `config.json`        |

> **Note:** All builds are self-contained — no .NET installation required on target machines.

---

## Output Locations

```
publish/
├── windows/              # Console app for Windows
└── mac/                  # Console app for Mac

publish-gui/
├── windows/              # GUI app for Windows (single .exe)
└── mac/                  # GUI app for Mac (with native libs)

publish-machine/
├── windows/              # Machine Integration for Windows
│   ├── MachineIntegration.exe
│   └── config.json
└── mac/                  # Machine Integration for Mac
    ├── MachineIntegration
    └── config.json
```
