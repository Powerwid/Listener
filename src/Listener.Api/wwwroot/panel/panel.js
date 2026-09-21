document.querySelectorAll('.work-form').forEach(form => {
  form.addEventListener('submit', event => {
    if (form.dataset.busy) { event.preventDefault(); return; }
    form.dataset.busy = 'true';
    form.querySelector('.working').hidden = false;
    // Keep the submitter enabled so testOnly=true remains part of the request.
    form.querySelectorAll('button').forEach(button => button.setAttribute('aria-disabled', 'true'));
  });
});
function updateResults() {
  const filter = document.querySelector('#result-filter');
  const search = document.querySelector('#result-search');
  if (!filter || !search) return;
  let visible = 0;
  document.querySelectorAll('tr[data-group]').forEach(row => {
    row.hidden = (filter.value && row.dataset.group !== filter.value) ||
      !row.textContent.toLocaleLowerCase('es').includes(search.value.toLocaleLowerCase('es'));
    if (!row.hidden) visible++;
  });
  const empty = document.querySelector('#no-matches');
  empty.hidden = visible > 0;
  empty.textContent = document.querySelector('tr[data-group]')
    ? 'No hay resultados para este filtro.'
    : 'Aún no hay notificaciones guardadas. El lector procesa los avisos nuevos desde su primera activación.';
}
document.addEventListener('change', event => {
  if (event.target.id === 'result-filter') updateResults();
});
document.addEventListener('input', event => {
  if (event.target.id === 'result-search') updateResults();
});
updateResults();
window.addEventListener('pageshow', () => {
  document.querySelectorAll('.work-form').forEach(form => {
    delete form.dataset.busy;
    form.querySelector('.working').hidden = true;
    form.querySelectorAll('button').forEach(button => button.removeAttribute('aria-disabled'));
  });
  const password = document.querySelector('#AppPassword');
  if (password) password.value = '';
});

async function refreshAutomaticStatus() {
  try {
    const response = await fetch('/api/lector', {cache:'no-store'});
    if (!response.ok) throw new Error('status');
    const state = await response.json();
    document.querySelector('#automatic-status').textContent = state.estado;
    document.querySelector('#automatic-detail').textContent =
      `${state.buzon || 'Sin buzón activo'} · ${state.guardados} correos guardados en esta sesión · Última consulta: ${state.ultimaConsulta ? new Date(state.ultimaConsulta).toLocaleString('es-PE') : 'Pendiente'} · Consulta cada ${state.intervaloSegundos} segundos`;
    document.querySelector('#automatic-error').textContent = state.error || '';
  } catch {
    document.querySelector('#automatic-error').textContent = 'No se pudo consultar el estado. Comprueba que el servidor esté encendido.';
  }
}
setInterval(refreshAutomaticStatus, 10000);

// Two navigable views in the same local panel. No server restart is needed for
// this static UI change, so an active in-memory Gmail session stays connected.
const navigationStyles = document.createElement('link');
navigationStyles.rel = 'stylesheet';
navigationStyles.href = '/panel/navigation.css';
document.head.appendChild(navigationStyles);
const readerLink = document.querySelector('.nav-item[href="/panel"]');
const resultsLink = document.querySelector('.nav-item[href="#results"], .nav-item[href="/panel?view=resultados"]');
if (resultsLink) resultsLink.href = '/panel?view=resultados';
const readerSections = [...document.querySelectorAll('main > .intro, main > .automatic-status, main > .actions-grid, main > .notice')];
let refreshInProgress = false;
let isResultsView = false;

function reportIsPersistent() {
  try { return JSON.parse(document.querySelector('#report-json')?.textContent || 'null')?.persistido === true; }
  catch { return false; }
}

const resultsIntro = document.createElement('section');
resultsIntro.className = 'intro results-intro';
const titleBlock = document.createElement('div');
const title = document.createElement('h1');
title.textContent = 'Resultados de las notificaciones';
const subtitle = document.createElement('p');
subtitle.textContent = 'Consulta los datos extraídos, los descartes y los casos pendientes de validación.';
titleBlock.append(title, subtitle);
resultsIntro.appendChild(titleBlock);
document.querySelector('#results').before(resultsIntro);

const refreshLink = document.createElement('a');
refreshLink.className = 'button secondary';
refreshLink.href = '/panel?view=resultados';
refreshLink.textContent = 'Actualizar resultados';
resultsIntro.appendChild(refreshLink);

const refreshStatus = document.createElement('p');
refreshStatus.className = 'results-refresh-status';
refreshStatus.setAttribute('role', 'status');
resultsIntro.after(refreshStatus);

function showView(results, push = false) {
  isResultsView = results;
  readerSections.forEach(section => { section.hidden = results; });
  document.querySelector('#results').hidden = !results;
  resultsIntro.hidden = !results;
  refreshStatus.hidden = !results;
  readerLink?.classList.toggle('active', !results);
  resultsLink?.classList.toggle('active', results);
  [readerLink, resultsLink].forEach(link => {
    if (!link) return;
    if (link.classList.contains('active')) link.setAttribute('aria-current', 'page');
    else link.removeAttribute('aria-current');
  });
  document.querySelector('.breadcrumb').textContent = results ? 'Administración / Resultados' : 'Administración / Correo';
  document.title = results ? 'Listener · Resultados' : 'Listener · Lector automático';
  if (push) history.pushState(null, '', results ? '/panel?view=resultados' : '/panel');
  window.scrollTo({top:0, behavior:'instant'});
  if (results) updateResults();
}

async function refreshSavedResults() {
  if (refreshInProgress) return;
  refreshInProgress = true;
  refreshStatus.textContent = 'Consultando resultados guardados…';
  try {
    // Reuse the server-rendered, HTML-escaped table and its antiforgery form.
    // Only replace the results, leaving connection inputs and credentials alone.
    const response = await fetch('/panel?view=resultados', {cache:'no-store'});
    if (!response.ok) throw new Error('results');
    const html = new DOMParser().parseFromString(await response.text(), 'text/html');
    const next = html.querySelector('#results');
    if (!next || html.querySelector('.notice.error')) throw new Error('results');
    // Capture the latest input after the request, including any typing while it ran.
    const currentSearch = document.querySelector('#result-search');
    const previousFilter = document.querySelector('#result-filter')?.value || '';
    const previousSearch = currentSearch?.value || '';
    const focusedId = document.activeElement?.id;
    const selection = focusedId === 'result-search'
      ? [currentSearch.selectionStart, currentSearch.selectionEnd] : null;
    const jsonOpen = document.querySelector('.json-details')?.open || false;
    document.querySelector('#results').replaceWith(next);
    next.hidden = !isResultsView;
    const filter = next.querySelector('#result-filter');
    const search = next.querySelector('#result-search');
    if (filter) filter.value = previousFilter;
    if (search) search.value = previousSearch;
    if (focusedId === 'result-search' || focusedId === 'result-filter') {
      next.querySelector(`#${focusedId}`)?.focus({preventScroll:true});
      if (selection && search) search.setSelectionRange(...selection);
    }
    if (next.querySelector('.json-details')) next.querySelector('.json-details').open = jsonOpen;
    updateResults();
    refreshStatus.textContent = `Actualizado a las ${new Date().toLocaleTimeString('es-PE')} · Actualización automática cada 10 segundos.`;
  } catch {
    refreshStatus.textContent = 'No se pudieron actualizar los resultados. Se conserva la última vista; comprueba el servidor y SQL Server.';
  } finally { refreshInProgress = false; }
}

function navigate(event, results) {
  if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
  event.preventDefault();
  const targetUrl = results ? '/panel?view=resultados' : '/panel';
  showView(results, location.pathname + location.search !== targetUrl || !!location.hash);
  if (results) refreshSavedResults();
}
readerLink?.addEventListener('click', event => navigate(event, false));
resultsLink?.addEventListener('click', event => navigate(event, true));
refreshLink.addEventListener('click', event => navigate(event, true));
document.querySelector('.automatic-status a[href="/panel"]')?.addEventListener('click', event => navigate(event, true));
window.addEventListener('popstate', () => {
  showView(new URLSearchParams(location.search).get('view') === 'resultados');
});
const query = new URLSearchParams(location.search);
const diagnosticResult = document.querySelector('#report-json') && !reportIsPersistent();
showView(query.get('view') === 'resultados' || !!diagnosticResult || location.hash === '#results');
refreshAutomaticStatus();
setInterval(() => {
  if (isResultsView && reportIsPersistent() && !document.hidden) refreshSavedResults();
}, 10000);
