// gate-scanner.js - Thiết bị quét & Xác thực vé tại cổng sự kiện (RBAC: Chỉ dành cho GATE_STAFF & ADMIN)
(function () {
  const API_BASE = window.location.origin.includes('http')
    ? window.location.origin
    : 'http://localhost:5000';

  const PRESENTED_QR_KEY = 'passportal_presented_qr';
  let currentSession = null;

  document.addEventListener('DOMContentLoaded', initScanner);

  function initScanner() {
    if (window.PassPortalAuth && !window.PassPortalAuth.requireRole(['GATE_STAFF', 'ADMIN'])) {
      return;
    }

    currentSession = window.PassPortalAuth ? window.PassPortalAuth.getSession() : null;

    clearDefaultInputs();
    injectPresentedQrHelper();
    autoFillFromUrl();
    setupCheckInAction();
    loadRecentLogs();
  }

  function clearDefaultInputs() {
    const ticketInput = document.getElementById('ticketId');
    const userInput = document.getElementById('userId');
    const tokenInput = document.getElementById('token');
    if (ticketInput && ticketInput.value === 'TCK-4821') ticketInput.value = '';
    if (userInput && userInput.value === 'USER-1001') userInput.value = '';
    if (tokenInput && tokenInput.value === '4821') tokenInput.value = '';
    if (ticketInput) ticketInput.placeholder = 'Nhập hoặc nạp mã vé (UUID)...';
    if (userInput) userInput.placeholder = 'Mã định danh người sở hữu vé...';
    if (tokenInput) tokenInput.placeholder = 'Chuỗi HMAC-SHA256 làm mới mỗi 30s...';
  }

  function injectPresentedQrHelper() {
    const panel = document.querySelector('.panel');
    const heading = panel?.querySelector('h3');
    if (!panel || !heading) return;

    const helperDiv = document.createElement('div');
    helperDiv.style.cssText = 'margin: 10px 0 16px; padding: 12px; border-radius: 10px; background: rgba(59, 130, 246, 0.1); border: 1px dashed rgba(96, 165, 250, 0.45);';

    let presented = null;
    try {
      const raw = localStorage.getItem(PRESENTED_QR_KEY);
      if (raw) presented = JSON.parse(raw);
    } catch {
      presented = null;
    }

    const subtitle = presented
      ? `Phát hiện vé vừa xuất trình: <strong>${presented.eventName || 'Sự kiện'} • Ghế ${presented.seatLabel || '?'}</strong> (${presented.holderName || 'Khách hàng'})`
      : 'Chưa có vé xuất trình trong phiên trình duyệt này (hoặc nhập trực tiếp bên dưới).';

    helperDiv.innerHTML = `
      <div style="font-size:0.8rem; color:#bfdbfe; margin-bottom:8px;" id="presentedQrStatus">${subtitle}</div>
      <button type="button" id="loadPresentedQrBtn" class="btn btn-secondary" style="width:100%; padding:8px 12px; font-size:0.84rem;">
        📥 Nạp mã QR khách vừa xuất trình
      </button>
    `;

    heading.insertAdjacentElement('afterend', helperDiv);

    document.getElementById('loadPresentedQrBtn')?.addEventListener('click', () => {
      try {
        const raw = localStorage.getItem(PRESENTED_QR_KEY);
        if (!raw) {
          showResult('warning', '⚠️ Chưa có khách hàng nào bấm "Xuất trình mã QR" từ Ví vé trên trình duyệt này.');
          return;
        }
        const data = JSON.parse(raw);
        const ticketInput = document.getElementById('ticketId');
        const userInput = document.getElementById('userId');
        const tokenInput = document.getElementById('token');

        if (ticketInput) ticketInput.value = data.ticketId || '';
        if (userInput) userInput.value = data.userId || '';
        if (tokenInput) tokenInput.value = data.token || '';

        const ageSec = Math.max(0, Math.floor((Date.now() - (data.presentedAt || Date.now())) / 1000));
        showResult(
          'success',
          `📥 Đã nạp thông tin vé <strong>Ghế ${data.seatLabel}</strong> của khách <strong>${data.holderName || data.userId}</strong> (xuất trình ${ageSec}s trước). Nhấn <strong>Xác nhận check-in</strong> để kiểm tra!`
        );
      } catch {
        showResult('danger', '❌ Không đọc được dữ liệu mã QR xuất trình.');
      }
    });
  }

  function autoFillFromUrl() {
    const params = new URLSearchParams(window.location.search);
    const ticketId = params.get('ticketId');
    const userId = params.get('userId');
    const token = params.get('token');

    if (ticketId) {
      const ticketInput = document.getElementById('ticketId');
      if (ticketInput) ticketInput.value = ticketId;
    }

    if (userId) {
      const userInput = document.getElementById('userId');
      if (userInput) userInput.value = userId;
    }

    if (token) {
      const tokenInput = document.getElementById('token');
      if (tokenInput) tokenInput.value = token;
    }
  }

  function setupCheckInAction() {
    const checkinBtn = document.querySelector('.panel .btn-primary');
    if (!checkinBtn) return;

    const panel = document.querySelector('.panel');
    const resultBox = document.createElement('div');
    resultBox.id = 'scanResultBox';
    resultBox.style.cssText = 'margin-top: 16px; padding: 14px; border-radius: 8px; display: none;';
    panel.appendChild(resultBox);

    checkinBtn.addEventListener('click', handleVerifyCheckIn);
  }

  async function handleVerifyCheckIn() {
    const ticketIdInput = document.getElementById('ticketId');
    const userIdInput = document.getElementById('userId');
    const tokenInput = document.getElementById('token');
    const statusPill = document.querySelector('.status-pill');
    const checkinBtn = document.querySelector('.panel .btn-primary');

    const ticketId = ticketIdInput?.value.trim();
    const userId = userIdInput?.value.trim();
    const token = tokenInput?.value.trim();

    if (!ticketId || !userId || !token) {
      showResult('warning', '⚠️ Vui lòng điền đủ Ticket ID, User ID và Dynamic Token (hoặc nhấn Nạp mã QR khách vừa xuất trình).');
      return;
    }

    checkinBtn.disabled = true;
    checkinBtn.textContent = 'Đang xác thực bảo mật...';

    try {
      const headers = window.PassPortalAuth
        ? window.PassPortalAuth.getAuthHeaders()
        : { 'Content-Type': 'application/json' };

      const res = await fetch(`${API_BASE}/api/v1/gate/checkin`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
          ticketId: ticketId,
          userId: userId,
          dynamicToken: token,
          staffId: currentSession?.email || 'GATE_STAFF_MAIN'
        })
      });

      const data = await res.json();

      if (res.ok) {
        showResult('success', `
          <div style="font-size: 1.1rem; font-weight: 700; color: #4ade80; margin-bottom: 6px;">
            ✅ XÁC THỰC THÀNH CÔNG - CHO QUA CỔNG
          </div>
          <div><strong>Khách hàng định danh:</strong> ${data.holderName || userId}</div>
          <div><strong>Sự kiện:</strong> ${data.eventName || 'Sự kiện'}</div>
          <div><strong>Ghế ngồi:</strong> <span style="font-size:1.2rem; color:#fde047; font-weight:bold;">${data.seatLabel}</span></div>
          <div style="font-size:0.8rem; color:#9ca3af; margin-top:4px;">Nhân viên soát vé: ${currentSession?.fullName || 'Gate Staff'} • ${new Date(data.verifiedAt).toLocaleTimeString()}</div>
        `);

        if (statusPill) {
          statusPill.textContent = '● Vé hợp lệ - Đã duyệt';
          statusPill.style.color = '#4ade80';
        }
      } else if (res.status === 403) {
        showResult('danger', `
          <div style="font-size: 1.05rem; font-weight: 700; color: #ef4444; margin-bottom: 4px;">
            🚫 KHÔNG ĐỦ QUYỀN QUÉT VÉ (HTTP 403)
          </div>
          <div style="font-size:0.9rem;">${data.message}</div>
        `);
      } else if (res.status === 409) {
        showResult('danger', `
          <div style="font-size: 1.05rem; font-weight: 700; color: #ef4444; margin-bottom: 4px;">
            🚨 CẢNH BÁO: VÉ ĐÃ CHECK-IN TRƯỚC ĐÓ!
          </div>
          <div style="font-size:0.9rem;">${data.message}</div>
          <div style="font-size:0.75rem; color:#fca5a5; margin-top:6px;">
            (Hệ thống chống phe vé phát hiện mã vé này đã được sử dụng qua cổng trước đó).
          </div>
        `);

        if (statusPill) {
          statusPill.textContent = '● Cảnh báo gian lận';
          statusPill.style.color = '#ef4444';
        }
      } else {
        showResult('danger', `
          <div style="font-size: 1.05rem; font-weight: 700; color: #ef4444; margin-bottom: 4px;">
            ❌ TỪ CHỐI CHECK-IN: MÃ QR KHÔNG HỢP LỆ
          </div>
          <div style="font-size:0.9rem;">${data.message || 'Mã xác thực không hợp lệ.'}</div>
          <div style="font-size:0.75rem; color:#fca5a5; margin-top:6px;">
            (Mã QR động thay đổi sau mỗi 30s và gắn liền với chủ sở hữu vé. Khách cần mở ví vé chính chủ trực tiếp để lấy mã mới).
          </div>
        `);

        if (statusPill) {
          statusPill.textContent = '● Mã không hợp lệ';
          statusPill.style.color = '#ef4444';
        }
      }

      await loadRecentLogs();
    } catch (err) {
      showResult('danger', '❌ Không thể kết nối đến máy chủ kiểm soát vé.');
    } finally {
      checkinBtn.disabled = false;
      checkinBtn.textContent = 'Xác nhận check-in';
    }
  }

  function showResult(type, htmlContent) {
    const box = document.getElementById('scanResultBox');
    if (!box) return;

    box.style.display = 'block';
    if (type === 'success') {
      box.style.background = 'rgba(34, 197, 94, 0.15)';
      box.style.border = '1px solid #22c55e';
      box.style.color = '#dcfce7';
    } else if (type === 'danger') {
      box.style.background = 'rgba(239, 68, 68, 0.15)';
      box.style.border = '1px solid #ef4444';
      box.style.color = '#fee2e2';
    } else {
      box.style.background = 'rgba(234, 179, 8, 0.15)';
      box.style.border = '1px solid #eab308';
      box.style.color = '#fef9c3';
    }

    box.innerHTML = htmlContent;
  }

  async function loadRecentLogs() {
    try {
      const headers = window.PassPortalAuth ? window.PassPortalAuth.getAuthHeaders() : {};
      const res = await fetch(`${API_BASE}/api/v1/gate/logs`, { headers });
      if (!res.ok) return;

      const logs = await res.json();
      renderLogsTable(logs);
    } catch (err) {
      // Bỏ qua lỗi log
    }
  }

  function renderLogsTable(logs) {
    let logContainer = document.getElementById('gateLogContainer');
    if (!logContainer) {
      const container = document.querySelector('main .container');
      if (!container) return;

      logContainer = document.createElement('div');
      logContainer.id = 'gateLogContainer';
      logContainer.style.cssText = 'margin-top: 36px;';
      container.appendChild(logContainer);
    }

    if (!logs || logs.length === 0) {
      logContainer.innerHTML = '';
      return;
    }

    logContainer.innerHTML = `
      <h3 style="margin-bottom: 12px; font-size:1.1rem; color:#e4e4e7;">📋 Nhật ký quét vé gần nhất tại cổng</h3>
      <div style="overflow-x:auto; background:#18181b; border:1px solid #27272a; border-radius:8px;">
        <table style="width:100%; border-collapse:collapse; font-size:0.85rem; text-align:left;">
          <thead>
            <tr style="border-bottom:1px solid #27272a; color:#a1a1aa;">
              <th style="padding:10px 14px;">Thời gian</th>
              <th style="padding:10px 14px;">Mã vé</th>
              <th style="padding:10px 14px;">Khách hàng</th>
              <th style="padding:10px 14px;">Nhân viên</th>
              <th style="padding:10px 14px;">Kết quả</th>
              <th style="padding:10px 14px;">Chi tiết</th>
            </tr>
          </thead>
          <tbody>
            ${logs.slice(0, 8).map(l => `
              <tr style="border-bottom:1px solid #27272a;">
                <td style="padding:8px 14px; color:#9ca3af;">${new Date(l.scannedAt).toLocaleTimeString()}</td>
                <td style="padding:8px 14px; font-family:monospace;">${l.ticketId.substring(0, 8)}...</td>
                <td style="padding:8px 14px; font-family:monospace;">${String(l.userId || '').substring(0, 12)}...</td>
                <td style="padding:8px 14px; color:#cbd5e1;">${l.staffId || 'GATE_STAFF'}</td>
                <td style="padding:8px 14px;">
                  <span style="display:inline-block; padding:2px 8px; border-radius:4px; font-weight:600; font-size:0.75rem; ${
                    l.status === 'SUCCESS' ? 'background:#166534; color:#86efac;' : 'background:#991b1b; color:#fca5a5;'
                  }">
                    ${l.status}
                  </span>
                </td>
                <td style="padding:8px 14px; color:#a1a1aa;">${l.reason || 'Check-in thành công'}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    `;
  }
})();
