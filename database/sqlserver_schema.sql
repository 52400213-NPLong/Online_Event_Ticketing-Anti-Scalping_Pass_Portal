-- =========================================================================================
-- ĐỀ TÀI 13: ONLINE EVENT TICKETING & ANTI-SCALPING PASS PORTAL (COURSE 502045 - TDTU)
-- HỆ QUẢN TRỊ CSDL: MICROSOFT SQL SERVER (TRANSACTIONAL SCHEMA + UPDLOCK/ROWLOCK + ROWVERSION)
-- =========================================================================================

IF DB_ID(N'EventTicketingDb') IS NULL
BEGIN
    CREATE DATABASE [EventTicketingDb];
END
GO

USE [EventTicketingDb];
GO

-- 1. Bảng Users (Tài khoản người mua vé, quản trị viên, nhân viên cổng soát vé)
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id]           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [Email]        NVARCHAR(255)    NOT NULL,
        [PasswordHash] NVARCHAR(255)    NOT NULL,
        [FullName]     NVARCHAR(100)    NOT NULL,
        [Role]         NVARCHAR(20)     NOT NULL DEFAULT N'CUSTOMER',
        [CreatedAt]    DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [IX_Users_Email] UNIQUE ([Email])
    );
END
GO

-- 2. Bảng Events (Thông tin sự kiện mở bán)
IF OBJECT_ID(N'dbo.Events', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Events] (
        [Id]        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [Title]     NVARCHAR(255)    NOT NULL,
        [Venue]     NVARCHAR(255)    NOT NULL,
        [StartTime] DATETIME2        NOT NULL,
        [CreatedAt] DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 3. Bảng Seats (Sơ đồ ghế + khóa dòng UPDLOCK/ROWLOCK + RowVersion chống Race Condition - FR-14)
IF OBJECT_ID(N'dbo.Seats', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Seats] (
        [Id]            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [EventId]       UNIQUEIDENTIFIER NOT NULL,
        [SeatLabel]     NVARCHAR(20)     NOT NULL,
        [Price]         DECIMAL(18, 2)   NOT NULL,
        [Status]        INT              NOT NULL DEFAULT 0, -- 0: Available, 1: Held, 2: Sold
        [HeldByUserId]  NVARCHAR(100)    NULL,
        [HoldExpiresAt] DATETIME2        NULL,
        [RowVersion]    ROWVERSION       NOT NULL,           -- Optimistic Concurrency Control
        CONSTRAINT [FK_Seats_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [dbo].[Events]([Id]) ON DELETE CASCADE,
        CONSTRAINT [IX_Seats_EventId_SeatLabel] UNIQUE ([EventId], [SeatLabel])
    );
END
GO

-- 4. Bảng SeatHolds (Phiên tạm giữ chỗ 10 phút - FR-14 -> FR-18)
IF OBJECT_ID(N'dbo.SeatHolds', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SeatHolds] (
        [Id]        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [SeatId]    UNIQUEIDENTIFIER NOT NULL,
        [UserId]    NVARCHAR(100)    NOT NULL,
        [ExpiresAt] DATETIME2        NOT NULL,
        [CreatedAt] DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [FK_SeatHolds_Seats_SeatId] FOREIGN KEY ([SeatId]) REFERENCES [dbo].[Seats]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_SeatHolds_SeatId] ON [dbo].[SeatHolds]([SeatId]);
    CREATE INDEX [IX_SeatHolds_UserId] ON [dbo].[SeatHolds]([UserId]);
END
GO

-- 5. Bảng Tickets (Vé điện tử định danh chính chủ & khóa bí mật QR động - FR-31 -> FR-37)
IF OBJECT_ID(N'dbo.Tickets', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Tickets] (
        [Id]                 UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [UserId]             NVARCHAR(100)    NOT NULL,
        [SeatId]             UNIQUEIDENTIFIER NOT NULL,
        [DynamicTokenSecret] NVARCHAR(64)     NOT NULL,
        [Status]             INT              NOT NULL DEFAULT 0, -- 0: Issued, 1: Used, 2: Cancelled
        [CreatedAt]          DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [FK_Tickets_Seats_SeatId] FOREIGN KEY ([SeatId]) REFERENCES [dbo].[Seats]([Id]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_Tickets_UserId] ON [dbo].[Tickets]([UserId]);
END
GO

-- 6. Bảng CheckInLogs (Nhật ký quét soát vé tại cổng sự kiện - FR-42)
IF OBJECT_ID(N'dbo.CheckInLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CheckInLogs] (
        [Id]        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [TicketId]  UNIQUEIDENTIFIER NOT NULL,
        [UserId]    NVARCHAR(100)    NOT NULL,
        [StaffId]   NVARCHAR(100)    NOT NULL,
        [ScannedAt] DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        [Status]    NVARCHAR(20)     NOT NULL,
        [Reason]    NVARCHAR(255)    NULL
    );
    CREATE INDEX [IX_CheckInLogs_TicketId] ON [dbo].[CheckInLogs]([TicketId]);
END
GO
