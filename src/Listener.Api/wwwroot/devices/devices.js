const createButton = document.querySelector('#create-qr');
const statusBox = document.querySelector('#pairing-status');
const devices = document.querySelector('#devices');
const csrf = document.querySelector('input[name="__RequestVerificationToken"]').value;
let expiresAt = null;
let previousIds = new Set();
let awaitingDevice = false;
let issuedAt = null;

function message(text, error = false) {
  statusBox.textContent = text;
  statusBox.classList.toggle('error', error);
  statusBox.hidden = false;
}
async function api(url, options = {}) {
  const response = await fetch(url, { ...options, cache: 'no-store', headers: { 'X-CSRF-TOKEN': csrf, ...options.headers } });
  if (response.status === 401) { window.location.assign('/acceso'); throw new Error('La sesión terminó.'); }
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.error || (response.status === 429 ? 'Espera un minuto antes de volver a intentarlo.' : 'No se pudo completar la operación.'));
  }
  return response.status === 204 ? null : response.json();
}
function date(value) { return value ? new Date(value).toLocaleString('es-PE') : 'Sin actividad todavía'; }
async function refreshDevices() {
  try {
    const items = await api('/api/v1/admin/devices');
    if (awaitingDevice && items.some(x => !x.revoked && new Date(x.linkedAt) >= issuedAt && !previousIds.has(`${x.id}:${x.linkedAt}`))) {
      message('Teléfono vinculado. Ya puede enviar notificaciones a Listener.');
      document.querySelector('#qr-area').hidden = true;
      awaitingDevice = false;
      expiresAt = null;
    }
    previousIds = new Set(items.filter(x => !x.revoked).map(x => `${x.id}:${x.linkedAt}`));
    devices.replaceChildren();
    if (!items.length) { const empty = document.createElement('p'); empty.className = 'muted'; empty.textContent = 'Todavía no hay teléfonos vinculados.'; devices.append(empty); }
    for (const item of items) {
      const row = document.createElement('div'); row.className = 'device';
      const info = document.createElement('div');
      const name = document.createElement('strong'); name.textContent = item.model;
      const state = document.createElement('span'); state.className = `badge ${item.revoked ? 'revoked' : ''}`; state.textContent = item.revoked ? 'Acceso revocado' : 'Vinculado';
      const linked = document.createElement('small'); linked.textContent = `Vinculado: ${date(item.linkedAt)}`;
      const seen = document.createElement('small'); seen.textContent = `Última conexión: ${date(item.lastSeenAt)}`;
      info.append(name, state, linked, seen); row.append(info);
      if (!item.revoked) {
        const revoke = document.createElement('button'); revoke.type = 'button'; revoke.className = 'danger'; revoke.textContent = 'Revocar acceso';
        revoke.addEventListener('click', async () => {
          revoke.disabled = true;
          try { await api(`/api/v1/admin/devices/${item.id}`, { method: 'DELETE' }); await refreshDevices(); }
          catch (error) { message(error.message, true); revoke.disabled = false; }
        }); row.append(revoke);
      }
      devices.append(row);
    }
  } catch (error) { message(error.message, true); }
}
createButton.addEventListener('click', async () => {
  createButton.disabled = true;
  try {
    const result = await api('/api/v1/admin/devices/pairings', { method: 'POST' });
    expiresAt = new Date(result.expiresAt);
    issuedAt = new Date(expiresAt.getTime() - 5 * 60 * 1000);
    document.querySelector('#pairing-qr').src = result.qrImage;
    document.querySelector('#qr-area').hidden = false;
    statusBox.hidden = true;
    awaitingDevice = true;
    updateExpiry();
  } catch (error) { message(error.message, true); }
  finally { createButton.disabled = false; }
});
function updateExpiry() {
  if (!expiresAt) return;
  const seconds = Math.max(0, Math.ceil((expiresAt - Date.now()) / 1000));
  document.querySelector('#qr-expiry').textContent = `Vence en ${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  if (!seconds) { document.querySelector('#qr-area').hidden = true; awaitingDevice = false; expiresAt = null; message('El QR venció. Genera uno nuevo para vincular el teléfono.'); }
}
refreshDevices();
setInterval(() => { if (!document.hidden) refreshDevices(); }, 5000);
setInterval(updateExpiry, 1000);
