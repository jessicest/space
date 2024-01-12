using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
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
        const string _version = "2.1.0";
        readonly Dictionary<string, List<TextTarget>> _text_targets = new Dictionary<string, List<TextTarget>>();
        readonly HashSet<string> _categories_seen = new HashSet<string>();
        readonly List<IMyTerminalBlock> _detailed_info_sources = new List<IMyTerminalBlock>();
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
        readonly List<IMyThrust> _thrusters = new List<IMyThrust>();
        readonly List<IMyUserControllableGun> _weapons = new List<IMyUserControllableGun>();
        readonly DateTime _start_time;

        IMyShipController _main_cockpit = null;
        IMyTextPanel _production_id_mappings = null;
        readonly Dictionary<string, string> _item_ids_to_blueprint_ids = new Dictionary<string, string>();
        readonly Dictionary<string, string> _blueprint_ids_to_item_ids = new Dictionary<string, string>();

        int _reinit_counter = 0;
        bool _echoed = false;

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
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            var parts = Storage?.Split(',');
            if (parts != null && parts.Length == 2 || parts[0] == _version) {
                DateTime.TryParse(parts[1], out _start_time);
            }
        }

        public void Save() {
            Storage = _version + "," + _start_time.ToString();
        }

        public void Main(string argument, UpdateType updateSource) {
            try {
                if (_reinit_counter <= 0) {
                    Reinit();
                }
                _reinit_counter -= 1;

                Dictionary<string, string> infos = new Dictionary<string, string>();
                Dictionary<string, Dictionary<string, MyFixedPoint>> cargoCounts;

                CompileGridTidbits(infos);
                CompileWeaponInfos(infos);
                CompileRepairInfos(infos);
                CompileDetailedInfos(infos);
                CompileHelpText(infos);

                CompileCargoInfos(infos, out cargoCounts);
                Dictionary<string, MyFixedPoint> productionCounts = CompileProductionInfos(infos);
                Dictionary<string, MyFixedPoint> quotas = CompileQuotas();
                QueueQuotas(quotas, cargoCounts, productionCounts, infos);

                CompileOxygenInfos(infos);
                CompileThrustInfos(infos);

                ImmutableDictionary<string, string> bakedInfos = infos.ToImmutableDictionary();
                _categories_seen.UnionWith(bakedInfos.Keys);
                EchoScriptInfo();
                WriteLCDs(bakedInfos);
                BroadcastInfos(bakedInfos);
                FlushFlushables();
            } catch (Exception ex) {
                Runtime.UpdateFrequency = UpdateFrequency.None;
                foreach (var surface in _text_targets.Values.SelectMany(a => a).Where(b => b.IsWorking)) {
                    surface.Write(ex.ToString());
                }
            }
        }

        void ReadProductionIDMappings() {
            if (_production_id_mappings == null) {
                return;
            }

            foreach (var row in _production_id_mappings.GetText().Split('\n')) {
                var parts = row.Split(':');
                if (parts.Length == 2) {
                    string itemId = parts[0].Trim();
                    string blueprintId = parts[1].Trim();
                    if (!_item_ids_to_blueprint_ids.ContainsKey(itemId) && !_blueprint_ids_to_item_ids.ContainsKey(blueprintId)) {
                        _item_ids_to_blueprint_ids.Add(itemId, blueprintId);
                        _blueprint_ids_to_item_ids.Add(blueprintId, itemId);
                    }
                }
            }
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

        T LoadOneBlock<T>(Func<T, bool> f, bool throwIfAbsent) where T : class, IMyTerminalBlock {
            List<T> list = new List<T>();
            LoadBlocks(list, f);
            if (list.Count > 0) {
                return list[0];
            } else if (throwIfAbsent) {
                throw new Exception("wait but i didn't find that thingo");
            } else {
                return null;
            }
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
            LoadBlocks(_oxygen_tanks, block => !block.BlockDefinition.SubtypeName.Contains("Hydrogen"));
            LoadBlocks(_quota_screens, block => block.CustomName.Contains("Quota Input"));
            LoadBlocks(_repair_projectors, block => block.CustomName.Contains("Repair"));
            LoadBlocks(_stashes, block => block.HasInventory && block.CustomName.Contains("Stash"));
            LoadBlocks(_thrusters);
            LoadBlocks(_tools);
            LoadBlocks(_turrets);
            LoadBlocks(_weapons);
            LoadBlocks(_vents);

            _flushables.AddRange(_assemblers);
            _flushables.AddRange(_tools);

            _main_cockpit = LoadOneBlock<IMyShipController>(block => block.IsMainCockpit, false);
            _production_id_mappings = LoadOneBlock<IMyTextPanel>(block => block.CustomName.Contains("Production IDs"), true);
            ReadProductionIDMappings();

            ReinitTextTargets();
        }

        void ReinitTextTargets() {
            _text_targets.Clear();

            List<IMyTextPanel> lcds = new List<IMyTextPanel>();
            LoadBlocks(lcds, block => block.CustomName.Contains("CargoInfo:"));
            HashSet<string> detailedInfoSources = new HashSet<string>();

            foreach (var lcd in lcds) {
                var parts = lcd.CustomName.Split(':');
                if (parts.Length != 4) {
                    continue;
                }

                string grid_name = parts[1];
                string[] categories = parts[2].Split(',');

                if (!_text_targets.ContainsKey(grid_name)) {
                    _text_targets.Add(grid_name, new List<TextTarget>());
                }

                lcd.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                var target = new LcdTarget(lcd, categories);
                _text_targets[grid_name].Add(target);

                foreach (string s in categories.Where(s => s.StartsWith("Block/"))) {
                    detailedInfoSources.Add(s.Substring("Block/".Length));
                }
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
                        string[] categories = parts[3].Split(',');

                        if (!_text_targets.ContainsKey(grid_name)) {
                            _text_targets.Add(grid_name, new List<TextTarget>());
                        }

                        var surface = provider.GetSurface(index);
                        surface.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                        var target = new SurfaceTarget(block, surface, categories);
                        _text_targets[grid_name].Add(target);

                        foreach (string s in categories.Where(s => s.StartsWith("Block/"))) {
                            detailedInfoSources.Add(s.Substring("Block/".Length));
                        }
                    }
                }
            }

            _detailed_info_sources.AddRange(detailedInfoSources
                .Select(s => GridTerminalSystem.GetBlockWithName(s))
                .Where(b => b != null));
        }

        IEnumerable<T> Functional<T>(IEnumerable<T> blocks) where T : IMyTerminalBlock {
            return blocks.Where(block => block.IsFunctional);
        }

        string WithUnits(float value, string label, params string[] labels) {
            int candidate = 0;
            while (candidate < labels.Count() && value >= 10000.0f) {
                value /= 1000.0f;
                label = labels[candidate];
                ++candidate;
            }
            return value + label;
        }

        string ToUnits(double[] values, params string[] units) {
            int unitsIndex = 0;
            while (unitsIndex + 1 < units.Length && values[0] >= 10000.0) {
                for (int i = 0; i < values.Length; ++i) {
                    values[i] /= 1000.0;
                }
                ++unitsIndex;
            }
            return units[unitsIndex];
        }

        string Ratio<T>(IEnumerable<T> values, Func<T, int> getEnumerator, Func<T, int> getDenominator, params string[] units) {
            return Ratio(values, v => (float)getEnumerator(v), v => (float)getDenominator(v), units);
        }

        string Ratio<T>(IEnumerable<T> values, Func<T, MyFixedPoint> getEnumerator, Func<T, MyFixedPoint> getDenominator, params string[] units) {
            return Ratio(values, v => (float)getEnumerator(v), v => (float)getDenominator(v), units);
        }

        string Ratio<T>(IEnumerable<T> values, Func<T, double> getEnumerator, Func<T, double> getDenominator, params string[] units) {
            return DeepRatio(
                values,
                vs => vs.Select(v => getEnumerator(v)).Sum(),
                vs => vs.Select(v => getDenominator(v)).Sum(),
                "{0:0} of {1:0} {2} ({3:0}%)",
                units);
        }

        string DeepRatio<T>(IEnumerable<T> values, Func<IEnumerable<T>, double> getEnumerator, Func<IEnumerable<T>, double> getDenominator, string formatString, params string[] units) {
            IEnumerable<T> values2 = values.Where(v => !(v is IMyTerminalBlock) || ((IMyTerminalBlock)v).IsFunctional);

            
            var vals = new[] { getDenominator(values2), getEnumerator(values2) };
            var unit = ToUnits(vals, units);
            var a = vals[1];
            var b = vals[0];

            return b != 0 ? String.Format(formatString, a, b, unit, a * 100.0 / b) : "None";
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
            s += String.Format("Assemblers: active {0}, blocked {0}, idle {0}\n",
                _assemblers.Where(a => a.IsWorking && a.IsProducing).Count(),
                _assemblers.Where(a => a.IsWorking && !a.IsProducing && !a.IsQueueEmpty).Count(),
                _assemblers.Where(a => a.IsWorking && a.IsQueueEmpty).Count());
            s += String.Format("Battery in: {0}\n", Ratio(_batteries, b => b.CurrentInput, b => b.MaxInput, "MW", "GW"));
            s += String.Format("Battery out: {0}\n", Ratio(_batteries, b => b.CurrentOutput, b => b.MaxOutput, "MW", "GW"));
            s += String.Format("Battery: {0}\n", Ratio(_batteries, b => b.CurrentStoredPower, b => b.MaxStoredPower, "MWh", "GWh"));
            s += String.Format("Cargo: {0}\n", Ratio(Inventories(_cargos), inv => inv.CurrentVolume, inv => inv.MaxVolume, "kL", "ML"));
            s += String.Format("Cargo mass: {0}\n", WithUnits(Inventories(_cargos).Select(inv => (float)inv.CurrentMass).Sum(), "kg", "t", "kt", "Mt"));
            s += String.Format("Docked ships: {0}\n", _connectors.Where(c => c.IsFunctional && c.IsConnected).Count());
            s += String.Format("H2: {0}\n", Ratio(_hydrogen_tanks, b => b.FilledRatio * b.Capacity, b => b.Capacity, "L", "kL", "ML"));
            s += String.Format("O2: {0}\n", Ratio(_oxygen_tanks, b => b.FilledRatio * b.Capacity, b => b.Capacity, "L", "kL", "ML"));
            s += String.Format("Script v{0}: {1} days uptime\n", _version, Math.Floor(elapsedTime));
            if (_main_cockpit != null) {
                s += String.Format("Ship mass: {0}\n", _main_cockpit.CalculateShipMass().PhysicalMass);
            }
            s += String.Format("Turrets with target: {0}\n", Ratio(_turrets, t => t.HasTarget ? 1 : 0, _ => 1, ""));

            infos.Add("GridTidbits", s);
        }

        private void CompileHelpText(Dictionary<string, string> infos) {
            infos.Add("Help", GenerateHelpText());
        }

        private void CompileRepairInfos(Dictionary<string, string> infos) {
            var groups = _repair_projectors.Where(p => p.IsWorking)
                .SelectMany(p => p.RemainingBlocksPerType)
                .GroupBy(pair => pair.Key.ToString().Split('/')[1]);

            if (groups.Count() > 0) {
                WriteInfos(infos, "Damage", groups, group => group.Key + ": " + group.Sum(pair => pair.Value));
            }
        }

        void CompileDetailedInfos(Dictionary<string, string> infos) {
            foreach (var block in _detailed_info_sources) {
                infos.Add("Block/" + block.CustomName, block.DetailedInfo);
            }
        }

        private float? GetBlockDamage(IMyTerminalBlock block) {
            IMySlimBlock slim = block.CubeGrid.GetCubeBlock(block.Position);
            if (slim == null || slim.IsDestroyed) {
                return null;
            }
            return slim.DamageRatio;
        }

        private void CompileWeaponInfos(Dictionary<string, string> infos) {
            List<string> lines = new List<string>();
            
            foreach (IMyShipToolBase tool in _tools) {
                string s = tool.CustomName + ": ";
                float? damage = GetBlockDamage(tool);
                if (damage == null) {
                    s += "destroyed";
                } else {
                    s += (int)(damage * 100.0f) + "% dmg, ";

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

        private void CompileThrustInfos(Dictionary<string, string> infos) {
            if (_main_cockpit == null) {
                infos.Add("Thrust", "Thrust: no main cockpit found");
                return;
            }

            var shipMass = _main_cockpit.CalculateShipMass().PhysicalMass;

            var thrusters = _thrusters.Where(t => t.IsFunctional);

            var thrusterGroups = thrusters
                .Where(t => t.IsWorking)
                .GroupBy(t => VRageMath.Base6Directions.GetFlippedDirection(_main_cockpit.Orientation.TransformDirection(t.Orientation.Forward)));

            List<string> lines = new List<string>();
            lines.AddRange(thrusterGroups
                .Select(group => String.Format("Go {0}: {1}, {2}", group.Key.ToString()[0],
                    Ratio(group, t => t.CurrentThrust, t => t.MaxEffectiveThrust, "N", "kN", "MN", "GN"),
                    Ratio(
                        group.Select(t => t.CubeGrid.GetCubeBlock(t.Position)).Where(s => s != null && !s.IsDestroyed),
                        s => s.CurrentDamage, s => s.MaxIntegrity, "i", "ki", "Mi", "Gi"))));

            VRageMath.Vector3D gravity = VRageMath.Vector3D.TransformNormal(_main_cockpit.GetTotalGravity(),
                VRageMath.MatrixD.Transpose(_main_cockpit.WorldMatrix));
            VRageMath.Vector3D gravityDirection = gravity.Normalized();

            lines.Add(String.Format("Gravitation: {0}",
                DeepRatio(_thrusters,
                    _ => gravity.Length(),
                    ts => ts
                        .Select(t => t.MaxEffectiveThrust * ((VRageMath.Vector3D)t.GridThrustDirection).Dot(ref gravityDirection))
                        .Where(f => f > 0).Sum() / shipMass,
                    "{0:0.00} of {1:0.00} {2} ({3:0}%)", "mss", "kmss")));

            var velocity = _main_cockpit.GetShipVelocities().LinearVelocity;
            var velocityDirection = velocity.Normalized();
            var speed = velocity.Length();
            var stoppingPower = _thrusters
                .Select(t => t.MaxEffectiveThrust * ((VRageMath.Vector3D)t.GridThrustDirection).Dot(ref velocityDirection))
                .Where(v => v > 0.001)
                .Sum() / shipMass;

            lines.Add(String.Format("Time to stop: {0}\n",
                speed < 0.001 ? "stopped" : stoppingPower != 0 ? String.Format("{0:0.00}s", speed / stoppingPower) : "forever"));

            WriteInfos(infos, "Thrust", lines, a => a);
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
            Dictionary<string, MyFixedPoint> productionCounts,
            Dictionary<string, string> infos) {

            string ss = "Quota Info\n\n";

            if (_autoAssemblers.Count == 0) {
                return;
            }

            foreach (var quota in quotas) {
                var amountNeeded = quota.Value;
                string s = quota.Key + ": " + quota.Value;
                foreach (var counts in cargoCounts.Values) {
                    if (counts.ContainsKey(quota.Key)) {
                        amountNeeded -= counts[quota.Key];
                        s += " (-" + counts[quota.Key] + ")";
                    }
                }
                if (productionCounts.ContainsKey(quota.Key)) {
                    amountNeeded -= productionCounts[quota.Key];
                    s += " {-" + productionCounts[quota.Key] + "}";
                }
                if (amountNeeded > 0) {
                    string blueprintId;
                    if (_item_ids_to_blueprint_ids.TryGetValue(quota.Key, out blueprintId)) {
                        MyDefinitionId id;
                        if (MyDefinitionId.TryParse(blueprintId, out id)) {
                            try {
                                _autoAssemblers[0].AddQueueItem(id, amountNeeded);
                            } catch (Exception ex) {
                                s += " " + ex.Message;
                            }
                        } else {
                            s += " unparsed: " + blueprintId;
                        }
                    } else {
                        s += " unfound: " + quota.Key;
                    }
                }

                ss += s + "\n";
            }

            infos.Add("Quota", ss);
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
                    string id;
                    if (!_blueprint_ids_to_item_ids.TryGetValue(item.BlueprintId.ToString(), out id)) {
                        id = item.BlueprintId.SubtypeName;
                    }
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
                + "3) text surface custom data format, each line, is 'CargoInfo:<grid>:<index>:<categories>:'\n"
                + "4) use category Block/abc to show the Detailed Info panel from a block named 'abc'.\n"
                + "5) lcd with name 'Quota Input' will send tasks to an uncooperative assembler.\n"
                + "6) Half-full assemblers, tools, and blocks with 'Flush' in their customdata will be flushed to Stash-named boxes.\n"
                + "7) Name your self-repair projector with 'Repair' to see its info on the Damage category.\n"
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
