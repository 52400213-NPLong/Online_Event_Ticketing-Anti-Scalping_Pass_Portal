## 1. Linked Issue
Closes #[Issue Number] (e.g., Closes #101)

## 2. Description of Changes
- Implemented API endpoint for reservation locks / seat selection.
- Added dynamic TOTP QR code generation service for anti-scalping passes.
- Integrated automated Playwright contract tests for ticket booking endpoints.

## 3. Type of Change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [x] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update

## 4. Verification & Evidence Checklist
- [x] Code compiles cleanly without linting warnings.
- [x] Added unit tests covering new business logic.
- [x] Unit test coverage maintained above 60%.
- [x] Attached screenshot/log of passing automated test suite below.

## 5. Peer Review Request
@team-member-2 Please review the reservation locking algorithm and OpenAPI contract alignment.

EventTicketingSystem/
├── .github/
│   └── workflows/
│       └── ci.yml                      # Workflow CI tự động (Build, Unit Test, Check formatting)
│
├── docs/                               # Tài liệu thiết kế & Hợp đồng API
│   ├── openapi.yaml                    # OpenAPI 3.1 Spec (API Contract giữa FE & BE)
│   ├── erd-diagram.png                 # Biểu đồ Thực thể Mối quan hệ CSDL SQL Server[cite: 8]
│   └── architecture-sequence.puml      # Sơ đồ Sequence Diagram chọn ghế & Dynamic QR[cite: 8]
│
├── src/                                # Mã nguồn ứng dụng
│   │
│   ├── EventTicketing.Core/            # LAYER 1: Core Domain (Entities, Interfaces, Enums)
│   │   ├── Domain/
│   │   │   ├── Entities/
│   │   │   │   ├── User.cs             # Thông tin tài khoản & Vai trò (Buyer, Admin, GateStaff)[cite: 8]
│   │   │   │   ├── Event.cs            # Thông tin sự kiện, thời gian mở bán[cite: 8]
│   │   │   │   ├── Seat.cs             # Danh sách ghế, vị trí & trạng thái[cite: 8]
│   │   │   │   ├── SeatHold.cs         # Thông tin giữ chỗ tạm thời (Concurrency Lock)[cite: 8]
│   │   │   │   ├── Ticket.cs           # Vé điện tử định danh[cite: 8]
│   │   │   │   └── CheckInLog.cs       # Lịch sử soát vé tại cổng[cite: 8]
│   │   │   └── Enums/
│   │   │       ├── SeatStatus.cs       # Available, Held, Sold[cite: 8]
│   │   │       └── TicketStatus.cs     # Valid, CheckedIn, Cancelled[cite: 8]
│   │   │
│   │   ├── Interfaces/
│   │   │   ├── IDynamicQrService.cs    # Interface cho Engine Dynamic QR[cite: 8]
│   │   │   ├── ITicketBookingService.cs# Interface cho Service chọn ghế & khóa đồng thời[cite: 8]
│   │   │   └── IUserRepository.cs
│   │   │
│   │   └── Constants/
│   │       └── AntiScalpingConfig.cs   # Hằng số (TOKEN_VALIDITY_SECONDS=30, MAX_TICKETS=4)[cite: 8]
│   │
│   ├── EventTicketing.Infrastructure/ # LAYER 2: Infrastructure (Database, Security, Dynamic QR)
│   │   ├── Data/
│   │   │   ├── ApplicationDbContext.cs # EF Core DbContext (Parameterized LINQ queries)[cite: 8]
│   │   │   └── Migrations/             # EF Core DB Migration Scripts[cite: 8]
│   │   ├── Security/
│   │   │   ├── DynamicQrService.cs     # Cài đặt HMAC-SHA256 Dynamic QR Engine[cite: 8]
│   │   │   ├── PasswordHasher.cs       # Hash mật khẩu bằng BCrypt[cite: 8]
│   │   │   └── JwtTokenGenerator.cs    # Tạo và xác minh JWT Access / Refresh Token[cite: 8]
│   │   └── Repositories/
│   │       └── TicketRepository.cs
│   │
│   ├── EventTicketing.WebApi/          # LAYER 3: Presentation (Controllers, Realtime SignalR, Middleware)
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs       # API /api/v1/auth (Login, Register)
│   │   │   ├── EventsController.cs     # API /api/v1/events (Danh sách & Sơ đồ ghế)
│   │   │   ├── TicketsController.cs    # API /api/v1/tickets (Hold, Reserve, Dynamic QR)[cite: 8]
│   │   │   └── GateController.cs       # API /api/v1/gate/checkin (Soát vé tại cổng)[cite: 8]
│   │   ├── Hubs/
│   │   │   └── SeatHub.cs              # SignalR Hub broadcast trạng thái ghế Realtime[cite: 8]
│   │   ├── Middlewares/
│   │   │   ├── RateLimitingMiddleware.cs# Giới hạn số request chống Bot[cite: 8]
│   │   │   └── ErrorHandlingMiddleware.cs # Global Exception Handling
│   │   ├── appsettings.json            # Cấu hình hệ thống chung
│   │   └── Program.cs                  # Entry point & Dependency Injection (DI) setup
│   │
│   └── EventTicketing.WebClient/       # FRONTEND: Mobile Web Responsive (M1 Lead)[cite: 8]
│       ├── public/
│       │   └── favicon.ico
│       ├── src/
│       │   ├── assets/
│       │   │   └── css/style.css       # Style sơ đồ ghế & UI responsive[cite: 8]
│       │   ├── js/
│       │   │   ├── seatmap.js          # Sơ đồ ghế tương tác & SignalR Client[cite: 8]
│       │   │   ├── dynamic-qr.js       # Tự động render mã Dynamic QR 30 giây/lần[cite: 8]
│       │   │   └── gate-scanner.js     # Quét mã QR bằng Camera cho Gate Staff[cite: 8]
│       │   └── pages/
│       │       ├── index.html          # Trang chủ & Danh sách sự kiện[cite: 8]
│       │       ├── seat-selection.html # Trang chọn ghế thời gian thực[cite: 8]
│       │       ├── my-tickets.html     # Ví vé điện tử chứa Dynamic QR[cite: 8]
│       │       └── gate-checkin.html   # Trạm soát vé dành cho bảo vệ cổng[cite: 8]
│       └── package.json
│
├── tests/                              # KIỂM THỬ TỰ ĐỘNG (M4 Lead)[cite: 8]
│   └── EventTicketing.Tests/
│       ├── UnitTests/
│       │   ├── DynamicQrServiceTests.cs # Unit Tests AAA cho Dynamic QR[cite: 8]
│       │   └── TicketBookingTests.cs   # Unit Tests AAA kiểm tra khóa ghế & chống overbooking[cite: 8]
│       └── EventTicketing.Tests.csproj
│
├── .editorconfig                       # Quy ước Code Style & Formatting cho C#[cite: 8]
├── .env.example                        # Mẫu cấu hình biến môi trường (Secrets)[cite: 8]
├── .gitignore                          # Loại bỏ bin, obj, node_modules, .env[cite: 8]
├── docker-compose.yml                  # File đóng gói chạy toàn bộ app & DB[cite: 8]
├── README.md                           # Hướng dẫn chạy dán Git Repo
└── EventTicketingSystem.sln            # Visual Studio Solution File