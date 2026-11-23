import { Cart } from '../domain/cart.js';
import { PosApi } from '../infrastructure/api.js';
import { FINAL_CONSUMER, isValidEcuadorianId } from '../domain/ecuadorian-id.js';

let failed = 0;
function check(name, action) { try { action(); console.log(`PASS ${name}`); } catch (error) { failed++; console.error(`FAIL ${name}: ${error.message}`); } }
check('P07 actualiza carrito, cantidades y total', () => { const cart = new Cart(); const product = { id: 1, name: 'Café', price: 2.5, available: true }; cart.add(product); cart.add(product); cart.setQuantity(1, 3); if (cart.subtotal() !== 7.5 || cart.total('DOCENTE10') !== 6.75) throw new Error('Total incorrecto'); cart.remove(1); if (cart.items.length) throw new Error('No eliminó la línea'); });
check('P07 rechaza cantidad inválida', () => { const cart = new Cart(); cart.add({ id: 1, price: 1, available: true }); try { cart.setQuantity(1, 0); } catch (error) { return; } throw new Error('Aceptó cantidad cero'); });
check('RF-04 valida identificadores ecuatorianos', () => { if (!isValidEcuadorianId('1804283537') || !isValidEcuadorianId('1804283537001') || !isValidEcuadorianId(FINAL_CONSUMER) || isValidEcuadorianId('1804283536')) throw new Error('Validación SRI incorrecta'); });
check('RF-05 desglosa IVA incluido', () => { const cart = new Cart(); cart.add({ id: 1, price: 115, available: true }); const fiscal = cart.fiscal('NONE'); if (fiscal.taxableBase !== 100 || fiscal.tax !== 15) throw new Error('IVA incorrecto'); });

const previousFetch = globalThis.fetch;
globalThis.fetch = async () => { throw new TypeError('connection refused'); };
try { await new PosApi('http://localhost:0').products(); console.error('FAIL P08 presenta error comprensible'); failed++; }
catch (error) { if (!error.message.includes('API .NET')) { console.error('FAIL P08 mensaje incomprensible'); failed++; } else console.log('PASS P08 presenta error comprensible'); }
globalThis.fetch = previousFetch;
console.log(`Resultado: ${5 - failed}/5 verificaciones correctas.`);
process.exitCode = failed ? 1 : 0;
