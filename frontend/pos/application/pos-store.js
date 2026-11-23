import { Cart } from '../domain/cart.js';

export class PosStore {
  constructor(api) { this.api = api; this.cart = new Cart(); this.products = []; }
  async search(term) { this.products = await this.api.products(term); return this.products; }
  totals(discount) { return this.cart.fiscal(discount); }
  async confirm(discountCode, paymentMethod, customer) {
    const lines = this.cart.items.map(item => ({ productId: item.product.id, quantity: item.quantity }));
    const key = crypto.randomUUID();
    const sale = await this.api.createSale({ lines, discountCode, paymentMethod, customerDocument: customer.document, customerName: customer.name }, key);
    this.cart.clear(); return sale;
  }
}
