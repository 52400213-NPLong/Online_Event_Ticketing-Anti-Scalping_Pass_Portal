// dynamic-qr.js - Quản lý Ví vé điện tử chính chủ & Tạo mã QR động 30s chống chụp màn hình (RBAC: CUSTOMER / ADMIN)
(function () {
  const API_BASE = window.location.origin.includes('http')
    ? window.location.origin
    : 'http://localhost:5000';

  const PRESENTED_QR_KEY = 'passportal_presented_qr';
  let activeTimers = [];
  let currentSession = null;

  document.addEventListener('DOMContentLoaded', initWallet);

  async function initWallet() {
    if (window.PassPortalAuth && !window.PassPortalAuth.requireRole(['CUSTOMER', 'ADMIN'])) {
      return;
    }

    currentSession = window.PassPortalAuth ? window.PassPortalAuth.getSession() : null;
    if (!currentSession) return;

    await loadUserTickets();
  }

  async function loadUserTickets() {
    const container = document.querySelector('.ticket-list');
    if (!container) return;

    container.innerHTML = '<div style="padding: 24px; color: #a1a1aa;">Đang tải danh sách vé định danh của bạn...</div>';

    activeTimers.forEach((timer) => clearInterval(timer));
    activeTimers = [];

    try {
      const headers = window.PassPortalAuth
        ? window.PassPortalAuth.getAuthHeaders()
        : {};
      const res = await fetch(`${API_BASE}/api/v1/tickets/user/${encodeURIComponent(currentSession.userId)}`, { headers });
      if (!res.ok) throw new Error('Không thể tải vé');

      const tickets = await res.json();

      if (!tickets || tickets.length === 0) {
        container.innerHTML = `
          <div style="background:#18181b; border:1px dashed #3f3f46; border-radius:12px; padding:32px; text-align:center; width:100%; grid-column: 1 / -1;">
            <p style="color:#9ca3af; margin-bottom:16px;">
              Tài khoản <strong>${currentSession.fullName}</strong> (${currentSession.email}) hiện chưa sở hữu vé nào trong ví.
            </p>
            <a href="seat-selection.html" class="btn btn-primary">Đặt vé chọn ghế ngay</a>
          </div>
        `;
        return;
      }

      container.innerHTML = '';
      tickets.forEach((ticket) => {
        const card = createTicketCard(ticket);
        container.appendChild(card);
        if (ticket.status !== 'Used') {
          setupTicketDynamicQr(ticket, `ticket-${ticket.ticketId}`);
        }
      });
    } catch (err) {
      console.warn('Lỗi khi tải danh sách vé:', err);
      container.innerHTML = `
        <div style="background:#18181b; border:1px solid #ef4444; border-radius:12px; padding:24px; color:#ef4444; grid-column: 1 / -1;">
          Không thể kết nối đến máy chủ API (${API_BASE}). Vui lòng đảm bảo backend đang chạy.
        </div>
      `;
    }
  }

  function createTicketCard(ticket) {
    const card = document.createElement('article');
    card.className = 'ticket-card';
    card.style.cssText = 'position: relative; overflow: hidden;';

    const isUsed = ticket.status === 'Used';
    const statusBadge = isUsed
      ? '<span class="badge" style="background:#ef4444; color:#fff;">Đã sử dụng</span>'
      : '<span class="badge success">Hợp lệ (Chính chủ)</span>';

    const cardId = `ticket-${ticket.ticketId}`;

    card.innerHTML = `
      <div class="ticket-top">
        <div>
          <div class="pass-meta">VIP PASS • Chủ vé: ${currentSession.fullName}</div>
          <strong style="font-size: 1.15rem;">${ticket.eventName}</strong>
        </div>
        ${statusBadge}
      </div>

      <h4 style="margin: 12px 0 4px 0; font-size: 1.1rem; color: #f4f4f5;">
        Ghế ${ticket.seatLabel} • ${ticket.venue}
      </h4>
      <p style="color: #9ca3af; font-size: 0.85rem; margin-bottom: 16px;">
        Mã vé: <code style="color:#a78bfa;">${ticket.ticketId.substring(0, 8)}...</code> • Giá: ${ticket.price.toLocaleString('vi-VN')}đ
      </p>

      ${
        isUsed
          ? `<div style="background:#27272a; padding:16px; border-radius:8px; text-align:center; color:#9ca3af;">
               Vé này đã check-in qua cổng soát vé. Mã QR đã bị vô hiệu hóa để chống tái sử dụng.
             </div>`
          : `
          <div class="qr-token" style="background:#18181b; border:1px solid #3f3f46; border-radius:10px; padding:16px;">
            <div style="flex:1;">
              <div class="pass-meta" style="color:#60a5fa; font-weight:600;">🛡️ DYNAMIC QR (Chống chụp ảnh)</div>
              <div style="margin-top:6px; font-size:0.8rem; color:#9ca3af;">Mã xác thực 30s:</div>
              <div id="${cardId}-token" style="font-family:monospace; font-size:1.05rem; font-weight:bold; color:#fde047; word-break:break-all;">
                Đang nạp token...
              </div>
              <div style="margin-top:8px;">
                <div style="display:flex; justify-content:space-between; font-size:0.75rem; color:#9ca3af; margin-bottom:4px;">
                  <span>Làm mới sau:</span>
                  <strong id="${cardId}-timer" style="color:#4ade80; font-size:0.85rem;">30s</strong>
                </div>
                <div style="background:#3f3f46; height:6px; border-radius:3px; overflow:hidden;">
                  <div id="${cardId}-progress" style="background:#4ade80; height:100%; width:100%; transition:width 0.9s linear;"></div>
                </div>
              </div>
            </div>

            <div style="margin-left:16px; text-align:center;">
              <div id="${cardId}-qrcode" style="width:100px; height:100px; background:#fff; padding:6px; border-radius:8px; display:flex; align-items:center; justify-content:center;">
                <span style="color:#000; font-size:0.75rem;">Loading QR...</span>
              </div>
            </div>
          </div>

          <div style="margin-top:14px; display:flex; gap:8px; flex-wrap:wrap;">
            <button type="button" id="${cardId}-presentBtn" class="btn btn-secondary" style="flex:1; text-align:center; font-size:0.85rem; padding:9px 12px;">
              📲 Xuất trình mã QR để quét tại cổng
            </button>
          </div>
          <div id="${cardId}-presentNotice" style="display:none; margin-top:10px; padding:10px 12px; border-radius:8px; background:rgba(34,197,94,0.12); border:1px solid rgba(34,197,94,0.35); font-size:0.8rem; color:#bbf7d0; line-height:1.5;"></div>
        `
      }
    `;

    return card;
  }

  function setupTicketDynamicQr(ticket, cardId) {
    let lastWindow = -1;
    let latestDynamicToken = '';
    let isPresenting = false;

    function savePresentedPayload(tokenValue) {
      const payload = {
        ticketId: ticket.ticketId,
        userId: ticket.userId,
        holderName: currentSession.fullName,
        eventName: ticket.eventName,
        seatLabel: ticket.seatLabel,
        token: tokenValue,
        presentedAt: Date.now()
      };
      localStorage.setItem(PRESENTED_QR_KEY, JSON.stringify(payload));

      // Gửi tín hiệu xuất trình vé lên máy chủ để các tab ẩn danh hoặc thiết bị khác cũng nhận được
      try {
        const headers = window.PassPortalAuth ? window.PassPortalAuth.getAuthHeaders() : { 'Content-Type': 'application/json' };
        fetch(`${API_BASE}/api/v1/gate/present`, {
          method: 'POST',
          headers,
          body: JSON.stringify(payload)
        }).catch(() => {});
      } catch {}
    }

    async function fetchNewToken() {
      try {
        const headers = window.PassPortalAuth ? window.PassPortalAuth.getAuthHeaders() : {};
        const res = await fetch(
          `${API_BASE}/api/v1/tickets/dynamic-qr?ticketId=${ticket.ticketId}&userId=${encodeURIComponent(ticket.userId)}`,
          { headers }
        );
        if (!res.ok) return;

        const data = await res.json();
        latestDynamicToken = data.dynamicToken;

        if (isPresenting) {
          savePresentedPayload(latestDynamicToken);
        }

        const tokenElem = document.getElementById(`${cardId}-token`);
        const qrContainer = document.getElementById(`${cardId}-qrcode`);

        if (tokenElem) {
          tokenElem.textContent = data.dynamicToken.substring(0, 16) + '...';
          tokenElem.title = data.dynamicToken;
        }

        if (qrContainer) {
          const qrPayload = encodeURIComponent(
            JSON.stringify({
              ticketId: ticket.ticketId,
              userId: ticket.userId,
              token: data.dynamicToken
            })
          );
          qrContainer.innerHTML = `
            <img src="https://api.qrserver.com/v1/create-qr-code/?size=90x90&data=${qrPayload}" 
                 alt="Dynamic QR" style="width:100%; height:100%; display:block;"
                 onerror="this.onerror=null; this.parentElement.innerHTML='<div style=\\'background:#111; color:#fff; font-size:10px; padding:4px; text-align:center;\\'>QR Token Active</div>';" />
          `;
        }
      } catch (err) {
        console.error('Lỗi sinh mã QR động:', err);
      }
    }

    const presentBtn = document.getElementById(`${cardId}-presentBtn`);
    const presentNotice = document.getElementById(`${cardId}-presentNotice`);

    presentBtn?.addEventListener('click', () => {
      if (!latestDynamicToken) {
        alert('Đang tạo mã QR động, vui lòng thử lại sau giây lát.');
        return;
      }

      isPresenting = true;
      savePresentedPayload(latestDynamicToken);

      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(latestDynamicToken).catch(() => {});
      }

      if (currentSession.role === 'ADMIN') {
        window.location.href = `gate-checkin.html?ticketId=${ticket.ticketId}&userId=${encodeURIComponent(
          ticket.userId
        )}&token=${encodeURIComponent(latestDynamicToken)}`;
        return;
      }

      if (presentNotice) {
        presentNotice.style.display = 'block';
        presentNotice.innerHTML = `
          ✅ <strong>Đã phát mã QR xuất trình (Ghế ${ticket.seatLabel})!</strong><br/>
          Vui lòng xuất trình màn hình này cho Nhân viên soát vé tại cổng để quét xác thực trong chu kỳ 30 giây.
        `;
      }
    });

    function updateCountdown() {
      const nowSec = Math.floor(Date.now() / 1000);
      const currentWindow = Math.floor(nowSec / 30);
      const secondsLeft = 30 - (nowSec % 30);

      if (currentWindow !== lastWindow) {
        lastWindow = currentWindow;
        fetchNewToken();
      }

      const timerElem = document.getElementById(`${cardId}-timer`);
      const progressElem = document.getElementById(`${cardId}-progress`);

      if (timerElem) {
        timerElem.textContent = `${secondsLeft}s`;
      }
      if (progressElem) {
        const percent = (secondsLeft / 30) * 100;
        progressElem.style.width = `${percent}%`;
        progressElem.style.background = secondsLeft <= 5 ? '#ef4444' : '#4ade80';
      }
    }

    updateCountdown();

    const interval = setInterval(updateCountdown, 1000);
    activeTimers.push(interval);
  }
})();
