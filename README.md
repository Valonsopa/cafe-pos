# Cafe POS

A local-first cafe point-of-sale system with a Windows desktop cashier application and a Flutter mobile ordering client.

The desktop app hosts a local HTTP/WebSocket server, manages open orders and payments, stores data in SQLite, and prints or generates receipts. The mobile app lets service staff select a table, add menu items to a cart, and send an order to the desktop server.

## Features

- Windows WPF desktop cashier interface
- Open-order management for table and ticket orders
- Cash and card payment recording
- Automatic and manual order closing
- Text receipt printing
- PDF receipt generation
- SQLite persistence
- Embedded ASP.NET Core API on port `5055`
- WebSocket support for order updates
- Flutter mobile ordering interface
- Table selection and configurable desktop server URL
- Idempotent order submission

## Architecture

```text
cafe-pos/
├── desktop/
│   ├── CafePos.slnx
│   ├── CafePos.Desktop/
│   │   ├── Data/              # SQLite database and schema
│   │   ├── Printing/          # Receipt printers
│   │   ├── Server/            # HTTP API, WebSocket, and PDF receipts
│   │   ├── App.xaml
│   │   └── MainWindow.xaml
│   └── phone-android/
│       └── cafe_pos_mobile/   # Flutter client
```

The desktop application listens on `0.0.0.0:5055`, so devices on the same network can use it. The mobile app sends orders to the desktop API at:

```text
http://<desktop-ip-address>:5055
```

## Technology

### Desktop

- .NET 8
- WPF
- ASP.NET Core minimal APIs
- SQLite with `Microsoft.Data.Sqlite`
- Dapper
- QuestPDF
- WebSockets

### Mobile

- Flutter
- Dart
- HTTP package
- Material 3

## Prerequisites

### Desktop

- Windows 10 or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A printer supported by Windows, if using silent text receipt printing

### Mobile

- [Flutter SDK](https://docs.flutter.dev/get-started/install)
- Android Studio or another configured Flutter target
- A mobile device or emulator that can reach the desktop host

## Running the Desktop App

Clone the repository, then open a terminal at the repository root:

```powershell
git clone https://github.com/Valonsopa/cafe-pos.git
cd cafe-pos\desktop
dotnet restore
cd CafePos.Desktop
dotnet run
```

Alternatively, open `desktop/CafePos.slnx` in Visual Studio and run `CafePos.Desktop`.

The app starts the local API on:

```text
http://localhost:5055
```

Check its health endpoint:

```text
http://localhost:5055/api/health
```

## Data Location

The desktop application creates and uses a SQLite database at:

```text
C:\ProgramData\CafePOS\pos.sqlite
```

The `Schema.sql` file initializes the database tables when the desktop application starts.

## Running the Mobile App

1. Start the desktop application.
2. Find the Windows PC's IPv4 address, for example:

   ```powershell
   ipconfig
   ```

3. Open `desktop/phone-android/cafe_pos_mobile/lib/main.dart`.
4. Change `baseUrl` in the `HomeScreen` state to the desktop PC address:

   ```dart
   String baseUrl = "http://<desktop-ip-address>:5055";
   ```

   The Flutter settings dialog can also update the server URL while the app is running.

5. Install packages and run the app:

   ```powershell
   cd desktop\phone-android\cafe_pos_mobile
   flutter pub get
   flutter run
   ```

The desktop PC and mobile device must be on the same network. Allow the app through the Windows Firewall if LAN connections are blocked.

## API Overview

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `/api/health` | Check server health |
| `GET` | `/api/orders/open` | List open orders |
| `GET` | `/api/orders/{orderId}` | Get order details |
| `POST` | `/api/orders/add-items` | Create or add items to an order |
| `POST` | `/api/orders/{orderId}/payments` | Record a cash or card payment |
| `POST` | `/api/orders/{orderId}/close` | Close a fully paid order |
| `GET` | `/api/orders/{orderId}/receipt.pdf` | Download a receipt PDF |
| `WS` | `/ws` | Receive order WebSocket events |

## Development

### Desktop build

```powershell
cd desktop
dotnet build CafePos.slnx
```

### Flutter checks

```powershell
cd desktop\phone-android\cafe_pos_mobile
flutter analyze
flutter test
```

## Notes

- This project is intended for local or demo use.
- The mobile menu is currently defined in the Flutter app, while server-side menu-item management is not yet included.
- Authentication and role management are not implemented; the demo uses fixed worker and device identifiers.
- The API listens over plain HTTP. Do not expose it directly to an untrusted network without adding authentication and HTTPS.
- Payment entries record payment activity; no external card-payment provider is integrated.
