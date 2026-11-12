import assert from 'node:assert/strict';
import { alphanumeric, digits, emailChars, letters, MAX, today, validCedula, validEmail } from '../shared/validation.js';

assert.equal(letters('Ana123 Pérez!').length, 'Ana Pérez'.length);
assert.equal(digits('17-123abc4567').slice(0, 10), '171234567');
assert.equal(alphanumeric('A-12_x').includes('-'), false);
assert.equal(emailChars('ana+test@gmail.com'), 'anatest@gmail.com');
assert.equal(letters('x'.repeat(80)).length, MAX);
assert.equal(validCedula('1712345678'), true);
assert.equal(validCedula('171234567'), false);
assert.equal(validEmail('persona@gmail.com'), true);
assert.equal(validEmail('persona@gmail.com.com'), false);
assert.equal(today().length, 10);
console.log('Frontend validation checks: 10 passed');
