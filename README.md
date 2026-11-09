# Online Event Ticketing & Anti-Scalping Pass Portal

Dự án này là một nền tảng mô phỏng hệ thống đặt vé sự kiện trực tuyến với tính năng chống scalping, giữ chỗ ghế tạm thời, tạo mã QR động và xác thực tại cổng vào sự kiện.

## 1. Mô tả tổng quan

Hệ thống bao gồm 3 phần chính:

- Backend API: ASP.NET Core Web API
- Frontend UI: HTML/CSS/JS tĩnh (responsive)
- Test: xUnit cho các nghiệp vụ quan trọng như Dynamic QR và Seat Booking

Các chức năng chính:
- Hiển thị danh sách sự kiện
- Chọn ghế và giữ chỗ tạm thời
- Chống overbooking / scalping
- Tạo token QR động theo thời gian
- Kiểm tra vé tại cổng bằng QR / token
- Giao diện ví vé điện tử cho người dùng
- Giao diện check-in cho nhân viên cổng

---

## 2. Công nghệ sử dụng

- .NET 10
- ASP.NET Core Web API
- C#
- HTML5 / CSS3 / JavaScript
- xUnit
- SignalR (được chuẩn bị trong dự án)
- Docker Compose (để chạy môi trường ứng dụng)

---

## 3. Cấu trúc thư mục dự án đầy đủ

```text
Online_Event_Ticketing-Anti-Scalping_Pass_Portal/
├── .gitignore
├── .vscode/
├── docker-compose.yml
├── EventTicketingSystem.slnx
├── README.md
├── PULL_REQUEST_TEMPLATE.md
├── openapi-spec.yaml
├── software_engineering_implementation_guide.md
├── img/
│   └── top_banner.jpg
├── docs/
│   ├── architecture-sequence.puml
│   └── openapi.yaml
├── github/
│   └── workflow/
│       └── ci.yml
├── src/
│   ├── EventTicketing.Core/
│   │   ├── Class1.cs
│   │   ├── EventTicketing.Core.csproj
│   │   ├── Constants/
│   │   │   └── AntiScalpingConfig.cs
│   │   ├── Domain/
│   │   │   ├── Entities/
│   │   │   └── Enums/
│   │   └── Interfaces/
│   │       ├── IDynamicQrService.cs
│   │       ├── ITicketBookingService.cs
│   │       └── IUserRepository.cs
│   │
│   ├── EventTicketing.Infrastructure/
│   │   ├── Class1.cs
│   │   ├── EventTicketing.Infrastructure.csproj
│   │   ├── Data/
│   │   │   ├── ApplicationDbContext.cs
│   │   │   └── Migrations/
│   │   ├── Repositories/
│   │   │   ├── TicketRepository.cs
│   │   │   └── UserRepository.cs
│   │   ├── Security/
│   │   │   ├── DynamicQrService.cs
│   │   │   ├── JwtTokenGenerator.cs
│   │   │   └── PasswordHasher.cs
│   │   └── Services/
│   │       └── TicketBookingService.cs
│   │
│   ├── EventTicketing.WebApi/
│   │   ├── appsettings.Development.json
│   │   ├── appsettings.json
│   │   ├── EventTicketing.WebApi.csproj
│   │   ├── EventTicketing.WebApi.http
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs
│   │   │   ├── EventsController.cs
│   │   │   ├── GateController.cs
│   │   │   └── TicketsController.cs
│   │   ├── Hubs/
│   │   │   └── SeatHub.cs
│   │   ├── Middlewares/
│   │   │   ├── ErrorHandlingMiddleware.cs
│   │   │   └── RateLimitingMiddleware.cs
│   │   └── Properties/
│   │       └── launchSettings.json
│   │
│   └── EventTicketing.WebClient/
│       ├── package.json
│       ├── public/
│       │   ├── assets/
│       │   │   └── css/
│       │   │       └── style.css
│       │   ├── pages/
│       │   │   ├── all-events.html
│       │   │   ├── event-calendar.html
│       │   │   ├── gate-checkin.html
│       │   │   ├── index.html
│       │   │   ├── my-tickets.html
│       │   │   └── seat-selection.html
│       │   └── favicon.ico
│       └── src/
│           └── pages/
│
├── tests/
│   └── EventTicketing.Tests/
│       ├── EventTicketing.Tests.csproj
│       ├── UnitTest1.cs
│       ├── UnitTests/
│       │   ├── DynamicQrServiceTest.cs
│       │   ├── DynamicQrServiceTests.cs
│       │   └── TicketBookingTests.cs
│       └── obj/
│
└──
```

---

## 4. Hướng dẫn cài đặt môi trường

### Yêu cầu

- Windows 10/11 hoặc Linux/macOS
- .NET SDK 10.0
- VS Code hoặc Visual Studio
- Git
- Browser Chrome/Edge/Firefox

### Kiểm tra .NET

Mở terminal và chạy:

```bash
dotnet --version
```

Nếu xuất hiện phiên bản .NET SDK thì máy bạn đã chuẩn bị xong.

---

## 5. Cách chạy dự án

## 5.1 Chạy Backend API

Mở terminal ở thư mục gốc của project:

```bash
cd D:\CNPM CKY\Online_Event_Ticketing-Anti-Scalping_Pass_Portal
dotnet restore
dotnet build
```

Sau đó chạy Web API:

```bash
cd src/EventTicketing.WebApi
dotnet run
```

Nếu chạy thành công, server ASP.NET Core sẽ khởi động và hiển thị địa chỉ local như:

```text
http://localhost:5000
https://localhost:5001
```

Tùy cấu hình của môi trường, địa chỉ có thể khác nhau. Bạn có thể xem log trong terminal.

---

## 5.2 Chạy Frontend UI

Frontend của dự án là HTML tĩnh, nên bạn không cần build package phức tạp.

Cách đơn giản nhất:
- Mở trực tiếp file `index.html` trong browser
- Hoặc dùng extension Live Server trong VS Code

Đường dẫn:

```text
src/EventTicketing.WebClient/public/pages/index.html
```

Các trang chính:

```text
src/EventTicketing.WebClient/public/pages/index.html
src/EventTicketing.WebClient/public/pages/seat-selection.html
src/EventTicketing.WebClient/public/pages/my-tickets.html
src/EventTicketing.WebClient/public/pages/gate-checkin.html
```

---

## 5.3 Chạy Docker (nếu dùng)

File `docker-compose.yml` đã được cấu hình sẵn để khởi động môi trường ứng dụng:

```bash
docker-compose up --build
```

Sau khi chạy xong, bạn có thể truy cập:
- Web API
- CSDL / môi trường theo cấu hình trong file docker-compose

---

## 6. Chạy test

Trong thư mục gốc project:

```bash
dotnet test tests/EventTicketing.Tests/EventTicketing.Tests.csproj --nologo
```

Hoặc nếu muốn test toàn bộ solution:

```bash
dotnet test --nologo
```

Các test hiện có bao gồm:
- Dynamic QR generation
- QR validation
- Seat hold / booking logic

---

## 7. Mô tả các module chính

### 7.1 EventTicketing.Core
Chứa các đối tượng nghiệp vụ và contract chính:

- `AntiScalpingConfig.cs`
- `User.cs`
- `Event.cs`
- `Seat.cs`
- `Ticket.cs`
- `SeatHold.cs`
- `CheckInLog.cs`
- `SeatStatus.cs`
- `TicketStatus.cs`

Mục đích:
- định nghĩa domain model
- mô hình trạng thái ghế và vé
- interface nghiệp vụ

### 7.2 EventTicketing.Infrastructure
Chứa tầng kỹ thuật và xử lý dữ liệu:

- `ApplicationDbContext.cs`
- `DynamicQrService.cs`
- `PasswordHasher.cs`
- `JwtTokenGenerator.cs`
- `TicketBookingService.cs`
- `UserRepository.cs`
- `TicketRepository.cs`

Mục đích:
- thao tác dữ liệu
- tạo mã QR động
- băm mật khẩu
- sinh JWT
- logic giữ ghế và xác thực

### 7.3 EventTicketing.WebApi
Chứa API cho hệ thống:

- `AuthController.cs` → đăng ký / đăng nhập
- `EventsController.cs` → lấy sự kiện và sơ đồ ghế
- `TicketsController.cs` → giữ chỗ, tạo mã QR
- `GateController.cs` → check-in tại cổng
- `SeatHub.cs` → realtime SignalR
- `Middlewares` → xử lý lỗi và rate limiting

### 7.4 EventTicketing.WebClient
Chứa giao diện người dùng:

- `index.html` → trang chủ
- `seat-selection.html` → chọn ghế
- `my-tickets.html` → ví vé
- `gate-checkin.html` → màn hình kiểm tra vé tại cổng
- `style.css` → giao diện UI

---

## 8. Luồng hoạt động chính

### Đặt vé
1. Người dùng mở trang sự kiện
2. Chọn ghế trên seat map
3. Hệ thống giữ chỗ trong thời gian quy định
4. Nếu ghế còn trống → tạo holdId
5. Nếu ghế đã được giữ hoặc đã bán → từ chối

### QR động
1. Hệ thống tạo token QR dựa trên:
   - `ticketId`
   - `userId`
   - thời gian hiện tại
   - secret key
2. Token được kiểm tra lại ở cổng
3. Nếu token sai hoặc hết hạn → không được check-in

### Check-in
1. Nhân viên cổng nhập `TicketId`, `UserId`, `DynamicToken`
2. API gọi validate
3. Nếu hợp lệ → xác nhận vào cổng
4. Nếu không hợp lệ → trả lỗi

---

## 9. Lưu ý khi chạy dự án

- Dự án hiện đang ở dạng demo / prototype và có thể chưa hoàn toàn kết nối database thật.
- Một số phần đã được phát triển theo mô hình prototype để phục vụ học tập và báo cáo đồ án.
- Nếu bạn muốn chạy hoàn chỉnh thực tế, nên bổ sung:
  - SQL Server / PostgreSQL
  - EF Core migrations
  - JWT auth thực tế
  - Redis cho seat lock đồng thời
  - storage cho ảnh QR hoặc tài liệu

---

## 10. Tài liệu tham khảo

- `openapi-spec.yaml`
- `docs/openapi.yaml`
- `software_engineering_implementation_guide.md`

---

## 11. Kết luận

Dự án `Online Event Ticketing & Anti-Scalping Pass Portal` là một mô hình nền tảng đặt vé sự kiện hiện đại, tập trung vào:
- chống overbooking
- giữ ghế tạm thời
- mã QR động
- xác nhận cổng vào sự kiện
- giao diện UX đẹp hơn cho người dùng

Nếu bạn cần, bạn có thể tiếp tục mở rộng:
- thêm chức năng đăng ký / đăng nhập thật
- tích hợp CSDL SQL Server
- thêm API quản trị cho admin
- thêm realtime cập nhật ghế theo SignalR
- phát triển UI thành React/Vue nếu muốn hiện đại hơn

---

## 12. Cách chạy nhanh nhất

Nếu bạn muốn chạy nhanh trong môi trường DEV:

### B1. Backend
```bash
cd src/EventTicketing.WebApi
dotnet run
```

### B2. Frontend
Mở file:
```text
src/EventTicketing.WebClient/public/pages/index.html
```

### B3. Test
```bash
dotnet test tests/EventTicketing.Tests/EventTicketing.Tests.csproj --nologo
```

---

Nếu bạn muốn, mình có thể tiếp tục giúp:
1. Viết lại README theo kiểu chuyên nghiệp hơn cho báo cáo đồ án
2. Chỉnh sửa file `README.md` theo đúng phong cách của nhóm bạn
3. Soạn luôn phần "Hướng dẫn chạy đồ án" theo format báo cáo cuối kỳ
