export function clonePayload(payload) {
  return JSON.parse(JSON.stringify(payload));
}

export function newSaleAttempt(payload) {
  return { key: crypto.randomUUID(), payload: clonePayload(payload), uncertain: false };
}

export function isUncertainSaleError(error) {
  return error?.status === 502 && error?.code === 'odoo.result_uncertain';
}
