(() => {
    'use strict';
    const app = document.getElementById('aichat-app');
    if (!app) return;
    const get = id => document.getElementById('aichat-' + id);
    const form = get('form'), question = get('question'), log = get('log');
    const timeline = get('timeline'), errorBox = get('error'), title = get('title');
    const list = get('history-list'), older = get('older'), more = get('more-history');
    const initialTitle = title.textContent;
    let conversationId = null, firstMessageId = null, busy = false, historyPage = 1;
    let conversations = [];

    function showError(error) {
        errorBox.textContent = error instanceof Error ? error.message : String(error);
        errorBox.hidden = false;
    }
    function setBusy(value, text = '') {
        busy = value;
        app.querySelectorAll('button').forEach(button => button.disabled = value);
        question.disabled = value;
        get('status').textContent = value ? text : 'Enter để gửi · Shift + Enter để xuống dòng';
    }
    async function request(endpoint, options = {}, parameters = {}) {
        const url = new URL(endpoint, location.origin);
        Object.entries(parameters).forEach(([key, value]) => {
            if (value !== null && value !== undefined) url.searchParams.set(key, value);
        });
        const response = await fetch(url, { credentials: 'same-origin', ...options });
        if (response.redirected || response.status === 401)
            throw new Error('Phiên đăng nhập đã hết. Vui lòng đăng nhập lại.');
        const data = await response.json().catch(() => null);
        if (!response.ok || !data)
            throw new Error(data?.error || 'Không tải được dữ liệu. Kiểm tra kết nối và cấu hình bảng lịch sử.');
        return data;
    }
    const headers = () => ({ 'Content-Type': 'application/json',
        'RequestVerificationToken': form.querySelector('[name="__RequestVerificationToken"]').value });
    function fit() {
        question.style.height = 'auto';
        question.style.height = Math.min(question.scrollHeight, 150) + 'px';
        get('count').textContent = `${question.value.length} / 1500`;
    }
    function welcome() {
        log.replaceChildren();
        const wrapper = document.createElement('div'); wrapper.className = 'aichat-welcome';
        const icon = document.createElement('div'); icon.className = 'aichat-welcome-icon'; icon.textContent = '✦';
        const heading = document.createElement('h3'); heading.textContent = 'Hôm nay, bạn đang tìm cơ hội nào?';
        const text = document.createElement('p');
        text.textContent = 'Mình có thể tra cứu doanh nghiệp, công việc, mức lương và yêu cầu tuyển dụng trên AlumniJobConnect. Bạn chỉ cần đặt câu hỏi.';
        const tags = document.createElement('div'); tags.className = 'aichat-capabilities';
        ['Thông tin doanh nghiệp', 'Tìm việc phù hợp', 'Yêu cầu tuyển dụng'].forEach(value => {
            const tag = document.createElement('span'); tag.textContent = value; tags.append(tag);
        });
        wrapper.append(icon, heading, text, tags); log.append(wrapper);
    }
    function makeMessage(role, content, sources = [], timestamp = null) {
        const row = document.createElement('div');
        row.className = 'aichat-message' + (role === 'user' ? ' is-user' : '');
        const avatar = document.createElement('div'); avatar.className = 'aichat-message-avatar';
        avatar.textContent = role === 'user' ? 'BẠN' : '✦'; avatar.setAttribute('aria-hidden', 'true');
        const body = document.createElement('div'); body.className = 'aichat-message-body';
        const label = document.createElement('span'); label.className = 'aichat-message-label';
        label.textContent = role === 'user' ? 'Bạn' : 'Trợ lý AI';
        if (timestamp) {
            const date = new Date(timestamp);
            if (!Number.isNaN(date.getTime())) label.textContent += ' · ' + date.toLocaleString('vi-VN', {
                hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit'
            });
        }
        const bubble = document.createElement('div'); bubble.className = 'aichat-message-bubble';
        bubble.textContent = content; body.append(label, bubble);
        const links = document.createElement('div'); links.className = 'aichat-sources';
        if (Array.isArray(sources)) sources.forEach(source => {
            try {
                const url = new URL(source.url ?? source.Url, location.origin);
                if (url.origin !== location.origin ||
                    !/^\/(Jobs|CompanyProfile)\/Details(?:\/|\?)/i.test(url.pathname + url.search)) return;
                const link = document.createElement('a'); link.href = url.href;
                link.textContent = '↗ ' + (source.label ?? source.Label ?? 'Xem nguồn'); links.append(link);
            } catch { /* Bỏ đường dẫn không hợp lệ. */ }
        });
        if (links.childElementCount) body.append(links);
        row.append(avatar, body); return row;
    }
    function renderHistory() {
        list.replaceChildren();
        conversations.forEach(item => {
            const row = document.createElement('div'); row.className = 'aichat-history-row';
            if (item.conversationId === conversationId) row.classList.add('is-active');
            const open = document.createElement('button'); open.type = 'button';
            open.className = 'aichat-history-open'; open.title = item.title;
            if (item.conversationId === conversationId) open.setAttribute('aria-current', 'true');
            const heading = document.createElement('strong'); heading.textContent = item.title;
            const date = document.createElement('small');
            date.textContent = new Date(item.updatedAtUtc).toLocaleString('vi-VN', {
                hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit', year: 'numeric'
            });
            open.append(heading, date); open.disabled = busy;
            open.addEventListener('click', () => openConversation(item.conversationId));
            const remove = document.createElement('button'); remove.type = 'button';
            remove.className = 'aichat-history-delete'; remove.textContent = '×';
            remove.title = 'Xóa cuộc trò chuyện'; remove.setAttribute('aria-label', 'Xóa: ' + item.title);
            remove.disabled = busy; remove.addEventListener('click', () => deleteConversation(item));
            row.append(open, remove); list.append(row);
        });
        get('history-empty').hidden = conversations.length !== 0;
    }
    async function loadHistory(append = false) {
        const page = append ? historyPage + 1 : 1;
        const data = await request(app.dataset.history, {}, { page });
        historyPage = page;
        if (append) {
            const ids = new Set(conversations.map(c => c.conversationId));
            conversations.push(...data.items.filter(item => !ids.has(item.conversationId)));
        } else conversations = data.items;
        more.hidden = !data.hasMore; renderHistory();
    }
    function resetConversation() {
        conversationId = null; firstMessageId = null; older.hidden = true;
        title.textContent = initialTitle; question.value = ''; errorBox.hidden = true;
        welcome(); renderHistory(); fit(); timeline.scrollTop = 0;
    }
    async function openConversation(id) {
        if (busy) return;
        setBusy(true, 'Đang mở cuộc trò chuyện…'); errorBox.hidden = true;
        try {
            const data = await request(app.dataset.messages, {}, { id });
            conversationId = data.conversationId; title.textContent = data.title;
            firstMessageId = data.items[0]?.messageId ?? null;
            log.replaceChildren(...data.items.map(m => makeMessage(m.role, m.content, m.sources, m.createdAtUtc)));
            older.hidden = !data.hasMore; question.value = ''; fit(); renderHistory();
            timeline.scrollTop = timeline.scrollHeight;
        } catch (error) { showError(error); }
        finally { setBusy(false); }
    }
    async function deleteConversation(item) {
        if (busy || !confirm('Xóa cuộc trò chuyện “' + item.title + '”? Các tin nhắn trong cuộc này sẽ bị xóa.')) return;
        setBusy(true, 'Đang xóa cuộc trò chuyện…'); errorBox.hidden = true;
        try {
            await request(app.dataset.delete, { method: 'POST', headers: headers() }, { id: item.conversationId });
            if (conversationId === item.conversationId) resetConversation();
            await loadHistory();
        } catch (error) { showError(error); }
        finally { setBusy(false); }
    }
    form.addEventListener('submit', async event => {
        event.preventDefault();
        const message = question.value.trim();
        if (busy || !message) return;
        setBusy(true, 'Đang tra cứu và soạn câu trả lời…'); errorBox.hidden = true;
        const emptyWelcome = log.querySelector('.aichat-welcome');
        if (emptyWelcome) emptyWelcome.remove();
        const pending = makeMessage('user', message);
        const typing = makeMessage('assistant', 'Đang tra cứu dữ liệu…');
        log.append(pending, typing); timeline.scrollTop = timeline.scrollHeight;
        try {
            const data = await request(app.dataset.send, { method: 'POST', headers: headers(),
                body: JSON.stringify({ message, conversationId }) });
            if (!data.reply || !data.conversationId) throw new Error('Chưa nhận được câu trả lời đã lưu.');
            conversationId = data.conversationId; title.textContent = data.title;
            typing.remove(); log.append(makeMessage('assistant', data.reply, data.sources));
            question.value = ''; fit(); timeline.scrollTop = timeline.scrollHeight;
            // Nếu tải danh sách thất bại, không gửi lại câu hỏi đã được lưu thành công.
            try { await loadHistory(); }
            catch { showError('Câu trả lời đã được lưu. Bấm tải lại lịch sử nếu danh sách chưa cập nhật.'); }
        } catch (error) {
            pending.remove(); typing.remove(); if (!log.childElementCount) welcome();
            showError(error);
        } finally { setBusy(false); question.focus(); }
    });
    older.addEventListener('click', async () => {
        if (busy || !conversationId || firstMessageId === null) return;
        setBusy(true, 'Đang tải tin nhắn trước…'); errorBox.hidden = true;
        try {
            const data = await request(app.dataset.messages, {}, { id: conversationId, beforeId: firstMessageId });
            const previousHeight = timeline.scrollHeight;
            log.prepend(...data.items.map(m => makeMessage(m.role, m.content, m.sources, m.createdAtUtc)));
            firstMessageId = data.items[0]?.messageId ?? firstMessageId;
            older.hidden = !data.hasMore;
            timeline.scrollTop += timeline.scrollHeight - previousHeight;
        } catch (error) { showError(error); }
        finally { setBusy(false); }
    });
    async function reload(append = false) {
        if (busy) return;
        setBusy(true, 'Đang tải lịch sử…'); errorBox.hidden = true;
        try { await loadHistory(append); }
        catch (error) { showError(error); }
        finally { setBusy(false); }
    }
    get('refresh').addEventListener('click', () => reload());
    more.addEventListener('click', () => reload(true));
    get('new').addEventListener('click', () => { if (!busy) { resetConversation(); question.focus(); } });
    question.addEventListener('input', fit);
    question.addEventListener('keydown', event => {
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
            event.preventDefault(); form.requestSubmit();
        }
    });
    app.querySelectorAll('[data-question]').forEach(button => button.addEventListener('click', () => {
        if (busy) return;
        question.value = button.dataset.question; fit(); question.focus();
    }));
    welcome(); fit(); reload();
})();
