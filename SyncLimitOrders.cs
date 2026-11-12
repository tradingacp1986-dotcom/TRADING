#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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
        private readonly Dictionary<string, double> lastLevel = new Dictionary<string, double>();
        private readonly object sync = new object();

        // Órdenes ya vistas (para no crear la contraria dos veces)
        private readonly HashSet<Order> handled = new HashSet<Order>();
        // original -> contraria, y contraria -> original
        private readonly Dictionary<Order, Order> contraOf = new Dictionary<Order, Order>();
        private readonly Dictionary<Order, Order> originalOf = new Dictionary<Order, Order>();
        private const string ContraName = "SyncContra";
        // Firmas de las contrarias que acabamos de enviar (evita que una contraria genere otra contraria)
        private readonly List<string> pendingContra = new List<string>();

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
            }
            else if (State == State.DataLoaded)
            {
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
            return acc.Name + "|" + (buys ? "B" : "S") + "|" + type + "|" + quantity
                + "|" + (long)Math.Round(limitPrice / tick) + "|" + (long)Math.Round(stopPrice / tick);
        }

        // Nombre de la plantilla ATM seleccionada en el Chart Trader de este gráfico (null si no hay ninguna).
        // Debe llamarse desde el hilo de la interfaz.
        private string GetChartTraderAtm()
        {
            if (ChartControl == null)
                return null;

            System.Windows.Window window = System.Windows.Window.GetWindow(ChartControl.Parent);
            if (window == null)
                return null;

            NinjaTrader.Gui.NinjaScript.AtmStrategy.AtmStrategySelector selector =
                window.FindFirst("ChartTraderControlATMStrategySelector") as NinjaTrader.Gui.NinjaScript.AtmStrategy.AtmStrategySelector;
            if (selector == null)
                return null;

            NinjaTrader.NinjaScript.AtmStrategy atm = selector.SelectedAtmStrategy;
            if (atm == null || string.IsNullOrEmpty(atm.Template))
                return null;

            return atm.Template;
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
                        lock (sync) lastLevel[o.OrderId] = LevelOf(o.OrderType, o.LimitPrice, o.StopPrice);
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

        // Crea en la otra cuenta una orden de sentido contrario, al mismo precio y con la misma cantidad
        private void SubmitOpposite(Account target, Order original, OrderType contraType, double price, int quantity)
        {
            price = Instrument.MasterInstrument.RoundToTickSize(price);

            bool originalBuys = original.OrderAction == OrderAction.Buy || original.OrderAction == OrderAction.BuyToCover;
            OrderAction action = originalBuys ? OrderAction.Sell : OrderAction.Buy;

            double limitPrice = price;
            double stopPrice  = contraType == OrderType.StopLimit ? price : 0;

            if (UseChartTraderAtm && ChartControl != null)
            {
                // El ATM seleccionado en el Chart Trader solo se puede leer desde el hilo de la interfaz
                ChartControl.Dispatcher.InvokeAsync((Action)(() =>
                {
                    try
                    {
                        SendOpposite(target, original, action, contraType, quantity, limitPrice, stopPrice, GetChartTraderAtm());
                    }
                    catch (Exception ex)
                    {
                        Print("SyncLimitOrders error al enviar la contraria: " + ex.Message);
                    }
                }));
            }
            else
                SendOpposite(target, original, action, contraType, quantity, limitPrice, stopPrice, null);
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
        [Display(Name = "Crear orden contraria", Description = "Activo: una Limit nueva crea una StopLimit contraria en la otra cuenta, y una StopLimit nueva crea una Limit contraria, al mismo precio y cantidad.", Order = 4, GroupName = "Parámetros")]
        public bool CreateOpposite { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Contraria con el ATM del Chart Trader", Description = "Activo: la orden contraria se envía con la estrategia ATM seleccionada en el Chart Trader de este gráfico. Si no hay ninguna seleccionada, se envía sin ATM.", Order = 5, GroupName = "Parámetros")]
        public bool UseChartTraderAtm { get; set; }
        #endregion
    }
}
