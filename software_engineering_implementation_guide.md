# Online Event Ticketing & Anti-Scalping Pass Portal
## Complete SDLC & Engineering Execution Blueprint (Course 502045)

---

## 1. Team Allocation & File Structure Setup

### 1.1 Role Matrix (4-Member Scope)
* **Member 1 (UI/UX Lead):** Interactive Seat Map UI, Dynamic Pass Wallet Component, Event Browsing.
* **Member 2 (Backend Core Lead):** Reservation Locking Engine, Dynamic TOTP Pass Generator, REST APIs.
* **Member 3 (Database & Security Lead):** PostgreSQL Transactional Schema, Redis Session/Lock Keying, JWT Auth & RBAC.
* **Member 4 (QA, DevOps & CI/CD Lead):** Playwright API Contract Testing, Jest Unit Testing (>60%), Docker setup & GitHub Actions CI/CD.

### 1.2 Unified Repository Folder Structure
Create this structure in your GitHub repository:

```text
Online_Event_Ticketing-Anti-Scalping_Pass_Portal/
├── .github/
│   ├── PULL_REQUEST_TEMPLATE.md
│   └── workflows/
│       └── ci-pipeline.yml
├── docs/
│   ├── SRS.md
│   ├── openapi-spec.yaml
│   └── architecture-diagrams/
├── backend/
│   ├── src/
│   │   ├── modules/
│   │   │   ├── auth/
│   │   │   ├── events/
│   │   │   ├── reservations/
│   │   │   └── tickets/
│   │   ├── database/
│   │   └── app.ts
│   ├── tests/
│   │   ├── unit/
│   │   └── integration/
│   ├── Dockerfile
│   └── package.json
├── frontend/
│   ├── src/
│   │   ├── components/
│   │   │   ├── SeatMap.tsx
│   │   │   └── PassWallet.tsx
│   │   └── pages/
│   └── package.json
├── docker-compose.yml
└── README.md
```

---

## 2. Database & Anti-Scalping Architecture (Member 2 & 3)

### 2.1 Database Schema (PostgreSQL DDL)
Save this inside `backend/src/database/schema.sql`:

```sql
-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Users Table
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    full_name VARCHAR(100) NOT NULL,
    role VARCHAR(20) DEFAULT 'CUSTOMER' CHECK (role IN ('CUSTOMER', 'ORGANIZER', 'GATE_STAFF', 'ADMIN')),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Events Table
CREATE TABLE events (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    title VARCHAR(255) NOT NULL,
    venue VARCHAR(255) NOT NULL,
    start_time TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Seats Table
CREATE TABLE seats (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    event_id UUID REFERENCES events(id) ON DELETE CASCADE,
    seat_label VARCHAR(20) NOT NULL,
    price DECIMAL(10, 2) NOT NULL,
    status VARCHAR(20) DEFAULT 'AVAILABLE' CHECK (status IN ('AVAILABLE', 'LOCKED', 'SOLD')),
    version INT DEFAULT 0, -- Used for Optimistic Locking
    UNIQUE(event_id, seat_label)
);

-- Reservations Table (Temporary Lock)
CREATE TABLE reservations (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID REFERENCES users(id),
    seat_id UUID REFERENCES seats(id),
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    status VARCHAR(20) DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE', 'COMPLETED', 'EXPIRED'))
);

-- Tickets Table (Issued Passes)
CREATE TABLE tickets (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID REFERENCES users(id),
    seat_id UUID REFERENCES seats(id),
    totp_secret VARCHAR(64) NOT NULL, -- Secret key used for Dynamic 10s TOTP QR Generation
    status VARCHAR(20) DEFAULT 'ISSUED' CHECK (status IN ('ISSUED', 'USED', 'CANCELLED')),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
```

### 2.2 Concurrency Seat Locking Mechanism (TypeScript Backend Node.js/Express)
Save this logic under `backend/src/modules/reservations/reservation.service.ts`:

```typescript
import { Pool } from 'pg';
import Redis from 'ioredis';

const db = new Pool({ connectionString: process.env.DATABASE_URL });
const redis = new Redis(process.env.REDIS_URL || 'redis://localhost:6379');

export class ReservationService {
    /**
     * Locks a seat preventing double-booking using Redis & PostgreSQL Transaction
     */
    async lockSeat(userId: string, eventId: string, seatId: string): Promise<any> {
        const lockKey = `lock:seat:${seatId}`;
        
        // 1. Acquire Distributed Redis Lock (Expiration 300 seconds = 5 mins)
        const acquiredLock = await redis.set(lockKey, userId, 'EX', 300, 'NX');
        if (!acquiredLock) {
            throw new Error('COLLISION_DETECTED: Seat is currently locked by another user.');
        }

        const client = await db.connect();
        try {
            await client.query('BEGIN');

            // 2. Query Seat Status with Row Lock (Pessimistic Locking)
            const seatRes = await client.query(
                'SELECT status FROM seats WHERE id = $1 AND event_id = $2 FOR UPDATE',
                [seatId, eventId]
            );

            if (seatRes.rows.length === 0 || seatRes.rows[0].status !== 'AVAILABLE') {
                await redis.del(lockKey); // Release lock
                throw new Error('SEAT_UNAVAILABLE');
            }

            // 3. Mark seat locked in Database
            await client.query(
                "UPDATE seats SET status = 'LOCKED' WHERE id = $1",
                [seatId]
            );

            // 4. Create Reservation Record
            const expiresAt = new Date(Date.now() + 5 * 60 * 1000);
            const resVal = await client.query(
                `INSERT INTO reservations (user_id, seat_id, expires_at) 
                 VALUES ($1, $2, $3) RETURNING id, status, expires_at`,
                [userId, seatId, expiresAt]
            );

            await client.query('COMMIT');
            return {
                reservationId: resVal.rows[0].id,
                seatId,
                status: 'LOCKED',
                expiresAt: resVal.rows[0].expires_at,
                lockDurationSeconds: 300
            };
        } catch (err) {
            await client.query('ROLLBACK');
            await redis.del(lockKey);
            throw err;
        } finally {
            client.release();
        }
    }
}
```

### 2.3 Dynamic TOTP Pass Generator (Anti-Scalping Engine)
Save under `backend/src/modules/tickets/pass.service.ts`:

```typescript
import { authenticator } from 'otplib';

// Configure TOTP window for strict 10-second expiration
authenticator.options = { step: 10, window: 1 };

export class PassService {
    /**
     * Generates dynamic TOTP pass token refreshed every 10 seconds
     */
    generateDynamicPass(ticketId: string, userSecret: string) {
        const totpToken = authenticator.generate(userSecret);
        return {
            ticketId,
            totpToken,
            expiresInSeconds: 10
        };
    }

    /**
     * Validates scanned TOTP pass at the gate
     */
    verifyGatePass(scannedToken: string, userSecret: string): boolean {
        return authenticator.check(scannedToken, userSecret);
    }
}
```

---

## 3. Automated Testing & DevOps (Member 4)

### 3.1 Unit Testing Suite (Jest - `backend/tests/unit/pass.test.ts`)
Must achieve >60% test coverage for course requirement:

```typescript
import { PassService } from '../../src/modules/tickets/pass.service';
import { authenticator } from 'otplib';

describe('Anti-Scalping PassService Unit Tests', () => {
    let passService: PassService;
    const mockSecret = 'KVKXS2ZTJB2CE3QR';

    beforeEach(() => {
        passService = new PassService();
    });

    test('Should generate valid 6-digit TOTP token', () => {
        const result = passService.generateDynamicPass('ticket-uuid-123', mockSecret);
        expect(result.totpToken).toHaveLength(6);
        expect(result.expiresInSeconds).toBe(10);
    });

    test('Should verify authentic TOTP token successfully', () => {
        const token = authenticator.generate(mockSecret);
        const isValid = passService.verifyGatePass(token, mockSecret);
        expect(isValid).toBe(true);
    });

    test('Should reject invalid or expired TOTP token', () => {
        const isValid = passService.verifyGatePass('000000', mockSecret);
        expect(isValid).toBe(false);
    });
});
```

### 3.2 Automated CI/CD Pipeline (`.github/workflows/ci-pipeline.yml`)

```yaml
name: Software Engineering CI/CD Pipeline

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  quality-gate:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout Code Repository
        uses: actions/checkout@v3

      - name: Setup Node.js Environment
        uses: actions/setup-node@v3
        with:
          node-version: 18

      - name: Install Backend Dependencies
        run: cd backend && npm ci

      - name: Run Unit Tests & Check Code Coverage (>60%)
        run: cd backend && npm test -- --coverage --coverageThreshold='{"global":{"lines":60}}'

      - name: OpenAPI Contract Validation
        run: npx @redocly/cli lint docs/openapi-spec.yaml
```

---

## 4. Step-by-Step Execution Checklist & Commands

### Step 1: Initialize Git Repository Local Environment (Week 2)
Run in terminal:
```bash
git clone https://github.com/52400213-NPLong/Online_Event_Ticketing-Anti-Scalping_Pass_Portal.git
cd Online_Event_Ticketing-Anti-Scalping_Pass_Portal

# Create local branches for feature development
git checkout -b feature/us-101-seat-locking
```

### Step 2: Implement & Test Feature Locally
```bash
cd backend
npm install
npm test
```

### Step 3: Commit and Create Peer-Reviewed Pull Request (Mandatory)
```bash
git add .
git commit -m "feat(reservations): add redis lock and optimistic DB check for seat booking"
git push origin feature/us-101-seat-locking
```
1. Go to GitHub Web UI -> Click **New Pull Request**.
2. Select `main` <- `feature/us-101-seat-locking`.
3. GitHub will auto-populate the `PULL_REQUEST_TEMPLATE.md` template.
4. Mention a teammate (e.g., `@member2`) to review and approve.
5. Once approved and CI runs green, merge into `main`.