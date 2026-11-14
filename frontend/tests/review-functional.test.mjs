import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

process.env.TZ = 'America/Guayaquil';
globalThis.sessionStorage = { getItem: () => 'test-token' };
const { api } = await import('../shared/api.js');
const { today } = await import('../shared/validation.js');
const { isUncertainSaleError, newSaleAttempt } = await import('../shared/saleAttempt.js');

// 409: una sesión inexistente debe ser distinguible para que las interfaces bloqueen el POS.
globalThis.fetch = async () => new Response(JSON.stringify({ code: 'pos.session_not_found', message: 'No active session' }), { status: 409 });
await assert.rejects(() => api.session(), error => error.status === 409 && error.code === 'pos.session_not_found');

const payload = {
  operationDate: '2026-11-13', sessionId: 7, paymentMethodId: 2,
  lines: [{ productId: 11, quantity: 2, expectedUnitPrice: 4.5, discountPercent: 0 }],
  customer: { identification: '1712345678', name: 'Ana', email: 'ana@gmail.com' },
};

// 502 incierto: dos envíos deben conservar payload y clave.
let calls = [];
globalThis.fetch = async (_url, options) => {
  calls.push({ body: JSON.parse(options.body), key: options.headers.get('Idempotency-Key') });
  return new Response(JSON.stringify({ code: 'odoo.result_uncertain', message: 'uncertain' }), { status: 502 });
};
const attempt = newSaleAttempt(payload);
await assert.rejects(() => api.createSale(attempt.payload, attempt.key), error => error.status === 502 && isUncertainSaleError(error));
await assert.rejects(() => api.createSale(attempt.payload, attempt.key), error => isUncertainSaleError(error));
assert.deepEqual(calls[0], calls[1], '502 debe reintentar con el mismo payload y la misma clave');

// 504: también es incierto y no puede crear una clave nueva.
calls = [];
globalThis.fetch = async (_url, options) => {
  calls.push({ body: JSON.parse(options.body), key: options.headers.get('Idempotency-Key') });
  return new Response('gateway timeout', { status: 504 });
};
await assert.rejects(() => api.createSale(attempt.payload, attempt.key), error => error.status === 504 && isUncertainSaleError(error));
await assert.rejects(() => api.createSale(attempt.payload, attempt.key), error => isUncertainSaleError(error));
assert.deepEqual(calls[0], calls[1], '504 debe conservar el mismo intento');

// Pérdida de red después de enviar: el frontend conserva el mismo intento para reintentar.
calls = [];
globalThis.fetch = async (_url, options) => {
  calls.push(options.headers.get('Idempotency-Key'));
  throw new TypeError('connection reset');
};
await assert.rejects(() => api.createSale(attempt.payload, attempt.key), error => isUncertainSaleError(error));
await assert.rejects(() => api.createSale(attempt.payload, attempt.key), error => isUncertainSaleError(error));
assert.deepEqual(calls, [attempt.key, attempt.key], 'error de red debe conservar la clave');

// Confirmación normal y segundo método de pago.
let captured;
globalThis.fetch = async (_url, options) => {
  captured = { body: JSON.parse(options.body), key: options.headers.get('Idempotency-Key') };
  return new Response(JSON.stringify({ externalId: 'ODOO-1' }), { status: 201 });
};
const result = await api.createSale({ ...payload, paymentMethodId: 2 }, attempt.key);
assert.equal(result.externalId, 'ODOO-1');
assert.equal(captured.body.paymentMethodId, 2);
assert.equal(captured.key, attempt.key);

// Fecha comercial local de Ecuador.
assert.equal(today(new Date('2026-11-14T01:30:00Z')), '2026-11-13');

// Contratos de comportamiento de las tres vistas: no deben construir una venta sin sesión.
const react = readFileSync(new URL('../react/src/main.jsx', import.meta.url), 'utf8');
const vue = readFileSync(new URL('../vue/src/main.js', import.meta.url), 'utf8');
const html = readFileSync(new URL('../html/app.js', import.meta.url), 'utf8');
assert.match(react, /Sin sesión POS activa/);
assert.match(react, /hasActiveSession\(session\)/);
assert.match(vue, /sessionUnavailable \|\| !session/);
assert.match(vue, /hasActiveSession\(active\)/);
assert.match(html, /renderNoSession/);
assert.match(html, /if \(!state\.session\?\.id\)/);

console.log('Functional review checks: 15 passed');
