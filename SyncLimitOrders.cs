#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui.NinjaScript;
#endregion

// Copia en: Documentos\NinjaTrader 8\bin\Custom\Indicators\SyncLimitOrders.cs
// Luego: NinjaScript Editor > Compile

// Lista desplegable con todas las cuentas disponibles en NinjaTrader
namespace SyncLimitOrdersTools
{
    public class AccountNameListConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        {
            return true;
        }

        // true = solo se puede elegir de la lista, no escribir a mano
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        {
            return true;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            List<string> names;
            lock (Account.All)
                names = Account.All.Select(a => a.Name).OrderBy(n => n).ToList();
            return new StandardValuesCollection(names);
        }
    }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class SyncLimitOrders : Indicator
    {
        private Account accA;
        private Account accB;
        // El estado es COMPARTIDO (static) entre todas las copias del indicador: así, aunque esté cargado en
        // varios gráficos, cada orden se procesa una sola vez y una contraria nunca genera otra contraria.
        private static readonly Dictionary<string, double> lastLevel = new Dictionary<string, double>();
        private static readonly object sync = new object();

        // Órdenes ya vistas (para no crear la contraria dos veces)
        private static readonly HashSet<Order> handled = new HashSet<Order>();
        // original -> contraria, y contraria -> original
        private static readonly Dictionary<Order, Order> contraOf = new Dictionary<Order, Order>();
        private static readonly Dictionary<Order, Order> originalOf = new Dictionary<Order, Order>();
        private const string ContraName = "SyncContra";

        // Clave pública de la licencia (la privada solo la tiene el autor, en el generador de claves)
        private const string LicensePublicKey = "<RSAKeyValue><Modulus>1lSNzOfNdXkEYiNkZ2M6EgfzVhHg36iGja7LLUvNOYh92xYpFuJHqHbEEEWN5HCDNq5jtjbEJklfWw6RexfTnHqF8+7JmhU5Oa4LdHwmyO/QAmO5YqYrwJP2yXMIhhlDMWi7gJhaQ08fLTRI5yJLWQd51gVxHIPz4Hz3+ZE6II/fmx1wrrQXjdRrTfVYEtMZfzzlnaNpDblJ8x/p+o3oltqYlQaylIsuVQdzuxuUkBVzfn8UzTdcGaEqbUPO6G9/KsC0VC7vDogg1N5gOImtzu01ooGaKgCMvZj6seQNtuJ6yn+eLMBh7+blUGSvpgUGNlpQqzOvgLmVvUW5yRet9w==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
        private const string LicenseFileName = "SyncLimitOrders.lic";
        // Firmas de las contrarias que acabamos de enviar (evita que una contraria genere otra contraria)
        private static readonly List<string> pendingContra = new List<string>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name            = "SyncLimitOrders";
                Description     = "Crea la orden StopLimit contraria en la otra cuenta y sincroniza el nivel entre ambas";
                Calculate       = Calculate.OnBarClose;
                IsOverlay       = true;
                AccountA        = "Sim101";
                AccountB        = "Sim102";
                UseStopPrice    = true;
                CreateOpposite  = true;
                UseChartTraderAtm = true;
                LicenseKey      = string.Empty;
            }
            else if (State == State.DataLoaded)
            {
                if (!CheckLicense())
                    return;

                lock (Account.All)
                {
                    accA = Account.All.FirstOrDefault(a => a.Name == AccountA);
                    accB = Account.All.FirstOrDefault(a => a.Name == AccountB);
                }

                if (accA == null || accB == null)
                {
                    Log("SyncLimitOrders: no se encontró una de las cuentas.", LogLevel.Error);
                    return;
                }

                if (accA == accB)
                {
                    Log("SyncLimitOrders: Cuenta A y Cuenta B no pueden ser la misma.", LogLevel.Error);
                    accA = null;
                    accB = null;
                    return;
                }

                SeedExistingOrders(accA);
                SeedExistingOrders(accB);

                accA.OrderUpdate += OnOrderUpdate;
                accB.OrderUpdate += OnOrderUpdate;
            }
            else if (State == State.Terminated)
            {
                if (accA != null) accA.OrderUpdate -= OnOrderUpdate;
                if (accB != null) accB.OrderUpdate -= OnOrderUpdate;
            }
        }

        #region Licencia
        private static string GetMachineId()
        {
            return (NinjaTrader.Cbi.License.MachineId ?? string.Empty).Trim().ToUpperInvariant();
        }

        // La clave es la firma RSA de "SyncLimitOrders|<Machine ID>": solo vale en ese ordenador
        private static bool IsValidKey(string key, string machineId)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrEmpty(machineId))
                return false;

            try
            {
                byte[] signature = Convert.FromBase64String(key.Trim());
                byte[] data = Encoding.UTF8.GetBytes("SyncLimitOrders|" + machineId);

                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(LicensePublicKey);
                    return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Acepta la clave del parámetro o la del archivo SyncLimitOrders.lic de la carpeta de NinjaTrader 8
        private bool CheckLicense()
        {
            string machineId = GetMachineId();
            string file = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, LicenseFileName);

            if (IsValidKey(LicenseKey, machineId))
            {
                // Se guarda para no tener que pegarla cada vez
                try { File.WriteAllText(file, LicenseKey.Trim()); } catch (Exception) { }
                return true;
            }

            try
            {
                if (File.Exists(file) && IsValidKey(File.ReadAllText(file), machineId))
                    return true;
            }
            catch (Exception) { }

            string msg = "SyncLimitOrders: licencia no válida para este ordenador. El indicador NO está funcionando. "
                + "Envía este Machine ID al autor para recibir tu clave: " + machineId;
            Print(msg);
            Log(msg, LogLevel.Error);
            return false;
        }
        #endregion

        #region Helpers
        private static bool IsLive(OrderState s)
        {
            return s == OrderState.Working || s == OrderState.Accepted || s == OrderState.TriggerPending;
        }

        private static bool IsSyncType(Order o)
        {
            return o.OrderType == OrderType.Limit || o.OrderType == OrderType.StopLimit;
        }

        // Nivel de referencia de una orden: Limit -> precio límite; StopLimit -> precio stop (o límite, según parámetro)
        private double LevelOf(OrderType type, double limitPrice, double stopPrice)
        {
            if (type == OrderType.StopLimit)
                return UseStopPrice ? stopPrice : limitPrice;
            return limitPrice;
        }

        // Solo órdenes de ENTRADA: excluye Stop/Target de un ATM y cualquier orden que reduzca la posición abierta
        private static bool IsEntry(Account acc, Order o)
        {
            string n = o.Name ?? string.Empty;
            if (n.StartsWith("Target") || n.StartsWith("Stop"))
                return false;

            MarketPosition mp = MarketPosition.Flat;
            lock (acc.Positions)
            {
                foreach (Position p in acc.Positions)
                {
                    if (p.Instrument.FullName == o.Instrument.FullName)
                    {
                        mp = p.MarketPosition;
                        break;
                    }
                }
            }

            // Dentro de una operación, solo cuenta como entrada la orden que aumenta la posición
            if (mp == MarketPosition.Long)  return o.OrderAction == OrderAction.Buy;
            if (mp == MarketPosition.Short) return o.OrderAction == OrderAction.SellShort;

            return true; // sin posición abierta, toda orden es una entrada
        }

        private string Signature(Account acc, OrderAction action, OrderType type, int quantity, double limitPrice, double stopPrice)
        {
            double tick = Instrument.MasterInstrument.TickSize;
            bool buys = action == OrderAction.Buy || action == OrderAction.BuyToCover;
            return acc.Name + "|" + Instrument.FullName + "|" + (buys ? "B" : "S") + "|" + type + "|" + quantity
                + "|" + (long)Math.Round(limitPrice / tick) + "|" + (long)Math.Round(stopPrice / tick);
        }

        // Lee el Chart Trader del gráfico que tiene elegida la cuenta indicada (mismo instrumento):
        // devuelve el nombre de la plantilla ATM seleccionada (null si no hay) y, en "quantity", sus contratos
        // (0 si no hay gráfico con esa cuenta).
        // NO debe llamarse desde el hilo de la interfaz ni desde un evento de órdenes: consulta cada ventana en su propio hilo.
        private string GetChartTraderAtm(Account account, out int quantity)
        {
            quantity = 0;
            List<System.Windows.Window> windows;
            try
            {
                windows = NinjaTrader.Core.Globals.AllWindows.ToList();
            }
            catch (Exception)
            {
                return null;
            }

            string instrumentName = Instrument.FullName;

            foreach (System.Windows.Window w in windows)
            {
                NinjaTrader.Gui.Chart.Chart chart = w as NinjaTrader.Gui.Chart.Chart;
                if (chart == null)
                    continue;

                try
                {
                    bool accountFound = false;
                    int chartQuantity = 0;

                    string template = (string)chart.Dispatcher.Invoke(new Func<string>(() =>
                    {
                        // Solo gráficos del mismo instrumento
                        NinjaTrader.Gui.Chart.ChartControl cc = chart.ActiveChartControl;
                        if (cc != null && cc.Instrument != null && cc.Instrument.FullName != instrumentName)
                            return null;

                        // Solo el gráfico cuyo Chart Trader tiene seleccionada esta cuenta
                        NinjaTrader.Gui.Tools.AccountSelector accSel =
                            chart.FindFirst("ChartTraderControlAccountSelector") as NinjaTrader.Gui.Tools.AccountSelector;
                        if (accSel == null || accSel.SelectedAccount == null || accSel.SelectedAccount.Name != account.Name)
                            return null;

                        accountFound = true;

                        // Contratos puestos en ese Chart Trader
                        NinjaTrader.Gui.Tools.QuantityUpDown qtySel =
                            chart.FindFirst("ChartTraderControlQuantitySelector") as NinjaTrader.Gui.Tools.QuantityUpDown;
                        if (qtySel != null)
                            chartQuantity = qtySel.Value;

                        NinjaTrader.Gui.NinjaScript.AtmStrategy.AtmStrategySelector atmSel =
                            chart.FindFirst("ChartTraderControlATMStrategySelector") as NinjaTrader.Gui.NinjaScript.AtmStrategy.AtmStrategySelector;
                        if (atmSel == null)
                            return null;

                        NinjaTrader.NinjaScript.AtmStrategy atm = atmSel.SelectedAtmStrategy;
                        if (atm == null || string.IsNullOrEmpty(atm.Template))
                            return null;

                        return atm.Template;
                    }));

                    // El primer gráfico que tenga esa cuenta decide (con o sin ATM)
                    if (accountFound)
                    {
                        quantity = chartQuantity;
                        return template;
                    }
                }
                catch (Exception ex)
                {
                    Print("SyncLimitOrders: no se pudo leer el Chart Trader de una ventana: " + ex.Message);
                }
            }

            Print("SyncLimitOrders: no hay ningún gráfico de " + instrumentName + " con la cuenta " + account.Name
                + " en su Chart Trader; la contraria se envía sin ATM y con la cantidad de la orden original.");
            return null;
        }

        private void SeedExistingOrders(Account acc)
        {
            lock (acc.Orders)
            {
                foreach (Order o in acc.Orders)
                {
                    // Las órdenes que ya existían al cargar el indicador no generan contraria
                    lock (sync) handled.Add(o);

                    if (IsSyncType(o) && IsLive(o.OrderState) && IsEntry(acc, o))
                        lock (sync)
                        {
                            if (!lastLevel.ContainsKey(o.OrderId))
                                lastLevel[o.OrderId] = LevelOf(o.OrderType, o.LimitPrice, o.StopPrice);
                        }
                }
            }
        }
        #endregion

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            try
            {
                Order order = e.Order;
                if (order == null || !IsSyncType(order))
                    return;

                if (order.Instrument.FullName != Instrument.FullName)
                    return;

                if (!IsLive(e.OrderState))
                {
                    if (e.OrderState == OrderState.Filled || e.OrderState == OrderState.Cancelled || e.OrderState == OrderState.Rejected)
                    {
                        Order contra = null;
                        lock (sync)
                        {
                            lastLevel.Remove(order.OrderId);
                            handled.Remove(order);
                            pendingContra.Remove(Signature((Account)sender, order.OrderAction, order.OrderType, e.Quantity, e.LimitPrice, e.StopPrice));

                            Order other;
                            if (contraOf.TryGetValue(order, out other))
                            {
                                // Terminó la original
                                contraOf.Remove(order);
                                originalOf.Remove(other);
                                if (e.OrderState != OrderState.Filled)
                                    contra = other;
                            }
                            else if (originalOf.TryGetValue(order, out other))
                            {
                                // Terminó la contraria: la original se queda como está
                                originalOf.Remove(order);
                                contraOf.Remove(other);
                            }
                        }

                        // Original cancelada (o rechazada) -> se cancela también la contraria
                        if (contra != null && IsLive(contra.OrderState))
                            contra.Account.Cancel(new[] { contra });
                    }
                    return;
                }

                Account source = (Account)sender;
                Account target = source == accA ? accB : accA;

                // Los stops y targets (y cualquier salida) no se sincronizan
                if (!IsEntry(source, order))
                    return;

                double newLevel = LevelOf(order.OrderType, e.LimitPrice, e.StopPrice);
                bool changed;
                bool isNew;
                bool isOurContra;
                Order partner;

                lock (sync)
                {
                    double old;
                    changed = lastLevel.TryGetValue(order.OrderId, out old) && Math.Abs(old - newLevel) > 1e-9;
                    lastLevel[order.OrderId] = newLevel;

                    isNew = handled.Add(order);

                    if (!contraOf.TryGetValue(order, out partner))
                        originalOf.TryGetValue(order, out partner);

                    // ¿Es una contraria que acabamos de enviar nosotros?
                    isOurContra = pendingContra.Remove(Signature(source, order.OrderAction, order.OrderType, e.Quantity, e.LimitPrice, e.StopPrice))
                        || originalOf.ContainsKey(order)
                        || order.Name == ContraName;
                }

                // Orden nueva puesta por el usuario -> crear la contraria en la otra cuenta:
                // Limit -> StopLimit contraria; StopLimit -> Limit contraria
                if (isNew && CreateOpposite && !isOurContra)
                {
                    OrderType contraType = order.OrderType == OrderType.Limit ? OrderType.StopLimit : OrderType.Limit;
                    SubmitOpposite(target, order, contraType, newLevel, e.Quantity);
                    return;
                }

                if (!changed)
                    return;

                if (partner != null)
                {
                    // Orden emparejada: se mueve directamente su pareja
                    if (IsLive(partner.OrderState))
                        MoveOrder(partner, Instrument.MasterInstrument.RoundToTickSize(newLevel));
                }
                else
                    MirrorToTarget(target, order, newLevel);
            }
            catch (Exception ex)
            {
                Print("SyncLimitOrders error: " + ex.Message);
            }
        }

        // Crea en la otra cuenta una orden de sentido contrario al mismo precio, con el ATM y los contratos
        // del Chart Trader de esa otra cuenta
        private void SubmitOpposite(Account target, Order original, OrderType contraType, double price, int quantity)
        {
            price = Instrument.MasterInstrument.RoundToTickSize(price);

            bool originalBuys = original.OrderAction == OrderAction.Buy || original.OrderAction == OrderAction.BuyToCover;
            OrderAction action = originalBuys ? OrderAction.Sell : OrderAction.Buy;

            double limitPrice = price;
            double stopPrice  = contraType == OrderType.StopLimit ? price : 0;

            if (!UseChartTraderAtm)
            {
                SendOpposite(target, original, action, contraType, quantity, limitPrice, stopPrice, null);
                return;
            }

            // El ATM se lee del Chart Trader del gráfico de la cuenta destino. Se hace fuera del evento de
            // órdenes porque hay que consultar el hilo de la interfaz de cada ventana de gráfico.
            System.Threading.ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    int chartQuantity;
                    string atmTemplate = GetChartTraderAtm(target, out chartQuantity);

                    // Contratos del Chart Trader de la cuenta destino; si no se pudieron leer, los de la orden original
                    int contraQuantity = chartQuantity > 0 ? chartQuantity : quantity;

                    Action send = () =>
                    {
                        try
                        {
                            SendOpposite(target, original, action, contraType, contraQuantity, limitPrice, stopPrice, atmTemplate);
                        }
                        catch (Exception ex)
                        {
                            Print("SyncLimitOrders error al enviar la contraria: " + ex.Message);
                        }
                    };

                    if (ChartControl != null)
                        ChartControl.Dispatcher.InvokeAsync(send);
                    else
                        send();
                }
                catch (Exception ex)
                {
                    Print("SyncLimitOrders error al leer el ATM: " + ex.Message);
                }
            });
        }

        private void SendOpposite(Account target, Order original, OrderAction action, OrderType contraType,
            int quantity, double limitPrice, double stopPrice, string atmTemplate)
        {
            // Si la original ya no está viva (se canceló mientras tanto), no se envía nada
            if (!IsLive(original.OrderState))
                return;

            bool withAtm = !string.IsNullOrEmpty(atmTemplate);

            // Para que NinjaTrader arranque un ATM, la orden de entrada debe llamarse "Entry"
            Order contra = target.CreateOrder(original.Instrument, action, contraType, OrderEntry.Manual,
                original.TimeInForce, quantity, limitPrice, stopPrice, string.Empty, withAtm ? "Entry" : ContraName, Core.Globals.MaxDate, null);

            lock (sync)
            {
                handled.Add(contra);
                contraOf[original] = contra;
                originalOf[contra] = original;
                pendingContra.Add(Signature(target, action, contraType, quantity, limitPrice, stopPrice));
            }

            if (withAtm)
                NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(atmTemplate, contra);
            else
                target.Submit(new[] { contra });
        }

        private void MirrorToTarget(Account target, Order sourceOrder, double level)
        {
            level = Instrument.MasterInstrument.RoundToTickSize(level);

            // Cualquier orden de ENTRADA (Limit o StopLimit) viva del mismo instrumento,
            // sin importar si es compra o venta, ni si es Limit o StopLimit
            List<Order> matches;
            lock (target.Orders)
            {
                matches = target.Orders.Where(o =>
                    o.Instrument.FullName == sourceOrder.Instrument.FullName
                    && IsSyncType(o)
                    && IsLive(o.OrderState)
                    && IsEntry(target, o)).ToList();
            }

            // Si hay varias entradas, emparejar por nombre
            if (matches.Count > 1)
                matches = matches.Where(o => o.Name == sourceOrder.Name).ToList();

            if (matches.Count == 0)
                return;

            if (matches.Count > 1)
            {
                Print("SyncLimitOrders: hay más de una orden candidata en " + target.Name + ", no se movió ninguna.");
                return;
            }

            MoveOrder(matches[0], level);
        }

        private void MoveOrder(Order t, double level)
        {
            double currentLevel = LevelOf(t.OrderType, t.LimitPrice, t.StopPrice);

            if (Math.Abs(currentLevel - level) < 1e-9)
                return;

            // Evita el "eco": registramos el nuevo nivel antes de modificar
            lock (sync) lastLevel[t.OrderId] = level;

            if (t.OrderType == OrderType.StopLimit)
            {
                // Mueve stop y límite juntos, conservando la distancia que ya tenían
                double delta = level - currentLevel;
                t.StopPriceChanged  = Instrument.MasterInstrument.RoundToTickSize(t.StopPrice + delta);
                t.LimitPriceChanged = Instrument.MasterInstrument.RoundToTickSize(t.LimitPrice + delta);
            }
            else
            {
                t.LimitPriceChanged = level;
            }

            t.Account.Change(new[] { t });
        }

        #region Properties
        [NinjaScriptProperty]
        [TypeConverter(typeof(SyncLimitOrdersTools.AccountNameListConverter))]
        [Display(Name = "Cuenta A", Order = 1, GroupName = "Parámetros")]
        public string AccountA { get; set; }

        [NinjaScriptProperty]
        [TypeConverter(typeof(SyncLimitOrdersTools.AccountNameListConverter))]
        [Display(Name = "Cuenta B", Order = 2, GroupName = "Parámetros")]
        public string AccountB { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "StopLimit: usar precio stop como nivel", Description = "Activo: el nivel de un StopLimit es su precio stop. Desactivado: es su precio límite.", Order = 3, GroupName = "Parámetros")]
        public bool UseStopPrice { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Crear orden contraria", Description = "Activo: una Limit nueva crea una StopLimit contraria en la otra cuenta, y una StopLimit nueva crea una Limit contraria, al mismo precio.", Order = 4, GroupName = "Parámetros")]
        public bool CreateOpposite { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Contraria con ATM y contratos del Chart Trader", Description = "Activo: la orden contraria se envía con la estrategia ATM y la cantidad de contratos del Chart Trader del gráfico que tiene elegida la cuenta destino. Si no hay gráfico con esa cuenta, se envía sin ATM y con la cantidad de la orden original. Desactivado: sin ATM y con la misma cantidad que la original.", Order = 5, GroupName = "Parámetros")]
        public bool UseChartTraderAtm { get; set; }

        [Display(Name = "Clave de licencia", Description = "Pega aquí la clave que te dio el autor. Solo hace falta la primera vez: queda guardada en este ordenador.", Order = 1, GroupName = "Licencia")]
        public string LicenseKey { get; set; }
        #endregion
    }
}
