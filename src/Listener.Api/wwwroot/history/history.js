const form = document.querySelector('#history-filters');
const rows = document.querySelector('#history-rows');
const feedback = document.querySelector('#history-feedback');
const dialog = document.querySelector('#notification-detail');
const detailContent = document.querySelector('#detail-content');
let page = 1;
let pages = 1;
let revision = '';
let listRequest = null;
let detailRequest = null;

function element(tag, text, className) {
  const node = document.createElement(tag);
  if (text !== null && text !== undefined) node.textContent = text;
  if (className) node.className = className;
  return node;
}
function date(value) { return value ? new Date(value).toLocaleString('es-PE', { timeZone: 'America/Lima', dateStyle: 'short', timeStyle: 'medium' }) : 'Sin fecha'; }
function money(item) { return item.amount === null ? '—' : `${item.currency === 'PEN' ? 'S/' : item.currency || ''} ${Number(item.amount).toLocaleString('es-PE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`; }
function message(text, error = false) { feedback.textContent = text; feedback.classList.toggle('error', error); feedback.hidden = false; }
async function api(url, options = {}) {
  const response = await fetch(url, { ...options, cache: 'no-store' });
  if (response.status === 401) { window.location.assign('/acceso'); throw new Error('La sesión terminó.'); }
  if (!response.ok) {
    const data = await response.json().catch(() => null);
    throw new Error(data?.error || (response.status === 400 ? 'Revisa los filtros y el rango de fechas.' : response.status === 429 ? 'Espera un minuto antes de volver a intentarlo.' : 'No se pudo completar la consulta.'));
  }
  return response.json();
}
function parameters() {
  const params = new URLSearchParams();
  for (const [key, value] of new FormData(form)) if (value.trim()) params.set(key, value.trim());
  return params;
}
function render(item) {
  const row = element('tr');
  const received = element('td', date(item.receivedAt));
  const source = element('td'); source.append(element('strong', item.source));
  if (item.deviceId) source.append(element('small', item.deviceId));
  const name = element('td', null, 'notification-name'); name.append(element('strong', item.name || item.title || 'Sin nombre extraído'));
  name.append(element('small', item.name ? item.title : item.classification));
  const amount = element('td', money(item), 'notification-amount');
  const state = element('td'); state.append(element('span', item.statusLabel, `status-badge status-${item.status}`));
  state.append(element('small', item.classification));
  const action = element('td'); const button = element('button', 'Ver detalle', 'row-detail'); button.type = 'button';
  button.addEventListener('click', () => openDetail(item.id)); action.append(button);
  row.append(received, source, name, amount, state, action);
  return row;
}
async function refresh(targetPage = page, interactive = false) {
  listRequest?.abort();
  const request = new AbortController(); listRequest = request;
  const params = parameters(); params.set('page', targetPage);
  if (interactive) { feedback.hidden = true; document.querySelector('#history-updated').textContent = 'Actualizando…'; }
  try {
    const data = await api(`/api/v1/admin/history?${params}`, { signal: request.signal });
    page = data.page; pages = data.pages;
    const summary = data.summary;
    for (const [id, value] of Object.entries({ total: summary.total, payments: summary.bbvaPayments, pending: summary.pending, sent: summary.sent, discarded: summary.discarded })) document.querySelector(`#metric-${id}`).textContent = value.toLocaleString('es-PE');
    const nextRevision = JSON.stringify(data.items);
    if (nextRevision !== revision) { rows.replaceChildren(...data.items.map(render)); revision = nextRevision; }
    document.querySelector('#history-empty').hidden = data.items.length !== 0;
    document.querySelector('#history-table').hidden = data.items.length === 0;
    document.querySelector('#previous-page').disabled = page <= 1;
    document.querySelector('#next-page').disabled = page >= pages;
    document.querySelector('#history-page').textContent = `Página ${page} de ${pages}`;
    document.querySelector('#history-count').textContent = summary.total ? `${(page - 1) * data.pageSize + 1}–${Math.min(page * data.pageSize, summary.total)} de ${summary.total} avisos` : '0 avisos';
    document.querySelector('#history-updated').textContent = `Actualizado ${new Date().toLocaleTimeString('es-PE', { timeZone: 'America/Lima' })}`;
    params.set('page', page); window.history.replaceState(null, '', `/historial?${params}`);
  } catch (error) {
    if (error.name !== 'AbortError') { message(error.message, true); document.querySelector('#history-updated').textContent = 'No se pudo actualizar'; }
  }
}
function showDetail(data) {
  const item = data.item;
  document.querySelector('#detail-title').textContent = item.name || item.title || 'Notificación';
  detailContent.replaceChildren();
  detailContent.append(element('span', item.statusLabel, `status-badge status-${item.status}`));
  const grid = element('dl', null, 'detail-grid');
  const fields = { Origen: item.source, Monto: money(item), 'Nombre extraído': item.name || 'Sin nombre', Clasificación: item.classification,
    'Fecha del aviso (Perú)': date(item.receivedAt), 'Registrado en Listener': date(item.storedAt), Teléfono: item.deviceId || 'No aplica',
    Motivo: item.reason || 'Sin motivo' };
  for (const [label, value] of Object.entries(fields)) { const field = element('div'); field.append(element('dt', label), element('dd', value)); grid.append(field); }
  detailContent.append(grid);
  for (const [title, content] of [['Título original', item.title], ['Mensaje original', data.text], ['Mensaje ampliado', data.expandedText]]) {
    if (!content) continue;
    const section = element('section', null, 'detail-section'); section.append(element('h3', title), element('pre', content)); detailContent.append(section);
  }
}
async function openDetail(id) {
  detailRequest?.abort(); const request = new AbortController(); detailRequest = request;
  if (!dialog.open) dialog.showModal();
  detailContent.replaceChildren(element('p', 'Cargando detalle…', 'muted'));
  try { const data = await api(`/api/v1/admin/history/${id}`, { signal: request.signal }); showDetail(data); }
  catch (error) { if (error.name !== 'AbortError') detailContent.replaceChildren(element('p', error.message, 'notice error')); }
}
document.querySelector('#close-detail').addEventListener('click', () => dialog.close());
dialog.addEventListener('close', () => detailRequest?.abort());
form.addEventListener('submit', event => { event.preventDefault(); refresh(1, true); });
document.querySelector('#clear-filters').addEventListener('click', () => { form.reset(); refresh(1, true); });
document.querySelector('#refresh-history').addEventListener('click', () => refresh(page, true));
document.querySelector('#previous-page').addEventListener('click', () => refresh(page - 1, true));
document.querySelector('#next-page').addEventListener('click', () => refresh(page + 1, true));
const initial = new URLSearchParams(window.location.search);
for (const name of ['search', 'source', 'status', 'from', 'to']) if (initial.has(name)) form.elements[name].value = initial.get(name);
page = Math.max(1, Math.min(1000000, Number.parseInt(initial.get('page'), 10) || 1));
refresh(page, true);
setInterval(() => { if (!document.hidden && document.querySelector('#auto-refresh').checked) refresh(); }, 15000);
