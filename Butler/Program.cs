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

        readonly List<IMyBasicMissionBlock> _cruisers;
        readonly List<IMyPathRecorderBlock> _dockers;
        readonly IMyFlightMovementBlock _fly_fast;
        readonly IMyFlightMovementBlock _fly_safe;
        readonly ITerminalProperty<bool> _activate_behavior;
        readonly IMyShipConnector _connector;

        int _wait_counter;
        Mode _mode;
        int _target;

        public Program() {
            GridTerminalSystem.GetBlocksOfType(_cruisers, b => b.IsSameConstructAs(Me) && b.CustomName.Contains("cruise"));
            GridTerminalSystem.GetBlocksOfType(_dockers, b => b.IsSameConstructAs(Me) && b.CustomName.Contains("dock"));

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

            _activate_behavior = _fly_fast.GetProperty("ActivateBehavior").AsBool();

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

        void CruiseTo(int target) {
            _activate_behavior.SetValue(_fly_fast, true);
            _target = target;
            _activate_behavior.SetValue(_cruisers[target], true);
        }

        void StartDocking(int target) {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            _connector.Enabled = true;
            _target = target;

            _activate_behavior.SetValue(_fly_safe, true);
            _activate_behavior.SetValue(_dockers[target], true);
        }

        void TryConnect() {
            if (_connector.Status == MyShipConnectorStatus.Connectable) {
                _connector.Connect();
                _activate_behavior.SetValue(_fly_safe, false);
                _activate_behavior.SetValue(_dockers[_target], false);

                Runtime.UpdateFrequency = UpdateFrequency.Update100;
                _wait_counter = 30;
            }
        }

        void BeConnected() {
            _wait_counter -= 1;
            if (_wait_counter == 0) {
                Disconnect();
            }
        }

        void Disconnect() {
            _connector.Enabled = false;
            _target = (_target + 1) % _cruisers.Count;
            CruiseTo(_target);
            Runtime.UpdateFrequency = UpdateFrequency.None;
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
                case "cruise": CruiseTo(_target); return;
            }

            switch (_mode) {
                case Mode.Cruising:
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
