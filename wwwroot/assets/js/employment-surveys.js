(() => {
    'use strict';
    const form = document.getElementById('es-answer-form');
    if (!form) return;
    const status = document.getElementById('es-employment-status');
    const work = document.getElementById('es-work-fields');
    if (!status || !work) return;
    function update() {
        const employed = status.value === 'Employed';
        work.hidden = !employed;
        // Giữ dữ liệu đang nhập khi đổi lựa chọn; phía server xóa phần không áp dụng khi lưu.
        for (const input of work.querySelectorAll('input,select')) {
            input.disabled = !employed;
            input.required = employed && input.name !== 'Input.SalaryRange';
        }
    }
    status.addEventListener('change', update);
    update();
})();
