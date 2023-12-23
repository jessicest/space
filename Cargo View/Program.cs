using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Gui;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game.ModAPI.Ingame;
using VRage.Library;
using VRageRender;

namespace IngameScript {
    partial class Program : MyGridProgram {
        private readonly List<IMyTextPanel> _lcds = new List<IMyTextPanel>();
        private readonly List<IMyTerminalBlock> _cargos = new List<IMyTerminalBlock>();
        private int _counter = 0;

        private IMyTerminalBlock LoadBlock(string name) {
            var block = GridTerminalSystem.GetBlockWithName(name);
            if (block == null) {
                throw new Exception("Oh shit! I can't find block: '" + name + "'");
            }
            return block;
        }

        public Program() {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Main(string argument, UpdateType updateSource) {
            if (_counter <= 0) {
                Reinit();
            }
            _counter -= 1;
            Show(CountTypes());
        }

        private void Reinit() {
            _counter = 1000;
            _cargos.Clear();
            GridTerminalSystem.GetBlocksOfType(_cargos,
                block => block.IsSameConstructAs(Me) && block.HasInventory);
            _lcds.Clear();
            GridTerminalSystem.GetBlocksOfType(_lcds,
                block => block.IsSameConstructAs(Me) && block.CustomName.StartsWith("Cargo Info:"));
        }

        private Dictionary<string, SortedDictionary<string, MyFixedPoint>> CountTypes() {
            Dictionary<string, SortedDictionary<string, MyFixedPoint>> cargoCounts = new Dictionary<string, SortedDictionary<string, MyFixedPoint>>();
            List<MyInventoryItem> items = new List<MyInventoryItem>();

            foreach (IMyCargoContainer cargo in _cargos) {
                if (!cargo.IsFunctional) {
                    continue;
                }

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

            return cargoCounts;
        }

        private void Show(Dictionary<string, SortedDictionary<string, MyFixedPoint>> typeCounts) {
            Dictionary<string, string> outputs = new Dictionary<string, string>();

            foreach (KeyValuePair<string, SortedDictionary<string, MyFixedPoint>> subtypeCounts in typeCounts) {
                string typeId = subtypeCounts.Key.Substring(subtypeCounts.Key.IndexOf("_") + 1);
                string s = typeId + "\n\n";

                foreach (KeyValuePair<string, MyFixedPoint> subtypeCount in subtypeCounts.Value) {
                    s += subtypeCount.Key + ": " + subtypeCount.Value.ToIntSafe() + "\n";
                }

                outputs.Add(typeId, s);
            }

            Echo(String.Join(", ", outputs.Keys.ToArray()));

            foreach (IMyTextPanel lcd in _lcds) {
                if (!lcd.IsFunctional) {
                    continue;
                }

                string[] lcdNameParts = lcd.CustomName.Split(':');
                if (lcdNameParts.Length != 4) {
                    continue;
                }

                string[] targetNames = lcdNameParts[2].Split(',');
                string content;
                string s = "";

                foreach (string targetName in targetNames) {
                    if (outputs.TryGetValue(targetName, out content)) {
                        s += content + "\n\n";
                    }
                }

                lcd.WriteText(s);
            }
        }
    }
}
