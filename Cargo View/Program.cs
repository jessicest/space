using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Gui;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRage.Library;
using VRage.Serialization;
using VRageRender;

namespace IngameScript {
    partial class Program : MyGridProgram {
        const string _version = "2.0";
        readonly Dictionary<string, List<TextTarget>> _text_targets = new Dictionary<string, List<TextTarget>>();
        readonly HashSet<string> _categories_seen = new HashSet<string>();
        readonly List<IMyAirVent> _vents = new List<IMyAirVent>();
        readonly List<IMyAssembler> _assemblers = new List<IMyAssembler>();
        readonly List<IMyAssembler> _autoAssemblers = new List<IMyAssembler>();
        readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        readonly List<IMyGasTank> _hydrogen_tanks = new List<IMyGasTank>();
        readonly List<IMyGasTank> _oxygen_tanks = new List<IMyGasTank>();
        readonly List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
        readonly List<IMyProjector> _repair_projectors = new List<IMyProjector>();
        readonly List<IMyShipConnector> _connectors = new List<IMyShipConnector>();
        readonly List<IMyShipToolBase> _tools = new List<IMyShipToolBase>();
        readonly List<IMyTerminalBlock> _cargos = new List<IMyTerminalBlock>();
        readonly List<IMyTerminalBlock> _flushables = new List<IMyTerminalBlock>();
        readonly List<IMyTerminalBlock> _stashes = new List<IMyTerminalBlock>();
        readonly List<IMyTextPanel> _quota_screens = new List<IMyTextPanel>();
        readonly List<IMyUserControllableGun> _weapons = new List<IMyUserControllableGun>();
        readonly DateTime _start_time;
        int _reinit_counter = 0;
        bool _echoed = false;

        private MyFixedPoint? Divf(MyFixedPoint a, MyFixedPoint b, bool floor) {
            if (b == MyFixedPoint.Zero) {
                return null;
            } else {
                a.RawValue *= 1000000;
                a.RawValue /= b.RawValue;
                if (floor) {
                    a = MyFixedPoint.Floor(a);
                }
                return a;
            }
        }

        private MyFixedPoint Sumf(IEnumerable<MyFixedPoint> values, bool floor) {
            MyFixedPoint result = values.Aggregate(MyFixedPoint.Zero, (a, b) => a + b);
            if (floor) {
                result = MyFixedPoint.Floor(result);
            }
            return result;
        }

        private IMyTerminalBlock LoadBlock(string name) {
            var block = GridTerminalSystem.GetBlockWithName(name);
            if (block == null) {
                throw new Exception("Oh shit! I can't find block: '" + name + "'");
            }
            return block;
        }

        public Program() {
            IGC.RegisterBroadcastListener("CargoInfo");

            _start_time = DateTime.Now;
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            if (Storage != null && Storage != "") {
                DateTime.TryParse(Storage, out _start_time);
            }
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
            Dictionary<string, Dictionary<string, MyFixedPoint>> cargoCounts;

            CompileGridTidbits(infos);
            CompileWeaponInfos(infos);
            CompileRepairInfos(infos);
            CompileHelpText(infos);

            CompileCargoInfos(infos, out cargoCounts);
            Dictionary<string, MyFixedPoint> productionCounts = CompileProductionInfos(infos);
            Dictionary<string, MyFixedPoint> quotas = CompileQuotas();
            QueueQuotas(quotas, cargoCounts, productionCounts);

            CompileOxygenInfos(infos);

            ImmutableDictionary<string, string> bakedInfos = infos.ToImmutableDictionary();
            _categories_seen.UnionWith(bakedInfos.Keys);
            EchoScriptInfo();
            WriteLCDs(bakedInfos);
            BroadcastInfos(bakedInfos);
            FlushFlushables();
        }

        void FlushFlushables() {
            if (_stashes.Where(block => block.IsFunctional).Count() <= 0) {
                return;
            }

            var stash = _stashes
                .Where(block => block.IsFunctional)
                .SelectMany(block => Inventories(block))
                .MaxBy(inv => (float)(inv.MaxVolume - inv.CurrentVolume));

            if (stash.VolumeFillFactor > 0.9f) {
                return;
            }

            Random random = new Random();

            foreach (var flushable in _flushables.Where(block => block.IsFunctional)) {
                foreach (var inv in Inventories(flushable).Where(inv => inv.VolumeFillFactor >= 0.5f)) {
                    inv.TransferItemTo(stash, random.Next(inv.ItemCount));
                }
            }
        }

        void LoadBlocks<T>(List<T> list, Func<T, bool> f = null) where T : class, IMyTerminalBlock {
            list.Clear();
            GridTerminalSystem.GetBlocksOfType(list,
                block => block.IsSameConstructAs(Me)
                && (f == null || f(block)));
        }

        private void Reinit() {
            _reinit_counter = 1000;

            LoadBlocks(_assemblers);
            LoadBlocks(_autoAssemblers, block => !block.CooperativeMode);
            LoadBlocks(_batteries);
            LoadBlocks(_cargos, block => block.HasInventory);
            LoadBlocks(_connectors);
            LoadBlocks(_flushables, block => block.CustomData.Contains("Flush"));
            LoadBlocks(_hydrogen_tanks, block => block.BlockDefinition.SubtypeName.Contains("Hydrogen"));
            LoadBlocks(_oxygen_tanks, block => block.BlockDefinition.SubtypeName.Contains("Oxygen"));
            LoadBlocks(_quota_screens, block => block.CustomName.Contains("Quota Input"));
            LoadBlocks(_repair_projectors, block => block.CustomName.Contains("Repair"));
            LoadBlocks(_stashes, block => block.HasInventory && block.CustomName.Contains("Stash"));
            LoadBlocks(_tools);
            LoadBlocks(_turrets);
            LoadBlocks(_weapons);
            LoadBlocks(_vents);

            _flushables.AddRange(_assemblers);
            _flushables.AddRange(_tools);

            ReinitTextTargets();
        }

        void ReinitTextTargets() {
            _text_targets.Clear();

            List<IMyTextPanel> lcds = new List<IMyTextPanel>();
            LoadBlocks(lcds, block => block.CustomName.Contains("CargoInfo:"));

            foreach (var lcd in lcds) {
                var parts = lcd.CustomName.Split(':');
                if (parts.Length != 4) {
                    continue;
                }

                string grid_name = parts[1];
                string categories = parts[2];

                if (!_text_targets.ContainsKey(grid_name)) {
                    _text_targets.Add(grid_name, new List<TextTarget>());
                }

                var target = new LcdTarget(lcd, categories.Split(','));
                _text_targets[grid_name].Add(target);
            }

            List<IMyTerminalBlock> providers = new List<IMyTerminalBlock>();
            LoadBlocks(providers, block => block is IMyTextSurfaceProvider);

            foreach (var block in providers) {
                var provider = (IMyTextSurfaceProvider)block;

                foreach (string line in block.CustomData.Split('\n')) {
                    var parts = line.Split(':');

                    if (parts.Length != 5) {
                        continue;
                    }

                    if (!parts[0].EndsWith("CargoInfo")) {
                        continue;
                    }

                    int index;
                    if (int.TryParse(parts[2], out index)) {
                        string grid_name = parts[1];
                        string categories = parts[3];

                        var target = new SurfaceTarget(block, provider.GetSurface(index), categories.Split(','));
                        _text_targets[grid_name].Add(target);
                    }
                }
            }
        }

        IEnumerable<T> Functional<T>(IEnumerable<T> blocks) where T : IMyTerminalBlock {
            return blocks.Where(block => block.IsFunctional);
        }

        string Ratio<T>(IEnumerable<T> values, Func<T, double> getEnumerator, Func<T, double> getDenominator, string units) {
            return Ratio(values, v => (MyFixedPoint)getEnumerator(v), v => (MyFixedPoint)getDenominator(v), units);
        }

        string Ratio<T>(IEnumerable<T> values, Func<T, float> getEnumerator, Func<T, float> getDenominator, string units) {
            return Ratio(values, v => (MyFixedPoint)getEnumerator(v), v => (MyFixedPoint)getDenominator(v), units);
        }

        string Ratio<T>(IEnumerable<T> values, Func<T, MyFixedPoint> getEnumerator, Func<T, MyFixedPoint> getDenominator, string units) {
            IEnumerable<T> values2 = values.Where(v => !(v is IMyTerminalBlock) || ((IMyTerminalBlock)v).IsFunctional);
            var a = Sumf(values2.Select(v => getEnumerator(v)), false);
            var b = Sumf(values2.Select(v => getDenominator(v)), false);

            if (b == 0) {
                return "None";
            } else {
                return MyFixedPoint.Floor(a)
                    + " of " + MyFixedPoint.Floor(b) + " " + units
                    + " (" + (Divf(100 * a, b, true) ?? 0) + "%)";
            }
        }

        IEnumerable<IMyInventory> Inventories(IMyTerminalBlock block) {
            if (block.IsFunctional) {
                return Enumerable.Range(0, block.InventoryCount).Select(i => block.GetInventory(i));
            } else {
                return Enumerable.Empty<IMyInventory>();
            }
        }

        IEnumerable<IMyInventory> Inventories(IEnumerable<IMyTerminalBlock> blocks) {
            return blocks.SelectMany(block => Inventories(block));
        }

        private void CompileGridTidbits(Dictionary<string, string> infos) {
            string s = Me.CubeGrid.CustomName + " Tidbits\n";
            s += ((DateTime.Now.ToString())) + "\n\n";

            // the list below should stay sorted, because all the other screens are sorted

            double elapsedTime = (DateTime.Now - _start_time).TotalDays;
            s += "Assemblers, not producing: " + _assemblers.Where(a => a.IsWorking && !a.IsProducing).Count() + "\n";
            s += "Assemblers, not queued: " + _assemblers.Where(a => a.IsWorking && a.IsQueueEmpty).Count() + "\n";
            s += "Battery in: " + Ratio(_batteries, b => b.CurrentInput, b => b.MaxInput, "MW") + "\n";
            s += "Battery out: " + Ratio(_batteries, b => b.CurrentOutput, b => b.MaxOutput, "MW") + "\n";
            s += "Battery: " + Ratio(_batteries, b => b.CurrentStoredPower, b => b.MaxStoredPower, "MWh") + "\n";
            s += "Cargo: " + Ratio(Inventories(_cargos), inv => inv.CurrentVolume, inv => inv.MaxVolume, "kL") + "\n";
            s += "Cargo mass: " + Sumf(Inventories(_cargos).Select(inv => inv.CurrentMass), true) + "t\n";
            s += "Docked ships: " + _connectors.Where(c => c.IsFunctional && c.IsConnected).Count() + "\n";
            s += "H2: " + Ratio(_hydrogen_tanks, b => b.FilledRatio * b.Capacity, b => b.Capacity, "L(?)") + "\n";
            s += "O2: " + Ratio(_oxygen_tanks, b => b.FilledRatio * b.Capacity, b => b.Capacity, "L(?)") + "\n";
            s += "Script uptime: " + Math.Floor(elapsedTime) + " days\n";
            s += "Script version: v" + _version + "\n";
            s += "Turrets, idle: " + _turrets.Where(t => t.IsWorking && !t.HasTarget).Count() + "\n";
            s += "Turrets, targeting: " + _turrets.Where(t => t.IsWorking && t.HasTarget).Count() + "\n";

            infos.Add("GridTidbits", s);
        }

        private void CompileHelpText(Dictionary<string, string> infos) {
            infos.Add("Help", GenerateHelpText());
        }

        private void CompileRepairInfos(Dictionary<string, string> infos) {
            var groups = _repair_projectors.Where(p => p.IsWorking)
                .SelectMany(p => p.RemainingBlocksPerType)
                .GroupBy(pair => pair.Key.ToString());

            if (groups.Count() > 0) {
                WriteInfos(infos, "Damage", groups, group => group.Key + ": " + group.Sum(pair => pair.Value));
            }
        }

        private void CompileWeaponInfos(Dictionary<string, string> infos) {
            List<string> lines = new List<string>();
            
            foreach (IMyShipToolBase tool in _tools) {
                IMySlimBlock slim = tool.CubeGrid.GetCubeBlock(tool.Position);

                string s = tool.CustomName + ": ";
                if (slim == null || slim.IsDestroyed) {
                    s += "destroyed";
                } else {
                    s += (int)(slim.DamageRatio * 100.0f) + "% HP, ";

                    if (tool.IsFunctional) {
                        s += Ratio(Inventories(tool), inv => inv.CurrentVolume, inv => inv.MaxVolume, "kL");
                        if (tool.IsActivated) {
                            s += " >>";
                        }
                    } else {
                        s += "nonfunctional";
                    }
                }
                lines.Add(s);
            }

            foreach (IMyUserControllableGun weapon in _weapons) {
                IMySlimBlock slim = weapon.CubeGrid.GetCubeBlock(weapon.Position);

                string s = weapon.CustomName + ": ";
                if (slim == null || slim.IsDestroyed) {
                    s += "destroyed";
                } else {
                    s += (int)(slim.DamageRatio * 100.0f) + "% HP, ";

                    if (weapon.IsFunctional) {
                        s += Ratio(Inventories(weapon), inv => inv.CurrentVolume, inv => inv.MaxVolume, "kL");
                        if (weapon.IsShooting) {
                            s += " >>";
                        }
                    } else {
                        s += "nonfunctional";
                    }
                }
                lines.Add(s);
            }

            lines.Sort();
            infos.Add("Weapons", "Weapons\n\n" + string.Join("\n", lines));
        }

        private void CompileOxygenInfos(Dictionary<string, string> infos) {
            Dictionary<string, MyFixedPoint> counts = new Dictionary<string, MyFixedPoint>();

            foreach (IMyAirVent vent in _vents) {
                counts.Add(vent.CustomName, (MyFixedPoint)(100 * vent.GetOxygenLevel()));
            }

            foreach (IMyGasTank tank in _oxygen_tanks) {
                counts.Add(tank.CustomName, (MyFixedPoint)(100 * tank.FilledRatio));
            }

            WriteInfos(infos, "Oxygen", counts, a => a.Key + ": " + a.Value.ToIntSafe());
        }

        Dictionary<string, MyFixedPoint> CompileQuotas() {
            Dictionary<string, MyFixedPoint> targets = new Dictionary<string, MyFixedPoint>();
            System.Text.RegularExpressions.Regex regex
                = new System.Text.RegularExpressions.Regex(@"^\s*([^:]+): (\d+)\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

            foreach (IMyTextPanel lcd in _quota_screens) {
                if (!lcd.IsWorking) {
                    continue;
                }

                foreach (string line in lcd.GetText().Split('\n')) {
                    System.Text.RegularExpressions.Match match = regex.Match(line);
                    if (match.Success) {
                        string component = match.Groups[1].Value;
                        int quantity;
                        if (!int.TryParse(match.Groups[2].Value, out quantity)) {
                            continue;
                        }
                        targets.Add(component, (MyFixedPoint)quantity);
                    }
                }
            }

            return targets;
        }

        void QueueQuotas(
            Dictionary<string, MyFixedPoint> quotas,
            Dictionary<string, Dictionary<string, MyFixedPoint>> cargoCounts,
            Dictionary<string, MyFixedPoint> productionCounts) {

            if (_autoAssemblers.Count == 0) {
                return;
            }

            foreach (var quota in quotas) {
                var amountNeeded = quota.Value;
                foreach (var counts in cargoCounts.Values) {
                    if (counts.ContainsKey(quota.Key)) {
                        amountNeeded -= counts[quota.Key];
                    }
                }
                if (productionCounts.ContainsKey(quota.Key)) {
                    amountNeeded -= productionCounts[quota.Key];
                }
                if (amountNeeded > 0) {
                    MyDefinitionId id;
                    if (MyDefinitionId.TryParse("MyObjectBuilder_BlueprintDefinition/" + quota.Key, out id)) {
                        try {
                            _autoAssemblers[0].AddQueueItem(id, amountNeeded);
                        } catch (Exception) {
                            Echo("can't queue " + quota.Key + ": " + id);
                        }
                    }
                }
            }
        }

        private void CompileCargoInfos(Dictionary<string, string> infos, out Dictionary<string, Dictionary<string, MyFixedPoint>> cargoCounts) {
            cargoCounts = new Dictionary<string, Dictionary<string, MyFixedPoint>>();
            List<MyInventoryItem> items = new List<MyInventoryItem>();

            foreach (IMyTerminalBlock cargo in _cargos.Where(c => c.IsFunctional)) {
                for (int i = 0; i < cargo.InventoryCount; ++i) {
                    items.Clear();
                    cargo.GetInventory(i).GetItems(items);
                    foreach (MyInventoryItem item in items) {
                        Dictionary<string, MyFixedPoint> subtypeCounts;
                        if (!cargoCounts.TryGetValue(item.Type.TypeId, out subtypeCounts)) {
                            subtypeCounts = new Dictionary<string, MyFixedPoint>();
                            cargoCounts.Add(item.Type.TypeId, new Dictionary<string, MyFixedPoint>());
                        }

                        if (!subtypeCounts.ContainsKey(item.Type.SubtypeId)) {
                            subtypeCounts.Add(item.Type.SubtypeId, item.Amount);
                        } else {
                            subtypeCounts[item.Type.SubtypeId] += item.Amount;
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, Dictionary<string, MyFixedPoint>> categoryCounts in cargoCounts) {
                string category = categoryCounts.Key.Substring(categoryCounts.Key.IndexOf("_") + 1);
                WriteInfos(infos, category, categoryCounts.Value, a => a.Key + ": " + a.Value.ToIntSafe());
            }
        }

        private Dictionary<string, MyFixedPoint> CompileProductionInfos(Dictionary<string, string> infos) {
            Dictionary<string, MyFixedPoint> queue = new Dictionary<string, MyFixedPoint>();
            List<MyProductionItem> items = new List<MyProductionItem>();

            foreach (IMyAssembler a in _assemblers.Where(a => a.IsWorking)) {
                a.GetQueue(items);

                foreach (MyProductionItem item in items) {
                    string id = item.BlueprintId.SubtypeName;
                    if (!queue.ContainsKey(id)) {
                        queue.Add(id, item.Amount);
                    } else {
                        queue[id] += item.Amount;
                    }
                }
                items.Clear();
            }

            WriteInfos(infos, "Production", queue, a => a.Key + ": " + a.Value.ToIntSafe());
            return queue;
        }

        void WriteInfos<T>(Dictionary<string, string> infos, string category, IEnumerable<T> entries, Func<T, string> f) {
            IEnumerable<string> lines = entries
                .Select(f)
                .OrderBy(a => a);
            string s = category + "\n\n" + string.Join("\n", lines);
            infos.Add(category, s);
        }

        string GenerateHelpText() {
            return ("CargoInfo v" + _version + ". features:\n\n"
                + "1) local cargo display lcd name format is 'CargoInfo::<category>:'\n"
                + "2) remote display lcd name format is 'CargoInfo:<grid>:<categories>:' (untested)\n"
                + "3) text surface custom data format, each line, is 'CargoInfo:<grid>:<index>:<categories>:' (untested)\n"
                + "4) lcd with name 'Quota Input' will send tasks to an uncooperative assembler. (disabled)\n"
                + "5) Half-full assemblers, tools, and blocks with 'Flush' in their customdata will be flushed to Stash-named boxes.\n"
                + "6) Name your self-repair projector with 'Repair' to see its info. (untested)\n"
                + "Categories: " + String.Join(", ", _categories_seen));
        }

        void EchoScriptInfo() {
            if (!_echoed) {
                Echo(GenerateHelpText());
                _echoed = true;
            }
        }

        void WriteLCDs(ImmutableDictionary<string, string> infos, string gridName = "") {
            List<TextTarget> grid_targets;
            if (_text_targets.TryGetValue(gridName, out grid_targets)) {
                foreach (var target in grid_targets) {
                    if (!target.IsWorking) {
                        continue;
                    }

                    string s = "";
                    foreach (string category in target.Categories) {
                        string content;
                        if (infos.TryGetValue(category, out content)) {
                            s += content + "\n\n";
                        }
                    }

                    target.Write(s);
                }
            }
        }

        void ReceiveInfos() {
            List<IMyBroadcastListener> listeners = new List<IMyBroadcastListener>();
            IGC.GetBroadcastListeners(listeners);
            foreach (IMyBroadcastListener listener in listeners.Where(l => l.Tag == "CargoInfo")) {
                if (listener.HasPendingMessage) {
                    MyTuple<string, ImmutableDictionary<string, string>> message = (MyTuple<string, ImmutableDictionary<string, string>>)listener.AcceptMessage().Data;
                    WriteLCDs(message.Item2, message.Item1);
                }
            }
        }

        void BroadcastInfos(ImmutableDictionary<string, string> infos) {
            IGC.SendBroadcastMessage("CargoInfo", MyTuple.Create(Me.CubeGrid.CustomName, infos));
        }

        interface TextTarget {
            bool IsWorking { get; }
            IEnumerable<string> Categories { get; set; }
            void Write(string s);
        }

        class LcdTarget : TextTarget {
            readonly IMyTextPanel _lcd;

            public LcdTarget(IMyTextPanel lcd, IEnumerable<string> categories) {
                _lcd = lcd;
                Categories = categories;
            }

            public bool IsWorking {
                get {
                    return _lcd.IsWorking;
                }
            }

            public IEnumerable<string> Categories { get; set; }

            public void Write(string s) {
                _lcd.WriteText(s);
            }
        }

        class SurfaceTarget : TextTarget {
            readonly IMyTerminalBlock _owner;
            readonly IMyTextSurface _surface;

            public SurfaceTarget(IMyTerminalBlock owner, IMyTextSurface surface, IEnumerable<string> categories) {
                _owner = owner;
                _surface = surface;
                Categories = categories;
            }

            public bool IsWorking {
                get {
                    return _owner.IsWorking;
                }
            }

            public IEnumerable<string> Categories { get; set; }

            public void Write(string s) {
                _surface.WriteText(s);
            }
        }
    }
}
