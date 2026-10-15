'use strict';

const element = (id) => document.getElementById(id);
function showMessage(text, error = false) {
  element('message').textContent = text;
  element('message').classList.toggle('error', error);
  element('message').hidden = !text;
}
function date(value) { return new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }); }

(async () => {
  const id = location.pathname.split('/')[2];
  try {
    const [response, healthResponse] = await Promise.all([
      fetch(`/api/view/${encodeURIComponent(id)}`, { cache: 'no-store' }),
      fetch('/api/health', { cache: 'no-store' })
    ]);
    if (healthResponse.ok) {
      const health = await healthResponse.json();
      if (health.publicUrlConfigured) {
        element('scope-label').textContent = 'Shared clip';
        element('local-notice').hidden = true;
      }
    }
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      throw new Error(body.error || 'This clip is unavailable or has expired. Ask the sender for a new link.');
    }
    const clip = await response.json();
    document.title = `${clip.title} · FunClipper`;
    element('clip-title').textContent = clip.title;
    element('clip-meta').textContent = `${date(clip.createdAt)} · ${(clip.sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
    element('expiry').textContent = `This link expires ${date(clip.expiresAt)}. Anyone with the link can view the clip.`;
    element('video').src = `/media/${encodeURIComponent(clip.id)}`;
    element('download').href = `/media/${encodeURIComponent(clip.id)}`;
    element('clip').hidden = false;
    showMessage('');
    element('video').addEventListener('error', () => showMessage('Your browser could not play this video. Download the MP4 to open it in a video player.', true));
    element('copy-link').addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(clip.url);
        element('copy-fallback').hidden = true;
        showMessage('Clip link copied.');
      } catch {
        element('copy-url').value = clip.url;
        element('copy-fallback').hidden = false;
        element('copy-url').focus();
        element('copy-url').select();
        showMessage('Copy the selected clip link below.');
      }
    });
  } catch (error) { showMessage(error.message || 'The clip service is unavailable. Try this link again later.', true); }
})();
