#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NinjaTrader.Cbi;
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

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name            = "SyncLimitOrders";
                Description     = "Sincroniza el nivel de órdenes Limit y StopLimit entre dos cuentas";
                Calculate       = Calculate.OnBarClose;
                IsOverlay       = true;
                AccountA        = "Sim101";
                AccountB        = "Sim102";
                UseStopPrice    = true;
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

        private void SeedExistingOrders(Account acc)
        {
            lock (acc.Orders)
            {
                foreach (Order o in acc.Orders)
                {
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
                        lock (sync) lastLevel.Remove(order.OrderId);
                    return;
                }

                Account source = (Account)sender;

                // Los stops y targets (y cualquier salida) no se sincronizan
                if (!IsEntry(source, order))
                    return;

                double newLevel = LevelOf(order.OrderType, e.LimitPrice, e.StopPrice);
                bool changed;

                lock (sync)
                {
                    double old;
                    changed = lastLevel.TryGetValue(order.OrderId, out old) && Math.Abs(old - newLevel) > 1e-9;
                    lastLevel[order.OrderId] = newLevel;
                }

                if (!changed)
                    return;

                Account target = source == accA ? accB : accA;
                MirrorToTarget(target, order, newLevel);
            }
            catch (Exception ex)
            {
                Print("SyncLimitOrders error: " + ex.Message);
            }
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

            Order t = matches[0];
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

            target.Change(new[] { t });
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
        #endregion
    }
}
