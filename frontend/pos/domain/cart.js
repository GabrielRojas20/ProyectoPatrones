export class Cart {
  #items = new Map();
  add(product) { if (!product.available) throw new Error('El producto no está disponible.'); const current = this.#items.get(product.id); this.#items.set(product.id, { product, quantity: (current?.quantity ?? 0) + 1 }); }
  setQuantity(id, quantity) { if (!Number.isInteger(quantity) || quantity < 1 || quantity > 1000) throw new Error('La cantidad debe estar entre 1 y 1000.'); const item = this.#items.get(id); if (item) this.#items.set(id, { ...item, quantity }); }
  remove(id) { this.#items.delete(id); }
  clear() { this.#items.clear(); }
  get items() { return [...this.#items.values()]; }
  subtotal() { return this.items.reduce((sum, item) => sum + item.product.price * item.quantity, 0); }
  total(discountCode) { const subtotal = this.subtotal(); return discountCode === 'DOCENTE10' ? subtotal * .9 : subtotal; }
  fiscal(discountCode) { const subtotal = this.subtotal(); const total = this.total(discountCode); const taxableBase = Math.round((total / 1.15) * 100) / 100; return { subtotal, discount: subtotal - total, taxableBase, tax: total - taxableBase, total }; }
}
