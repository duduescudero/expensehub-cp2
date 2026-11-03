import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';

// Somente ambiente acadêmico local: cria dados fictícios e preserva todos os volumes.
const env = Object.fromEntries(readFileSync('.env', 'utf8').replace(/^\uFEFF/, '').split(/\r?\n/).filter(x => x && !x.startsWith('#')).map(x => { const i = x.indexOf('='); return [x.slice(0, i), x.slice(i + 1)]; }));
const docker = process.env.DOCKER_EXE || 'docker';
const base = process.env.CP3_URL || 'http://localhost:5187';
const run = randomUUID().slice(0, 8);
const password = `Test!9a${randomUUID()}`;
const evidence = { run, capturedAt: new Date().toISOString(), experiments: [], assertions: 0 };
mkdirSync('evidence', { recursive: true });
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
const dc = (...args) => execFileSync(docker, ['compose', ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
async function request(path, token, method = 'GET', body, expected = 200) {
  const response = await fetch(base + path, { method, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(20000) });
  assert.equal(response.status, expected, `${method} ${path}: ${response.status} != ${expected}`);
  evidence.assertions++;
  if (response.headers.get('content-type')?.includes('json')) {
    const data = await response.json();
    if (expected === 202) assert.ok(response.headers.get('location')?.endsWith(`/api/reports/${data.id}`), '202 aponta Location para o pedido salvo');
    return data;
  }
  return response;
}
async function healthy() {
  for (let n = 0; n < 100; n++) {
    try { if ((await fetch(base + '/health')).ok) return; } catch {}
    await pause(500);
  }
  throw new Error('API não ficou disponível.');
}
async function completed(id, token) {
  for (let n = 0; n < 100; n++) {
    const report = await request(`/api/reports/${id}`, token);
    if (report.status === 'Completed') return report;
    assert.notEqual(report.status, 'Failed');
    await pause(300);
  }
  throw new Error(`Relatório não concluiu: ${id}`);
}
const brokerAuth = 'Basic ' + Buffer.from(`${env.RABBIT_USER}:${env.RABBIT_PASSWORD}`).toString('base64');
async function broker(path, method = 'GET', body) {
  const r = await fetch('http://localhost:15672/api' + path, { method, headers: { Authorization: brokerAuth, 'Content-Type': 'application/json' }, ...(body ? { body: JSON.stringify(body) } : {}) });
  assert.ok(r.ok, `RabbitMQ ${path}: ${r.status}`);
  return await r.json();
}
await healthy();
const admin = (await request('/login', null, 'POST', { email: env.ADMIN_EMAIL, password: env.ADMIN_PASSWORD })).accessToken;
async function user(name, roles) {
  const email = `${name}-${run}@example.test`;
  await request('/register', null, 'POST', { email, password }, 201);
  const users = await request('/api/admin/users', admin);
  const person = users.find(x => x.email === email);
  assert.ok(person, 'Usuário cadastrado encontrado');
  await request(`/api/admin/users/${person.id}/roles`, admin, 'PUT', { roles });
  return { token: (await request('/login', null, 'POST', { email, password })).accessToken, id: person.id };
}
const a = await user('alice', ['Employee']);
const b = await user('bob', ['Employee']);
const auditor = await user('audit', ['Auditor']);
const ops = await user('ops', ['Approver', 'Finance']);
const combined = await user('combined', ['Employee', 'Auditor']);
async function expense(token, amount, state, date = '2026-09-01') {
  const x = await request('/api/expenses', token, 'POST', { description: `Despesa fictícia CP3 ${run}`, amount, expenseDate: date, categoryId: 1 });
  if (state !== 'Draft') await request(`/api/expenses/${x.id}/submit`, token, 'POST');
  if (state === 'Approved' || state === 'Paid') await request(`/api/expenses/${x.id}/approve`, ops.token, 'POST');
  if (state === 'Paid') await request(`/api/expenses/${x.id}/pay`, ops.token, 'POST');
  return x.id;
}
await expense(a.token, 100, 'Approved');
await expense(a.token, 50, 'Approved', '2026-09-30');
await expense(a.token, 25, 'Paid');
await expense(b.token, 200, 'Approved');
await expense(a.token, 500, 'Paid', '2026-10-01');
const input = { fromDate: '2026-09-01', toDate: '2026-09-30', scope: 'Mine' };
const submitted = await request('/api/reports', a.token, 'POST', input, 202);
const normal = await completed(submitted.id, a.token);
assert.equal(normal.result.totalCount, 3);
assert.equal(normal.result.totalAmount, 175);
assert.equal(normal.result.byStatus.length, 5);
const excel = await request(`/api/reports/${normal.id}/excel`, a.token);
assert.equal(excel.headers.get('content-type'), 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
assert.match(excel.headers.get('content-disposition'), /attachment.*\.xlsx/);
const bytes = Buffer.from(await excel.arrayBuffer());
assert.equal(bytes.subarray(0, 2).toString(), 'PK');
writeFileSync('evidence/report-example.xlsx', bytes);
evidence.experiments.push({ id: 'E01', reportId: normal.id, result: normal.result, xlsxBytes: bytes.length, outcome: 'passed' });
dc('stop', 'worker');
const waiting = await request('/api/reports', a.token, 'POST', input, 202);
assert.equal((await request(`/api/reports/${waiting.id}`, a.token)).status, 'Pending');
await request(`/api/reports/${waiting.id}/excel`, a.token, 'GET', null, 409);
await pause(1500);
let queue;
for (let n = 0; n < 30; n++) {
  queue = await broker('/queues/%2F/expensehub.reports');
  if (queue.messages_ready >= 1) break;
  await pause(500);
}
assert.ok(queue.messages_ready >= 1);
dc('up', '-d', 'worker');
await completed(waiting.id, a.token);
evidence.experiments.push({ id: 'E02', reportId: waiting.id, ready: queue.messages_ready, outcome: 'passed' });
dc('restart', 'worker');
const published = await broker('/exchanges/%2F/amq.default/publish', 'POST', { properties: { delivery_mode: 2, content_type: 'application/json' }, routing_key: 'expensehub.reports', payload: JSON.stringify({ reportId: normal.id }), payload_encoding: 'string' });
assert.equal(published.routed, true);
let duplicateLog = '';
for (let n = 0; n < 60; n++) {
  duplicateLog = dc('logs', '--no-color', 'worker').split('\n').find(line => line.includes(normal.id) && line.includes('outcome=duplicate')) || '';
  if (duplicateLog) break;
  await pause(300);
}
assert.ok(duplicateLog);
assert.deepEqual(await request(`/api/reports/${normal.id}`, a.token), normal);
evidence.experiments.push({ id: 'E03', reportId: normal.id, duplicateLog, outcome: 'passed' });
await request('/api/reports', null, 'GET', null, 401);
await request('/api/reports', null, 'POST', input, 401);
await request(`/api/reports/${normal.id}`, null, 'GET', null, 401);
await request(`/api/reports/${normal.id}/excel`, null, 'GET', null, 401);
await request('/api/reports', a.token, 'POST', { ...input, scope: 'Global' }, 403);
await request('/api/reports', auditor.token, 'POST', input, 403);
await request('/api/reports', admin, 'POST', input, 403);
await request('/api/reports', ops.token, 'POST', input, 403);
await request(`/api/reports/${normal.id}`, b.token, 'GET', null, 404);
await request(`/api/reports/${normal.id}/excel`, b.token, 'GET', null, 404);
await request(`/api/reports/${normal.id}`, auditor.token, 'GET', null, 404);
await request(`/api/reports/${normal.id}/excel`, auditor.token, 'GET', null, 404);
assert.equal((await request('/api/reports', b.token)).length, 0);
const global = await completed((await request('/api/reports', auditor.token, 'POST', { ...input, scope: 'Global' }, 202)).id, auditor.token);
// Cada execução acrescenta despesas; Global inclui dados anteriores preservados.
assert.ok(global.result.totalAmount >= 375);
await request('/api/reports', combined.token, 'POST', input, 202);
await request('/api/reports', combined.token, 'POST', { ...input, scope: 'Global' }, 202);
await request('/api/reports', a.token, 'POST', { ...input, fromDate: '2026-10-01' }, 400);
await request('/api/reports', a.token, 'POST', { ...input, scope: 'anything' }, 400);
evidence.experiments.push({ id: 'E04', reportId: normal.id, globalReportId: global.id, outcome: 'passed' });
const before = await request('/version');
dc('up', '-d', '--force-recreate', 'web', 'worker');
await healthy();
assert.deepEqual(await request(`/api/reports/${normal.id}`, a.token), normal);
assert.deepEqual(await request('/version'), before);
evidence.experiments.push({ id: 'E05', reportId: normal.id, version: before, containers: dc('ps', '--format', 'json'), outcome: 'passed' });
// Sexta prova adicional: publicação sem broker não dá falso 202.
dc('stop', 'rabbitmq');
try {
  const failedPublish = await request('/api/reports', a.token, 'POST', input, 503);
  assert.ok(failedPublish.id);
  assert.equal((await request(`/api/reports/${failedPublish.id}`, a.token)).status, 'Pending');
  evidence.publicationFailure = { reportId: failedPublish.id, code: failedPublish.error.code, outcome: 'passed' };
} finally { dc('up', '-d', 'rabbitmq'); }
writeFileSync('evidence/experiments.json', JSON.stringify(evidence, null, 2) + '\n');
writeFileSync('evidence/worker.log', dc('logs', '--no-color', 'worker'));
console.log(`E01–E05 aprovados; ${evidence.assertions} verificações HTTP. Evidências sem tokens gravadas em evidence/.`);
