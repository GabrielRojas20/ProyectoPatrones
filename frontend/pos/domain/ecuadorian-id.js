export const FINAL_CONSUMER = '9999999999999';
export function isValidEcuadorianId(value) {
  if (value === FINAL_CONSUMER) return true;
  if (!/^\d{10}(\d{3})?$/.test(value)) return false;
  const cedula = value.slice(0, 10); const province = Number(cedula.slice(0, 2));
  if (!((province >= 1 && province <= 24) || province === 30) || Number(cedula[2]) > 5) return false;
  const coefficients = [2, 1, 2, 1, 2, 1, 2, 1, 2];
  const sum = coefficients.reduce((total, factor, index) => { const product = Number(cedula[index]) * factor; return total + (product > 9 ? product - 9 : product); }, 0);
  if ((10 - (sum % 10)) % 10 !== Number(cedula[9])) return false;
  return value.length === 10 || value.slice(10) === '001';
}
