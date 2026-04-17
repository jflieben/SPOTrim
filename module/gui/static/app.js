// SPOTrim — GUI Application
'use strict';

const state = {
    connected: false,
    scanning: false,
    activeScanId: null,
    tenantId: null,
    tenantDomain: null,
    version: null,
    refreshTokenExpiry: null,
    currentPage: 'dashboard',
    pollTimer: null,
    scans: [],
    selectedScanId: null,
};

// ───── API helpers ─────

async function api(method, path, body) {
    const opts = { method, headers: {} };
    if (body) {
        opts.headers['Content-Type'] = 'application/json';
        opts.body = JSON.stringify(body);
    }
    const res = await fetch('/api' + path, opts);
    if (res.status === 204) return null;
    if (path.includes('/export')) return res;
    return res.json();
}

const apiGet = (p) => api('GET', p);
const apiPost = (p, b) => api('POST', p, b);
const apiPut = (p, b) => api('PUT', p, b);

function tenantQuery(sep = '?') {
    return state.tenantId ? `${sep}tenantId=${encodeURIComponent(state.tenantId)}` : '';
}

// ───── DOM helpers ─────

const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => document.querySelectorAll(sel);
const show = (el) => el && (el.style.display = '');
const hide = (el) => el && (el.style.display = 'none');
function setText(sel, text) { const el = $(sel); if (el) el.textContent = text ?? ''; }
function setHTML(sel, html) { const el = $(sel); if (el) el.innerHTML = html; }

function formatBytes(bytes) {
    if (!bytes || bytes === 0) return '0 B';
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return (bytes / Math.pow(1024, i)).toFixed(1) + ' ' + sizes[i];
}

function formatDate(iso) {
    if (!iso) return '—';
    return new Date(iso).toLocaleString();
}

function formatDuration(start, end) {
    if (!start) return '—';
    const s = new Date(start);
    const e = end ? new Date(end) : new Date();
    const sec = Math.floor((e - s) / 1000);
    if (sec < 60) return sec + 's';
    if (sec < 3600) return Math.floor(sec / 60) + 'm ' + (sec % 60) + 's';
    return Math.floor(sec / 3600) + 'h ' + Math.floor((sec % 3600) / 60) + 'm';
}

// ───── Navigation ─────

function navigateTo(page) {
    state.currentPage = page;
    $$('.nav-btn').forEach(b => b.classList.toggle('active', b.dataset.page === page));
    $$('.page').forEach(p => p.classList.toggle('active', p.id === 'page-' + page));

    if (page === 'dashboard') loadDashboard();
    if (page === 'scan') loadScanPage();
    if (page === 'sites') loadSitesPage();
    if (page === 'settings') loadSettingsPage();
    if (page === 'audit') loadAuditPage();
}

// ───── Status polling ─────

async function pollStatus() {
    try {
        const data = await apiGet('/status');
        if (!data) return;

        state.connected = data.connected;
        state.scanning = data.scanning;
        state.activeScanId = data.activeScanId;
        state.tenantId = data.tenantId;
        state.tenantDomain = data.tenantDomain;
        state.version = data.version;
        state.refreshTokenExpiry = data.refreshTokenExpiry;

        updateConnectionUI();

        if (state.scanning && state.currentPage === 'scan') {
            await loadScanProgress();
        }
    } catch (e) {
        console.error('Status poll error:', e);
    }
}

function updateConnectionUI() {
    const badge = $('.connection-status');
    if (state.connected) {
        badge.className = 'connection-status connected';
        badge.textContent = state.tenantDomain || 'Connected';
        setText('#btn-connect', 'Disconnect');
    } else {
        badge.className = 'connection-status disconnected';
        badge.textContent = 'Not connected';
        setText('#btn-connect', 'Connect');
    }
    if (state.version) setText('.version', 'v' + state.version);

    const tokenInfo = $('#token-expiry');
    if (tokenInfo && state.refreshTokenExpiry) {
        tokenInfo.textContent = 'Token expires: ' + formatDate(state.refreshTokenExpiry);
    } else if (tokenInfo) {
        tokenInfo.textContent = '';
    }
}

// ───── Auth ─────

async function toggleConnection() {
    if (state.connected) {
        await apiPost('/disconnect');
    } else {
        await apiPost('/connect');
    }
    await pollStatus();
}

// ───── Dashboard ─────

async function loadDashboard() {
    try {
        const data = await apiGet('/dashboard' + tenantQuery());
        if (!data || !data.success) return;
        const s = data.data;
        setText('#stat-sites', s.totalSites ?? 0);
        setText('#stat-libraries', s.totalLibraries ?? 0);
        setText('#stat-storage', formatBytes(s.totalVersionBytes));
        setText('#stat-savings', formatBytes(s.potentialSavings));
        setText('#stat-scans', s.totalScans ?? 0);
        setText('#stat-cleanups', s.totalCleanups ?? 0);
    } catch (e) {
        console.error('Dashboard error:', e);
    }
}

// ───── Scan Page ─────

async function loadScanPage() {
    await loadScans();
    if (state.scanning) {
        await loadScanProgress();
        show($('#scan-progress'));
    } else {
        hide($('#scan-progress'));
    }
}

async function loadScans() {
    try {
        const data = await apiGet('/scans' + tenantQuery());
        if (!data || !data.success) return;
        state.scans = data.data || [];
        renderScansTable();
        renderScanSelector();
    } catch (e) {
        console.error('Load scans error:', e);
    }
}

function renderScansTable() {
    const tbody = $('#scans-table tbody');
    if (!tbody) return;
    if (state.scans.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6" style="text-align:center;color:var(--text-secondary)">No scans yet</td></tr>';
        return;
    }
    tbody.innerHTML = state.scans.map(s => `
        <tr>
            <td>${s.id}</td>
            <td><span class="badge badge-${statusBadge(s.status)}">${s.status}</span></td>
            <td>${s.scanType || '—'}</td>
            <td>${formatDate(s.startedAt)}</td>
            <td>${formatDuration(s.startedAt, s.completedAt)}</td>
            <td>${s.totalSites ?? 0} sites</td>
        </tr>
    `).join('');
}

function statusBadge(status) {
    switch (status?.toLowerCase()) {
        case 'completed': return 'success';
        case 'running': return 'info';
        case 'failed': return 'danger';
        case 'cancelled': return 'warning';
        default: return 'neutral';
    }
}

function renderScanSelector() {
    const sel = $('#scan-selector');
    if (!sel) return;
    const completed = state.scans.filter(s => s.status === 'Completed');
    sel.innerHTML = '<option value="">Select a scan…</option>' +
        completed.map(s => `<option value="${s.id}">Scan #${s.id} — ${formatDate(s.startedAt)}</option>`).join('');
}

async function startScan() {
    const scanType = $('#scan-type')?.value || 'Full';
    try {
        const data = await apiPost('/scan/start', { scanType });
        if (data?.success) {
            state.scanning = true;
            state.activeScanId = data.data;
            show($('#scan-progress'));
            await loadScanProgress();
        } else {
            alert('Failed to start scan: ' + (data?.error || 'Unknown error'));
        }
    } catch (e) {
        alert('Error starting scan: ' + e.message);
    }
}

async function cancelScan() {
    try {
        await apiPost('/scan/cancel');
        state.scanning = false;
        hide($('#scan-progress'));
        await loadScans();
    } catch (e) {
        console.error('Cancel scan error:', e);
    }
}

async function loadScanProgress() {
    try {
        const data = await apiGet('/scan/progress');
        if (!data || !data.success) return;
        const p = data.data;
        const pct = p.overallPercent ?? 0;
        const fill = $('#progress-fill');
        const text = $('#progress-text');
        if (fill) fill.style.width = pct + '%';
        if (text) text.textContent = `${p.currentPhase || 'Waiting'} — ${pct}%`;

        const logEl = $('#scan-log');
        if (logEl && p.recentLogs?.length) {
            logEl.textContent = p.recentLogs.join('\n');
            logEl.scrollTop = logEl.scrollHeight;
        }

        if (p.overallPercent >= 100 || !state.scanning) {
            state.scanning = false;
            hide($('#scan-progress'));
            await loadScans();
        }
    } catch (e) {
        console.error('Progress error:', e);
    }
}

// ───── Sites Page ─────

async function loadSitesPage() {
    const scanId = $('#scan-selector')?.value || state.scans.find(s => s.status === 'Completed')?.id;
    if (!scanId) {
        setHTML('#sites-table tbody', '<tr><td colspan="6" style="text-align:center;color:var(--text-secondary)">No completed scan to show</td></tr>');
        return;
    }
    try {
        const data = await apiGet(`/scans/${scanId}/sites?page=1&pageSize=200`);
        if (!data || !data.success) return;
        const sites = data.data || [];
        renderSitesTable(sites);
    } catch (e) {
        console.error('Load sites error:', e);
    }
}

function renderSitesTable(sites) {
    const tbody = $('#sites-table tbody');
    if (!tbody) return;
    if (sites.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6" style="text-align:center;color:var(--text-secondary)">No sites found</td></tr>';
        return;
    }
    tbody.innerHTML = sites.map(s => `
        <tr>
            <td title="${escapeHtml(s.url)}">${escapeHtml(s.title || s.url)}</td>
            <td><span class="badge badge-info">${s.siteType || '—'}</span></td>
            <td>${s.libraryCount ?? 0}</td>
            <td>${formatBytes(s.totalSize)}</td>
            <td>${formatBytes(s.versionSize)}</td>
            <td>${s.versioningEnabled ? '✓' : '✗'}</td>
        </tr>
    `).join('');
}

function escapeHtml(str) {
    if (!str) return '';
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}

// ───── Settings Page ─────

async function loadSettingsPage() {
    try {
        const data = await apiGet('/config');
        if (!data || !data.success) return;
        const c = data.data;
        setVal('#cfg-maxthreads', c.maxThreads);
        setVal('#cfg-versionlimit', c.defaultVersionLimit);
        setVal('#cfg-minorversionlimit', c.minorVersionLimit);
        setVal('#cfg-batchsize', c.cleanupBatchSize);
        setVal('#cfg-maxretries', c.maxJobRetries);
        setVal('#cfg-includeonedrive', c.includeOneDrive);
        setVal('#cfg-dryrun', c.dryRun);
        setVal('#cfg-outputformat', c.outputFormat);
        setVal('#cfg-loglevel', c.logLevel);
    } catch (e) {
        console.error('Load settings error:', e);
    }
}

function setVal(sel, val) {
    const el = $(sel);
    if (!el) return;
    if (el.type === 'checkbox') el.checked = !!val;
    else el.value = val ?? '';
}

function getVal(sel) {
    const el = $(sel);
    if (!el) return null;
    if (el.type === 'checkbox') return el.checked;
    if (el.type === 'number') return parseInt(el.value, 10);
    return el.value;
}

async function saveSettings() {
    const config = {
        maxThreads: getVal('#cfg-maxthreads'),
        defaultVersionLimit: getVal('#cfg-versionlimit'),
        minorVersionLimit: getVal('#cfg-minorversionlimit'),
        cleanupBatchSize: getVal('#cfg-batchsize'),
        maxJobRetries: getVal('#cfg-maxretries'),
        includeOneDrive: getVal('#cfg-includeonedrive'),
        dryRun: getVal('#cfg-dryrun'),
        outputFormat: getVal('#cfg-outputformat'),
        logLevel: getVal('#cfg-loglevel'),
    };
    try {
        const data = await apiPut('/config', config);
        if (data?.success) {
            alert('Settings saved');
        } else {
            alert('Failed: ' + (data?.error || 'Unknown'));
        }
    } catch (e) {
        alert('Error saving: ' + e.message);
    }
}

// ───── Export ─────

async function exportResults() {
    const scanId = $('#scan-selector')?.value;
    if (!scanId) { alert('Select a scan first'); return; }
    try {
        const res = await apiGet(`/scans/${scanId}/export?format=xlsx`);
        if (!res.ok) { alert('Export failed'); return; }
        const blob = await res.blob();
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `SPOTrim_Scan_${scanId}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
    } catch (e) {
        alert('Export error: ' + e.message);
    }
}

// ───── Audit Page ─────

async function loadAuditPage() {
    try {
        const data = await apiGet('/audit?limit=100' + tenantQuery('&'));
        if (!data || !data.success) return;
        const entries = data.data || [];
        const tbody = $('#audit-table tbody');
        if (!tbody) return;
        if (entries.length === 0) {
            tbody.innerHTML = '<tr><td colspan="4" style="text-align:center;color:var(--text-secondary)">No audit entries</td></tr>';
            return;
        }
        tbody.innerHTML = entries.map(e => `
            <tr>
                <td>${formatDate(e.timestamp)}</td>
                <td>${escapeHtml(e.action)}</td>
                <td>${escapeHtml(e.detail)}</td>
                <td>${escapeHtml(e.user)}</td>
            </tr>
        `).join('');
    } catch (e) {
        console.error('Audit error:', e);
    }
}

// ───── Initialization ─────

function init() {
    // Navigation
    $$('.nav-btn').forEach(btn => {
        btn.addEventListener('click', () => navigateTo(btn.dataset.page));
    });

    // Header connect button
    const connectBtn = $('#btn-connect');
    if (connectBtn) connectBtn.addEventListener('click', toggleConnection);

    // Scan controls
    const startBtn = $('#btn-start-scan');
    if (startBtn) startBtn.addEventListener('click', startScan);

    const cancelBtn = $('#btn-cancel-scan');
    if (cancelBtn) cancelBtn.addEventListener('click', cancelScan);

    // Export button
    const exportBtn = $('#btn-export');
    if (exportBtn) exportBtn.addEventListener('click', exportResults);

    // Settings save
    const saveBtn = $('#btn-save-settings');
    if (saveBtn) saveBtn.addEventListener('click', saveSettings);

    // Scan selector change
    const scanSel = $('#scan-selector');
    if (scanSel) scanSel.addEventListener('change', () => {
        state.selectedScanId = scanSel.value;
        if (state.currentPage === 'sites') loadSitesPage();
    });

    // Start polling
    pollStatus();
    state.pollTimer = setInterval(pollStatus, 3000);

    // Navigate to dashboard
    navigateTo('dashboard');
}

document.addEventListener('DOMContentLoaded', init);
