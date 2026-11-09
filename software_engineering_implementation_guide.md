# Online Event Ticketing & Anti-Scalping Pass Portal
## Complete SDLC & Engineering Execution Blueprint (Course 502045)

---

## 1. Team Allocation & File Structure Setup

### 1.1 Role Matrix (4-Member Scope based on Topic 13 Specification)
* **Member 1 (UI/UX Developer):** Interactive Seat Map UI, Dynamic Pass Wallet Component, Event Browsing & Responsive Frontend (`EventTicketing.WebClient`).
* **Member 2 (Backend & Security Engineer):** Anti-bot Rate Limiting, Dynamic QR HMAC-SHA256 Token Generator, Gate Check-in verification (`DynamicQrService.cs`, `GateController.cs`).
* **Member 3 (Database & Core Systems Architect):** Transactional Schema, Concurrency Seat Locking Engine, Auto-Release Expired Holds (`TicketBookingService.cs`, `ExpiredHoldCleanupWorker.cs`).
* **Member 4 (QA, DevOps & CI/CD Lead):** xUnit Automated Unit Testing Suite (>80% coverage), GitHub Actions CI/CD pipeline, Code Quality rules (`EventTicketing.Tests`, `.github/workflows/ci.yml`).

### 1.2 Unified Repository Folder Structure (.NET 10 Clean Architecture)
Repository folder structure:

```text
Online_Event_Ticketing-Anti-Scalping_Pass_Portal/
├── .github/
│   ├── PULL_REQUEST_TEMPLATE.md
│   └── workflows/
│       └── ci.yml
├── docs/
│   ├── architecture-sequence.puml
│   └── openapi.yaml
├── src/
│   ├── EventTicketing.Core/             # Tầng Domain (Nghiệp vụ cốt lõi, Entities, Interfaces)
│   │   ├── Constants/
│   │   │   └── AntiScalpingConfig.cs   # Hằng số chống Magic Numbers (30s QR, 10m Hold, max 4 tickets)
│   │   ├── Domain/
│   │   │   ├── Entities/               # User, Event, Seat, SeatHold, Ticket, CheckInLog
│   │   │   └── Enums/                  # SeatStatus, TicketStatus
│   │   └── Interfaces/                 # ITicketBookingService, IDynamicQrService, IUserRepository, ITicketRepository
│   ├── EventTicketing.Infrastructure/   # Tầng Hạ tầng (Data Access, Security, Repositories)
│   │   ├── Data/
│   │   │   └── ApplicationDbContext.cs # Quản lý dữ liệu và SeedSampleData
│   │   ├── Repositories/               # UserRepository, TicketRepository
│   │   ├── Security/                   # DynamicQrService (HMAC-SHA256), PasswordHasher
│   │   └── Services/                   # TicketBookingService (Concurrency lock, Anti-scalping)
│   ├── EventTicketing.WebApi/           # Tầng Web API & Middleware
│   │   ├── Controllers/                # TicketsController, GateController, EventsController, AuthController
│   │   ├── Hubs/                       # SeatHub (SignalR realtime seat updates)
│   │   ├── Middlewares/                # RateLimitingMiddleware, ErrorHandlingMiddleware
│   │   ├── Services/                   # ExpiredHoldCleanupWorker (BackgroundService quét ghế hết hạn)
│   │   └── Program.cs                  # Cấu hình DI, CORS, Static Files, Middleware
│   └── EventTicketing.WebClient/        # Tầng Giao diện người dùng (Frontend)
│       └── public/
│           ├── assets/css/style.css
│           ├── js/
│           │   ├── seatmap.js          # Sơ đồ chọn ghế, giữ chỗ 10 phút, đặt vé
│           │   ├── dynamic-qr.js       # Ví vé điện tử, tự làm mới QR mỗi 30s
│           │   └── gate-scanner.js     # Quét và xác thực vé tại cổng sự kiện
│           └── pages/
│               ├── index.html
│               ├── seat-selection.html
│               ├── my-tickets.html
│               └── gate-checkin.html
├── tests/
│   └── EventTicketing.Tests/            # Tầng Kiểm thử tự động (xUnit)
│       └── UnitTests/
│           ├── TicketBookingTests.cs   # Test giữ chỗ, thanh toán, chống quá 4 vé, giải phóng ghế
│           ├── DynamicQrServiceTests.cs# Test sinh và xác thực mã QR động HMAC-SHA256
│           └── GateCheckInTests.cs     # Test soát vé tại cổng, chống vé giả & chụp ảnh màn hình
├── docker-compose.yml
├── openapi-spec.yaml
├── README.md
└── software_engineering_implementation_guide.md
```

---

## 2. Database & Anti-Scalping Architecture (Member 2 & 3)

### 2.1 Database Schema (Transactional DDL)

```sql
-- Users Table
CREATE TABLE Users (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Email NVARCHAR(255) UNIQUE NOT NULL,
    PasswordHash NVARCHAR(255) NOT NULL,
    FullName NVARCHAR(100) NOT NULL,
    Role NVARCHAR(20) DEFAULT 'CUSTOMER',
    CreatedAt DATETIME2 DEFAULT SYSUTCDATETIME()
);

-- Events Table
CREATE TABLE Events (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Title NVARCHAR(255) NOT NULL,
    Venue NVARCHAR(255) NOT NULL,
    StartTime DATETIME2 NOT NULL,
    CreatedAt DATETIME2 DEFAULT SYSUTCDATETIME()
);

-- Seats Table
CREATE TABLE Seats (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    EventId UNIQUEIDENTIFIER FOREIGN KEY REFERENCES Events(Id) ON DELETE CASCADE,
    SeatLabel NVARCHAR(20) NOT NULL,
    Price DECIMAL(18, 2) NOT NULL,
    Status INT DEFAULT 0, -- 0: Available, 1: Held, 2: Sold
    HeldByUserId NVARCHAR(100) NULL,
    HoldExpiresAt DATETIME2 NULL,
    RowVersion ROWVERSION -- Optimistic Concurrency Control
);

-- Tickets Table (Issued Passes)
CREATE TABLE Tickets (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId NVARCHAR(100) NOT NULL,
    SeatId UNIQUEIDENTIFIER FOREIGN KEY REFERENCES Seats(Id),
    DynamicTokenSecret NVARCHAR(64) NOT NULL,
    Status INT DEFAULT 0, -- 0: Issued, 1: Used, 2: Cancelled
    CreatedAt DATETIME2 DEFAULT SYSUTCDATETIME()
);

-- CheckInLogs Table (Gate Scans)
CREATE TABLE CheckInLogs (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    TicketId UNIQUEIDENTIFIER NOT NULL,
    UserId NVARCHAR(100) NOT NULL,
    StaffId NVARCHAR(100) NOT NULL,
    ScannedAt DATETIME2 DEFAULT SYSUTCDATETIME(),
    Status NVARCHAR(20) NOT NULL,
    Reason NVARCHAR(255) NULL
);
```

### 2.2 Concurrency Seat Locking Mechanism (C# ASP.NET Core + SQL Server)
Được triển khai trong `TicketBookingService.cs`:
1. **Kiểm tra vi phạm chống đầu cơ (Anti-Scalping Rule - FR-18):** Chặn người dùng nếu tổng số vé sở hữu + ghế đang giữ chỗ vượt quá 4 vé (`AntiScalpingConfig.MAX_TICKETS_PER_USER`).
2. **Transaction ACID & Khóa dòng SQL Server (`UPDLOCK, ROWLOCK` + `RowVersion` - FR-14, NFR-12):** Mở `BeginTransactionAsync`, sắp xếp `SeatId` tăng dần chống Deadlock, truy vấn `SELECT * FROM [Seats] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = @p0` kết hợp kiểm tra `RowVersion` (Optimistic Concurrency Control) ngăn chặn triệt để hiện tượng đặt trùng ghế (Double-Booking).
3. **Tự động phục hồi ghế hết hạn (FR-16, NFR-15):** Kiểm tra nếu ghế có trạng thái `Held` nhưng `HoldExpiresAt <= DateTime.UtcNow`, hệ thống và `ExpiredHoldCleanupWorker` (chu kỳ 5 giây) tự động hoàn lại trạng thái `Available` cho người khác mua.

### 2.3 Dynamic QR Pass Generator (HMAC-SHA256 Anti-Scalping Engine)
Được triển khai trong `DynamicQrService.cs`:
- Thuật toán băm mã hóa: `HMAC-SHA256` kết hợp giữa `ticketId`, `userId`, và `currentTimestampWindow` (`UnixTimestamp / 30`).
- Token tự động thay đổi sau mỗi 30 giây.
- Xác thực tại cổng (`ValidateToken`): Cho phép độ lệch tối đa 1 window thời gian (30 giây) để bù đắp độ trễ mạng (`A-01`, `C-03`).
- **Chống gian lận chụp màn hình (Screenshot replay):** Khi một vé đã được quét thành công lần đầu, trạng thái lập tức chuyển sang `TicketStatus.Used`. Mọi lần quét tiếp theo của cùng một vé sẽ bị hệ thống từ chối ngay lập tức (Conflict 409).

---

## 3. Automated Testing & DevOps (Member 4)

### 3.1 Unit Testing Suite (xUnit - `EventTicketing.Tests`)
Bộ test tự động bao gồm **19 bài kiểm thử** phủ trọn vẹn 4 nhóm tính năng trọng yếu:
1. **Seat Booking & Anti-Scalping:**
   - Giữ chỗ thành công khi ghế trống.
   - Trả về lỗi khi ghế đang bị giữ hoặc đã bán.
   - Kích hoạt quy tắc chống đầu cơ chặn người mua vé thứ 5.
   - Tự động thu hồi ghế khi hết hạn 10 phút.
   - Xác nhận đặt vé và phát hành vé điện tử.
2. **Dynamic QR Token:**
   - Sinh chuỗi token HMAC-SHA256 hợp lệ.
   - Xác thực token thành công trong khung thời gian hợp lệ.
   - Từ chối token khi người quét không phải chính chủ vé.
3. **Gate Check-in:**
   - Quét vé thành công và chuyển trạng thái vé sang `Used`.
   - Phát hiện và chặn đứng vé đã check-in trước đó (Chống chụp ảnh bán lại).
   - Từ chối vé có mã token hết hạn hoặc giả mạo.

### 3.2 Automated CI/CD Pipeline (`.github/workflows/ci.yml`)

```yaml
name: Software Engineering CI Quality Gate

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main, develop ]

jobs:
  build-and-test:
    runs-on: ubuntu-latest

    steps:
    - name: Checkout Source Code
      uses: actions/checkout@v3

    - name: Setup .NET SDK
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'

    - name: Restore Dependencies
      run: dotnet restore

    - name: Build Solution
      run: dotnet build --configuration Release --no-restore

    - name: Run xUnit Unit Tests
      run: dotnet test --no-restore --verbosity normal
```

---

## 4. Hướng dẫn Khởi chạy và Trải nghiệm (Execution Guide)

### Bước 1: Khởi chạy trọn gói bằng Docker Compose (Khuyên dùng - Đồng nhất mọi máy)
Mở Docker Desktop và chạy tại thư mục gốc của dự án:
```bash
cd Online_Event_Ticketing-Anti-Scalping_Pass_Portal
docker compose up --build
```
*(Hoặc nếu phát triển trực tiếp bằng mã nguồn: chạy `dotnet run --project src/EventTicketing.WebApi/EventTicketing.WebApi.csproj`)*.

Hệ thống sẽ khởi chạy tại: `http://localhost:5000`.

### Bước 2: Trải nghiệm Giao diện WebClient
Mở trình duyệt web và truy cập các liên kết:
- **Trang chủ & Khám phá sự kiện:** `http://localhost:5000/pages/index.html`
- **Sơ đồ chọn ghế & Giữ chỗ 10 phút:** `http://localhost:5000/pages/seat-selection.html`
- **Ví vé điện tử & Mã QR động 30s:** `http://localhost:5000/pages/my-tickets.html`
- **Cổng quét soát vé (Gate Check-in):** `http://localhost:5000/pages/gate-checkin.html`

### Bước 3: Chạy toàn bộ Unit Tests
```bash
dotnet test tests/EventTicketing.Tests/EventTicketing.Tests.csproj
```
Kết quả kiểm thử đạt **19/19 Passed** đảm bảo chất lượng kỹ thuật cao theo yêu cầu môn học.
