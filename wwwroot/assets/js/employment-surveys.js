(() => {
    'use strict';

    /* ---------- Trang danh sách: lọc nhanh theo trạng thái ---------- */
    const filters = document.querySelectorAll('[data-es-filter]');
    if (filters.length) {
        const cards = Array.from(document.querySelectorAll('.es-card'));
        const emptyMsg = document.getElementById('es-filter-empty');
        const match = {
            all: () => true,
            open: c => c.dataset.open === '1',
            pending: c => c.dataset.done === '0',
            done: c => c.dataset.done === '1'
        };
        filters.forEach(btn => btn.addEventListener('click', () => {
            const test = match[btn.dataset.esFilter] || match.all;
            filters.forEach(b => {
                const active = b === btn;
                b.classList.toggle('is-active', active);
                b.setAttribute('aria-pressed', active);
            });
            let shown = 0;
            cards.forEach(c => { const ok = test(c); c.hidden = !ok; if (ok) shown++; });
            if (emptyMsg) emptyMsg.hidden = shown !== 0;
        }));
    }

    /* ---------- Trang trả lời khảo sát ---------- */
    const form = document.getElementById('es-answer-form');
    if (!form) return;

    const status = document.getElementById('es-employment-status');
    const work = document.getElementById('es-work-fields');

    if (status && work) {
        let first = true;
        const update = () => {
            const employed = status.value === 'Employed';
            const wasHidden = work.hidden;
            work.hidden = !employed;
            status.setAttribute('aria-expanded', employed);
            // Giữ dữ liệu đang nhập khi đổi lựa chọn; phía server xóa phần không áp dụng khi lưu.
            for (const input of work.querySelectorAll('input,select')) {
                input.disabled = !employed;
                input.required = employed && input.name !== 'Input.SalaryRange';
            }
            if (employed && wasHidden && !first) {
                work.classList.remove('es-reveal');
                void work.offsetWidth; // restart animation
                work.classList.add('es-reveal');
            }
            first = false;
        };
        status.addEventListener('change', update);
        update();
    }

    /* Bộ đếm ký tự cho ô ghi chú */
    const notes = form.querySelector('textarea');
    const counter = document.getElementById('es-notes-count');
    if (notes && counter) {
        const max = parseInt(notes.getAttribute('maxlength') || '1000', 10);
        const box = counter.parentElement;
        const sync = () => {
            counter.textContent = notes.value.length;
            box.classList.toggle('is-near', notes.value.length >= max * 0.9);
        };
        notes.addEventListener('input', sync);
        sync();
    }

    /* Chống bấm gửi nhiều lần + hiệu ứng đang gửi */
    const submit = form.querySelector('button[type="submit"]');
    let locked = false;
    const unlock = () => {
        locked = false;
        if (submit) { submit.classList.remove('is-loading'); submit.removeAttribute('aria-busy'); }
    };
    form.addEventListener('submit', e => {
        if (locked) { e.preventDefault(); return; }
        if (!form.checkValidity()) return;
        locked = true;
        if (submit) { submit.classList.add('is-loading'); submit.setAttribute('aria-busy', 'true'); }
        // Nếu jquery-validation chặn gửi, mở khóa lại
        setTimeout(() => { if (form.querySelector('.input-validation-error')) unlock(); }, 80);
    });
    window.addEventListener('pageshow', unlock);

    /* Cuộn tới lỗi đầu tiên khi server trả lại form có lỗi */
    const firstError = form.querySelector('.validation-summary-errors, .input-validation-error');
    if (firstError) firstError.scrollIntoView({ behavior: 'smooth', block: 'center' });

    /* Thông báo thành công: tự cuộn lên đầu cho dễ thấy */
    const ok = document.querySelector('.es-success');
    if (ok) ok.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
})();
