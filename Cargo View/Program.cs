using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Gui;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game.ModAPI.Ingame;
using VRage.Library;
using VRageRender;

namespace IngameScript {
    partial class Program : MyGridProgram {
        private const string _version = "1.1";
        private readonly List<IMyTextPanel> _lcds = new List<IMyTextPanel>();
        private readonly List<IMyTerminalBlock> _cargos = new List<IMyTerminalBlock>();
        private readonly List<IMyAssembler> _assemblers = new List<IMyAssembler>();
        private readonly List<IMyGasTank> _oxygen_tanks= new List<IMyGasTank>();
        private readonly List<IMyGasTank> _hydrogen_tanks = new List<IMyGasTank>();
        private readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        private readonly List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
        private readonly List<IMyShipConnector> _connectors = new List<IMyShipConnector>();
        private readonly DateTime _start_time;
        private int _reinit_counter = 0;
        private bool _echoed = false;

        struct Message {
            public string GridName;
            public Dictionary<string, string> Infos;

            public Message(string customName, Dictionary<string, string> infos) {
                this.GridName = customName;
                this.Infos = infos;
            }
        }

        private IMyTerminalBlock LoadBlock(string name) {
            var block = GridTerminalSystem.GetBlockWithName(name);
            if (block == null) {
                throw new Exception("Oh shit! I can't find block: '" + name + "'");
            }
            return block;
        }

        public Program() {
            IGC.RegisterBroadcastListener("cargo info");

            _start_time = DateTime.Now;
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            DateTime.TryParse(Storage, out _start_time);
        }

        public void Save() {
            Storage = _start_time.ToString();
        }

        public void Main(string argument, UpdateType updateSource) {
            if (_reinit_counter <= 0) {
                Reinit();
            }
            _reinit_counter -= 1;

            Dictionary<string, string> infos = new Dictionary<string, string>();
            CompileGridTidbits(infos);
            CompileCargoTypes(infos);
            CompileProductionInfos(infos, true);

            EchoScriptInfo(infos);
            WriteLCDs(infos);
            BroadcastInfos(infos);
        }

        private void Reinit() {
            _reinit_counter = 1000;

            _cargos.Clear();
            GridTerminalSystem.GetBlocksOfType(_cargos,
                block => block.IsSameConstructAs(Me)
                && block.HasInventory && block.IsWorking);

            _assemblers.Clear();
            GridTerminalSystem.GetBlocksOfType(_assemblers,
                block => block.IsSameConstructAs(Me)
                && block.IsWorking);

            _lcds.Clear();
            GridTerminalSystem.GetBlocksOfType(_lcds,
                block => block.IsSameConstructAs(Me)
                && block.CustomName.StartsWith("Cargo Info:"));

            _oxygen_tanks.Clear();
            GridTerminalSystem.GetBlocksOfType(_oxygen_tanks,
                block => block.IsSameConstructAs(Me)
                && block.BlockDefinition.SubtypeName.Contains("Oxygen"));

            _hydrogen_tanks.Clear();
            GridTerminalSystem.GetBlocksOfType(_hydrogen_tanks,
                block => block.IsSameConstructAs(Me)
                && block.BlockDefinition.SubtypeName.Contains("Hydrogen"));

            _batteries.Clear();
            GridTerminalSystem.GetBlocksOfType(_batteries,
                block => block.IsSameConstructAs(Me)
                && block.IsWorking);

            _turrets.Clear();
            GridTerminalSystem.GetBlocksOfType(_turrets,
                block => block.IsSameConstructAs(Me)
                && block.IsWorking);

            _connectors.Clear();
            GridTerminalSystem.GetBlocksOfType(_connectors,
                block => block.IsSameConstructAs(Me)
                && block.IsWorking);
        }

        MyFixedPoint CountFullness<T>(List<T> blocks, Func<T, double> getCurrent, Func<T, double> getMax) where T : IMyTerminalBlock {
            return CountFullness(blocks, a => (float)getCurrent(a), a => (float)getMax(a));
        }

        MyFixedPoint CountFullness<T>(List<T> blocks, Func<T, float> getCurrent, Func<T, float> getMax) where T: IMyTerminalBlock {
            float current = 0;
            float max = 0;

            foreach (T b in blocks.Where(b => b.IsWorking)) {
                current += getCurrent(b);
                max += getMax(b);
            }

            if (max > 0) {
                return (MyFixedPoint)(current / max);
            } else {
                return MyFixedPoint.Zero;
            }
        }

        float QueryInventories(IEnumerable<IMyTerminalBlock> blocks, Func<IMyInventory, MyFixedPoint> f) {
            float result = 0;

            foreach(IMyTerminalBlock block in blocks.Where(b => b.IsWorking)) {
                for (int i = 0; i < block.InventoryCount; ++i) {
                    result += (float)f(block.GetInventory(i));
                }
            }

            return result;
        }

        private void CompileGridTidbits(Dictionary<string, string> infos) {
            string s = Me.CubeGrid.CustomName + " Stats\n\n";

            // the list below should stay sorted

            double elapsedTime = (DateTime.Now - _start_time).TotalDays;
            s += "Assemblers, not producing: " + _assemblers.Where(a => a.IsWorking && !a.IsProducing).Count() + "\n";
            s += "Assemblers, not queued: " + _assemblers.Where(a => a.IsWorking && a.IsQueueEmpty).Count() + "\n";
            s += "Battery input: " + (100 * CountFullness(_batteries, b => b.CurrentInput, b => b.MaxInput)) + "%\n";
            s += "Battery output: " + (100 * CountFullness(_batteries, b => b.CurrentOutput, b => b.MaxOutput)) + "%\n";
            s += "Battery stored: " + (100 * CountFullness(_batteries, b => b.CurrentStoredPower, b => b.MaxStoredPower)) + "%\n";
            s += "Cargo mass: " + ((int)QueryInventories(_cargos, inv => inv.CurrentMass) / 1000) + "t\n";
            s += "Docked ships: " + _connectors.Where(c => c.IsWorking && c.IsConnected).Count() + "\n";
            s += "Hydrogen:" + (100 * CountFullness(_hydrogen_tanks, b => b.FilledRatio * b.Capacity, b => b.Capacity)) + "%\n";
            s += "Oxygen: " + (100 * CountFullness(_oxygen_tanks, b => b.FilledRatio * b.Capacity, b => b.Capacity)) + "%\n";
            s += "Script activity: " + ((DateTime.Now.ToString())) + "\n";
            s += "Script uptime: " + ((DateTime.Now - _start_time).TotalDays) + " days\n";
            s += "Script version: v" + _version + "\n";
            s += "Turrets, idle: " + _turrets.Where(t => t.IsWorking && !t.HasTarget).Count() + "\n";
            s += "Turrets, targeting: " + _turrets.Where(t => t.IsWorking && t.HasTarget).Count() + "\n";

            infos.Add("GridTidbits", s);
        }

        private void CompileCargoTypes(Dictionary<string, string> infos) {
            Dictionary<string, SortedDictionary<string, MyFixedPoint>> cargoCounts = new Dictionary<string, SortedDictionary<string, MyFixedPoint>>();
            List<MyInventoryItem> items = new List<MyInventoryItem>();

            foreach (IMyTerminalBlock cargo in _cargos.Where(c => c.IsWorking)) {
                for (int i = 0; i < cargo.InventoryCount; ++i) {
                    items.Clear();
                    cargo.GetInventory(i).GetItems(items);
                    foreach (MyInventoryItem item in items) {
                        SortedDictionary<string, MyFixedPoint> subtypeCounts;
                        if (!cargoCounts.TryGetValue(item.Type.TypeId, out subtypeCounts)) {
                            subtypeCounts = new SortedDictionary<string, MyFixedPoint>();
                            cargoCounts.Add(item.Type.TypeId, new SortedDictionary<string, MyFixedPoint>());
                        }

                        if (!subtypeCounts.ContainsKey(item.Type.SubtypeId)) {
                            subtypeCounts.Add(item.Type.SubtypeId, item.Amount);
                        } else {
                            subtypeCounts[item.Type.SubtypeId] += item.Amount;
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, SortedDictionary<string, MyFixedPoint>> categoryCounts in cargoCounts) {
                string category = categoryCounts.Key.Substring(categoryCounts.Key.IndexOf("_") + 1);
                WriteInfos(infos, category, categoryCounts.Value);
            }
        }

        private void CompileProductionInfos(Dictionary<string, string> infos, bool clearAssemblers = true) {
            SortedDictionary<string, MyFixedPoint> counts = new SortedDictionary<string, MyFixedPoint>();
            List<MyProductionItem> items = new List<MyProductionItem>();

            foreach (IMyAssembler a in _assemblers.Where(a => a.IsWorking)) {
                a.GetQueue(items);

                foreach (MyProductionItem item in items) {
                    string id = item.BlueprintId.SubtypeName;
                    if (!counts.ContainsKey(id)) {
                        counts.Add(id, item.Amount);
                    } else {
                        counts[id] += item.Amount;
                    }
                }

                if (clearAssemblers && a.IsQueueEmpty) {
                    if (a.Mode == MyAssemblerMode.Assembly && a.InputInventory.CurrentVolume > a.InputInventory.MaxVolume - a.InputInventory.CurrentVolume) {
                        a.Mode = MyAssemblerMode.Disassembly;
                    } else if (a.Mode == MyAssemblerMode.Disassembly && a.InputInventory.ItemCount == 0) {
                        a.Mode = MyAssemblerMode.Assembly;
                    }
                }
            }

            WriteInfos(infos, "Production", counts);
        }

        void WriteInfos(Dictionary<string, string> infos, string category, SortedDictionary<string, MyFixedPoint> counts) {
            string s = category + "\n\n";

            foreach (KeyValuePair<string, MyFixedPoint> subtypeCount in counts) {
                s += subtypeCount.Key + ": " + subtypeCount.Value.ToIntSafe() + "\n";
            }

            infos.Add(category, s);
        }

        void EchoScriptInfo(Dictionary<string, string> infos) {
            if (!_echoed) {
                Echo("Cargo Info v" + _version + "\n\nCargo types:\n" + String.Join(", ", infos.Keys.ToArray()));
                _echoed = true;
            }
        }

        void WriteLCDs(Dictionary<string, string> infos, string gridName = "") {
            foreach (IMyTextPanel lcd in _lcds) {
                if (!lcd.IsFunctional) {
                    continue;
                }

                string[] lcdNameParts = lcd.CustomName.Split(':');
                if (lcdNameParts.Length != 4) {
                    continue;
                }

                if (lcdNameParts[1] != gridName) {
                    continue;
                }

                string[] targetNames = lcdNameParts[2].Split(',');
                string content;
                string s = "";

                foreach (string targetName in targetNames) {
                    if (infos.TryGetValue(targetName, out content)) {
                        s += content + "\n\n";
                    }
                }

                lcd.WriteText(s);
            }
        }

        void ReceiveInfos() {
            List<IMyBroadcastListener> listeners = new List<IMyBroadcastListener>();
            IGC.GetBroadcastListeners(listeners);
            foreach (IMyBroadcastListener listener in listeners.Where(l => l.Tag == "cargo info")) {
                if (listener.HasPendingMessage) {
                    Message message = (Message)listener.AcceptMessage().Data;
                    WriteLCDs(message.Infos, message.GridName);
                }
            }
        }

        void BroadcastInfos(Dictionary<string, string> infos) {
            IGC.SendBroadcastMessage("cargo info", new Message(Me.CubeGrid.CustomName, infos));
        }
    }
}
