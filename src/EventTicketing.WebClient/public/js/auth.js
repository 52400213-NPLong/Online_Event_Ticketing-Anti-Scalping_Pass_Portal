// auth.js - Quản lý xác thực JWT, Phiên đăng nhập & Phân quyền giao diện theo Vai trò (RBAC - FR-01 -> FR-03)
(function () {
  const STORAGE_KEY = 'passportal_auth';

  const ROLE_META = {
    CUSTOMER: {
      label: 'Người mua vé',
      badgeBg: 'rgba(34, 197, 94, 0.16)',
      badgeBorder: 'rgba(34, 197, 94, 0.45)',
      badgeColor: '#86efac',
      icon: '🎫',
      homePage: 'index.html'
    },
    GATE_STAFF: {
      label: 'Nhân viên cổng',
      badgeBg: 'rgba(245, 158, 11, 0.16)',
      badgeBorder: 'rgba(245, 158, 11, 0.45)',
      badgeColor: '#fcd34d',
      icon: '🚪',
      homePage: 'gate-checkin.html'
    },
    ADMIN: {
      label: 'Ban tổ chức / Admin',
      badgeBg: 'rgba(124, 58, 237, 0.22)',
      badgeBorder: 'rgba(167, 139, 250, 0.5)',
      badgeColor: '#d8b4fe',
      icon: '🛡️',
      homePage: 'admin-dashboard.html'
    }
  };

  function getSession() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw);
      if (!parsed || !parsed.token || !parsed.userId) return null;
      return parsed;
    } catch {
      return null;
    }
  }

  function saveSession(authData) {
    const session = {
      token: authData.token,
      userId: authData.userId,
      email: authData.email,
      fullName: authData.fullName || authData.email,
      role: (authData.role || 'CUSTOMER').toUpperCase()
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    localStorage.setItem('passportal_userId', session.userId);
    return session;
  }

  function logout(redirectToLogin = true) {
    localStorage.removeItem(STORAGE_KEY);
    localStorage.removeItem('passportal_userId');
    if (redirectToLogin) {
      window.location.href = 'login.html';
    } else {
      window.location.reload();
    }
  }

  function getAuthHeaders() {
    const session = getSession();
    const headers = { 'Content-Type': 'application/json' };
    if (session && session.token) {
      headers['Authorization'] = `Bearer ${session.token}`;
    }
    return headers;
  }

  function getRoleHomePage(role) {
    const key = (role || 'CUSTOMER').toUpperCase();
    return ROLE_META[key]?.homePage || 'index.html';
  }

  function getCurrentFileName() {
    const path = window.location.pathname;
    const file = path.substring(path.lastIndexOf('/') + 1);
    return file || 'index.html';
  }

  function renderRoleNavbar() {
    const nav = document.querySelector('.topbar .nav');
    const navActions = document.querySelector('.topbar .nav-actions');
    const session = getSession();
    const currentFile = getCurrentFileName();

    // 1. Xây dựng danh sách menu điều hướng dựa theo Role
    if (nav) {
      let links = [];

      if (!session) {
        links = [
          { href: 'index.html', text: 'Trang chủ' },
          { href: 'all-events.html', text: 'Sự kiện' },
          { href: 'event-calendar.html', text: 'Lịch sự kiện' },
          { href: 'seat-selection.html', text: 'Chọn ghế' }
        ];
      } else if (session.role === 'CUSTOMER') {
        links = [
          { href: 'index.html', text: 'Trang chủ' },
          { href: 'event-calendar.html', text: 'Lịch sự kiện' },
          { href: 'seat-selection.html', text: 'Chọn ghế' },
          { href: 'my-tickets.html', text: 'Ví vé của tôi' }
        ];
      } else if (session.role === 'GATE_STAFF') {
        links = [
          { href: 'index.html', text: 'Trang chủ' },
          { href: 'gate-checkin.html', text: 'Cổng Check-in' }
        ];
      } else if (session.role === 'ADMIN') {
        links = [
          { href: 'index.html', text: 'Trang chủ' },
          { href: 'event-calendar.html', text: 'Lịch sự kiện' },
          { href: 'gate-checkin.html', text: 'Cổng Check-in' },
          { href: 'admin-dashboard.html', text: 'Quản trị & Báo cáo' }
        ];
      }

      nav.innerHTML = links
        .map(
          (l) =>
            `<a href="${l.href}" class="${currentFile === l.href ? 'active' : ''}">${l.text}</a>`
        )
        .join('');
    }

    // 2. Xây dựng cụm tài khoản / nút Đăng nhập ở góc phải
    if (navActions) {
      if (!session) {
        navActions.innerHTML = `
          <a href="login.html" class="btn btn-secondary" style="padding: 9px 15px; font-size: 0.9rem;">Đăng nhập</a>
          <a href="login.html?tab=register" class="btn btn-primary" style="padding: 9px 15px; font-size: 0.9rem;">Đăng ký</a>
        `;
      } else {
        const meta = ROLE_META[session.role] || ROLE_META.CUSTOMER;
        navActions.innerHTML = `
          <div style="display:flex; align-items:center; gap:10px; flex-wrap:wrap;">
            <span style="display:inline-flex; align-items:center; gap:6px; padding:5px 10px; border-radius:999px; font-size:0.78rem; font-weight:700; background:${meta.badgeBg}; border:1px solid ${meta.badgeBorder}; color:${meta.badgeColor};">
              ${meta.icon} ${meta.label}
            </span>
            <span style="font-size:0.88rem; color:#e2e8f0; font-weight:600;">${escapeHtml(session.fullName)}</span>
            <button id="globalLogoutBtn" class="btn btn-secondary" style="padding:7px 12px; font-size:0.82rem;">Đăng xuất</button>
          </div>
        `;
        document.getElementById('globalLogoutBtn')?.addEventListener('click', () => logout(true));
      }
    }
  }

  /**
   * Kiểm tra quyền truy cập trang. Trả về true nếu hợp lệ, false nếu đã chặn giao diện.
   */
  function requireRole(allowedRoles) {
    const session = getSession();
    const mainElem = document.querySelector('main');
    const currentUrl = encodeURIComponent(getCurrentFileName() + window.location.search);

    if (!session) {
      if (mainElem) {
        mainElem.innerHTML = `
          <section class="section">
            <div class="container" style="max-width: 560px; text-align: center;">
              <div class="panel" style="padding: 40px 32px;">
                <div style="font-size: 3rem; margin-bottom: 12px;">🔐</div>
                <h2 style="margin: 0 0 12px; font-size: 1.65rem;">Yêu cầu đăng nhập</h2>
                <p style="color: #94a3b8; line-height: 1.6; margin-bottom: 24px;">
                  Vui lòng đăng nhập tài khoản để truy cập chức năng này. Hệ thống phân quyền riêng biệt cho <strong>Người mua vé</strong>, <strong>Nhân viên soát vé</strong> và <strong>Ban tổ chức (Admin)</strong>.
                </p>
                <div style="display:flex; gap:12px; justify-content:center; flex-wrap:wrap;">
                  <a href="login.html?returnUrl=${currentUrl}" class="btn btn-primary">Đăng nhập ngay</a>
                  <a href="index.html" class="btn btn-secondary">Về trang chủ</a>
                </div>
              </div>
            </div>
          </section>
        `;
      }
      return false;
    }

    const upperAllowed = allowedRoles.map((r) => r.toUpperCase());
    if (!upperAllowed.includes(session.role)) {
      const currentMeta = ROLE_META[session.role] || ROLE_META.CUSTOMER;
      const requiredLabels = upperAllowed.map((r) => ROLE_META[r]?.label || r).join(' hoặc ');

      if (mainElem) {
        mainElem.innerHTML = `
          <section class="section">
            <div class="container" style="max-width: 600px; text-align: center;">
              <div class="panel" style="padding: 40px 32px; border-color: rgba(239, 68, 68, 0.45);">
                <div style="font-size: 3rem; margin-bottom: 12px;">🚫</div>
                <div class="kicker" style="color:#fca5a5; border-color:rgba(239,68,68,0.4); background:rgba(239,68,68,0.12); margin-bottom:12px;">
                  HTTP 403 • Phân quyền truy cập (RBAC)
                </div>
                <h2 style="margin: 0 0 12px; font-size: 1.65rem; color: #fecaca;">Không đủ quyền truy cập trang này</h2>
                <p style="color: #94a3b8; line-height: 1.6; margin-bottom: 24px;">
                  Tài khoản hiện tại của bạn đang mang vai trò <strong>${currentMeta.icon} ${currentMeta.label}</strong>.<br/>
                  Trang này chỉ dành riêng cho vai trò: <strong style="color:#f8fafc;">${requiredLabels}</strong>.
                </p>
                <div style="display:flex; gap:12px; justify-content:center; flex-wrap:wrap;">
                  <a href="${currentMeta.homePage}" class="btn btn-primary">Về trang làm việc của bạn</a>
                  <button id="switchRoleAccountBtn" class="btn btn-secondary">Đổi sang tài khoản khác</button>
                </div>
              </div>
            </div>
          </section>
        `;
        document.getElementById('switchRoleAccountBtn')?.addEventListener('click', () => logout(true));
      }
      return false;
    }

    return true;
  }

  function escapeHtml(str) {
    return String(str || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  window.PassPortalAuth = {
    ROLE_META,
    getSession,
    saveSession,
    logout,
    getAuthHeaders,
    getRoleHomePage,
    requireRole,
    renderRoleNavbar
  };

  document.addEventListener('DOMContentLoaded', renderRoleNavbar);
})();
