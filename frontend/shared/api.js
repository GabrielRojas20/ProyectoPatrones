const DEFAULT_API_BASE = 'http://localhost:8080';

export function apiBase() {
  return (globalThis.NORTHWIND_API_BASE || import.meta?.env?.VITE_API_BASE || DEFAULT_API_BASE).replace(/\/$/, '');
}

export async function apiRequest(path, options = {}) {
  const token = sessionStorage.getItem('pos_token');
  const headers = new Headers(options.headers || {});
  headers.set('Accept', 'application/json');
  if (options.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  const response = await fetch(`${apiBase()}${path}`, { ...options, headers });
  const text = await response.text();
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
  sale: (id) => apiRequest(`/api/sales/${encodeURIComponent(id)}`),
  createSale: (payload) => apiRequest('/api/sales', { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify(payload) }),
};
