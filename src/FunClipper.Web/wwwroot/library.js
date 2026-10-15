'use strict';

const element = (id) => document.getElementById(id);
let accessKey = '';
let uploadRequest = null;
let expiryDays = 14;
const maxUploadBytes = 100 * 1024 * 1024;
const previewObserver = 'IntersectionObserver' in window ? new IntersectionObserver((entries) => {
  for (const entry of entries) {
    if (!entry.isIntersecting) continue;
    entry.target.src = entry.target.dataset.src;
    previewObserver.unobserve(entry.target);
  }
}, { rootMargin: '120px' }) : null;

function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.classList.add('icon');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('focusable', 'false');
  const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
  use.setAttribute('href', `/icons.svg#${name}`);
  svg.append(use);
  return svg;
}

function showMessage(text, error = false) {
  element('message').textContent = text;
  element('message').classList.toggle('error', error);
  element('message').hidden = !text;
}

function setConnected(connected) {
  element('connect-panel').hidden = connected;
  element('library-actions').hidden = !connected;
  element('clips-panel').hidden = !connected;
  element('disconnect').hidden = !connected;
  if (!connected) {
    previewObserver?.disconnect();
    element('upload-panel').hidden = true;
    element('show-upload').setAttribute('aria-expanded', 'false');
    element('clip-list').replaceChildren();
    element('copy-fallback').hidden = true;
  }
}

function saveKey(key) {
  accessKey = key;
  try {
    if (key) sessionStorage.setItem('funclipper-key', key);
    else sessionStorage.removeItem('funclipper-key');
  } catch { /* Connection still works for this page when browser storage is disabled. */ }
}

function readFragmentKey() {
  const fragmentKey = new URLSearchParams(location.hash.slice(1)).get('key');
  if (!fragmentKey) return false;
  saveKey(fragmentKey);
  history.replaceState(null, '', location.pathname + location.search);
  return true;
}

async function request(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: { 'X-FunClipper-Key': accessKey, ...options.headers },
    cache: 'no-store'
  });
  if (response.status === 401) {
    saveKey('');
    setConnected(false);
  }
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error || 'The clip service could not complete this request. Try again.');
  }
  return response.status === 204 ? null : response.json();
}

function fileSize(bytes) { return `${(bytes / (1024 * 1024)).toFixed(1)} MB`; }
function date(value) { return new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }); }

async function copyLink(url) {
  try {
    await navigator.clipboard.writeText(url);
    element('copy-fallback').hidden = true;
    return true;
  } catch {
    element('copy-url').value = url;
    element('copy-fallback').hidden = false;
    element('copy-url').focus();
    element('copy-url').select();
    return false;
  }
}

function renderClips(clips) {
  const list = element('clip-list');
  previewObserver?.disconnect();
  list.replaceChildren();
  element('empty-state').hidden = clips.length > 0;
  element('list-status').textContent = `${clips.length} ${clips.length === 1 ? 'clip' : 'clips'} · Links expire after ${expiryDays} days`;
  for (const clip of clips) {
    const row = document.createElement('li');
    row.className = 'clip-row';
    const info = document.createElement('div');
    info.className = 'clip-info';
    const title = document.createElement('a');
    title.className = 'clip-title';
    title.href = `/c/${encodeURIComponent(clip.id)}`;
    title.textContent = clip.title;
    const preview = document.createElement('a');
    preview.className = 'clip-preview';
    preview.href = title.href;
    preview.setAttribute('aria-label', `Watch ${clip.title}`);
    const video = document.createElement('video');
    video.muted = true;
    video.playsInline = true;
    video.preload = 'metadata';
    video.tabIndex = -1;
    video.setAttribute('aria-hidden', 'true');
    video.dataset.src = `/media/${encodeURIComponent(clip.id)}#t=0.1`;
    video.addEventListener('loadeddata', () => preview.classList.add('has-frame'), { once: true });
    preview.append(icon('video'), video);
    const meta = document.createElement('p');
    meta.className = 'clip-meta';
    meta.textContent = `${date(clip.createdAt)} · ${fileSize(clip.sizeBytes)} · Expires ${date(clip.expiresAt)}`;
    info.append(title, meta);
    const actions = document.createElement('div');
    actions.className = 'actions';
    const watch = document.createElement('a');
    watch.href = title.href;
    watch.className = 'button';
    watch.textContent = 'Watch';
    watch.prepend(icon('play'));
    watch.setAttribute('aria-label', `Watch ${clip.title}`);
    const copy = document.createElement('button');
    copy.type = 'button';
    copy.textContent = 'Copy link';
    copy.prepend(icon('copy'));
    copy.setAttribute('aria-label', `Copy link for ${clip.title}`);
    copy.addEventListener('click', async () => {
      const copied = await copyLink(clip.url);
      showMessage(copied ? 'Clip link copied.' : 'Copy the selected clip link below.');
    });
    const remove = document.createElement('button');
    remove.type = 'button';
    remove.className = 'delete text-button';
    remove.textContent = 'Delete';
    remove.prepend(icon('delete'));
    remove.setAttribute('aria-label', `Delete ${clip.title}`);
    remove.addEventListener('click', async () => {
      if (!window.confirm(`Delete “${clip.title}”? Its link will stop working.`)) return;
      remove.disabled = true;
      try {
        await request(`/api/clips/${encodeURIComponent(clip.id)}`, { method: 'DELETE' });
        await loadClips();
        showMessage('Clip deleted. Its link is no longer available.');
      } catch (error) { showMessage(error.message, true); remove.disabled = false; }
    });
    actions.append(watch, copy, remove);
    row.append(preview, info, actions);
    list.append(row);
    if (previewObserver) previewObserver.observe(video);
    else video.src = video.dataset.src;
  }
}

async function loadClips() {
  element('refresh').disabled = true;
  element('clips-panel').setAttribute('aria-busy', 'true');
  element('list-status').textContent = 'Loading clips…';
  try {
    const clips = await request('/api/clips');
    setConnected(true);
    renderClips(clips);
  } catch (error) {
    element('list-status').textContent = 'Clips could not be loaded. Use Refresh to try again.';
    throw error;
  } finally {
    element('refresh').disabled = false;
    element('clips-panel').setAttribute('aria-busy', 'false');
  }
}

function openUpload() {
  element('upload-panel').hidden = false;
  element('show-upload').setAttribute('aria-expanded', 'true');
  element('clip-file').focus();
}

function setUploading(uploading) {
  element('upload').disabled = uploading;
  element('clip-file').disabled = uploading;
  element('clip-title').disabled = uploading;
  element('disconnect').disabled = uploading;
  element('upload-progress').hidden = !uploading;
  element('cancel-upload').textContent = uploading ? 'Cancel upload' : 'Cancel';
  element('upload-form').setAttribute('aria-busy', String(uploading));
}

element('connect-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  saveKey(element('access-key').value.trim());
  element('connect').disabled = true;
  showMessage('Connecting…');
  try { await loadClips(); element('access-key').value = ''; showMessage(''); }
  catch (error) { showMessage(error.message, true); }
  finally { element('connect').disabled = false; }
});
element('disconnect').addEventListener('click', () => { saveKey(''); setConnected(false); showMessage('Library disconnected from this browser tab.'); element('access-key').focus(); });
element('refresh').addEventListener('click', () => loadClips().then(() => showMessage('')).catch((error) => showMessage(error.message, true)));
element('show-upload').addEventListener('click', openUpload);
element('empty-upload').addEventListener('click', openUpload);
element('cancel-upload').addEventListener('click', () => {
  if (uploadRequest) uploadRequest.abort();
  else { element('upload-panel').hidden = true; element('show-upload').setAttribute('aria-expanded', 'false'); element('show-upload').focus(); }
});
element('upload-form').addEventListener('submit', (event) => {
  event.preventDefault();
  const file = element('clip-file').files[0];
  if (!file) return;
  if (file.size > maxUploadBytes) { showMessage('This clip exceeds the 100 MB upload limit. Trim it in the desktop editor first.', true); return; }
  const body = new FormData();
  body.append('file', file);
  body.append('title', element('clip-title').value);
  const xhr = new XMLHttpRequest();
  uploadRequest = xhr;
  setUploading(true);
  element('upload-progress').value = 0;
  element('upload-status').textContent = 'Uploading…';
  showMessage('');
  xhr.open('POST', '/api/clips');
  xhr.setRequestHeader('X-FunClipper-Key', accessKey);
  xhr.upload.addEventListener('progress', (progress) => {
    if (progress.lengthComputable) {
      const percent = Math.round(progress.loaded / progress.total * 100);
      element('upload-progress').value = percent;
      element('upload-status').textContent = percent === 100 ? 'Saving clip…' : `Uploading ${percent}%`;
    }
  });
  const finish = () => { uploadRequest = null; setUploading(false); element('upload-status').textContent = ''; };
  xhr.addEventListener('load', async () => {
    finish();
    let result;
    try { result = JSON.parse(xhr.responseText); } catch { result = {}; }
    if (xhr.status !== 201) {
      if (xhr.status === 401) { saveKey(''); setConnected(false); }
      showMessage(result.error || (xhr.status === 413 ? 'This clip exceeds the 100 MB upload limit. Trim it before uploading.' : 'The upload failed. Try again.'), true);
      return;
    }
    element('upload-form').reset();
    element('upload-panel').hidden = true;
    element('show-upload').setAttribute('aria-expanded', 'false');
    const copied = await copyLink(result.url);
    showMessage(copied ? 'Clip uploaded. Link copied.' : 'Clip uploaded. Copy the selected link below.');
    try { await loadClips(); } catch { showMessage('Clip uploaded, but the library could not refresh. Use Refresh to try again.', true); }
  });
  xhr.addEventListener('error', () => { finish(); showMessage('The upload could not reach the local service. Start the service and try again.', true); });
  xhr.addEventListener('abort', () => { finish(); showMessage('Upload cancelled.'); });
  xhr.send(body);
});

window.addEventListener('hashchange', async () => {
  if (!readFragmentKey()) return;
  setConnected(true);
  try { await loadClips(); showMessage(''); }
  catch (error) { showMessage(error.message, true); }
});

(async () => {
  if (!readFragmentKey()) {
    try { accessKey = sessionStorage.getItem('funclipper-key') || ''; } catch { }
  }
  setConnected(Boolean(accessKey));
  try {
    const response = await fetch('/api/health', { cache: 'no-store' });
    if (!response.ok) throw new Error();
    const health = await response.json();
    expiryDays = health.expiresAfterDays;
    if (health.publicUrlConfigured) {
      element('scope-label').textContent = 'Shared clips';
      element('local-notice').textContent = `Anyone with a clip link can view it. Links expire after ${expiryDays} days.`;
    }
    if (accessKey) await loadClips();
  } catch (error) { showMessage(error.message || 'The local clip service is unavailable. Start it and refresh this page.', true); }
})();
