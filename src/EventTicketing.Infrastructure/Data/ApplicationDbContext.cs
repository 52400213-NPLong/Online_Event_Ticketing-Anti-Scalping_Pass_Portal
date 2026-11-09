using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Domain.Enums;
using EventTicketing.Infrastructure.Security;

namespace EventTicketing.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public const string DefaultSqlServerConnection =
        "Server=localhost\\SQLEXPRESS;Database=EventTicketingDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private readonly SqliteConnection? _inMemoryConnection;

    /// <summary>
    /// Constructor mặc định chỉ phục vụ Unit Test độc lập (In-Memory) để chạy xUnit và CI không phụ thuộc máy chủ ngoài
    /// </summary>
    public ApplicationDbContext()
    {
        _inMemoryConnection = new SqliteConnection("Data Source=:memory:");
        _inMemoryConnection.Open();
        Database.EnsureCreated();
    }

    /// <summary>
    /// Constructor chính thức dùng cho ứng dụng WebApi kết nối SQL Server qua Dependency Injection
    /// </summary>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<SeatHold> SeatHolds => Set<SeatHold>();
    public DbSet<CheckInLog> CheckInLogs => Set<CheckInLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            if (_inMemoryConnection is not null)
            {
                optionsBuilder.UseSqlite(_inMemoryConnection);
            }
            else
            {
                optionsBuilder.UseSqlServer(DefaultSqlServerConnection);
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        bool isSqlServer = Database.IsSqlServer();

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).HasMaxLength(255).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(u => u.FullName).HasMaxLength(100).IsRequired();
            entity.Property(u => u.Role).HasMaxLength(20).IsRequired();
            entity.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("Events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Venue).HasMaxLength(255).IsRequired();
        });

        modelBuilder.Entity<Seat>(entity =>
        {
            entity.ToTable("Seats");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.SeatLabel).HasMaxLength(20).IsRequired();
            entity.Property(s => s.Price).HasPrecision(18, 2);
            entity.Property(s => s.HeldByUserId).HasMaxLength(100);
            entity.HasIndex(s => new { s.EventId, s.SeatLabel }).IsUnique();

            if (isSqlServer)
            {
                // Kích hoạt Optimistic Concurrency Control bằng cột ROWVERSION của SQL Server (FR-14)
                entity.Property(s => s.RowVersion).IsRowVersion();

                entity.HasOne<Event>()
                      .WithMany()
                      .HasForeignKey(s => s.EventId)
                      .OnDelete(DeleteBehavior.Cascade);
            }
            else
            {
                entity.Ignore(s => s.RowVersion);
            }
        });

        modelBuilder.Entity<SeatHold>(entity =>
        {
            entity.ToTable("SeatHolds");
            entity.HasKey(h => h.Id);
            entity.Property(h => h.UserId).HasMaxLength(100).IsRequired();
            entity.HasIndex(h => h.SeatId);
            entity.HasIndex(h => h.UserId);

            if (isSqlServer)
            {
                entity.HasOne<Seat>()
                      .WithMany()
                      .HasForeignKey(h => h.SeatId)
                      .OnDelete(DeleteBehavior.Cascade);
            }
        });

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.ToTable("Tickets");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.UserId).HasMaxLength(100).IsRequired();
            entity.Property(t => t.DynamicTokenSecret).HasMaxLength(64).IsRequired();
            entity.HasIndex(t => t.UserId);

            if (isSqlServer)
            {
                entity.HasOne<Seat>()
                      .WithMany()
                      .HasForeignKey(t => t.SeatId)
                      .OnDelete(DeleteBehavior.Restrict);
            }
        });

        modelBuilder.Entity<CheckInLog>(entity =>
        {
            entity.ToTable("CheckInLogs");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.UserId).HasMaxLength(100).IsRequired();
            entity.Property(c => c.StaffId).HasMaxLength(100).IsRequired();
            entity.Property(c => c.Status).HasMaxLength(20).IsRequired();
            entity.Property(c => c.Reason).HasMaxLength(255);
            entity.HasIndex(c => c.TicketId);
        });
    }

    public void SeedSampleData()
    {
        Database.EnsureCreated();

        string defaultDemoPasswordHash = PasswordHasher.Hash("Password123!");
        var buyerGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var staffGuid = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var adminGuid = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var sampleTicketGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        if (Users.Any())
        {
            // Đồng bộ tài khoản mẫu nếu CSDL đã được tạo từ phiên bản trước (đảm bảo mật khẩu băm hợp lệ & đủ 3 vai trò)
            bool updated = false;

            var existingBuyer = Users.FirstOrDefault(u => u.Email == "buyer@example.com");
            if (existingBuyer is not null && existingBuyer.PasswordHash == "demo-hash")
            {
                existingBuyer.PasswordHash = defaultDemoPasswordHash;
                updated = true;
            }

            var existingStaff = Users.FirstOrDefault(u => u.Email == "staff@example.com");
            if (existingStaff is not null && existingStaff.PasswordHash == "staff-hash")
            {
                existingStaff.PasswordHash = defaultDemoPasswordHash;
                updated = true;
            }

            if (!Users.Any(u => u.Email == "admin@example.com"))
            {
                Users.Add(new User
                {
                    Id = adminGuid,
                    Email = "admin@example.com",
                    FullName = "Lê Hoàng C (Event Admin)",
                    PasswordHash = defaultDemoPasswordHash,
                    Role = "ADMIN",
                    CreatedAt = DateTime.UtcNow
                });
                updated = true;
            }

            var legacySampleTicket = Tickets.FirstOrDefault(t => t.Id == sampleTicketGuid);
            if (legacySampleTicket is not null && legacySampleTicket.UserId == "USER-1001" && existingBuyer is not null)
            {
                legacySampleTicket.UserId = existingBuyer.Id.ToString();
                updated = true;
            }

            if (updated)
            {
                SaveChanges();
            }

            return;
        }

        var demoBuyer = new User
        {
            Id = buyerGuid,
            Email = "buyer@example.com",
            FullName = "Nguyễn Văn A (Khách mua vé)",
            PasswordHash = defaultDemoPasswordHash,
            Role = "CUSTOMER",
            CreatedAt = DateTime.UtcNow
        };

        var staffUser = new User
        {
            Id = staffGuid,
            Email = "staff@example.com",
            FullName = "Trần Thị B (Nhân viên cổng)",
            PasswordHash = defaultDemoPasswordHash,
            Role = "GATE_STAFF",
            CreatedAt = DateTime.UtcNow
        };

        var adminUser = new User
        {
            Id = adminGuid,
            Email = "admin@example.com",
            FullName = "Lê Hoàng C (Ban tổ chức / Admin)",
            PasswordHash = defaultDemoPasswordHash,
            Role = "ADMIN",
            CreatedAt = DateTime.UtcNow
        };

        Users.AddRange(demoBuyer, staffUser, adminUser);

        var event1 = new Event
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Title = "Sunset Music Fest 2026",
            Venue = "Sân vận động Quân Khu 7 - TP.HCM",
            StartTime = DateTime.UtcNow.AddDays(15),
            CreatedAt = DateTime.UtcNow
        };

        var event2 = new Event
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Title = "Jazz Night Live Acoustic",
            Venue = "Nhà hát Lớn TP.HCM",
            StartTime = DateTime.UtcNow.AddDays(25),
            CreatedAt = DateTime.UtcNow
        };

        var event3 = new Event
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Title = "Art & Light Festival",
            Venue = "Công viên Cầu Rồng - Đà Nẵng",
            StartTime = DateTime.UtcNow.AddDays(35),
            CreatedAt = DateTime.UtcNow
        };

        var event4 = new Event
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Title = "Rock Symphony Concert 2027",
            Venue = "Cung Điền kinh Mỹ Đình - Hà Nội",
            StartTime = DateTime.UtcNow.AddDays(45),
            CreatedAt = DateTime.UtcNow
        };

        Events.AddRange(event1, event2, event3, event4);

        var rows = new[] { "A", "B", "C", "D" };
        var allEvents = new[] { event1, event2, event3, event4 };

        foreach (var ev in allEvents)
        {
            for (int r = 0; r < rows.Length; r++)
            {
                string rowLabel = rows[r];
                decimal price = rowLabel == "A" ? 1200000m : rowLabel == "B" ? 890000m : 650000m;
                for (int c = 1; c <= 8; c++)
                {
                    string seatLabel = $"{rowLabel}{c}";
                    var seat = new Seat
                    {
                        Id = Guid.NewGuid(),
                        EventId = ev.Id,
                        SeatLabel = seatLabel,
                        Price = price,
                        Status = SeatStatus.Available
                    };
                    Seats.Add(seat);
                }
            }
        }

        SaveChanges();

        // Đánh dấu ghế A4 đã bán kèm 1 vé mẫu gắn cho tài khoản Demo Buyer để test Ví vé và Check-in ngay sau khi đăng nhập
        var sampleSeat = Seats.First(s => s.SeatLabel == "A4" && s.EventId == event1.Id);
        sampleSeat.Status = SeatStatus.Sold;

        var sampleTicket = new Ticket
        {
            Id = sampleTicketGuid,
            UserId = demoBuyer.Id.ToString(),
            SeatId = sampleSeat.Id,
            DynamicTokenSecret = "SampleSecretTokenKey4821",
            Status = TicketStatus.Issued,
            CreatedAt = DateTime.UtcNow
        };
        Tickets.Add(sampleTicket);

        SaveChanges();
    }

    public override void Dispose()
    {
        base.Dispose();
        _inMemoryConnection?.Dispose();
    }
}
