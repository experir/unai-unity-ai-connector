// Run from a temporary directory containing @modelcontextprotocol/sdk.
// This drives only the isolated UnaiIntegrationServer fixture, never a user project.
import assert from 'node:assert/strict';
import { readFile, writeFile, rename } from 'node:fs/promises';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';

const directory = resolve(process.argv[2]);
const report = resolve(process.argv[3]);
const state = () => readFile(resolve(directory, 'server.json'), 'utf8').then(JSON.parse);
async function command(value) {
  const temporary = resolve(directory, 'control.pending');
  await writeFile(temporary, JSON.stringify(value));
  await rename(temporary, resolve(directory, 'control.json'));
  await delay(750);
}
async function connect(config) {
  const client = new Client({ name: 'unai-live-validation', version: '1' }, { capabilities: {} });
  await client.connect(new StreamableHTTPClientTransport(new URL(config.url), {
    requestInit: { headers: { Authorization: `Bearer ${config.token}` } }
  }));
  return client;
}
async function authStatus(config, token) {
  const response = await fetch(config.url, { method: 'POST', headers: {
    Authorization: `Bearer ${token}`, 'Content-Type': 'application/json',
    Accept: 'application/json, text/event-stream'
  }, body: '{}' });
  return response.status;
}

const initial = await state();
const results = { unity: initial.unity, checks: {} };
let client;
try {
  assert.equal(await authStatus(initial, 'invalid'), 401);
  results.checks.authentication = 'passed';
  client = await connect(initial);
  const catalog = await client.listTools();
  assert(catalog.tools.some(tool => tool.name === 'get_selection'));
  assert(!catalog.tools.some(tool => tool.name === 'integration_mutate'));
  const selection = await client.callTool({ name: 'get_selection', arguments: {} });
  assert(!selection.isError && selection.content.length > 0);
  results.checks.builtInTool = 'passed';
  await assert.rejects(() => client.callTool({ name: 'integration_mutate', arguments: {} }));
  await command({ action: 'permissions', allowMutating: true });
  assert((await client.listTools()).tools.some(tool => tool.name === 'integration_mutate'));
  const mutation = await client.callTool({ name: 'integration_mutate', arguments: {} });
  assert(!mutation.isError);
  await command({ action: 'permissions', allowMutating: false });
  results.checks.permissions = 'passed';

  const abort = new AbortController();
  const waiting = client.callTool({ name: 'integration_wait', arguments: {} }, undefined, { signal: abort.signal });
  const cancellation = assert.rejects(waiting);
  await delay(300);
  abort.abort();
  await cancellation;
  // Confirm the connection remains usable after the cancellation notification.
  await client.listTools();
  results.checks.cancellation = 'passed';
  await client.close();
  client = null;

  await command({ action: 'recompile' });
  let current;
  const deadline = Date.now() + 120000;
  while (Date.now() < deadline) {
    await delay(500);
    try { current = await state(); } catch { continue; }
    if (current.generation > initial.generation) break;
  }
  assert(current?.generation > initial.generation, 'Unity must restart the fixture after script recompilation');
  assert.equal(current.url, initial.url);
  assert.notEqual(current.token, initial.token);
  assert.equal(await authStatus(current, initial.token), 401);
  client = await connect(current);
  assert((await client.listTools()).tools.some(tool => tool.name === 'get_selection'));
  results.checks.recompileAndReconnect = 'passed';
  results.result = 'passed';
} catch (error) {
  results.result = 'failed';
  results.error = { name: error.name, message: String(error.message).replaceAll(initial.token, '[REDACTED]') };
  process.exitCode = 1;
} finally {
  try { await command({ action: 'permissions', allowMutating: false }); } catch { /* Editor may have stopped. */ }
  await client?.close();
  await writeFile(report, JSON.stringify(results, null, 2) + '\n');
  console.log(JSON.stringify(results, null, 2));
}
