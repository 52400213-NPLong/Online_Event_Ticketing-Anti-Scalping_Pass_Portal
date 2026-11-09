// seatmap.js - Logic chọn nhiều ghế (tối đa 4 ghế), giữ chỗ 10 phút, chống đầu cơ vé & tích hợp phân quyền RBAC
(function () {
  const API_BASE = window.location.origin.includes('http')
    ? window.location.origin
    : 'http://localhost:5000';

  const MAX_ALLOWED_SEATS = 4; // Quy tắc chống phe vé: tối đa 4 vé/người

  let currentEventId = null;
  let allEvents = [];
  let selectedSeats = []; // Danh sách các ghế người dùng đang chọn (tối đa 4)
  let currentBatchHold = null; // Thông tin các ghế đang được khóa giữ chỗ
  let holdInterval = null;

  const EVENT_SLUG_KEYWORD = {
    'sunset-music-fest-2026': 'sunset',
    'jazz-night-live': 'jazz',
    'art-light-festival': 'art',
    'rock-symphony-concert': 'rock'
  };

  function getActiveSession() {
    return window.PassPortalAuth ? window.PassPortalAuth.getSession() : null;
  }

  document.addEventListener('DOMContentLoaded', initSeatMap);

  async function initSeatMap() {
    await loadEventAndSeats();
    setupActions();
    updateSummary();
  }

  function resolveInitialEvent(events) {
    const params = new URLSearchParams(window.location.search);
    const eventIdParam = params.get('eventId');
    if (eventIdParam) {
      const byId = events.find(e => e.id.toLowerCase() === eventIdParam.toLowerCase());
      if (byId) return byId;
    }

    const slug = (params.get('event') || '').toLowerCase();
    const keyword = EVENT_SLUG_KEYWORD[slug] || slug;
    if (keyword) {
      const bySlug = events.find(e => e.title.toLowerCase().includes(keyword));
      if (bySlug) return bySlug;
    }

    return events[0];
  }

  function renderEventSelector(events, selectedEvent) {
    const mutedHeader = document.querySelector('.muted');
    if (!mutedHeader) return;

    mutedHeader.innerHTML = `
      <label for="eventPickerSelect" style="font-size:0.85rem; color:#94a3b8; margin-right:8px;">Sự kiện:</label>
      <select id="eventPickerSelect" style="background:#18181b; color:#f4f4f5; border:1px solid #3f3f46; border-radius:8px; padding:6px 12px; font-size:0.9rem;">
        ${events.map(ev => `<option value="${ev.id}" ${ev.id === selectedEvent.id ? 'selected' : ''}>${ev.title} • ${ev.venue}</option>`).join('')}
      </select>
    `;

    document.getElementById('eventPickerSelect')?.addEventListener('change', async (e) => {
      if (currentBatchHold) {
        alert('Vui lòng hoàn tất thanh toán hoặc hủy giữ chỗ hiện tại trước khi chuyển sự kiện.');
        e.target.value = currentEventId;
        return;
      }
      currentEventId = e.target.value;
      selectedSeats = [];
      await loadEventAndSeats();
      updateSummary();
    });
  }

  async function loadEventAndSeats() {
    const seatGrid = document.querySelector('.seat-grid');
    if (!seatGrid) return;

    seatGrid.innerHTML = '<div style="padding: 20px; color: #a1a1aa;">Đang tải sơ đồ ghế từ API...</div>';

    try {
      // 1. Lấy danh sách sự kiện từ SQL Server
      const eventsRes = await fetch(`${API_BASE}/api/v1/events`);
      if (!eventsRes.ok) throw new Error('Không thể tải sự kiện');
      const events = await eventsRes.json();
      if (!events || events.length === 0) return;

      allEvents = events;
      const event = currentEventId
        ? (events.find(e => e.id === currentEventId) || events[0])
        : resolveInitialEvent(events);
      currentEventId = event.id;

      // Cập nhật tiêu đề & bộ chọn sự kiện + Summary Panel
      document.title = `${event.title} | Pass Portal`;
      renderEventSelector(events, event);
      const summaryItems = document.querySelectorAll('.summary-item strong');
      if (summaryItems.length >= 1) {
        summaryItems[0].textContent = event.title;
      }

      // 2. Lấy danh sách ghế của sự kiện
      const seatsRes = await fetch(`${API_BASE}/api/v1/events/${currentEventId}/seats`);
      if (!seatsRes.ok) throw new Error('Không thể tải danh sách ghế');
      const seats = await seatsRes.json();

      renderSeats(seats);
    } catch (err) {
      console.warn('API fetch seats error, using fallback:', err);
    }
  }

  function renderSeats(seats) {
    const seatGrid = document.querySelector('.seat-grid');
    if (!seatGrid) return;

    seatGrid.innerHTML = '';
    seats.forEach((seat) => {
      const btn = document.createElement('button');
      const statusClass = seat.status.toLowerCase();

      // Kiểm tra xem ghế này có đang nằm trong danh sách được chọn không
      const isSelected = selectedSeats.some(s => s.id === seat.id);
      btn.className = `seat ${statusClass} ${isSelected ? 'selected' : ''}`;
      btn.textContent = seat.seatLabel;
      btn.title = `Ghế ${seat.seatLabel} - ${seat.price.toLocaleString('vi-VN')}đ - Trạng thái: ${seat.status}`;

      if (statusClass === 'available') {
        btn.addEventListener('click', () => onSeatClick(seat, btn));
      } else {
        btn.disabled = true;
      }

      seatGrid.appendChild(btn);
    });
  }

  function onSeatClick(seat, btnElem) {
    const session = getActiveSession();
    if (session && session.role === 'GATE_STAFF') {
      alert('🚫 Tài khoản Nhân viên soát vé (GATE_STAFF) chỉ có quyền quét vé tại cổng, không được phép mua vé.');
      return;
    }

    if (currentBatchHold) {
      alert('Bạn đang có các ghế đang trong thời gian giữ chỗ. Vui lòng thanh toán hoặc hủy giữ chỗ trước.');
      return;
    }

    const existingIndex = selectedSeats.findIndex(s => s.id === seat.id);

    if (existingIndex >= 0) {
      selectedSeats.splice(existingIndex, 1);
      btnElem.classList.remove('selected');
    } else {
      if (selectedSeats.length >= MAX_ALLOWED_SEATS) {
        alert(`🛡️ QUY TẮC CHỐNG ĐẦU CƠ VÉ (Anti-Scalping):\nMỗi tài khoản chỉ được chọn và giữ chỗ tối đa ${MAX_ALLOWED_SEATS} ghế cùng lúc!`);
        return;
      }

      selectedSeats.push(seat);
      btnElem.classList.add('selected');
    }

    updateSummary();
  }

  function updateSummary() {
    const summaryItems = document.querySelectorAll('.summary-item strong');
    const totalSpan = document.querySelector('.summary-total span:last-child');
    const holdBtn = document.getElementById('holdSeatBtn');
    const session = getActiveSession();

    if (session && session.role === 'GATE_STAFF') {
      if (holdBtn) {
        holdBtn.disabled = true;
        holdBtn.textContent = '🚫 Nhân viên cổng không thể đặt vé';
      }
      return;
    }

    if (selectedSeats.length === 0) {
      if (summaryItems.length >= 5) {
        summaryItems[1].textContent = '(Chưa chọn ghế)';
        summaryItems[2].textContent = '-';
        summaryItems[3].textContent = '0đ';
        summaryItems[4].textContent = '0đ';
      }
      if (totalSpan) totalSpan.textContent = '0đ';
      if (holdBtn) {
        holdBtn.disabled = true;
        holdBtn.textContent = 'Chọn từ 1 đến 4 ghế trống';
      }
      return;
    }

    const labels = selectedSeats.map(s => s.seatLabel).join(', ');
    const totalPrice = selectedSeats.reduce((sum, s) => sum + s.price, 0);
    const totalFee = 50000 * selectedSeats.length;

    if (summaryItems.length >= 5) {
      summaryItems[1].textContent = `${labels} (${selectedSeats.length} ghế)`;
      const types = [...new Set(selectedSeats.map(s => s.seatLabel.startsWith('A') ? 'VIP' : s.seatLabel.startsWith('B') ? 'Standard' : 'Economy'))];
      summaryItems[2].textContent = types.join(' / ');
      summaryItems[3].textContent = `${totalPrice.toLocaleString('vi-VN')}đ`;
      summaryItems[4].textContent = `${totalFee.toLocaleString('vi-VN')}đ`;
    }

    if (totalSpan) {
      totalSpan.textContent = `${(totalPrice + totalFee).toLocaleString('vi-VN')}đ`;
    }

    if (holdBtn) {
      holdBtn.disabled = false;
      holdBtn.textContent = session
        ? `Giữ chỗ ${selectedSeats.length} ghế trong 10 phút`
        : `🔐 Đăng nhập để giữ chỗ ${selectedSeats.length} ghế`;
    }
  }

  function setupActions() {
    const summaryPanel = document.querySelector('.summary-panel');
    if (!summaryPanel) return;

    const actionContainer = summaryPanel.querySelector('div[style*="margin-top"]') || summaryPanel;
    const session = getActiveSession();

    const roleHintHtml = !session
      ? `<div style="margin-bottom:10px; padding:10px 12px; border-radius:8px; background:rgba(59,130,246,0.12); border:1px solid rgba(59,130,246,0.35); font-size:0.82rem; color:#bfdbfe;">
           💡 Bạn đang xem ở chế độ khách. Hãy <a href="login.html?returnUrl=${encodeURIComponent('seat-selection.html' + window.location.search)}" style="color:#60a5fa; text-decoration:underline; font-weight:700;">Đăng nhập</a> tài khoản <strong>Người mua vé</strong> để giữ chỗ & xuất vé định danh.
         </div>`
      : session.role === 'GATE_STAFF'
      ? `<div style="margin-bottom:10px; padding:10px 12px; border-radius:8px; background:rgba(245,158,11,0.14); border:1px solid rgba(245,158,11,0.4); font-size:0.82rem; color:#fde68a;">
           🚪 Bạn đang đăng nhập vai trò <strong>Nhân viên cổng</strong>. Vui lòng chuyển sang <a href="gate-checkin.html" style="color:#fbbf24; text-decoration:underline; font-weight:700;">Cổng Check-in</a> để làm việc.
         </div>`
      : `<div style="margin-bottom:10px; padding:8px 12px; border-radius:8px; background:rgba(34,197,94,0.1); border:1px solid rgba(34,197,94,0.3); font-size:0.8rem; color:#bbf7d0;">
           🎫 Vé đặt sẽ được cấp định danh cho: <strong>${session.fullName}</strong> (${session.email})
         </div>`;

    actionContainer.innerHTML = `
      ${roleHintHtml}
      <div id="countdownBox" style="display:none; background:#27272a; border-radius:8px; padding:12px; margin-bottom:12px; text-align:center;">
        <div style="font-size:0.8rem; color:#fbbf24; font-weight:600;">⏳ THỜI GIAN GIỮ CHỖ CÒN LẠI</div>
        <div id="timerText" style="font-size:1.6rem; font-weight:700; color:#4ade80;">10:00</div>
        <div style="font-size:0.75rem; color:#9ca3af;">Quá thời gian trên, các ghế sẽ tự động giải phóng.</div>
      </div>
      <div id="buttonGroup">
        <button id="holdSeatBtn" class="btn btn-primary" style="width: 100%;">Chọn từ 1 đến 4 ghế trống</button>
      </div>
      <div id="noticeMsg" style="margin-top:10px; font-size:0.85rem; display:none;"></div>
    `;

    document.getElementById('holdSeatBtn')?.addEventListener('click', handleHoldSeats);
  }

  async function handleHoldSeats() {
    if (selectedSeats.length === 0) {
      alert('Vui lòng chọn từ 1 đến 4 ghế trống trên sơ đồ trước.');
      return;
    }

    const session = getActiveSession();
    if (!session) {
      const returnUrl = encodeURIComponent(`seat-selection.html?eventId=${currentEventId || ''}`);
      window.location.href = `login.html?returnUrl=${returnUrl}`;
      return;
    }

    if (session.role === 'GATE_STAFF') {
      alert('🚫 Tài khoản Nhân viên soát vé không được phép đặt mua vé.');
      return;
    }

    const notice = document.getElementById('noticeMsg');
    const holdBtn = document.getElementById('holdSeatBtn');
    holdBtn.disabled = true;
    holdBtn.textContent = `Đang khóa giữ ${selectedSeats.length} ghế...`;

    try {
      const headers = window.PassPortalAuth
        ? window.PassPortalAuth.getAuthHeaders()
        : { 'Content-Type': 'application/json' };

      const res = await fetch(`${API_BASE}/api/v1/tickets/hold-batch`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
          userId: session.userId,
          seatIds: selectedSeats.map(s => s.id)
        })
      });

      const data = await res.json();

      if (!res.ok) {
        notice.style.display = 'block';
        notice.style.color = '#ef4444';
        notice.textContent = `❌ ${data.message || 'Không thể giữ chỗ'}`;
        holdBtn.disabled = false;
        holdBtn.textContent = `Giữ chỗ ${selectedSeats.length} ghế trong 10 phút`;
        return;
      }

      // Giữ chỗ thành công
      currentBatchHold = {
        holdIds: data.holdIds,
        seatIds: data.seatIds,
        count: data.count,
        expiresAt: Date.now() + 10 * 60 * 1000
      };

      const labels = selectedSeats.map(s => s.seatLabel).join(', ');
      notice.style.display = 'block';
      notice.style.color = '#4ade80';
      notice.textContent = `✅ Giữ thành công ${data.count} ghế [${labels}]! Các ghế này được tạm khóa riêng cho ${session.fullName} trong 10 phút.`;

      startHoldCountdown(10 * 60);

      // Đổi nút sang Thanh toán hoặc Hủy
      const buttonGroup = document.getElementById('buttonGroup');
      buttonGroup.innerHTML = `
        <button id="confirmPayBtn" class="btn btn-primary" style="width: 100%; margin-bottom: 8px; background:#16a34a; border-color:#16a34a;">
          💳 Thanh toán & Xuất ${currentBatchHold.count} vé ngay
        </button>
        <button id="cancelHoldBtn" class="btn btn-secondary" style="width: 100%;">
          ❌ Hủy giữ chỗ các ghế này
        </button>
      `;

      document.getElementById('confirmPayBtn')?.addEventListener('click', handleConfirmBookingBatch);
      document.getElementById('cancelHoldBtn')?.addEventListener('click', handleCancelHoldBatch);

      await loadEventAndSeats();
    } catch (err) {
      notice.style.display = 'block';
      notice.style.color = '#ef4444';
      notice.textContent = '❌ Lỗi kết nối tới hệ thống đặt vé.';
      holdBtn.disabled = false;
      holdBtn.textContent = `Giữ chỗ ${selectedSeats.length} ghế trong 10 phút`;
    }
  }

  async function handleConfirmBookingBatch() {
    if (!currentBatchHold) return;

    const notice = document.getElementById('noticeMsg');
    const payBtn = document.getElementById('confirmPayBtn');
    payBtn.disabled = true;
    payBtn.textContent = 'Đang thanh toán & xuất vé...';

    try {
      const headers = window.PassPortalAuth
        ? window.PassPortalAuth.getAuthHeaders()
        : { 'Content-Type': 'application/json' };

      const res = await fetch(`${API_BASE}/api/v1/tickets/confirm-batch`, {
        method: 'POST',
        headers,
        body: JSON.stringify({ holdIds: currentBatchHold.holdIds })
      });

      const data = await res.json();
      if (!res.ok) {
        notice.style.display = 'block';
        notice.style.color = '#ef4444';
        notice.textContent = `❌ ${data.message || 'Lỗi xác nhận đặt vé'}`;
        payBtn.disabled = false;
        payBtn.textContent = `💳 Thanh toán & Xuất ${currentBatchHold.count} vé ngay`;
        return;
      }

      clearInterval(holdInterval);
      document.getElementById('countdownBox').style.display = 'none';

      notice.style.display = 'block';
      notice.style.color = '#4ade80';
      notice.innerHTML = `
        🎉 <strong>Đặt thành công ${data.count} vé định danh!</strong><br>
        Toàn bộ vé điện tử cùng mã QR động 30s đã được chuyển vào Ví vé cá nhân của bạn.<br>
        <a href="my-tickets.html" class="btn btn-primary" style="display:inline-block; margin-top:10px; width:100%; text-align:center;">
          📱 Mở ví vé & xem các mã QR động
        </a>
      `;

      document.getElementById('buttonGroup').innerHTML = '';
      currentBatchHold = null;
      selectedSeats = [];
      await loadEventAndSeats();
    } catch (err) {
      notice.style.color = '#ef4444';
      notice.textContent = '❌ Lỗi kết nối xác nhận thanh toán.';
      payBtn.disabled = false;
    }
  }

  async function handleCancelHoldBatch() {
    if (!currentBatchHold) return;
    const session = getActiveSession();
    if (!session) return;

    if (!confirm('Bạn có chắc chắn muốn hủy giữ chỗ các ghế này không?')) return;

    try {
      const headers = window.PassPortalAuth
        ? window.PassPortalAuth.getAuthHeaders()
        : { 'Content-Type': 'application/json' };

      await fetch(`${API_BASE}/api/v1/tickets/cancel-hold-batch`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
          holdIds: currentBatchHold.holdIds,
          userId: session.userId
        })
      });

      clearInterval(holdInterval);
      currentBatchHold = null;
      selectedSeats = [];
      setupActions();
      updateSummary();
      await loadEventAndSeats();
    } catch (err) {
      alert('Lỗi hủy giữ chỗ');
    }
  }

  function startHoldCountdown(secondsLeft) {
    const box = document.getElementById('countdownBox');
    const timerText = document.getElementById('timerText');
    if (!box || !timerText) return;

    box.style.display = 'block';

    if (holdInterval) clearInterval(holdInterval);

    holdInterval = setInterval(() => {
      secondsLeft--;
      if (secondsLeft <= 0) {
        clearInterval(holdInterval);
        box.style.display = 'none';
        alert('⏳ Hết thời gian giữ chỗ! Các ghế của bạn đã được tự động giải phóng.');
        currentBatchHold = null;
        selectedSeats = [];
        setupActions();
        updateSummary();
        loadEventAndSeats();
        return;
      }

      const mins = Math.floor(secondsLeft / 60);
      const secs = secondsLeft % 60;
      timerText.textContent = `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }, 1000);
  }
})();
