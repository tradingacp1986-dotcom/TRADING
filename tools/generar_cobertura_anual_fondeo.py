"""Genera COBERTURA_ANUAL_FONDEO.xlsx: seguimiento anual de coberturas solo entre
cuentas de fondeo (sin cuenta real). Solo formulas, sin macros."""
import sys
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.formatting.rule import FormulaRule, CellIsRule
from openpyxl.utils import get_column_letter as L

OUT = sys.argv[1] if len(sys.argv) > 1 else "COBERTURA_ANUAL_FONDEO.xlsx"
N_ACC, N_STAGE = 6, 8
MESES = ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO",
         "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"]
CFG, DIA, MEN = "Configuración", "Seguimiento_Diario", "Seguimiento Mensual"
YEAR = f"'{CFG}'!$B$3"

F = "Arial"


def font(b=False, color="000000", size=10, i=False):
    return Font(name=F, bold=b, color=color, size=size, italic=i)


def fill(c):
    return PatternFill("solid", fgColor=c)


NAVY, LBLUE, GREY, YEL, MONTH = fill("1F3864"), fill("D9E1F2"), fill("F2F2F2"), fill("FFF2CC"), fill("305496")
T = Side(style="thin", color="BFBFBF")
BORDER = Border(left=T, right=T, top=T, bottom=T)
USD = '"$"#,##0.00;[Red]("$"#,##0.00);-'
CENTER = Alignment(horizontal="center", vertical="center", wrap_text=True)
GREEN = Font(name=F, bold=True, color="006100")


def put(ws, ref, value=None, fnt=None, fl=None, fmt=None, al=None):
    c = ws[ref]
    if value is not None:
        c.value = value
    c.font = fnt or font()
    c.border = BORDER
    if fl:
        c.fill = fl
    if fmt:
        c.number_format = fmt
    if al:
        c.alignment = al
    return c


wb = Workbook()

# ======================= Configuración =======================
cf = wb.active
cf.title = CFG
for col, w in zip("ABCDEFGHI", [30, 22, 16, 20, 18, 16, 16, 16, 16]):
    cf.column_dimensions[col].width = w
cf["A1"] = "CONFIGURACIÓN — COBERTURA ENTRE CUENTAS DE FONDEO (HASTA 6 COMPAÑÍAS)"
cf["A1"].font = font(True, size=14)
cf["A2"] = "Celdas amarillas = para introducir o modificar. El nombre de cada compañía se copia solo a las demás hojas."
cf["A2"].font = font(i=True)
put(cf, "A3", "AÑO DEL CALENDARIO", font(True), LBLUE)
put(cf, "B3", 2027, font(True, "0000FF", 12), YEL, "0", CENTER)
cf["C3"] = "Cambia el año y todas las fechas se recalculan (incluido el 29 de febrero)."
cf["C3"].font = font(i=True)

# Datos copiados del archivo de referencia del usuario (COBERTURA_ANUAL_V2.5).
# Topstep y las 2 compañías libres quedan vacías.
EV, FO = "Evaluacion", "Fondeada"
REF = [
    dict(nombre="LUCID", plan="LUCID FLEX", reglas=["FLEX 50K", 105, 3000, 2000, 0, 0.9, 150],
         etapas=[f"{EV} 1er dia", f"{EV} 2do dia", f"{FO} 1er dia", f"{FO} 2do dia", f"{FO} 3er dia",
                 f"{FO} 4to dia", f"{FO} 5to dia"],
         contratos=[16, 16, 16, 10, 10, 10, 10], tp=[190, 190, 400, 44, 44, 44, 44],
         sl=[255, 255, 255, 640, 680, 720, 760]),
    dict(nombre="TRADEIFY", reglas=["SELECT 50K", 109, 3000, 2000, 0, 0.9, 150],
         etapas=[f"{EV} 1er dia", f"{EV} 2do dia", f"{EV} 3er dia", f"{FO} 1er dia", f"{FO} 2do dia",
                 f"{FO} 3er dia", f"{FO} 4to dia", f"{FO} 5to dia"],
         contratos=[20] * 8, tp=[104, 104, 104, 414, 29, 29, 29, 29],
         sl=[205, 205, 205, 205, 410, 435, 460, 485]),
    dict(nombre="MFFU", reglas=["Rapid EOD", 125.4, None, None, None, None, None],
         etapas=[f"{EV} 1er dia", f"{EV} 2do dia", f"{EV} 3er dia", f"{EV} 4to dia", f"{FO} 1er dia"],
         contratos=[18, 18, 18, 18, 20], tp=[104, 104, 104, 38, 414], sl=[228, 228, 228, 300, 205]),
    dict(nombre="TOPSTEP"), dict(), dict(),
]
BLK, S0 = 11, 5


def name_cell(j):
    return f"'{CFG}'!$B${S0 + j * BLK + 1}"


for j, d in enumerate(REF):
    s = S0 + j * BLK
    cf.merge_cells(f"A{s}:I{s}")
    put(cf, f"A{s}", f"COMPAÑÍA {j+1}", font(True, "FFFFFF", 11), NAVY)
    put(cf, f"A{s+1}", "Nombre de compañía", font(True), LBLUE)
    put(cf, f"B{s+1}", d.get("nombre"), font(True, "0000FF"), YEL)
    put(cf, f"D{s+1}", "Plan / Cuenta", font(True), LBLUE)
    put(cf, f"E{s+1}", d.get("plan"), font(color="0000FF"), YEL)
    heads = ["Tipo / tamaño de cuenta", "Costo ($)", "Objetivo / Target ($)", "Max Drawdown ($)",
             "Daily Loss ($)", "Split (%)", "Días mínimos", "Notas"]
    fm = [None, USD, USD, USD, USD, "0%", "0", None]
    reg = d.get("reglas", [None] * 7) + [None]
    for k, col in enumerate("ABCDEFGH"):
        put(cf, f"{col}{s+2}", heads[k], font(True), LBLUE, al=CENTER)
        put(cf, f"{col}{s+3}", reg[k], font(color="0000FF"), YEL, fm[k])
    cf.merge_cells(f"A{s+4}:I{s+4}")
    put(cf, f"A{s+4}", "PARÁMETROS ATM / GESTIÓN", font(True), GREY)
    put(cf, f"A{s+5}", "Configuración", font(True), LBLUE)
    for r, lab in ((6, "Contratos (micros)"), (7, "Take Profit (Ticks)"), (8, "Stop Loss (Ticks)")):
        put(cf, f"A{s+r}", lab, font(True), LBLUE)
    for k in range(N_STAGE):
        col = L(2 + k)

        def g(key):
            v = d.get(key) or []
            return v[k] if k < len(v) else None

        put(cf, f"{col}{s+5}", g("etapas"), font(True, "0000FF"), YEL, al=CENTER)
        put(cf, f"{col}{s+6}", g("contratos"), font(color="0000FF"), YEL, "0")
        put(cf, f"{col}{s+7}", g("tp"), font(color="0000FF"), YEL, "0")
        put(cf, f"{col}{s+8}", g("sl"), font(color="0000FF"), YEL, "0")
r = S0 + N_ACC * BLK + 1
for i, t in enumerate([
    "NOTAS",
    "Origen de los datos: reglas, contratos, TP y SL de LUCID, TRADEIFY y MFFU copiados de tu archivo "
    "COBERTURA_ANUAL_V2.5 (ahí pensados para fondeo contra real). Revísalos.",
    "TOPSTEP y las compañías 5 y 6 están vacías: rellénalas tú.",
    "Se quitó la fila 'Contratos Real' y la proyección de resultado de la cuenta real. La proyección fondeo "
    "contra fondeo está pendiente de que definas la fórmula.",
]):
    cf[f"A{r+i}"] = t
    cf[f"A{r+i}"].font = font(i == 0, i=(i > 0))

# ======================= Seguimiento_Diario =======================
d = wb.create_sheet(DIA)
d.column_dimensions["A"].width = 30
d.column_dimensions["B"].width = 14
for c in range(3, 34):
    d.column_dimensions[L(c)].width = 10.5
d.freeze_panes = "C1"
d["A1"] = ("Costos en NEGATIVO, cobros en POSITIVO. Margen restante: lo que le queda a la cuenta, "
           "anotado a mano el día que opera. Amarillo = rellenar.")
d["A1"].font = font(i=True)
MB = 7 * N_ACC + 7          # filas por mes
DAYS = [L(c) for c in range(3, 34)]
tot = {"costo": [], "cobro": [], "gan": [], "margen": [], "rows": []}
for m in range(12):
    R = 2 + m * MB
    put(d, f"A{R}", MESES[m], font(True, "FFFFFF", 12), MONTH)
    put(d, f"B{R}", None, fl=MONTH)
    for k, col in enumerate(DAYS, 1):
        put(d, f"{col}{R}", f'=IF(MONTH(DATE({YEAR},{m+1},{k}))={m+1},DATE({YEAR},{m+1},{k}),"")',
            font(True), LBLUE, "ddd dd", CENTER)
    d.conditional_formatting.add(f"C{R}:AG{R}", FormulaRule(
        formula=[f'AND(C${R}<>"",WEEKDAY(C${R},2)>5)'], fill=PatternFill("solid", bgColor="BFBFBF")))
    for key in ("costo", "cobro", "gan", "margen"):
        tot[key].append([])
    for j in range(N_ACC):
        t = R + 1 + 7 * j
        rd, rc, rm, rp, rg = t + 1, t + 2, t + 3, t + 4, t + 5
        put(d, f"A{t}", f"Cuenta {j+1}", font(True, "FFFFFF"), NAVY)
        put(d, f"B{t}", "TOTAL", font(True), LBLUE, al=CENTER)
        d.merge_cells(f"C{t}:AG{t}")
        put(d, f"C{t}", f'=IF({name_cell(j)}="","",{name_cell(j)})', font(True), LBLUE, al=CENTER)
        for rr, lab in ((rd, "Día de la cuenta"), (rc, "Costo Cuenta ($)"), (rm, "Margen restante ($)"),
                        (rp, "Cobro de Fondeada ($)"), (rg, "GANANCIA TOTAL ($)")):
            put(d, f"A{rr}", lab, font(True), GREY if rr == rg else LBLUE)
        put(d, f"B{rd}", None, fl=GREY)
        put(d, f"B{rc}", f"=SUM(C{rc}:AG{rc})", font(True), GREY, USD)
        put(d, f"B{rm}", f'=IFERROR(LOOKUP(9.99E+307,C{rm}:AG{rm}),"")', font(True), GREY, USD)
        put(d, f"B{rp}", f"=SUM(C{rp}:AG{rp})", font(True), GREY, USD)
        put(d, f"B{rg}", f"=SUM(B{rc},B{rp})", font(True), GREY, USD)
        for k, col in enumerate(DAYS):
            p = DAYS[k - 1] if k else None
            put(d, f"{col}{rd}", "=1" if not k else f'=IF({col}${R}="","",IF({p}{rg}>0,1,{p}{rd}+1))',
                font(), GREY, "0", CENTER)
            for rr in (rc, rm, rp):
                put(d, f"{col}{rr}", None, font(color="0000FF"), YEL, USD)
            put(d, f"{col}{rg}", f"=SUM({col}{rc},{col}{rp})" if not k else
                f'=IF({col}${R}="","",SUM({col}{rc},{col}{rp})+IF({p}{rg}>0,0,{p}{rg}))',
                font(True), GREY, USD)
        d.conditional_formatting.add(f"B{rg}:AG{rg}", CellIsRule(operator="greaterThan", formula=["0"], font=GREEN))
        tot["costo"][m].append(f"B{rc}")
        tot["cobro"][m].append(f"B{rp}")
        tot["gan"][m].append(f"B{rg}")
        tot["margen"][m].append(f"B{rm}")
    e = R + 1 + 7 * N_ACC
    for i, (lab, key) in enumerate((("Total Compra de cuentas ($)", "costo"),
                                    ("Total Cobros fondeadas ($)", "cobro"), ("GANANCIA TOTAL ($)", "gan"))):
        put(d, f"A{e+i}", lab, font(True, "FFFFFF"), NAVY)
        put(d, f"B{e+i}", "=SUM(" + ",".join(tot[key][m]) + ")", font(True), GREY, USD)
    tot["rows"].append(e)

# ======================= Seguimiento Mensual =======================
s = wb.create_sheet(MEN)
for col, w in zip("ABCDEFG", [26, 18, 18, 18, 22, 18, 18]):
    s.column_dimensions[col].width = w
Q = f"{DIA}!"
s["A1"] = "RESUMEN MENSUAL"
s["A1"].font = font(True, size=14)
for k, h in enumerate(["Mes", "Cobro de cuentas", "Costo cuentas", "Resultado neto"]):
    put(s, f"{'ABCD'[k]}2", h, font(True, "FFFFFF"), NAVY, al=CENTER)
for m in range(12):
    r, e = 3 + m, tot["rows"][m]
    put(s, f"A{r}", f"=DATE({YEAR},{m+1},1)", font(True), LBLUE, "mmmm yyyy")
    put(s, f"B{r}", f"={Q}B{e+1}", fmt=USD)
    put(s, f"C{r}", f"={Q}B{e}", fmt=USD)
    put(s, f"D{r}", f"=B{r}+C{r}", font(True), GREY, USD)
put(s, "A15", "TOTAL", font(True, "FFFFFF"), NAVY)
for col in "BCD":
    put(s, f"{col}15", f"=SUM({col}3:{col}14)", font(True), GREY, USD)
s.conditional_formatting.add("D3:D15", CellIsRule(operator="greaterThan", formula=["0"], font=GREEN))

s["A18"] = "RESUMEN ANUAL POR CUENTA"
s["A18"].font = font(True, size=14)
for k, h in enumerate(["Cuenta", "Costo total", "Cobros total", "Resultado neto", "Último margen anotado"]):
    put(s, f"{'ABCDE'[k]}19", h, font(True, "FFFFFF"), NAVY, al=CENTER)
G0 = 31   # fila de cabecera de la tabla de márgenes
for j in range(N_ACC):
    r = 20 + j
    put(s, f"A{r}", f'=IF({name_cell(j)}="","Cuenta {j+1} (libre)",{name_cell(j)})', font(True), LBLUE)
    put(s, f"B{r}", "=" + "+".join(Q + tot["costo"][m][j] for m in range(12)), fmt=USD)
    put(s, f"C{r}", "=" + "+".join(Q + tot["cobro"][m][j] for m in range(12)), fmt=USD)
    put(s, f"D{r}", f"=B{r}+C{r}", font(True), GREY, USD)
    put(s, f"E{r}", f"={L(2 + j)}{G0+13}", font(True), GREY, USD)
put(s, f"A{20+N_ACC}", "TOTAL", font(True, "FFFFFF"), NAVY)
for col in "BCD":
    put(s, f"{col}{20+N_ACC}", f"=SUM({col}20:{col}{19+N_ACC})", font(True), GREY, USD)
s.conditional_formatting.add(f"D20:D{20+N_ACC}", CellIsRule(operator="greaterThan", formula=["0"], font=GREEN))

s[f"A{G0-1}"] = "MARGEN RESTANTE: ÚLTIMO ANOTADO DE CADA MES"
s[f"A{G0-1}"].font = font(True, size=14)
put(s, f"A{G0}", "Mes", font(True, "FFFFFF"), NAVY, al=CENTER)
for j in range(N_ACC):
    col = L(2 + j)
    put(s, f"{col}{G0}", f"=A{20+j}", font(True, "FFFFFF"), NAVY, al=CENTER)
    for m in range(12):
        put(s, f"{col}{G0+1+m}", f"={Q}{tot['margen'][m][j]}", fmt=USD)
    put(s, f"{col}{G0+13}", f'=IFERROR(LOOKUP(9.99E+307,{col}{G0+1}:{col}{G0+12}),"")', font(True), GREY, USD)
for m in range(12):
    put(s, f"A{G0+1+m}", f"=A{3+m}", font(True), LBLUE, "mmmm yyyy")
put(s, f"A{G0+13}", "ÚLTIMO ANOTADO (AÑO)", font(True, "FFFFFF"), NAVY)

wb.move_sheet(CFG, offset=2)   # orden como el archivo de referencia: Diario, Mensual, Configuración
wb.active = 0
wb.save(OUT)
print("OK", OUT)
