export const MAX = 40;

export function letters(value) { return value.replace(/[^\p{L} ]/gu, '').slice(0, MAX); }
export function digits(value) { return value.replace(/\D/g, '').slice(0, MAX); }
export function alphanumeric(value) { return value.replace(/[^\p{L}\d ]/gu, '').slice(0, MAX); }
export function emailChars(value) { return value.replace(/[^\p{L}\d@_.-]/gu, '').slice(0, MAX); }
export function postal(value) { return value.replace(/[^\p{L}\d-]/gu, '').slice(0, MAX); }
export function today() { return new Date().toISOString().slice(0, 10); }
export function validEmail(value) {
  return value.length <= MAX && (value.match(/@/g) || []).length === 1 &&
    /^[^\s@]+@[^\s@.]+\.[A-Za-z]{2,}$/.test(value) && !/\.[A-Za-z]{2,}\./.test(value);
}
export function validCedula(value) { return /^\d{10}$/.test(value); }
export function inputGuard(transform) {
  return (event) => {
    const next = transform(event.target.value);
    if (event.target.value !== next) event.preventDefault();
    event.target.value = next;
    event.target.dispatchEvent(new Event('input', { bubbles: true }));
  };
}
export function pasteGuard(transform) {
  return (event) => { event.preventDefault(); event.target.value = transform(event.clipboardData.getData('text')); event.target.dispatchEvent(new Event('input', { bubbles: true })); };
}
