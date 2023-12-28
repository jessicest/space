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
            Ready,
            Cruising,
            Docking,
            Connected,
            Aborted,
        }

        readonly IMyFlightMovementBlock _fly_fast;
        readonly IMyFlightMovementBlock _fly_safe;
        readonly IMyBasicMissionBlock _home_cruiser;
        readonly IMyBasicMissionBlock _away_cruiser;
        readonly IMyPathRecorderBlock _home_docker;
        readonly IMyPathRecorderBlock _away_docker;
        readonly ITerminalProperty<bool> _activate_behavior;
        readonly IMyShipConnector _connector;
        int _counter = 0;

        Mode _mode;
        bool _target_away;

        public Program() {
            string[] data = Storage.Split(',');
            
            _activate_behavior = _fly_fast.GetProperty("ActivateBehavior").AsBool();

            if (data.Length == 3) {
                bool.TryParse(data[0], out _target_away);
                Enum.TryParse(data[1], out _mode);
                int.TryParse(data[2], out _counter);

                _target_away = true;
            }
            Runtime.UpdateFrequency = UpdateFrequency.Once;
        }

        public void Save() {
            Storage = _target_away + "," + _mode + "," + _counter;
        }

        void Cruise(bool targetAway) {
            _activate_behavior.SetValue(_fly_fast, true);
            _target_away = targetAway;
            if (targetAway) {
                _activate_behavior.SetValue(_away_cruiser, true);
            } else {
                _activate_behavior.SetValue(_home_cruiser, true);
            }
        }

        void StartDocking(bool targetAway) {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            _connector.Enabled = true;
            _target_away = targetAway;

            _activate_behavior.SetValue(_fly_safe, true);
            if (targetAway) {
                _activate_behavior.SetValue(_away_docker, true);
            } else {
                _activate_behavior.SetValue(_home_docker, true);
            }
        }

        void TryConnect() {
            if (_connector.Status == MyShipConnectorStatus.Connectable) {
                _connector.Connect();
                Runtime.UpdateFrequency = UpdateFrequency.Update100;
                _activate_behavior.SetValue(_fly_safe, false);
                _activate_behavior.SetValue(_home_docker, false);
                _activate_behavior.SetValue(_away_docker, false);
                _counter = 30;
            }
        }

        void BeConnected() {
            _counter -= 1;
            if (_counter == 0) {
                _target_away = !_target_away;
                Disconnect();
            }
        }

        void Disconnect() {
            _connector.Enabled = false;
            Cruise(_target_away);
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        void Abort() {
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("aborted :(");
        }

        public void Main(string argument, UpdateType updateSource) {
            switch (argument) {
                case "abort": Abort(); return;
                case "start docking home": StartDocking(false); return;
                case "cruise home": Cruise(false); return;
                case "choose task": Cruise(_target_away); return;
            }

            switch (_mode) {
                case Mode.Ready:
                    _target_away = true;
                    Disconnect();
                    break;
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
