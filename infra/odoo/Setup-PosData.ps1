$ErrorActionPreference = 'Stop'
$python = @'
Tax = env['account.tax'].sudo()
tax = Tax.search([('name', '=', 'IVA 15% POS incluido'), ('type_tax_use', '=', 'sale')], limit=1)
if not tax:
    tax = Tax.create({
        'name': 'IVA 15% POS incluido',
        'amount': 15.0,
        'amount_type': 'percent',
        'type_tax_use': 'sale',
        'price_include_override': 'tax_included',
    })
else:
    tax.write({
        'amount': 15.0,
        'amount_type': 'percent',
        'price_include_override': 'tax_included',
        'active': True,
    })
products = env['product.template'].sudo().search([('available_in_pos', '=', True), ('list_price', '>', 0)])
products.write({'taxes_id': [(6, 0, [tax.id])]})
env.cr.commit()
print('CODEX_POS_DATA=tax:' + str(tax.id) + ',products:' + str(len(products)))
'@
$output = $python | docker exec -i proyectopatrones-odoo bash -lc 'odoo shell -d pos_demo_codex --db_host="$HOST" --db_user="$USER" --db_password="$PASSWORD" --no-http'
if ($LASTEXITCODE -ne 0) { throw 'No se pudo configurar el IVA de los productos POS.' }
$output | Where-Object { $_ -like 'CODEX_POS_DATA=*' }
