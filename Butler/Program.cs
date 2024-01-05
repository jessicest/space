using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript {
    partial class Program : MyGridProgram {
        enum Mode {
            Cruising,
            Docking,
            Connected,
            Aborted,
        }

        readonly List<IMyBasicMissionBlock> _cruisers = new List<IMyBasicMissionBlock>();
        readonly List<IMyPathRecorderBlock> _dockers = new List<IMyPathRecorderBlock>();
        readonly List<IMyOffensiveCombatBlock> _offensives = new List<IMyOffensiveCombatBlock>();
        readonly IMyFlightMovementBlock _fly_fast;
        readonly IMyFlightMovementBlock _fly_safe;
        readonly ITerminalProperty<bool> _activate_mission;
        readonly ITerminalProperty<bool> _activate_recorder;
        readonly ITerminalProperty<bool> _activate_combat;
        readonly ITerminalProperty<bool> _activate_mover;
        readonly IMyShipConnector _connector;
        readonly List<IMyTerminalBlock> _terminals = new List<IMyTerminalBlock>();
        readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        readonly List<IMyGasTank> _hydrogen_tanks = new List<IMyGasTank>();

        int _wait_counter;
        Mode _mode;
        int _target;

        private ITerminalProperty<bool> getActivateCommand<T>(List<T> list) where T: class, IMyTerminalBlock {
            List<T> blocks = new List<T>();
            GridTerminalSystem.GetBlocksOfType(blocks, b => b.IsSameConstructAs(Me));
            if (blocks.Count > 0) {
                return blocks[0].GetProperty("ActivateBehavior").AsBool();
            } else {
                return null;
            }
        }

        public Program() {
            GridTerminalSystem.GetBlocksOfType(_cruisers, b => b.IsSameConstructAs(Me) && b.CustomName.Contains("cruise"));
            GridTerminalSystem.GetBlocksOfType(_dockers, b => b.IsSameConstructAs(Me) && b.CustomName.Contains("dock"));
            GridTerminalSystem.GetBlocksOfType(_offensives, b => b.IsSameConstructAs(Me));
            GridTerminalSystem.GetBlocksOfType(_batteries, b => b.IsSameConstructAs(Me));
            GridTerminalSystem.GetBlocksOfType(_terminals, b => b.IsSameConstructAs(Me));
            GridTerminalSystem.GetBlocksOfType(_hydrogen_tanks, b => b.IsSameConstructAs(Me) && b.BlockDefinition.SubtypeName.Contains("Hydrogen"));

            _cruisers.SortNoAlloc((a, b) => Comparer<string>.Default.Compare(a.CustomName, b.CustomName));
            _dockers.SortNoAlloc((a, b) => Comparer<string>.Default.Compare(a.CustomName, b.CustomName));

            List<IMyFlightMovementBlock> movers = new List<IMyFlightMovementBlock>();
            GridTerminalSystem.GetBlocksOfType(movers, b => b.IsSameConstructAs(Me) && b.CustomName.Contains(": fly fast"));
            if (movers.Count != 1) {
                throw new Exception("can't find fly fast");
            }
            _fly_fast = movers[0];
            GridTerminalSystem.GetBlocksOfType(movers, b => b.IsSameConstructAs(Me) && b.CustomName.Contains(": fly safe"));
            if (movers.Count != 1) {
                throw new Exception("can't find fly safe");
            }
            _fly_safe = movers[0];

            List<IMyShipConnector> connectors = new List<IMyShipConnector>();
            GridTerminalSystem.GetBlocksOfType(connectors, b => b.IsSameConstructAs(Me));
            if (connectors.Count != 1) {
                throw new Exception("can't find my connector");
            }
            _connector = connectors[0];

            _activate_mover = _fly_fast.GetProperty("ActivateBehavior").AsBool();
            _activate_mission = getActivateCommand(_cruisers);
            _activate_combat = getActivateCommand(_offensives);
            _activate_recorder = getActivateCommand(_dockers);

            _target = 0;
            _mode = Mode.Connected;
            _wait_counter = 0;

            string[] data = Storage.Split(',');

            if (data.Length == 3) {
                int.TryParse(data[0], out _target);
                Enum.TryParse(data[1], out _mode);
                int.TryParse(data[2], out _wait_counter);
            } else {
                Disconnect();
            }
        }

        public void Save() {
            Storage = _target + "," + _mode + "," + _wait_counter;
        }

        float? CountFullness<T>(List<T> blocks, Func<T, double> getCurrent, Func<T, double> getMax) where T : IMyTerminalBlock {
            return CountFullness(blocks, a => (float)getCurrent(a), a => (float)getMax(a));
        }

        float? CountFullness<T>(List<T> blocks, Func<T, float> getCurrent, Func<T, float> getMax) where T : IMyTerminalBlock {
            float current = 0;
            float max = 0;

            foreach (T b in blocks.Where(b => b.IsWorking)) {
                current += getCurrent(b);
                max += getMax(b);
            }

            if (max > 0) {
                return (current / max);
            } else {
                return null;
            }
        }

        float QueryInventories(IEnumerable<IMyTerminalBlock> blocks, Func<IMyInventory, float> f) {
            float result = 0;

            foreach (IMyTerminalBlock block in blocks.Where(b => b.IsWorking)) {
                for (int i = 0; i < block.InventoryCount; ++i) {
                    result += (float)f(block.GetInventory(i));
                }
            }

            return result;
        }

        float QueryItems(IEnumerable<IMyTerminalBlock> blocks, Func<MyInventoryItem, float> f, Func<MyInventoryItem, bool> filter = null) {
            return QueryInventories(blocks, inv => {
                List<MyInventoryItem> items = new List<MyInventoryItem>();
                inv.GetItems(items, filter);
                return items.Select(f).Sum();
            });
        }

        void CruiseTo(int target) {
            _target = target;
            _activate_mover.SetValue(_fly_fast, true);
            _activate_mission.SetValue(_cruisers[target], true);
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        void StartDocking(int target) {
            _target = target;
            _connector.Enabled = true;

            foreach (IMyOffensiveCombatBlock o in _offensives.Where(b => b.IsWorking)) {
                _activate_combat.SetValue(o, false);
            }
            _activate_mover.SetValue(_fly_safe, true);
            _activate_recorder.SetValue(_dockers[target], true);

            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        void TryConnect() {
            if (_connector.Status == MyShipConnectorStatus.Connectable) {
                _connector.Connect();
                _activate_mover.SetValue(_fly_safe, false);
                _activate_recorder.SetValue(_dockers[_target], false);

                _wait_counter = 30;
                Runtime.UpdateFrequency = UpdateFrequency.Update100;
            }
        }

        void BeCruising() {
            if (_target == 0) {
                return;
            }
            if ((CountFullness(_batteries, b => b.CurrentStoredPower, b => b.MaxStoredPower) ?? 1.0f) < 0.1f) {
                Echo("low battery! going home");
                CruiseTo(0);
            } else if ((CountFullness(_hydrogen_tanks, t => t.FilledRatio * t.Capacity, t => t.Capacity) ?? 1.0f) < 0.1f) {
                Echo("low hydrogen! going home");
                CruiseTo(0);
            } else if (_offensives.Count > 0 && QueryItems(_terminals, i => (float)i.Amount, i => i.Type.TypeId == "AmmoMagazine") <= 0.0f) {
                Echo("low ammo! going home");
                foreach (IMyOffensiveCombatBlock o in _offensives.Where(b => b.IsWorking)) {
                    _activate_combat.SetValue(o, false);
                }
                CruiseTo(0);
            }
        }

        void BeConnected() {
            _wait_counter -= 1;
            if (_wait_counter <= 0) {
                Disconnect();
            }
        }

        void Disconnect() {
            _connector.Enabled = false;
            _target = (_target + 1) % _cruisers.Count;
            foreach (IMyOffensiveCombatBlock o in _offensives.Where(b => b.IsWorking)) {
                _activate_combat.SetValue(o, true);
            }
            CruiseTo(_target);
        }

        void Abort() {
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("aborted :(");
        }

        public void Main(string argument, UpdateType updateSource) {
            switch (argument) {
                case "abort": Abort(); return;
                case "start docking 0": StartDocking(0); return;
                case "cruise to 0": CruiseTo(0); return;
                case "cruise to 1": CruiseTo(1); return;
                case "cruise": CruiseTo(_target); return;
            }

            switch (_mode) {
                case Mode.Cruising:
                    BeCruising();
                    break;
                case Mode.Docking:
                    TryConnect();
                    break;
                case Mode.Connected:
                    BeConnected();
                    break;
                case Mode.Aborted:
                    break;
            }
        }
    }
}
