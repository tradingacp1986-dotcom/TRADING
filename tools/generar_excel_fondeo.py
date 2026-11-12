"""Genera Contabilidad_Fondeo_Coberturas.xlsx (solo formulas, sin macros)."""
import sys
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.worksheet.datavalidation import DataValidation
from openpyxl.formatting.rule import FormulaRule
from openpyxl.utils import get_column_letter as L

OUT = sys.argv[1] if len(sys.argv) > 1 else "Contabilidad_Fondeo_Coberturas.xlsx"
N_CTA, N_OP, N_MOV, N_CIA = 200, 1000, 500, 20   # filas disponibles

F = "Arial"
HDR = dict(font=Font(name=F, bold=True, color="FFFFFF"), fill=PatternFill("solid", fgColor="1F3864"),
           alignment=Alignment(horizontal="center", vertical="center", wrap_text=True))
INPUT_FILL = PatternFill("solid", fgColor="FFF2CC")
CALC_FILL = PatternFill("solid", fgColor="EDEDED")
BLUE = Font(name=F, color="0000FF")
BLACK = Font(name=F)
THIN = Side(style="thin", color="BFBFBF")
BORDER = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)
USD = '#,##0.00;[Red]-#,##0.00'
DATE = 'dd/mm/yyyy'

wb = Workbook()

def header(ws, cols, widths):
    for i, (t, w) in enumerate(zip(cols, widths), 1):
        c = ws.cell(1, i, t)
        c.font, c.fill, c.alignment, c.border = HDR["font"], HDR["fill"], HDR["alignment"], BORDER
        ws.column_dimensions[L(i)].width = w
    ws.row_dimensions[1].height = 45
    ws.freeze_panes = "B2"

def style(ws, n, inputs, calcs, fmts):
    """inputs/calcs: letras de columna; fmts: {letra: formato}"""
    for r in range(2, n + 2):
        for col in inputs:
            c = ws[f"{col}{r}"]; c.fill, c.font, c.border = INPUT_FILL, BLUE, BORDER
        for col in calcs:
            c = ws[f"{col}{r}"]; c.fill, c.font, c.border = CALC_FILL, BLACK, BORDER
        for col, f in fmts.items():
            ws[f"{col}{r}"].number_format = f

def dv_list(ws, formula, rng, allow_blank=True):
    dv = DataValidation(type="list", formula1=formula, allow_blank=allow_blank)
    dv.error, dv.errorTitle = "Elige un valor de la lista (hoja Listas).", "Valor no valido"
    ws.add_data_validation(dv); dv.add(rng)

# ---------------- LEEME ----------------
ws = wb.active; ws.title = "LEEME"
ws.column_dimensions["A"].width = 120
lines = [
    ("CONTABILIDAD DE CUENTAS DE FONDEO CON COBERTURAS", True),
    ("", False),
    ("COLORES", True),
    ("Celdas amarillas con texto azul = las rellenas tu. Celdas grises = formulas, NO escribir encima.", False),
    ("Todo en USD. Sin macros: funciona en Excel, Google Sheets y movil.", False),
    ("", False),
    ("ORDEN DE USO", True),
    ("1) Listas: escribe los nombres de tus companias de fondeo (columna A). Puedes cambiar los textos de las otras listas, pero no su posicion.", False),
    ("2) Cuentas: una fila por cuenta. ID unico, compania, tamano, saldo inicial y Estado (lo cambias tu a mano cuando pase de fase, se queme o la cierres).", False),
    ("3) Operaciones: una fila por cobertura entre 2 cuentas (A y B). Por cada cuenta: P&L bruto, comisiones y el margen restante que te muestra la plataforma al terminar.", False),
    ("4) Movimientos: dinero real. Costes (evaluacion, reset, cuota, activacion, datos...) y payouts.", False),
    ("5) Resumen: totales globales y por compania. No se escribe nada ahi.", False),
    ("", False),
    ("COMO SE CALCULA (para que lo compruebes)", True),
    ("Comisiones: se escriben en POSITIVO. Neto = Bruto - Comisiones.", False),
    ("Saldo actual de la cuenta = Saldo inicial + suma de netos de sus operaciones - payouts retirados de esa cuenta (todos los registrados, cobrados o solicitados).", False),
    ("Margen restante (hoja Cuentas) = el ultimo margen que TU anotaste para esa cuenta en Operaciones (la fila mas baja donde aparece). El Excel no calcula drawdown.", False),
    ("Importe en Movimientos: siempre en POSITIVO. En un payout, Importe = lo retirado de la cuenta y '% para ti' = tu parte del reparto; Neto cobrado = Importe x %.", False),
    ("Resultado real = Payouts con estado 'Cobrado' - Costes. El P&L de las operaciones NO es dinero real y no entra en ese resultado.", False),
    ("Costes sin cuenta concreta (p.ej. datos de mercado): deja la Cuenta vacia y pon la Compania; cuentan en el Resumen por compania pero no en ninguna cuenta.", False),
    ("", False),
    ("EJEMPLO", True),
    ("Las filas con 'EJEMPLO' / 'EJ-A' / 'EJ-B' son datos ficticios para mostrar el formato. Borra su contenido (solo celdas amarillas) antes de empezar.", False),
    ("", False),
    (f"CAPACIDAD: {N_CIA} companias, {N_CTA} cuentas, {N_OP} operaciones, {N_MOV} movimientos.", False),
]
for i, (t, b) in enumerate(lines, 1):
    c = ws.cell(i, 1, t); c.font = Font(name=F, bold=b, size=14 if i == 1 else 11)
    c.alignment = Alignment(wrap_text=True, vertical="top")

# ---------------- Listas ----------------
ls = wb.create_sheet("Listas")
header(ls, ["Companias de fondeo (rellenar)", "", "Estados de cuenta", "", "Tipos de movimiento", "", "Estado del payout"],
       [32, 3, 22, 3, 26, 3, 20])
ls.freeze_panes = "A2"
for r in range(2, N_CIA + 2):
    c = ls[f"A{r}"]; c.fill, c.font, c.border = INPUT_FILL, BLUE, BORDER
ls["A2"] = "EJEMPLO Fondeo"
estados = ["Evaluacion", "Fondeada", "Quemada", "Cerrada"]
tipos = ["Payout", "Compra evaluacion", "Reset", "Cuota mensual", "Activacion fondeada", "Datos de mercado", "Otro coste"]
pstat = ["Solicitado", "Cobrado"]
for col, vals in (("C", estados), ("E", tipos), ("G", pstat)):
    for i, v in enumerate(vals, 2):
        c = ls[f"{col}{i}"]; c.value, c.font, c.border = v, BLUE, BORDER
ls["E10"] = "La 1a fila de Tipos (E2) es SIEMPRE el payout; las demas son costes."
ls["G5"] = "G3 es el estado que cuenta como dinero cobrado."
ls["C7"] = "Orden fijo: C2 evaluacion, C3 fondeada, C4 quemada, C5 cerrada."
for a in ("E10", "G5", "C7"):
    ls[a].font = Font(name=F, italic=True, size=9)
CIAS = f"Listas!$A$2:$A${N_CIA+1}"
PAY, COB = "Listas!$E$2", "Listas!$G$3"

# ---------------- Cuentas ----------------
cu = wb.create_sheet("Cuentas")
header(cu, ["ID cuenta", "Compania", "Tamano cuenta (USD)", "Estado", "Fecha alta", "Saldo inicial (USD)",
            "No. operaciones", "P&L bruto acum.", "Comisiones acum.", "P&L neto acum.",
            "Payouts retirados de la cuenta", "Saldo actual", "Margen restante (ultimo anotado)",
            "Costes pagados", "Payouts cobrados (neto)", "Resultado real (cobrado - costes)", "Notas"],
       [16, 22, 14, 14, 12, 14, 12, 14, 14, 14, 16, 14, 18, 14, 16, 18, 30])
oe, me = N_OP + 1, N_MOV + 1
OA, OB = f"Operaciones!$D$2:$D${oe}", f"Operaciones!$I$2:$I${oe}"
def op(c): return f"Operaciones!${c}$2:${c}${oe}"
def mv(c): return f"Movimientos!${c}$2:${c}${me}"
for r in range(2, N_CTA + 2):
    a = f"$A{r}"
    g = lambda body: f'=IF({a}="","",{body})'
    cu[f"G{r}"] = g(f"COUNTIF({OA},{a})+COUNTIF({OB},{a})")
    cu[f"H{r}"] = g(f"SUMIFS({op('E')},{OA},{a})+SUMIFS({op('J')},{OB},{a})")
    cu[f"I{r}"] = g(f"SUMIFS({op('F')},{OA},{a})+SUMIFS({op('K')},{OB},{a})")
    cu[f"J{r}"] = g(f"H{r}-I{r}")
    cu[f"K{r}"] = g(f"SUMIFS({mv('E')},{mv('C')},{a},{mv('D')},{PAY})")
    cu[f"L{r}"] = g(f"F{r}+J{r}-K{r}")
    ra = f"SUMPRODUCT(MAX(({OA}={a})*ROW({OA})))"
    rb = f"SUMPRODUCT(MAX(({OB}={a})*ROW({OB})))"
    cu[f"M{r}"] = g(f'IF(MAX({ra},{rb})=0,"",IF({ra}>={rb},INDEX(Operaciones!$H$1:$H${oe},{ra}),INDEX(Operaciones!$M$1:$M${oe},{rb})))')
    cu[f"N{r}"] = g(f'SUMIFS({mv("E")},{mv("C")},{a},{mv("D")},"<>"&{PAY})')
    cu[f"O{r}"] = g(f"SUMIFS({mv('G')},{mv('C')},{a},{mv('H')},{COB})")
    cu[f"P{r}"] = g(f"O{r}-N{r}")
style(cu, N_CTA, "ABCDEFQ", "GHIJKLMNOP",
      {**{c: USD for c in "CFHIJKLMNOP"}, "E": DATE})
dv_list(cu, f"={CIAS}", f"B2:B{N_CTA+1}")
dv_list(cu, "=Listas!$C$2:$C$5", f"D2:D{N_CTA+1}")
# fila gris-roja si quemada / cerrada
cu.conditional_formatting.add(f"A2:Q{N_CTA+1}", FormulaRule(
    formula=['OR($D2=Listas!$C$4,$D2=Listas!$C$5)'], font=Font(name=F, color="9C0006", strike=True)))
for r, idc in ((2, "EJ-A"), (3, "EJ-B")):
    cu[f"A{r}"], cu[f"B{r}"], cu[f"C{r}"], cu[f"D{r}"], cu[f"F{r}"] = idc, "EJEMPLO Fondeo", 50000, "Evaluacion", 50000
    cu[f"Q{r}"] = "EJEMPLO - borrar"
cu.auto_filter.ref = f"A1:Q{N_CTA+1}"

# ---------------- Operaciones ----------------
o = wb.create_sheet("Operaciones")
header(o, ["No.", "Fecha", "Instrumento", "Cuenta A", "A: P&L bruto", "A: comisiones (+)", "A: P&L neto",
           "A: margen restante (a mano)", "Cuenta B", "B: P&L bruto", "B: comisiones (+)", "B: P&L neto",
           "B: margen restante (a mano)", "Neto conjunto A+B", "Notas"],
       [6, 12, 13, 16, 13, 13, 13, 16, 16, 13, 13, 13, 16, 15, 30])
for r in range(2, oe + 1):
    o[f"A{r}"] = f'=IF(D{r}="","",ROW()-1)'
    o[f"G{r}"] = f'=IF(D{r}="","",E{r}-F{r})'
    o[f"L{r}"] = f'=IF(I{r}="","",J{r}-K{r})'
    o[f"N{r}"] = f'=IF(OR(D{r}="",I{r}=""),"",G{r}+L{r})'
style(o, N_OP, "BCDEFHIJKMO", "AGLN", {**{c: USD for c in "EFGHJKLMN"}, "B": DATE})
ids = f"=Cuentas!$A$2:$A${N_CTA+1}"
dv_list(o, ids, f"D2:D{oe}"); dv_list(o, ids, f"I2:I{oe}")
o.conditional_formatting.add(f"D2:I{oe}", FormulaRule(
    formula=['AND($D2<>"",$D2=$I2)'], fill=PatternFill("solid", bgColor="FFC7CE")))
o["D2"], o["E2"], o["F2"], o["H2"] = "EJ-A", 1500, 20, 2000
o["I2"], o["J2"], o["K2"], o["M2"] = "EJ-B", -1500, 20, 480
o["C2"], o["O2"] = "EJEMPLO", "EJEMPLO - borrar"

# ---------------- Movimientos ----------------
m = wb.create_sheet("Movimientos")
header(m, ["Fecha", "Compania", "Cuenta (opcional)", "Tipo", "Importe USD (+)", "% para ti (solo payout)",
           "Neto cobrado (payout)", "Estado payout", "Notas"], [12, 22, 18, 22, 15, 14, 16, 14, 34])
for r in range(2, me + 1):
    m[f"G{r}"] = f'=IF(D{r}={PAY},E{r}*F{r},"")'
style(m, N_MOV, "ABCDEFHI", "G", {"A": DATE, "E": USD, "F": "0%", "G": USD})
dv_list(m, f"={CIAS}", f"B2:B{me}"); dv_list(m, ids, f"C2:C{me}")
dv_list(m, "=Listas!$E$2:$E$8", f"D2:D{me}"); dv_list(m, "=Listas!$G$2:$G$3", f"H2:H{me}")
m["B2"], m["C2"], m["D2"], m["E2"], m["I2"] = "EJEMPLO Fondeo", "EJ-A", "Compra evaluacion", 100, "EJEMPLO - borrar"

# ---------------- Resumen ----------------
s = wb.create_sheet("Resumen")
s["A1"] = "RESUMEN GLOBAL (USD)"; s["A1"].font = Font(name=F, bold=True, size=14)
ce = N_CTA + 1
glob = [
    ("Cuentas en evaluacion", f"=COUNTIF(Cuentas!$D$2:$D${ce},Listas!$C$2)", "0"),
    ("Cuentas fondeadas", f"=COUNTIF(Cuentas!$D$2:$D${ce},Listas!$C$3)", "0"),
    ("Cuentas quemadas", f"=COUNTIF(Cuentas!$D$2:$D${ce},Listas!$C$4)", "0"),
    ("Cuentas cerradas", f"=COUNTIF(Cuentas!$D$2:$D${ce},Listas!$C$5)", "0"),
    ("Costes pagados", f'=SUMIFS({mv("E")},{mv("D")},"<>"&{PAY})', USD),
    ("Payouts cobrados (neto)", f"=SUMIFS({mv('G')},{mv('H')},{COB})", USD),
    ("Payouts pendientes de cobro (neto)", f"=SUM({mv('G')})-B8", USD),
    ("RESULTADO REAL (cobrado - costes)", "=B8-B7", USD),
    ("P&L neto de todas las operaciones (simulado, no es dinero real)", f"=SUM({op('G')})+SUM({op('L')})", USD),
    ("Comisiones totales de las operaciones", f"=SUM({op('F')})+SUM({op('K')})", USD),
]
for i, (lab, f, fmt) in enumerate(glob, 3):
    s[f"A{i}"] = lab; s[f"A{i}"].font = Font(name=F, bold=(i == 10)); s[f"A{i}"].border = BORDER
    c = s[f"B{i}"]; c.value, c.number_format, c.font, c.fill, c.border = f, fmt, Font(name=F, bold=(i == 10)), CALC_FILL, BORDER
H0 = 15
cols = ["Compania", "Evaluacion", "Fondeadas", "Quemadas", "Cerradas", "Costes pagados",
        "Payouts cobrados", "Payouts pendientes", "Resultado real", "P&L neto operaciones (simulado)"]
for i, (t, w) in enumerate(zip(cols, [60, 14, 12, 12, 12, 15, 16, 16, 15, 18]), 1):
    c = s.cell(H0, i, t); c.font, c.fill, c.alignment, c.border = HDR["font"], HDR["fill"], HDR["alignment"], BORDER
    s.column_dimensions[L(i)].width = w
s.row_dimensions[H0].height = 45
CB, CD = f"Cuentas!$B$2:$B${ce}", f"Cuentas!$D$2:$D${ce}"
for k in range(N_CIA):
    r = H0 + 1 + k
    a = f"$A{r}"
    g = lambda body: f'=IF({a}="","",{body})'
    s[f"A{r}"] = f'=IF(Listas!A{k+2}="","",Listas!A{k+2})'
    for j, col in enumerate("BCDE"):
        s[f"{col}{r}"] = g(f"COUNTIFS({CB},{a},{CD},Listas!$C${j+2})")
    s[f"F{r}"] = g(f'SUMIFS({mv("E")},{mv("B")},{a},{mv("D")},"<>"&{PAY})')
    s[f"G{r}"] = g(f"SUMIFS({mv('G')},{mv('B')},{a},{mv('H')},{COB})")
    s[f"H{r}"] = g(f"SUMIFS({mv('G')},{mv('B')},{a})-G{r}")
    s[f"I{r}"] = g(f"G{r}-F{r}")
    s[f"J{r}"] = g(f"SUMIFS(Cuentas!$J$2:$J${ce},{CB},{a})")
    for col in "ABCDEFGHIJ":
        c = s[f"{col}{r}"]; c.font, c.fill, c.border = BLACK, CALC_FILL, BORDER
        if col in "FGHIJ": c.number_format = USD

wb.save(OUT)
print("OK", OUT)
