const form = document.querySelector('#report-filters');
const exportButton = document.querySelector('#export-excel');
const feedback = document.querySelector('#report-feedback');
let page = 1, pages = 1, total = 0;
let applied = new URLSearchParams();
let request = null;
let exporting = false;

function element(tag, text, className) {
  const node = document.createElement(tag);
  node.textContent = text;
  if (className) node.className = className;
  return node;
}
function money(value) {
  return value === null || value === undefined ? '—' : `S/ ${Number(value).toLocaleString('es-PE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}
function date(value) {
  return new Date(value).toLocaleString('es-PE', { timeZone: 'America/Lima', dateStyle: 'short', timeStyle: 'medium' });
}
function message(text, error = false) {
  feedback.textContent = text; feedback.classList.toggle('error', error); feedback.hidden = false;
}
function filters() {
  const params = new URLSearchParams();
  for (const [key, value] of new FormData(form)) if (value.trim()) params.set(key, value.trim());
  return params;
}
async function checked(response) {
  if (response.status === 401) { window.location.assign('/acceso'); throw new Error('La sesión terminó.'); }
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.error || (response.status === 400 ? 'Revisa el estado y el rango de fechas.' : response.status === 429 ? 'Espera un minuto antes de volver a exportar.' : 'No se pudo preparar el reporte. Vuelve a intentarlo.'));
  }
  return response;
}
function render(item) {
  const row = document.createElement('tr');
  const validation = item.validation;
  const result = element('td', '', 'report-result');
  result.append(element('span', validation ? validation.correcto ? 'Correcta' : 'Insuficiente' : item.classification,
    `report-validation${validation ? validation.correcto ? ' report-correct' : ' report-insufficient' : ''}`));
  const delivery = element('td', '');
  delivery.append(element('span', item.statusLabel, `status-badge status-${item.status}`));
  row.append(element('td', date(item.receivedAt)), element('td', item.name || 'Sin nombre extraído', 'report-name'),
    element('td', money(item.amount), 'notification-amount'), element('td', money(validation?.montoAcumulado), 'notification-amount'),
    element('td', money(validation?.montoFaltante), 'notification-amount'), result, delivery);
  return row;
}
async function load(target = 1) {
  request?.abort(); const pending = new AbortController(); request = pending;
  exportButton.disabled = true; feedback.hidden = true;
  const params = filters(); params.set('page', target);
  document.querySelector('#report-description').textContent = 'Consultando resultados…';
  try {
    const response = await checked(await fetch(`/api/v1/admin/reports?${params}`, { cache: 'no-store', signal: pending.signal }));
    const data = await response.json();
    page = data.page; pages = data.pages; total = data.summary.total;
    applied = new URLSearchParams(params); applied.delete('page');
    for (const key of ['total', 'insufficient', 'pending']) document.querySelector(`#report-${key}`).textContent = data.summary[key].toLocaleString('es-PE');
    document.querySelector('#report-rows').replaceChildren(...data.items.map(render));
    document.querySelector('#report-table').hidden = total === 0;
    document.querySelector('#report-empty').hidden = total !== 0;
    document.querySelector('#report-previous').disabled = page <= 1;
    document.querySelector('#report-next').disabled = page >= pages;
    document.querySelector('#report-page').textContent = `Página ${page} de ${pages}`;
    document.querySelector('#report-count').textContent = total ? `${(page - 1) * data.pageSize + 1}–${Math.min(page * data.pageSize, total)} de ${total.toLocaleString('es-PE')} ${total === 1 ? 'aviso' : 'avisos'}` : '0 avisos';
    document.querySelector('#report-description').textContent = `${total.toLocaleString('es-PE')} ${total === 1 ? 'aviso coincide' : 'avisos coinciden'} con los filtros. El Excel incluye todos los resultados.`;
    exportButton.disabled = exporting || total === 0 || total > 50000;
    if (total > 50000) message('El reporte supera los 50 000 registros. Reduce las fechas para exportarlo completo.');
    params.set('page', page); window.history.replaceState(null, '', `/reportes?${params}`);
  } catch (error) {
    if (error.name !== 'AbortError') { message(error.message, true); document.querySelector('#report-description').textContent = 'No se pudo cargar el reporte.'; }
  }
}
form.addEventListener('submit', event => { event.preventDefault(); load(); });
form.addEventListener('input', () => {
  request?.abort(); exportButton.disabled = true;
  document.querySelector('#report-previous').disabled = true; document.querySelector('#report-next').disabled = true;
  message('Aplica los filtros para actualizar la vista previa y el Excel.');
});
document.querySelector('#clear-report').addEventListener('click', () => { form.reset(); load(); });
document.querySelector('#report-previous').addEventListener('click', () => load(page - 1));
document.querySelector('#report-next').addEventListener('click', () => load(page + 1));
exportButton.addEventListener('click', async () => {
  if (exporting) return;
  const exportParams = new URLSearchParams(applied);
  exporting = true; exportButton.disabled = true; exportButton.textContent = 'Preparando Excel…'; feedback.hidden = true;
  try {
    const response = await checked(await fetch(`/api/v1/admin/reports/excel?${exportParams}`, { cache: 'no-store' }));
    const blobUrl = URL.createObjectURL(await response.blob());
    const filename = /filename="?([^";]+)"?/i.exec(response.headers.get('Content-Disposition') || '')?.[1] || 'Reporte_BBVA.xlsx';
    const link = document.createElement('a'); link.href = blobUrl; link.download = filename; document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(blobUrl), 10000);
    message('Excel descargado con los resultados de los filtros aplicados.');
  } catch (error) { message(error.message, true); }
  finally {
    exporting = false; exportButton.textContent = 'Exportar Excel';
    exportButton.disabled = total === 0 || total > 50000 || filters().toString() !== applied.toString();
  }
});
const initial = new URLSearchParams(window.location.search);
for (const name of ['status', 'from', 'to']) if (initial.has(name)) form.elements[name].value = initial.get(name);
page = Math.max(1, Math.min(1000000, Number.parseInt(initial.get('page'), 10) || 1));
load(page);
