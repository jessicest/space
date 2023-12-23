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
        private readonly List<IMyCargoContainer> _cargos = new List<IMyCargoContainer>();
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
            Reinit();
            Show(CountTypes());
        }

        private void Reinit() {
            if (_counter <= 0) {
                _counter = 1000;
                _cargos.Clear();
                GridTerminalSystem.GetBlocksOfType(_cargos, block => block.IsSameConstructAs(Me));
                _lcds.Clear();
                GridTerminalSystem.GetBlocksOfType(_lcds,
                    block => block.IsSameConstructAs(Me) && block.CustomName.StartsWith("Cargo Info:"));
            }
            _counter -= 1;
        }

        private SortedDictionary<MyItemType, MyFixedPoint> CountTypes() {
            SortedDictionary<MyItemType, MyFixedPoint> cargoCounts = new SortedDictionary<MyItemType, MyFixedPoint>();
            List<MyInventoryItem> items = new List<MyInventoryItem>();

            foreach (IMyCargoContainer cargo in _cargos) {
                cargo.GetInventory(0).GetItems(items);
                foreach (MyInventoryItem item in items) {
                    if (!cargoCounts.ContainsKey(item.Type)) {
                        cargoCounts.Add(item.Type, 0);
                    }
                    cargoCounts[item.Type] += item.Amount;
                }
            }

            return cargoCounts;
        }

        private void Show(SortedDictionary<MyItemType, MyFixedPoint> typeCounts) {
            Dictionary<string, string> outputs = new Dictionary<string, string>();

            foreach (KeyValuePair<MyItemType, MyFixedPoint> typeCount in typeCounts) {
                string typeId = typeCount.Key.TypeId;
                if (!outputs.ContainsKey(typeId)) {
                    outputs.Add(typeId.Substring(typeId.IndexOf("_") + 1), "");
                }

                outputs[typeCount.Key.TypeId] += typeCount.Key.SubtypeId + ": " + typeCount.Value + "\n";
            }

            Echo(String.Join(", ", outputs.Keys.ToArray()));

            foreach (IMyTextPanel lcd in _lcds) {
                string lcdName = lcd.CustomName;
                string targetNamePrefix = lcdName.Substring(lcdName.IndexOf(':') + 1);
                string targetName = targetNamePrefix.Substring(0, targetNamePrefix.IndexOf(':'));
                string content;
                if (outputs.TryGetValue(targetName, out content)) {
                    lcd.WriteText(content);
                }
            }
        }
    }
}
