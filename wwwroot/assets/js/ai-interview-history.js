(() => {
    'use strict';
    const app = document.getElementById('aivApp');
    if (!app) return;
    const get = name => document.getElementById('aiv' + name);
    const chat = get('Chat'), form = get('Form'), answer = get('Answer');
    const level = get('Level'), errorBox = get('Error'), list = get('HistoryList');
    let jobId = Number(app.dataset.jobId), sessionId = null, version = null;
    let answersCount = 0, completed = false, busy = false, pageNumber = 1, sessions = [];
    function showError(error) { errorBox.textContent = error.message || String(error); errorBox.hidden = false; }
    function syncControls() {
        app.querySelectorAll('button').forEach(button => button.disabled = busy);
        level.disabled = busy || !!sessionId;
        answer.disabled = busy || !sessionId || completed;
        get('Start').hidden = !!sessionId;
        get('AnswerGroup').hidden = !sessionId || completed;
        get('Finished').hidden = !completed;
        get('Progress').textContent = !sessionId ? 'Chưa bắt đầu'
            : completed ? 'Đã hoàn thành · 5 / 5 câu' : `Đã trả lời ${answersCount} / 5 câu`;
    }
    function setBusy(value, text = '') { busy = value; get('Status').textContent = text; syncControls(); }
    function counter() { get('Counter').textContent = `${answer.value.length} / 2000`; }
    async function request(endpoint, options = {}, parameters = {}) {
        const url = new URL(endpoint, location.origin);
        Object.entries(parameters).forEach(([key, value]) => { if (value != null) url.searchParams.set(key, value); });
        const response = await fetch(url, { credentials: 'same-origin', ...options });
        if (response.redirected || response.status === 401) throw new Error('Phiên đăng nhập đã hết. Vui lòng đăng nhập lại.');
        const data = await response.json().catch(() => null);
        if (!response.ok || !data) throw new Error(data?.error || 'Không tải được dữ liệu. Kiểm tra kết nối và bảng lịch sử luyện tập.');
        return data;
    }
    function headers() { return { 'Content-Type': 'application/json',
        'RequestVerificationToken': form.querySelector('[name="__RequestVerificationToken"]').value }; }
    function message(role, content) {
        const row = document.createElement('div'); row.className = 'aiv-message' + (role === 'user' ? ' is-user' : '');
        const avatar = document.createElement('div'); avatar.className = 'aiv-avatar'; avatar.textContent = role === 'user' ? 'Bạn' : 'AI';
        const body = document.createElement('div'); body.className = 'aiv-body';
        const label = document.createElement('strong'); label.textContent = role === 'user' ? 'Câu trả lời của bạn' : 'AI phỏng vấn';
        const bubble = document.createElement('div'); bubble.className = 'aiv-bubble'; bubble.textContent = content;
        body.append(label, bubble); row.append(avatar, body); return row;
    }
    function welcome() {
        const box = document.createElement('div'); box.className = 'aiv-welcome';
        const icon = document.createElement('span'); icon.textContent = '🎤';
        const title = document.createElement('h3'); title.textContent = 'Sẵn sàng cho buổi phỏng vấn?';
        const note = document.createElement('p'); note.textContent = 'Chọn mức kinh nghiệm rồi bấm Bắt đầu luyện tập. Hoặc mở một buổi trong lịch sử để xem lại và tiếp tục.';
        box.append(icon, title, note); chat.replaceChildren(box);
    }
    function updateUrl() {
        const url = new URL(app.dataset.index, location.origin);
        if (sessionId) url.searchParams.set('sessionId', sessionId);
        else url.searchParams.set('jobPostId', jobId);
        history.replaceState(null, '', url.pathname + url.search);
    }
    function renderHistory() {
        list.replaceChildren();
        sessions.forEach(item => {
            const row = document.createElement('div'); row.className = 'aiv-history-item';
            if (item.sessionId === sessionId) row.classList.add('is-selected');
            const open = document.createElement('button'); open.type = 'button'; open.className = 'aiv-history-open';
            open.title = item.jobTitle; open.disabled = busy;
            if (item.sessionId === sessionId) open.setAttribute('aria-current', 'true');
            const heading = document.createElement('strong'); heading.textContent = item.jobTitle;
            const detail = document.createElement('small'); detail.textContent = item.levelName + ' · ' + new Date(item.updatedAtUtc).toLocaleDateString('vi-VN');
            const progress = document.createElement('em'); progress.textContent = item.isCompleted ? '✓ Hoàn thành' : `Đang luyện · ${item.answersCount}/5`;
            if (item.isCompleted) progress.className = 'is-done';
            open.append(heading, detail, progress); open.addEventListener('click', () => openSession(item.sessionId));
            const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'aiv-delete';
            remove.textContent = '×'; remove.disabled = busy; remove.setAttribute('aria-label', 'Xóa buổi luyện: ' + item.jobTitle);
            remove.addEventListener('click', () => deleteSession(item));
            row.append(open, remove); list.append(row);
        });
        get('HistoryEmpty').hidden = sessions.length !== 0;
    }
    async function loadHistory(append = false) {
        const page = append ? pageNumber + 1 : 1;
        const data = await request(app.dataset.history, {}, { page }); pageNumber = page;
        if (append) {
            const ids = new Set(sessions.map(item => item.sessionId));
            sessions.push(...data.items.filter(item => !ids.has(item.sessionId)));
        } else sessions = data.items;
        get('More').hidden = !data.hasMore; renderHistory();
    }
    async function openSession(id) {
        if (busy) return;
        setBusy(true, 'Đang mở buổi luyện tập…'); errorBox.hidden = true;
        try {
            const data = await request(app.dataset.session, {}, { id });
            sessionId = data.sessionId; version = data.version; jobId = data.jobPostId;
            answersCount = data.answersCount; completed = data.isCompleted;
            get('JobTitle').textContent = data.jobTitle; get('Company').textContent = data.companyName;
            level.value = data.level; answer.value = ''; counter();
            chat.replaceChildren(...data.items.map(turn => message(turn.role, turn.content)));
            chat.scrollTop = chat.scrollHeight; renderHistory(); updateUrl();
        } catch (error) { showError(error); }
        finally { setBusy(false); }
    }
    function newSession() {
        sessionId = null; version = null; answersCount = 0; completed = false;
        answer.value = ''; counter(); welcome(); errorBox.hidden = true;
        renderHistory(); syncControls(); updateUrl();
    }
    async function sendAnswer(text) {
        if (busy || completed || (sessionId && !text)) return;
        const starting = !sessionId;
        setBusy(true, starting ? 'AI đang chuẩn bị câu hỏi đầu tiên…' : 'AI đang nhận xét câu trả lời…');
        errorBox.hidden = true; chat.querySelector('.aiv-welcome')?.remove();
        const pending = text ? message('user', text) : null;
        if (pending) chat.append(pending);
        const typing = message('assistant', 'Đang chuẩn bị phản hồi…'); chat.append(typing); chat.scrollTop = chat.scrollHeight;
        try {
            const data = await request(app.dataset.chat, { method: 'POST', headers: headers(),
                body: JSON.stringify({ jobPostId: jobId, level: level.value, sessionId, version, answer: text }) });
            if (!data.reply || !data.sessionId) throw new Error('Chưa nhận được phản hồi đã lưu.');
            sessionId = data.sessionId; version = data.version; answersCount = data.answersCount; completed = data.finished;
            typing.remove(); chat.append(message('assistant', data.reply));
            answer.value = ''; counter(); chat.scrollTop = chat.scrollHeight; updateUrl();
            try { await loadHistory(); }
            catch { showError(new Error('Buổi luyện đã được lưu. Bấm tải lại lịch sử để cập nhật danh sách.')); }
        } catch (error) {
            typing.remove(); pending?.remove(); if (!chat.childElementCount) welcome(); showError(error);
        } finally { setBusy(false); if (sessionId && !completed) answer.focus(); }
    }
    async function deleteSession(item) {
        if (busy || !confirm('Xóa buổi luyện “' + item.jobTitle + '” và toàn bộ nội dung?')) return;
        setBusy(true, 'Đang xóa lịch sử…'); errorBox.hidden = true;
        try {
            await request(app.dataset.delete, { method: 'POST', headers: headers() }, { id: item.sessionId });
            if (item.sessionId === sessionId) newSession();
            await loadHistory();
        } catch (error) { showError(error); }
        finally { setBusy(false); }
    }
    async function refresh(append = false) {
        if (busy) return;
        setBusy(true, 'Đang tải lịch sử…'); errorBox.hidden = true;
        try { await loadHistory(append); }
        catch (error) { showError(error); }
        finally { setBusy(false); }
    }
    get('Start').addEventListener('click', () => sendAnswer(null));
    get('New').addEventListener('click', () => { if (!busy) newSession(); });
    get('Refresh').addEventListener('click', () => refresh()); get('More').addEventListener('click', () => refresh(true));
    answer.addEventListener('input', counter);
    answer.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && event.key === 'Enter' && !event.isComposing) { event.preventDefault(); form.requestSubmit(); }
    });
    form.addEventListener('submit', event => {
        event.preventDefault(); const text = answer.value.trim();
        if (text && sessionId) sendAnswer(text);
    });
    async function initialize() {
        welcome(); syncControls(); counter(); await refresh();
        if (app.dataset.initialSession) await openSession(app.dataset.initialSession);
    }
    initialize();
})();
