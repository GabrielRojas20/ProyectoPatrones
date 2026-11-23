export class PosApi {
  constructor(baseUrl) { this.baseUrl = baseUrl; this.token = typeof sessionStorage === 'undefined' ? null : sessionStorage.getItem('pos-token'); }
  async request(path, options = {}) {
    const headers = { 'Content-Type': 'application/json', ...options.headers };
    if (this.token) headers.Authorization = `Bearer ${this.token}`;
    let response;
    try { response = await fetch(`${this.baseUrl}${path}`, { ...options, headers }); }
    catch { throw new Error('No se pudo conectar con la API .NET. Verifica que el backend esté ejecutándose.'); }
    if (!response.ok) { const problem = await response.json().catch(() => ({})); throw new Error(problem.title || `La API respondió HTTP ${response.status}.`); }
    return response.status === 204 ? null : response.json();
  }
  async status() { return this.request('/api/status'); }
  async login(user, password) { const result = await this.request('/api/auth/login', { method: 'POST', body: JSON.stringify({ user, password }) }); this.token = result.token; if (typeof sessionStorage !== 'undefined') sessionStorage.setItem('pos-token', result.token); return result; }
  products(search = '') { return this.request(`/api/products?search=${encodeURIComponent(search)}`); }
  createSale(payload, key) { return this.request('/api/sales', { method: 'POST', headers: { 'Idempotency-Key': key }, body: JSON.stringify(payload) }); }
  sales() { return this.request('/api/sales'); }
  sale(id) { return this.request(`/api/sales/${encodeURIComponent(id)}`); }
}
