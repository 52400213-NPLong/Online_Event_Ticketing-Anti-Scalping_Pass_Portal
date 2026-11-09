# Online Event Ticketing & Anti-Scalping Pass Portal
## Đồ án môn học: Công nghệ Phần mềm (Course 502045) — Topic 13

Dự án là nền tảng bán vé sự kiện trực tuyến chuyên sâu, tích hợp các cơ chế bảo mật và quy tắc nghiệp vụ chống đầu cơ / phe vé (Anti-Scalping): khóa giữ chỗ ghế tạm thời tránh xung đột đồng thời, mã QR động tự làm mới mỗi 30 giây chống chụp ảnh màn hình bán lại, giới hạn số vé mua tối đa trên mỗi tài khoản và quy trình soát vé thời gian thực tại cổng sự kiện.

---

## 1. Mô tả tổng quan kiến trúc

Hệ thống được thiết kế theo kiến trúc phân tầng chuẩn mực **Clean Architecture** trên nền tảng **.NET 10 (C#)**:

- **Backend API (`EventTicketing.WebApi`):** ASP.NET Core Web API, Middleware chống spam (Rate Limiting), Background Service quét ghế quá hạn định kỳ 5s, Hub SignalR cập nhật trạng thái ghế.
- **Domain & Core (`EventTicketing.Core`):** Định nghĩa thực thể nghiệp vụ độc lập (kèm `RowVersion` chống xung đột đồng thời), Enums, Interfaces, và hằng số tham số chống Magic Numbers (`AntiScalpingConfig.cs`).
- **Hạ tầng & CSDL (`EventTicketing.Infrastructure`):** Kết nối **Microsoft SQL Server** qua **Entity Framework Core**, quản lý Transaction ACID, khóa dòng `WITH (UPDLOCK, ROWLOCK)` kết hợp `RowVersion`, mã hóa mật khẩu, tạo token mã QR động HMAC-SHA256.
- **Giao diện Client (`EventTicketing.WebClient`):** Responsive HTML5/CSS3/JavaScript tương tác trực tiếp với API backend: Đăng nhập / Đăng ký phân quyền theo vai trò (RBAC), Tìm kiếm sự kiện, Sơ đồ chọn nhiều ghế (1–4 ghế), Ví vé điện tử QR động, Thiết bị quét vé tại cổng và Dashboard Quản trị & Báo cáo doanh thu.
- **Kiểm thử tự động (`EventTicketing.Tests`):** Bộ unit test xUnit tự động kiểm thử 100% các ca nghiệp vụ trọng yếu (**23/23 passed**).

---

## 2. Công nghệ sử dụng

| Thành phần | Công nghệ |
| :--- | :--- |
| **Backend Framework** | C# / .NET 10, ASP.NET Core Web API |
| **ORM** | Entity Framework Core (`Microsoft.EntityFrameworkCore.SqlServer`) |
| **Cơ sở dữ liệu** | **Microsoft SQL Server** (Transaction ACID, khóa dòng `WITH (UPDLOCK, ROWLOCK)`, `RowVersion`) |
| **Kiến trúc** | Clean / Layered Architecture, Repository Pattern |
| **Bảo mật & Anti-Scalping** | HMAC-SHA256 Dynamic QR Token (30s), Vé định danh chính chủ, Rate Limiting, Concurrency Locking |
| **Realtime** | ASP.NET Core SignalR |
| **Frontend** | HTML5, CSS3, JavaScript (Fetch API, Dynamic QR rendering) |
| **Kiểm thử tự động** | xUnit, Coverlet |
| **CI/CD** | GitHub Actions Pipeline (`.github/workflows/ci.yml`) |
| **Môi trường / Deploy** | SQL Server Express (Local) hoặc Docker Compose (`mssql/server:2022-latest`) |

---

## 3. Cấu trúc thư mục dự án

```text
Online_Event_Ticketing-Anti-Scalping_Pass_Portal/
├── .github/
│   ├── PULL_REQUEST_TEMPLATE.md
│   └── workflows/
│       └── ci.yml
├── database/
│   └── sqlserver_schema.sql                      # Script DDL khởi tạo bảng, FK, Index & ROWVERSION trên SQL Server
├── docs/
│   ├── architecture-sequence.puml
│   └── openapi.yaml
├── src/
│   ├── EventTicketing.Core/                      # Tầng Domain (Nghiệp vụ cốt lõi)
│   │   ├── Constants/
│   │   │   └── AntiScalpingConfig.cs            # Tham số hệ thống: 30s QR, 10m Hold, max 4 vé
│   │   ├── Domain/
│   │   │   ├── Entities/                        # User, Event, Seat, SeatHold, Ticket, CheckInLog
│   │   │   └── Enums/                           # SeatStatus, TicketStatus
│   │   └── Interfaces/                          # ITicketBookingService, IDynamicQrService, ITicketRepository, IUserRepository
│   │
│   ├── EventTicketing.Infrastructure/            # Tầng Hạ tầng & Bảo mật
│   │   ├── Data/
│   │   │   └── ApplicationDbContext.cs          # Quản lý bộ dữ liệu và Seeding mẫu
│   │   ├── Repositories/                        # UserRepository, TicketRepository
│   │   ├── Security/                            # DynamicQrService (HMAC-SHA256), PasswordHasher, JwtTokenGenerator
│   │   └── Services/                            # TicketBookingService (Xử lý giữ chỗ & chống đầu cơ)
│   │
│   ├── EventTicketing.WebApi/                    # Tầng Tiếp nhận API & Middleware
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs                # Đăng ký, đăng nhập & cấp phát JWT theo vai trò
│   │   │   ├── AdminController.cs               # API tạo sự kiện & thống kê báo cáo (Chỉ ADMIN)
│   │   │   ├── EventsController.cs              # Danh sách sự kiện, sơ đồ ghế
│   │   │   ├── GateController.cs                # Check-in soát vé tại cổng (Chỉ GATE_STAFF / ADMIN)
│   │   │   └── TicketsController.cs             # Giữ chỗ, thanh toán, lấy QR động, xem ví vé
│   │   ├── Hubs/
│   │   │   └── SeatHub.cs                       # Realtime cập nhật trạng thái ghế
│   │   ├── Middlewares/
│   │   │   ├── ErrorHandlingMiddleware.cs
│   │   │   └── RateLimitingMiddleware.cs        # Chống bot cào vé & spam request
│   │   ├── Services/
│   │   │   └── ExpiredHoldCleanupWorker.cs      # BackgroundService tự động thu hồi ghế quá hạn 10 phút
│   │   ├── Dockerfile                           # Đóng gói container WebApi + Frontend (.NET 10)
│   │   └── Program.cs                           # Cấu hình DI, SQL Server, CORS, Static Files, Pipeline
│   │
│   └── EventTicketing.WebClient/                 # Tầng Giao diện người dùng
│       └── public/
│           ├── assets/css/style.css             # Giao diện responsive tối ưu
│           ├── js/
│           │   ├── auth.js                      # Quản lý JWT, phiên đăng nhập & phân quyền thanh điều hướng
│           │   ├── seatmap.js                   # Chọn ghế, giữ chỗ 10 phút, thanh toán
│           │   ├── dynamic-qr.js                # Ví vé điện tử, tự động làm mới mã QR mỗi 30s
│           │   └── gate-scanner.js              # Mô phỏng quét vé tại cổng, phát hiện gian lận
│           └── pages/
│               ├── index.html                   # Trang chủ & thanh tìm kiếm sự kiện
│               ├── login.html                   # Trang Đăng nhập / Đăng ký khách hàng (ẩn chọn vai trò chống leo thang quyền)
│               ├── seat-selection.html          # Sơ đồ chọn ghế trực quan
│               ├── my-tickets.html              # Ví vé điện tử chính chủ (CUSTOMER)
│               ├── gate-checkin.html            # Cổng soát vé cho nhân viên (GATE_STAFF / ADMIN)
│               └── admin-dashboard.html         # Dashboard quản trị sự kiện & báo cáo doanh thu (ADMIN)
│
├── tests/
│   └── EventTicketing.Tests/                     # Bộ kiểm thử tự động (xUnit)
│       └── UnitTests/
│           ├── AuthAndRoleTests.cs              # Test đăng nhập JWT & phân quyền RBAC 3 vai trò
│           ├── TicketBookingTests.cs            # Test giữ chỗ, thanh toán, chặn mua quá 4 vé, giải phóng ghế
│           ├── DynamicQrServiceTests.cs         # Test sinh mã QR, xác thực token HMAC hợp lệ/không chính chủ
│           └── GateCheckInTests.cs              # Test quét soát vé tại cổng, chặn vé chụp màn hình dùng lại
├── docker-compose.yml
├── openapi-spec.yaml
├── PULL_REQUEST_TEMPLATE.md
├── README.md
└── software_engineering_implementation_guide.md
```

---

## 4. Hướng dẫn cài đặt và khởi chạy

### Yêu cầu tiên quyết
- **Hệ điều hành:** Windows 10/11, macOS (Intel & Apple Silicon) hoặc Linux
- **Tùy chọn A (Khuyên dùng — Chuẩn môi trường đồng nhất):** Cài đặt [Docker Desktop](https://www.docker.com/products/docker-desktop/) *(không cần cài SQL Server hay .NET SDK trên máy)*
- **Tùy chọn B (Chạy trực tiếp mã nguồn):** [.NET SDK 10.0](https://dotnet.microsoft.com/) + Docker SQL Server (hoặc SQL Server Express local)
- **Trình duyệt web:** Google Chrome, Microsoft Edge hoặc Mozilla Firefox

---

## 5. Khởi chạy hệ thống

### 5.1 Cách 1: Chạy trọn gói bằng Docker Compose (Khuyên dùng khi tải về test)

Chỉ với **1 câu lệnh duy nhất**, Docker sẽ tự động kéo **SQL Server 2022**, build **ASP.NET Core Web API + Giao diện Frontend**, tự động khởi tạo CSDL `EventTicketingDb` (6 bảng giao dịch, khóa ngoại, `RowVersion`) và nạp sẵn dữ liệu mẫu:

```bash
# Mở Docker Desktop, sau đó chạy lệnh sau tại thư mục gốc dự án:
docker compose up --build
```

- Khi thấy dòng log `Connected to Microsoft SQL Server and initialized EventTicketingDb successfully`, mở trình duyệt truy cập ngay: **`http://localhost:5000`**
- Để dừng hệ thống: nhấn `Ctrl + C` hoặc gõ `docker compose down` *(thêm `-v` nếu muốn xóa sạch dữ liệu volume để reset từ đầu)*.

### 5.2 Cách 2: Chạy trực tiếp bằng `dotnet run` (Dành cho phát triển / Debug)

Bạn có thể bật riêng container SQL Server bằng Docker (`docker compose up -d sqlserver`) hoặc dùng sẵn dịch vụ `localhost\SQLEXPRESS` trên Windows (ứng dụng tự động nhận diện trong [`Program.cs`](file:///d:/TDTU/ĐH_HK1_Junior/Software%20Engineering/Finalreport/Online_Event_Ticketing-Anti-Scalping_Pass_Portal/src/EventTicketing.WebApi/Program.cs)):

```bash
# Khôi phục dependencies và build toàn bộ giải pháp
dotnet restore
dotnet build

# Chạy Web API Server
dotnet run --project src/EventTicketing.WebApi/EventTicketing.WebApi.csproj
```

Server sẽ khởi chạy tại: `http://localhost:5000`. Bạn cũng có thể mở **SQL Server Management Studio (SSMS)** kết nối vào `localhost,1433` (`sa` / `YourPassword123!`) hoặc `localhost\SQLEXPRESS` để xem trực tiếp các bảng hoặc tham khảo script [`database/sqlserver_schema.sql`](file:///d:/TDTU/ĐH_HK1_Junior/Software%20Engineering/Finalreport/Online_Event_Ticketing-Anti-Scalping_Pass_Portal/database/sqlserver_schema.sql).

### 5.3 Tài khoản mẫu 3 vai trò (Role-Based Access Control) & Trải nghiệm Web

Hệ thống khởi tạo sẵn **3 tài khoản mẫu** trong CSDL SQL Server đại diện cho 3 nhóm tác nhân của đề tài (`FR-01` → `FR-03`). Để đảm bảo bảo mật chống leo thang đặc quyền (Privilege Escalation), giao diện đăng nhập/đăng ký công khai tại **`http://localhost:5000/pages/login.html`** không cho phép người dùng tự chọn vai trò — người dùng tự nhập Email & Mật khẩu của mình và máy chủ sẽ tự động xác thực quyền hạn từ CSDL:

| Vai trò (`Role`) | Email đăng nhập | Mật khẩu | Quyền hạn & Trang làm việc chính |
| :--- | :--- | :--- | :--- |
| 🎫 **`CUSTOMER`** *(Người mua vé)* | `buyer@example.com` | `Password123!` | Chọn ghế (tối đa 4 ghế), giữ chỗ 10 phút, thanh toán & mở **Ví vé QR động 30s** (`/pages/my-tickets.html`). Bị chặn truy cập Cổng soát vé & Admin. |
| 🚪 **`GATE_STAFF`** *(Nhân viên cổng)* | `staff@example.com` | `Password123!` | Truy cập **Cổng Check-in** (`/pages/gate-checkin.html`), quét xác thực QR động & xem nhật ký quét vé. Không được phép đặt mua vé hoặc vào Admin. |
| 🛡️ **`ADMIN`** *(Ban tổ chức / Quản trị)* | `admin@example.com` | `Password123!` | Truy cập **Dashboard Quản trị & Báo cáo** (`/pages/admin-dashboard.html`), tạo sự kiện mới kèm sơ đồ 32 ghế, thống kê doanh thu / tỷ lệ lấp đầy / check-in. |

**Quy trình kiểm thử nhanh trên trình duyệt:**
1. **Đăng nhập & Phân quyền tự động:** Mở `http://localhost:5000/pages/login.html` → Nhập tài khoản `buyer@example.com` / `Password123!` (hoặc tự đăng ký tài khoản khách hàng mới).
2. **Sơ đồ chọn ghế & Giữ chỗ 10 phút:** Vào `http://localhost:5000/pages/seat-selection.html`, chọn 1–4 ghế trống, nhấn **Giữ chỗ** rồi nhấn **Thanh toán & Xuất vé**.
3. **Ví vé điện tử & Mã QR động 30s:** Mở `http://localhost:5000/pages/my-tickets.html`, nhấn **📲 Xuất trình mã QR để quét tại cổng**.
4. **Cổng soát vé (`GATE_STAFF`):** Đăng nhập sang tài khoản **Nhân viên cổng** (`staff@example.com`), tại `http://localhost:5000/pages/gate-checkin.html` nhấn **📥 Nạp mã QR khách vừa xuất trình** → **Xác nhận check-in** (lần 1 hợp lệ ✅; quét lại lần 2 bị chặn `409 Conflict` 🚨).
5. **Quản trị & Báo cáo (`ADMIN`):** Đăng nhập tài khoản **Admin** (`admin@example.com`) để xem biểu đồ doanh thu trực tiếp và khởi tạo sự kiện mới tại `http://localhost:5000/pages/admin-dashboard.html`.

---

## 6. Chạy bộ kiểm thử tự động (Unit Tests)

Trong thư mục gốc dự án:

```bash
dotnet test tests/EventTicketing.Tests/EventTicketing.Tests.csproj
```

### Kết quả kiểm thử: **23/23 bài test ĐẠT (Passed 100%)**

```text
Passed!  - Failed: 0, Passed: 23, Skipped: 0, Total: 23
```

| Nhóm chức năng | Test Case | Ý nghĩa kiểm thử |
| :--- | :--- | :--- |
| **Giữ chỗ & Đặt vé** | `ReserveSeatAsync_WhenSeatIsAvailable_ReturnsSuccess` | Giữ chỗ thành công khi ghế còn trống |
| | `ReserveSeatAsync_WhenSeatIsHeld_ReturnsFailure` | Chặn không cho người thứ hai giữ ghế đang bị khóa |
| | `ConfirmSeatHoldAsync_ValidHold_IssuesTicketAndMarksSeatSold` | Thanh toán hợp lệ chuyển ghế sang `Sold` và cấp vé |
| **Chống phe vé / Scalping** | `ReserveSeatAsync_WhenUserExceedsAntiScalpingLimit_ReturnsFailure` | **Chặn mua vé thứ 5** khi vượt hạn mức 4 vé/người |
| | `ReleaseExpiredHoldsAsync_WhenHoldExpired_RevertsSeatToAvailable` | **Tự động thu hồi ghế** sau 10 phút để người khác mua |
| **Mã QR động HMAC** | `GenerateToken_ValidInput_ReturnsNonEmptyToken` | Thuật toán sinh token HMAC-SHA256 chuẩn |
| | `ValidateToken_SameWindow_ReturnsTrue` | Xác thực token thành công trong khung thời gian hợp lệ |
| | `ValidateToken_InvalidUserId_ReturnsFalse` | Từ chối token nếu không đúng ID người mua chính chủ |
| **Soát vé tại cổng** | `VerifyCheckIn_ValidToken_ReturnsSuccessAndMarksTicketUsed` | Quét vé hợp lệ và chuyển trạng thái vé sang `Used` |
| | `VerifyCheckIn_AlreadyUsedTicket_ReturnsConflictAntiScalpingFraud` | **Chống chụp màn hình**: Chặn vé đã check-in trước đó |
| | `VerifyCheckIn_InvalidOrForgedToken_ReturnsBadRequest` | Từ chối vé có mã token hết hạn hoặc giả mạo |

---

## 7. Các cơ chế chống đầu cơ vé (Anti-Scalping Mechanisms)

Hệ thống triển khai 4 lớp phòng vệ vững chắc chống lại nạn phe vé:

1. **Giới hạn số lượng vé trên mỗi tài khoản (`MAX_TICKETS_PER_USER = 4`):**
   - Chặn người dùng hoặc bot gom vé số lượng lớn. Mọi nỗ lực đặt từ chiếc vé thứ 5 đều bị hệ thống chặn đứng.
2. **Khóa dòng SQL Server & Giữ chỗ đồng thời có thời hạn (`HOLD_DURATION_MINUTES = 10`):**
   - Sử dụng Transaction ACID kết hợp khóa dòng `SELECT ... FROM Seats WITH (UPDLOCK, ROWLOCK)` và kiểm tra `RowVersion` trên SQL Server để chống tuyệt đối lỗi đặt trùng ghế (double-booking).
   - Người mua có thể chọn giữ tối đa 4 ghế trong 10 phút; `ExpiredHoldCleanupWorker` chạy ngầm mỗi **5 giây** (`CLEANUP_WORKER_INTERVAL_SECONDS = 5`) tự động thu hồi ghế quá hạn trả về `Available`.
3. **Mã QR động thay đổi mỗi 30 giây (`DYNAMIC_QR_VALIDITY_SECONDS = 30`):**
   - Token mã QR được ký bằng thuật toán `HMAC-SHA256` kết hợp giữa `TicketId`, `UserId` và `TimestampWindow`.
   - Ngăn chặn triệt để hành vi chụp ảnh màn hình hoặc in ra giấy để bán lại vé chợ đen.
4. **Quy tắc một lần check-in duy nhất (Anti-Replay at Gate):**
   - Ngay khi quét vé thành công, vé lập tức chuyển sang trạng thái `TicketStatus.Used`.
   - Nếu bất kỳ ai cố tình dùng lại mã QR đó để vào cổng lần thứ hai, hệ thống lập tức báo động gian lận (Conflict 409).
5. **Rate Limiting Middleware:**
   - Hạn chế số lượng request từ một địa chỉ IP (tối đa 60 requests/phút) để ngăn chặn bot dò vé và spam server.

---

## 8. Danh mục tài liệu đi kèm

- [**`software_engineering_implementation_guide.md`**](file:///d:/TDTU/ĐH_HK1_Junior/Software%20Engineering/Finalreport/Online_Event_Ticketing-Anti-Scalping_Pass_Portal/software_engineering_implementation_guide.md): Bản thiết kế SDLC, phân công vai trò 4 thành viên và hướng dẫn kỹ thuật chi tiết.
- [**`openapi-spec.yaml`**](file:///d:/TDTU/ĐH_HK1_Junior/Software%20Engineering/Finalreport/Online_Event_Ticketing-Anti-Scalping_Pass_Portal/openapi-spec.yaml): Đặc tả RESTful API theo chuẩn OpenAPI 3.0.
- [**`.github/workflows/ci.yml`**](file:///d:/TDTU/ĐH_HK1_Junior/Software%20Engineering/Finalreport/Online_Event_Ticketing-Anti-Scalping_Pass_Portal/.github/workflows/ci.yml): Kịch bản kiểm tra chất lượng mã nguồn tự động trên GitHub Actions.
- [**`.github/PULL_REQUEST_TEMPLATE.md`**](file:///d:/TDTU/ĐH_HK1_Junior/Software%20Engineering/Finalreport/Online_Event_Ticketing-Anti-Scalping_Pass_Portal/.github/PULL_REQUEST_TEMPLATE.md): Mẫu kiểm tra code review trước khi merge vào nhánh chính.
