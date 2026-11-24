export const MAX = 35;

export function letters(value) { return value.replace(/[^\p{L} ]/gu, '').slice(0, MAX); }
export function digits(value) { return value.replace(/\D/g, '').slice(0, MAX); }
export function alphanumeric(value) { return value.replace(/[^\p{L}\d ]/gu, '').slice(0, MAX); }
export function productSearchText(value) { return value.replace(/[^\p{L}\d .,'&()\/+_-]/gu, '').slice(0, MAX); }
export function emailChars(value) { return value.replace(/[^A-Za-z0-9.!#$%&'*+\-/=?^_`{|}~@]/g, '').slice(0, MAX); }
export function postal(value) { return value.replace(/[^\p{L}\d-]/gu, '').slice(0, MAX); }
export function validPersonName(value) {
  const name = value.trim().replace(/\s+/g, ' ');
  return name.length > 0 && name.length <= MAX && /^[\p{L}]+(?: [\p{L}]+)*$/u.test(name);
}

// La fecha comercial del POS debe corresponder al calendario local del operador,
// no a UTC (en Ecuador UTC ya puede estar en el día siguiente por la noche).
export function today(date = new Date()) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

export function validEmail(value) {
  if (!value || value.length > MAX || /\s/.test(value) || (value.match(/@/g) || []).length !== 1) return false;
  const [local, domain] = value.split('@');
  if (!local || local.length > 64 || local.startsWith('.') || local.endsWith('.') || local.includes('..')) return false;
  if (!/^[A-Za-z0-9.!#$%&'*+\-/=?^_`{|}~]+$/.test(local)) return false;
  const labels = domain.split('.');
  return labels.length >= 2 && labels.every(label => label.length > 0 && label.length <= 63 &&
    /^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?$/.test(label)) && /^[A-Za-z]{2,}$/.test(labels.at(-1));
}
export function validCedula(value) { return /^(\d{10}|\d{13})$/.test(value); }
export function saleStatusLabel(value) {
  const labels = {
    pending: 'Pendiente', draft: 'Borrador', paid: 'Pagada', done: 'Completada',
    confirmed: 'Confirmada', invoiced: 'Facturada', cancel: 'Anulada', cancelled: 'Anulada', canceled: 'Anulada'
  };
  return labels[String(value || '').toLowerCase()] || 'Estado no disponible';
}
export function inputGuard(transform) {
  return event => {
    const next = transform(event.target.value);
    if (event.target.value !== next) event.preventDefault();
    event.target.value = next;
    event.target.dispatchEvent(new Event('input', { bubbles: true }));
  };
}
export function pasteGuard(transform) {
  return event => { event.preventDefault(); event.target.value = transform(event.clipboardData.getData('text')); event.target.dispatchEvent(new Event('input', { bubbles: true })); };
}
