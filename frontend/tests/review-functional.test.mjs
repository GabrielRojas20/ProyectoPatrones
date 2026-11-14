import assert from 'node:assert/strict';

process.env.TZ = 'America/Guayaquil';
globalThis.sessionStorage = { getItem: () => 'test-token' };
const { api } = await import('../shared/api.js');
const { today } = await import('../shared/validation.js');
const { isUncertainSaleError, newSaleAttempt } = await import('../shared/saleAttempt.js');

globalThis.fetch = async () => new Response(JSON.stringify({ code: 'pos.session_not_found', message: 'No active session' }), { status: 409 });
await assert.rejects(() => api.session(), error => error.status === 409 && error.code === 'pos.session_not_found');

const payload = {
  operationDate: '2026-11-13', sessionId: 7, paymentMethodId: 2,
  lines: [{ productId: 11, quantity: 2, expectedUnitPrice: 4.5, discountPercent: 0 }],
  customer: { identification: '1712345678', name: 'Ana', email: 'ana@gmail.com' },
};

let calls = [];
globalThis.fetch = async (_url, options) => {
  calls.push(JSON.parse(options.body));
  return new Response(JSON.stringify({ code: 'odoo.result_uncertain', message: 'uncertain' }), {
    status: 502, headers: { 'Content-Type': 'application/json' },
  });
};

const attempt = newSaleAttempt(payload);
await assert.rejects(() => api.createSale(attempt.payload, attempt.key), error => {
  assert.equal(error.status, 502);
  assert.equal(error.code, 'odoo.result_uncertain');
  return isUncertainSaleError(error);
});
await assert.rejects(() => api.createSale(attempt.payload, attempt.key), error => isUncertainSaleError(error));
assert.equal(calls.length, 2, 'el reintento debe volver a enviar la solicitud');
assert.deepEqual(calls[0], calls[1], 'el reintento debe conservar el mismo payload');
assert.equal(attempt.key.length > 10, true, 'el intento debe tener una clave estable');

let captured;
globalThis.fetch = async (_url, options) => {
  captured = { body: JSON.parse(options.body), key: options.headers.get('Idempotency-Key') };
  return new Response(JSON.stringify({ externalId: 'ODOO-1' }), { status: 201 });
};
const result = await api.createSale({ ...payload, paymentMethodId: 2 }, attempt.key);
assert.equal(result.externalId, 'ODOO-1');
assert.equal(captured.body.paymentMethodId, 2, 'el segundo método de pago debe viajar en el payload');
assert.equal(captured.key, attempt.key, 'la misma venta conserva su clave');

const utcAfterMidnight = new Date('2026-11-14T01:30:00Z');
assert.equal(today(utcAfterMidnight), '2026-11-13', 'Ecuador debe usar su fecha local y no UTC');

console.log('Functional review checks: 9 passed');
