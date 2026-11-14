export const MAX = 40;

export function letters(value) { return value.replace(/[^\p{L} ]/gu, '').slice(0, MAX); }
export function digits(value) { return value.replace(/\D/g, '').slice(0, MAX); }
export function alphanumeric(value) { return value.replace(/[^\p{L}\d ]/gu, '').slice(0, MAX); }
export function emailChars(value) { return value.replace(/[^\p{L}\d@_.-]/gu, '').slice(0, MAX); }
export function postal(value) { return value.replace(/[^\p{L}\d-]/gu, '').slice(0, MAX); }

// La fecha comercial del POS debe corresponder al calendario local del operador,
// no a UTC (en Ecuador UTC ya puede estar en el día siguiente por la noche).
export function today(date = new Date()) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

export function validEmail(value) {
  return value.length <= MAX && (value.match(/@/g) || []).length === 1 &&
    /^[^\s@]+@[^\s@.]+\.[A-Za-z]{2,}$/.test(value) && !/\.[A-Za-z]{2,}\./.test(value);
}
export function validCedula(value) { return /^\d{10}$/.test(value); }
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
