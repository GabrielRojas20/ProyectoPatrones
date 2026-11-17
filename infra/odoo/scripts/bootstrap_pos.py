"""Configuracion idempotente del POS local para ProyectoPatrones.

Este archivo se ejecuta dentro de ``odoo shell``. No contiene secretos y puede
ejecutarse varias veces sin duplicar la configuracion funcional.
"""

company = env.company

tax = env["account.tax"].search(
    [("name", "=", "IVA POS 15%"), ("company_id", "=", company.id)], limit=1
)
if not tax:
    tax = env["account.tax"].create(
        {
            "name": "IVA POS 15%",
            "amount_type": "percent",
            "amount": 15.0,
            "type_tax_use": "sale",
            "company_id": company.id,
        }
    )

products = []
for values in (
    {"name": "Cafe Proyecto", "default_code": "POS-CAFE", "list_price": 2.50},
    {"name": "Sandwich Proyecto", "default_code": "POS-SANDWICH", "list_price": 4.00},
):
    product = env["product.product"].search(
        [("default_code", "=", values["default_code"])], limit=1
    )
    product_values = {
        **values,
        "sale_ok": True,
        "available_in_pos": True,
        "taxes_id": [(6, 0, tax.ids)],
    }
    if product:
        product.write(product_values)
    else:
        product = env["product.product"].create(product_values)
    products.append(product)

journal = env["account.journal"].search(
    [("code", "=", "PCSH"), ("company_id", "=", company.id)], limit=1
)
if not journal:
    journal = env["account.journal"].create(
        {
            "name": "Caja POS Proyecto",
            "code": "PCSH",
            "type": "cash",
            "company_id": company.id,
        }
    )

payment_method = env["pos.payment.method"].search(
    [("name", "=", "Efectivo Proyecto"), ("company_id", "=", company.id)], limit=1
)
if not payment_method:
    payment_method = env["pos.payment.method"].create(
        {
            "name": "Efectivo Proyecto",
            "type": "cash",
            "journal_id": journal.id,
            "company_id": company.id,
        }
    )

config = env["pos.config"].search(
    [("name", "=", "POS ProyectoPatrones"), ("company_id", "=", company.id)], limit=1
)
config_values = {
    "name": "POS ProyectoPatrones",
    "company_id": company.id,
    "payment_method_ids": [(6, 0, payment_method.ids)],
}
if config:
    config.write(config_values)
else:
    config = env["pos.config"].create(config_values)

session = env["pos.session"].search(
    [("config_id", "=", config.id), ("state", "in", ["opening_control", "opened"])],
    order="id desc",
    limit=1,
)
if not session:
    session = env["pos.session"].create({"config_id": config.id, "user_id": env.user.id})
if session.state == "opening_control":
    session.set_opening_control(0, "Apertura automatizada para P09")

env.cr.commit()
print(
    "BOOTSTRAP_OK "
    f"company_id={company.id} config_id={config.id} session_id={session.id} "
    f"product_ids={','.join(str(product.id) for product in products)} "
    f"payment_method_id={payment_method.id}"
)
