# dCom - Modbus TCP SCADA Client - Ignjat Radojicic PR81/2023

A WPF desktop application that acts as a Modbus TCP master (client) for monitoring and controlling field devices over an Ethernet network. The application periodically polls digital and analog values from a slave device (a Modbus simulator in development) and allows an operator to issue write commands through a dedicated control window.

Built as a university project for the course **AUSA (Automation and Systems Architecture)** at the Faculty of Technical Sciences, University of Novi Sad. The project focuses on implementing the industrial Modbus TCP protocol from scratch and integrating it into a multi-threaded MVVM application.



## What the application does

In real industrial automation systems, a SCADA (Supervisory Control and Data Acquisition) workstation communicates with field devices - PLCs, RTUs, sensors, actuators - located across a plant or substation. The SCADA side reads measurements (water level, voltage, valve state) and issues commands (open valve, start pump, set setpoint). dCom is a simplified version of that SCADA workstation.

On the other side of the network sits `ModbusSim.exe`, a simulator that pretends to be a Modbus-capable field device with a memory map of coils and registers that the operator can observe and modify.

The concrete system being simulated in this project is a **water tank with two pumps, one valve, and a level sensor**:

| Tag      | Type             | Modbus Address | Description                    |
| -------- | ---------------- | -------------- | ------------------------------ |
| L        | Analog input     | 2100           | Water level in the tank (liters) |
| STOP     | Digital output   | 2200           | Emergency stop switch (ON/OFF) |
| Valve V1 | Digital output   | 2202           | Main valve state (open/closed) |
| P1       | Digital output   | 2205           | Pump 1 switch (ON/OFF)         |
| P2       | Digital output   | 2206           | Pump 2 switch (ON/OFF)         |
| N1       | Analog output    | 2500           | Pump motor power supply        |

## Tech Stack

- **C# / .NET Framework 4.x**
- **WPF** with MVVM pattern
- **TCP sockets** (`System.Net.Sockets`) for Modbus TCP transport
- **Multi-threading** (`System.Threading`) - timer, acquisition, command execution, and automation threads
- **INotifyPropertyChanged / IDataErrorInfo** for data binding and input validation

## Architecture

The solution is split into four class library projects, each with a clear single responsibility. Higher layers depend only on abstractions from lower layers:

```
┌─────────────────────────────────────────────────────────────┐
│                        dCom (WPF)                           │
│  MainWindow ──► MainViewModel ──► Point ViewModels          │
│                                    (DigitalOutput,          │
│                                     AnalogOutput, ...)      │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    ProcessingModule                         │
│  Acquisitor          ProcessingManager    AutomationManager │
│  (periodic polling)  (read/write         (rule-based        │
│                       orchestration)      automation)       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                          Modbus                             │
│  FunctionExecutor ──► TCPConnection ──► Socket ──► Sim      │
│  ModbusFunction (PackRequest / ParseResponse)               │
│  FunctionFactory (creates the correct function by FC)       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                          Common                             │
│  Interfaces: IProcessingManager, IConfiguration, IStorage   │
│  Enums: PointType, AlarmType, ConnectionState, DState       │
│  Structs: PointIdentifier                                   │
└─────────────────────────────────────────────────────────────┘
```

**Project responsibilities:**

- **Common** - interfaces, enums, and shared types. Has no dependencies.
- **Modbus** - low-level protocol implementation. Packs request bytes, parses response bytes, handles Modbus exception codes, manages the TCP socket.
- **ProcessingModule** - orchestration. `Acquisitor` drives periodic reads, `ProcessingManager` translates domain-level calls ("read this configuration item") into Modbus commands, `AutomationManager` hosts user-defined automation rules.
- **dCom** - WPF UI and MVVM glue. `MainViewModel` wires everything together and also plays the role of both `IStateUpdater` and `IStorage`.

## How it works

### Read flow (periodic polling)

1. A **Timer thread** in `MainViewModel` fires an `AutoResetEvent` every second.
2. The **Acquisitor thread** wakes up, iterates through all configured points, and for each one checks whether its acquisition interval has elapsed.
3. If it has, the acquisitor calls `ProcessingManager.ExecuteReadCommand`, which builds a `ModbusReadCommandParameters`, asks `FunctionFactory` to create the appropriate `ReadXxxFunction`, and enqueues it.
4. The **FunctionExecutor thread** pops the command, calls `PackRequest()` to get the byte array, sends it over TCP, receives the response, and calls `ParseResponse()` to convert raw bytes into a dictionary of `{(PointType, Address) → value}`.
5. The executor raises `UpdatePointEvent`. `ProcessingManager` catches it, finds the right point in the storage, and updates its properties.
6. WPF data bindings automatically refresh the UI.

### Write flow (operator action)

1. The operator double-clicks a row in the main grid, which opens the control window.
2. They enter a value and click **Write**, triggering a `RelayCommand` on the point ViewModel.
3. `ProcessingManager.ExecuteWriteCommand` is called, which dispatches to either `WriteSingleCoilFunction` (FC 5) or `WriteSingleRegisterFunction` (FC 6).
4. From there the flow is identical to a read: factory → enqueue → pack → send → receive → parse → event → UI update.

### Threading

Four threads cooperate:

- **Main (UI) thread** - renders WPF and handles user input.
- **Timer thread** - heartbeat, fires acquisition and automation triggers every second.
- **Acquisition thread** - schedules read commands.
- **FunctionExecutor thread** - serializes the actual socket I/O so that requests do not collide.
- **Automation thread** - evaluates user-defined rules.

Cross-thread UI updates (logs, connection state) are marshaled back to the UI thread via `Dispatcher.Invoke`.

## Modbus TCP in one page

Modbus is a request/response protocol from 1979. Modbus TCP wraps the original serial frame in a simple TCP envelope called **MBAP** (Modbus Application Protocol) header:

```
┌──────────────────────────── Request ────────────────────────────┐
│ Transaction ID (2) │ Protocol ID = 0 (2) │ Length (2) │ Unit ID (1) │
│ Function Code (1)  │ Data (N) ...                                  │
└─────────────────────────────────────────────────────────────────┘
```

All multi-byte fields are **big-endian** (network byte order), which is why `IPAddress.HostToNetworkOrder` shows up all over the place in `PackRequest`.

The six function codes implemented in this project:

| FC | Name                    | Reads / Writes            |
| -- | ----------------------- | ------------------------- |
| 1  | Read Coils              | digital outputs (1 bit)   |
| 2  | Read Discrete Inputs    | digital inputs (1 bit)    |
| 3  | Read Holding Registers  | analog outputs (16 bit)   |
| 4  | Read Input Registers    | analog inputs (16 bit)    |
| 5  | Write Single Coil       | digital output (1 bit)    |
| 6  | Write Single Register   | analog output (16 bit)    |

If the slave cannot fulfill the request, it responds with the function code OR'd with `0x80` and a one-byte exception code (illegal function, illegal data address, etc.), which is handled by `ModbusFunction.HandleException`.

## Configuration

A plain-text file `RtuCfg.txt` drives the application. It must be placed next to the executable and is parsed by `ConfigReader` at startup.

```
STA 148         # slave address
TCP 51676       # TCP port the simulator listens on
DBC 5           # delay between automation commands (seconds)

# TYPE  NUM  ADDR  DEC  MIN  MAX  DEFAULT  PROC  @DESCRIPTION  INTERVAL
IN_REG  1    2100  0    0    10000 0       I     @L            4
DO_REG  1    2200  0    0    1    0        D     @STOP         2
DO_REG  1    2202  0    0    1    0        D     @ValveV1      2
DO_REG  1    2205  0    0    1    0        D     @P1           2
DO_REG  1    2206  0    0    1    0        D     @P2           2
HR_INT  1    2500  0    0    1000 0        I     @N1           4
```

Supported register types:
- `DO_REG` - digital output (coil)
- `DI_REG` - digital input (discrete input)
- `IN_REG` - analog input (input register)
- `HR_INT` - analog output (holding register)

## Running the project

### Prerequisites
- Visual Studio 2019 or later
- .NET Framework 4.x developer pack
- `ModbusSim.exe` provided with the course materials

### Steps
1. Place `RtuCfg.txt` next to the compiled `dCom.exe` (usually `dCom/bin/Debug/`).
2. Start `ModbusSim.exe`, set the TCP port to `51676` and slave address to `148` (must match `RtuCfg.txt`).
3. Set `dCom` as the startup project and run it.
4. The status bar should transition from `DISCONNECTED` to `CONNECTED` within a couple of seconds. Point values then update on their configured intervals.

## Project status

Skeleton plus the following work completed by me:

- `RtuCfg.txt` for the assigned water-tank system
- `Acquisitor.Acquisition_DoWork` - periodic polling loop
- All six Modbus function implementations:
  - `ReadCoilsFunction` (FC 1)
  - `ReadDiscreteInputsFunction` (FC 2)
  - `ReadHoldingRegistersFunction` (FC 3)
  - `ReadInputRegistersFunction` (FC 4)
  - `WriteSingleCoilFunction` (FC 5)
  - `WriteSingleRegisterFunction` (FC 6)

Colloquium extensions (not yet implemented):
- `AlarmProcessor` - `HighLimit` / `LowLimit` / `AbnormalValue` alarm detection
- `AutomationManager` - rule-based logic (e.g. "if water level drops below threshold and valve is open, start P1")

## What I learned

This project was my first serious contact with industrial communication protocols. The concrete things I took away:

**Low-level protocol work.** Writing `PackRequest` and `ParseResponse` by hand forced me to actually think about memory layout - how a number larger than a byte is split across bytes, why endianness matters, and why `IPAddress.HostToNetworkOrder` is necessary on Intel CPUs. It is one thing to read that Modbus TCP is big-endian, and another to spend half an hour debugging a byte-swap bug.

**Threading and synchronization.** Four threads run at the same time: UI, timer, acquisition, and function executor. Understanding that I cannot touch UI properties from a background thread (and that `Dispatcher.Invoke` is how you marshal back to the UI thread) was the first real threading lesson. Understanding how `AutoResetEvent` coordinates the timer and the acquisitor - one thread pulses a signal once per second, the other waits on that signal - was the second.

**The MVVM pattern in WPF.** Before this project, I had only built small forms-style WPF apps. Seeing a real separation between View (XAML), ViewModel (`BasePointItem`, `MainViewModel`), and Model (`ConfigItem`, `IPoint`) with proper `INotifyPropertyChanged` binding, plus `RelayCommand` for buttons and `IDataErrorInfo` for input validation, made the pattern click.

**Interface-driven architecture.** The `Common` project exposes only interfaces (`IProcessingManager`, `IStorage`, `IConfiguration`, `IStateUpdater`). Every concrete implementation lives in another project and depends only on those interfaces. This is classic dependency inversion, and seeing it in a working codebase (instead of a one-slide diagram in class) finally made the point stick.

**Periodic vs event-driven systems.** SCADA systems are fundamentally periodic - you poll even if nothing has changed, because the slave cannot push updates on its own. The acquisitor pattern (a single thread that ticks once per second and dispatches work based on per-point intervals) is a very concrete example of a pattern that shows up in many other domains - schedulers, cron jobs, game loops.

**The producer / consumer pattern.** The `FunctionExecutor` pulls `IModbusFunction` objects from a queue. Multiple producers (the acquisitor, the UI via write commands, the automation manager) all push into that queue. This decouples the "what do I want to do" question from the "when and how do I actually send it" question.

**Debugging with Wireshark.** Opening a raw network capture during development and seeing the exact bytes I packed leave my machine - and the exact bytes come back - made it possible to debug Modbus problems in a way that console logs never could.

## References

- Modbus Application Protocol Specification V1.1b3 - modbus.org
- Modbus Messaging on TCP/IP Implementation Guide V1.0b
- Course materials for AUSA, FTN Novi Sad
