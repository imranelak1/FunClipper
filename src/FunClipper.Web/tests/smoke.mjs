// Standard-library integration checks. Run against an isolated local service/storage.
// node smoke.mjs <base-url> <MP4-path> [isolated-storage-directory]
// Pass the access key through FUNCLIPPER_SHARE_KEY, never a URL or command-line argument.
import assert from 'node:assert/strict';
import { readFile, readdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import http from 'node:http';
import { once } from 'node:events';

const base = process.argv[2] || 'http://127.0.0.1:5182';
const clipPath = process.argv[3];
const storage = process.argv[4] && resolve(process.argv[4]);
const key = process.env.FUNCLIPPER_SHARE_KEY;
if (!clipPath || !key) throw new Error('Supply an MP4 path and set FUNCLIPPER_SHARE_KEY for the isolated test service.');
if (new URL(base).hostname !== '127.0.0.1') throw new Error('Smoke checks run only against an isolated 127.0.0.1 service.');
const headers = { 'X-FunClipper-Key': key };

async function upload(bytes, title = 'Smoke test clip') {
  const form = new FormData();
  form.append('file', new Blob([bytes], { type: 'video/mp4' }), '../unsafe-name.mp4');
  form.append('title', title);
  return fetch(`${base}/api/clips`, { method: 'POST', headers, body: form });
}

const health = await fetch(`${base}/api/health`);
assert.equal(health.status, 200);
assert.equal((await health.json()).scope, 'local');
assert.equal((await fetch(`${base}/api/clips`)).status, 401);
assert.equal((await fetch(`${base}/api/clips`, { headers: { 'X-FunClipper-Key': 'wrong-key' } })).status, 401);
assert.equal((await fetch(`${base}/api/clips`, { method: 'POST' })).status, 401);
assert.equal((await upload(Buffer.from('renaming plain text does not make it MP4'))).status, 400);

const bytes = await readFile(clipPath);
const response = await upload(bytes, '<script>unsafe title remains plain text</script>');
assert.equal(response.status, 201);
const clip = await response.json();
assert.match(clip.id, /^[a-f0-9]{32}$/);
assert.equal(clip.sizeBytes, bytes.length);
assert.equal(new URL(clip.url).pathname, `/c/${clip.id}`);
assert.equal((await fetch(`${base}/c/${clip.id}`)).status, 200);
assert.equal((await fetch(`${base}/api/view/${clip.id}`)).status, 200);
const library = await fetch(`${base}/api/clips`, { headers });
assert.ok((await library.json()).some(item => item.id === clip.id));
const media = await fetch(`${base}/media/${clip.id}`);
assert.equal(media.status, 200);
assert.equal(media.headers.get('content-type'), 'video/mp4');
assert.deepEqual(Buffer.from(await media.arrayBuffer()), bytes);
const range = await fetch(`${base}/media/${clip.id}`, { headers: { Range: 'bytes=0-15' } });
assert.equal(range.status, 206);
assert.deepEqual(Buffer.from(await range.arrayBuffer()), bytes.subarray(0, 16));
assert.equal((await fetch(`${base}/media/${clip.id}`, { method: 'HEAD' })).status, 200);
assert.equal((await fetch(`${base}/media/%2e%2e%2faccess-key.txt`)).status, 404);
assert.equal((await fetch(`${base}/api/view/%2e%2e%2faccess-key.txt`)).status, 404);

async function oversizedUpload() {
  const boundary = 'funclipper-size-check';
  const prefix = Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="file"; filename="oversize.mp4"\r\nContent-Type: video/mp4\r\n\r\n`);
  const suffix = Buffer.from(`\r\n--${boundary}--\r\n`);
  const fileLength = 100 * 1024 * 1024 + 1;
  const options = {
    method: 'POST',
    headers: { ...headers, 'Content-Type': `multipart/form-data; boundary=${boundary}`, 'Content-Length': prefix.length + fileLength + suffix.length }
  };
  const request = http.request(`${base}/api/clips`, options);
  const result = once(request, 'response').then(async ([response]) => {
    response.resume();
    await once(response, 'end');
    return response.statusCode;
  });
  let received = false;
  request.once('response', () => { received = true; });
  request.write(prefix);
  request.write(bytes.subarray(0, 32));
  const zeros = Buffer.alloc(65536);
  for (let remaining = fileLength - 32; remaining > 0 && !received;) {
    const chunk = zeros.subarray(0, Math.min(remaining, zeros.length));
    remaining -= chunk.length;
    if (!request.write(chunk)) await once(request, 'drain');
  }
  if (!received) request.end(suffix);
  return result;
}
assert.equal(await oversizedUpload(), 413);
if (storage) {
  assert.equal((await readdir(storage)).filter(name => name.endsWith('.upload')).length, 0, 'Failed uploads must not leave partial files.');
  const expiredResponse = await upload(bytes, 'Expiry test');
  const expired = await expiredResponse.json();
  const path = join(storage, expired.id + '.json');
  const metadata = JSON.parse(await readFile(path, 'utf8'));
  metadata.expiresAt = '2000-01-01T00:00:00Z';
  await writeFile(path, JSON.stringify(metadata));
  assert.equal((await fetch(`${base}/api/view/${expired.id}`)).status, 404);
  assert.equal((await fetch(`${base}/media/${expired.id}`)).status, 404);
  const remaining = await (await fetch(`${base}/api/clips`, { headers })).json();
  assert.ok(!remaining.some(item => item.id === expired.id));
  await fetch(`${base}/api/clips/${expired.id}`, { method: 'DELETE', headers });
}
assert.equal((await fetch(`${base}/api/clips/${clip.id}`, { method: 'DELETE' })).status, 401);
assert.equal((await fetch(`${base}/api/clips/${clip.id}`, { method: 'DELETE', headers })).status, 204);
assert.equal((await fetch(`${base}/media/${clip.id}`)).status, 404);
console.log('PASS: upload/playback/range/HEAD, access key, MP4 signature, traversal, 100 MB limit, expiry, and deletion.');
