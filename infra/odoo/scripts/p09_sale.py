"""Registra y verifica P09 dentro de una instancia real de Odoo.

La salida solo contiene identificadores tecnicos y totales; no imprime claves,
contraseñas, tokens, cookies ni encabezados de autenticacion.
"""

import json
import uuid

config = env["pos.config"].search([("name", "=", "POS ProyectoPatrones")], limit=1)
if not config:
    raise RuntimeError("No existe POS ProyectoPatrones; ejecute primero bootstrap_pos.py")

session = env["pos.session"].search(
    [("config_id", "=", config.id), ("state", "=", "opened")],
    order="id desc",
    limit=1,
)
if not session:
    raise RuntimeError("No existe una sesion POS abierta")

products = env["product.product"].search(
    [("default_code", "in", ["POS-CAFE", "POS-SANDWICH"])], order="default_code"
)
if len(products) != 2:
    raise RuntimeError("P09 requiere exactamente los dos productos de prueba")

payment_method = env["pos.payment.method"].search(
    [("name", "=", "Efectivo Proyecto")], limit=1
)
if not payment_method:
    raise RuntimeError("No existe el metodo de pago en efectivo")

line_commands = []
amount_tax = 0.0
amount_total = 0.0
for product in products:
    taxes = product.taxes_id.filtered(lambda tax: tax.company_id == session.company_id)
    totals = taxes.compute_all(
        product.lst_price,
        currency=session.currency_id,
        quantity=1.0,
        product=product,
        partner=False,
    )
    subtotal = totals["total_excluded"]
    subtotal_incl = totals["total_included"]
    amount_tax += subtotal_incl - subtotal
    amount_total += subtotal_incl
    line_commands.append(
        (
            0,
            0,
            {
                "product_id": product.id,
                "full_product_name": product.display_name,
                "qty": 1.0,
                "price_unit": product.lst_price,
                "price_subtotal": subtotal,
                "price_subtotal_incl": subtotal_incl,
                "tax_ids": [(6, 0, taxes.ids)],
            },
        )
    )

reference = f"P09-{uuid.uuid4().hex[:12].upper()}"
order = env["pos.order"].create(
    {
        "name": reference,
        "pos_reference": reference,
        "session_id": session.id,
        "company_id": session.company_id.id,
        "config_id": config.id,
        "currency_id": session.currency_id.id,
        "pricelist_id": config.pricelist_id.id,
        "user_id": env.user.id,
        "amount_tax": amount_tax,
        "amount_total": amount_total,
        "amount_paid": amount_total,
        "amount_return": 0.0,
        "lines": line_commands,
    }
)
env["pos.payment"].create(
    {
        "pos_order_id": order.id,
        "payment_method_id": payment_method.id,
        "amount": amount_total,
        "session_id": session.id,
    }
)
order.action_pos_order_paid()
env.cr.commit()

verified = env["pos.order"].browse(order.id).exists()
if not verified or verified.state not in ("paid", "done", "invoiced"):
    raise RuntimeError("La venta se creo, pero Odoo no la confirmo como pagada")
if len(verified.lines) != 2 or len(verified.payment_ids) != 1:
    raise RuntimeError("La venta confirmada no conserva dos lineas y un pago")

evidence = {
    "test": "P09",
    "result": "PASS",
    "database": env.cr.dbname,
    "order_id": verified.id,
    "pos_reference": verified.pos_reference,
    "session_id": verified.session_id.id,
    "product_ids": verified.lines.product_id.ids,
    "payment_method_id": verified.payment_ids.payment_method_id.id,
    "payment_kind": "cash",
    "line_count": len(verified.lines),
    "amount_total": verified.amount_total,
    "state": verified.state,
}
print("P09_EVIDENCE " + json.dumps(evidence, sort_keys=True))
