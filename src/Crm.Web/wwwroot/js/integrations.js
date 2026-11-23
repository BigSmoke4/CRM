/* Integration builder interactions: test connection, preview, auth fields, sync */
(function () {
    'use strict';

    // Show/hide auth fields by selected auth type
    const authSelect = document.getElementById('authType');
    function syncAuthFields() {
        if (!authSelect) return;
        const v = authSelect.value;
        document.querySelectorAll('.auth-field').forEach(f => {
            const allowed = (f.dataset.auth || '').split(',');
            f.style.display = allowed.includes(v) ? '' : 'none';
        });
    }
    if (authSelect) { authSelect.addEventListener('change', syncAuthFields); syncAuthFields(); }

    function token() {
        const i = document.querySelector('input[name="__RequestVerificationToken"]');
        return i ? i.value : '';
    }

    function showNotice(el, ok, html) {
        el.hidden = false;
        el.className = 'notice ' + (ok ? 'notice--ok' : 'notice--warn');
        el.innerHTML = html;
    }

    // Test connection (edit mode: saved config; create mode: draft payload)
    const btnTest = document.getElementById('btnTest');
    if (btnTest) {
        btnTest.addEventListener('click', async () => {
            btnTest.disabled = true; btnTest.textContent = 'Testing…';
            const resultEl = document.getElementById('testResult');
            try {
                const idInput = document.querySelector('input[name="Id"]');
                let res;
                if (idInput && idInput.value) {
                    res = await fetch(`/Integrations/TestConnection?id=${idInput.value}`, {
                        method: 'POST', headers: { 'RequestVerificationToken': token() }
                    });
                } else {
                    const payload = collectForm();
                    res = await fetch('/Integrations/TestDraft', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                        body: JSON.stringify(payload)
                    });
                }
                const data = await res.json();
                let html = `<strong>${data.Success ? 'Connected' : 'Failed'}</strong> — HTTP ${data.StatusCode} in ${data.DurationMs}ms`;
                if (data.Error) html += `<br><span>${escapeHtml(data.Error)}</span>`;
                if (data.Preview) html += `<details><summary>Response preview</summary><pre class="code-block">${escapeHtml(data.Preview.slice(0, 3000))}</pre></details>`;
                if (data.Structure) html += `<details><summary>JSON structure</summary><pre class="code-block">${escapeHtml(JSON.stringify(data.Structure, null, 2).slice(0, 4000))}</pre></details>`;
                showNotice(resultEl, data.Success, html);
            } catch (e) {
                showNotice(resultEl, false, 'Test request failed: ' + escapeHtml(e.message));
            } finally {
                btnTest.disabled = false; btnTest.textContent = 'Test connection';
            }
        });
    }

    // Sync now
    const btnSync = document.getElementById('btnSync');
    if (btnSync) {
        btnSync.addEventListener('click', async () => {
            const id = document.querySelector('input[name="Id"]').value;
            btnSync.disabled = true; btnSync.textContent = 'Syncing…';
            const el = document.getElementById('syncResult');
            try {
                const res = await fetch(`/Integrations/SyncNow?id=${id}`, {
                    method: 'POST', headers: { 'RequestVerificationToken': token() }
                });
                const d = await res.json();
                let html = `Sync finished: ${d.Fetched} fetched, ${d.Created} created, ${d.Updated} updated, ${d.Skipped} skipped, ${d.Failed} failed.`;
                if (d.Errors && d.Errors.length) html += `<br>${d.Errors.map(escapeHtml).join('<br>')}`;
                showNotice(el, d.Failed === 0, html);
                setTimeout(() => location.reload(), 2500);
            } catch (e) {
                showNotice(el, false, 'Sync failed: ' + escapeHtml(e.message));
            } finally {
                btnSync.disabled = false; btnSync.textContent = 'Sync now';
            }
        });
    }

    // Preview records
    const btnPreview = document.getElementById('btnPreview');
    if (btnPreview) {
        btnPreview.addEventListener('click', async () => {
            const idInput = document.querySelector('input[name="Id"]');
            if (!idInput || !idInput.value) {
                alert('Save the integration first, then preview records.');
                return;
            }
            btnPreview.disabled = true; btnPreview.textContent = 'Loading…';
            try {
                const res = await fetch(`/Integrations/PreviewRecords?id=${idInput.value}`, {
                    method: 'POST', headers: { 'RequestVerificationToken': token() }
                });
                const d = await res.json();
                const wrap = document.getElementById('previewResult');
                if (d.error) { alert(d.error); return; }
                document.getElementById('previewFields').innerHTML =
                    (d.fields || []).map(f => `<span class="chip">${escapeHtml(f)}</span>`).join('');
                document.getElementById('previewRecords').textContent =
                    JSON.stringify(d.records, null, 2).slice(0, 6000);
                wrap.hidden = false;
            } catch (e) { alert('Preview failed: ' + e.message); }
            finally { btnPreview.disabled = false; btnPreview.textContent = 'Preview records & fields'; }
        });
    }

    function collectForm() {
        const f = document.getElementById('integrationForm');
        const g = n => { const el = f.querySelector(`[name="${n}"]`); return el ? el.value : null; };
        const c = n => { const el = f.querySelector(`[name="${n}"]`); return el ? el.checked : false; };
        return {
            Name: g('Name'), ExternalSystem: g('ExternalSystem'), Description: g('Description'),
            BaseUrl: g('BaseUrl'), Endpoint: g('Endpoint'), HttpMethod: g('HttpMethod'),
            AuthType: parseInt(g('AuthType'), 10), ApiKey: g('ApiKey'), BearerToken: g('BearerToken'),
            Username: g('Username'), Password: g('Password'), CustomHeaderName: g('CustomHeaderName'),
            CustomHeaderValue: g('CustomHeaderValue'), CustomHeadersJson: g('CustomHeadersJson'),
            QueryParamsJson: g('QueryParamsJson'), RequestBody: g('RequestBody'),
            TimeoutSeconds: parseInt(g('TimeoutSeconds'), 10) || 30,
            TemplateKey: g('TemplateKey'), TargetEntity: g('TargetEntity'), RecordsPath: g('RecordsPath'),
            ExternalKeyField: g('ExternalKeyField'), MappingRulesJson: g('MappingRulesJson'),
            Direction: parseInt(g('Direction'), 10) || 0, IsEnabled: c('IsEnabled'),
            SyncSchedule: g('SyncSchedule'), PageSize: parseInt(g('PageSize'), 10) || 100,
            MaxRecordsPerSync: parseInt(g('MaxRecordsPerSync'), 10) || 5000,
            PaginationMode: g('PaginationMode'), SyncOptionsJson: g('SyncOptionsJson')
        };
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }
})();
