const DEFAULT_API_BASE = 'http://localhost:8080';

export function apiBase() {
  const configured = globalThis.NORTHWIND_API_BASE || import.meta.env?.VITE_API_BASE || DEFAULT_API_BASE;
  return configured.replace(/\/$/, '');
}

function uncertainNetworkError(cause) {
  const error = new Error('No se pudo confirmar la respuesta del servidor. El resultado puede ser incierto.');
  error.status = 0;
  error.code = 'network.uncertain';
  error.uncertain = true;
  error.cause = cause;
  return error;
}

export async function apiRequest(path, options = {}) {
  const token = sessionStorage.getItem('pos_token');
  const headers = new Headers(options.headers || {});
  headers.set('Accept', 'application/json');
  if (options.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  let response;
  let text;
  try {
    response = await fetch(`${apiBase()}${path}`, { ...options, headers });
    text = await response.text();
  } catch (cause) {
    throw uncertainNetworkError(cause);
  }
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = { message: text }; }
  if (!response.ok) {
    const error = new Error(data?.title || data?.message || `Error HTTP ${response.status}`);
    error.status = response.status;
    error.code = data?.code;
    throw error;
  }
  return data;
}

export const api = {
  login: (username, password) => apiRequest('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) }),
  products: ({ name = '', code = '', page = 1, pageSize = 10 } = {}) => apiRequest(`/api/products?name=${encodeURIComponent(name)}&code=${encodeURIComponent(code)}&page=${page}&pageSize=${pageSize}`),
  session: () => apiRequest('/api/pos/sessions/active'),
  sales: ({ name = '', id = '', page = 1, pageSize = 10 } = {}) => apiRequest(`/api/sales?name=${encodeURIComponent(name)}&id=${encodeURIComponent(id)}&page=${page}&pageSize=${pageSize}`),
  sale: id => apiRequest(`/api/sales/${encodeURIComponent(id)}`),
  createSale: (payload, idempotencyKey) => apiRequest('/api/sales', { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey }, body: JSON.stringify(payload) }),
};
