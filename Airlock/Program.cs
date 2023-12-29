using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
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
        readonly IMyDoor _inner_door;
        readonly IMyDoor _outer_door;
        readonly IMyAirVent _spline_vent;
        readonly IMyAirVent _drain_vent;
        readonly List<IMyGasTank> _airlock_tanks = new List<IMyGasTank>();

        enum Mode {
            Full,
            Depressurizing,
            Empty,
            Pressurizing,
            Abort,
        }
        Mode _mode;

        private T LoadBlock<T>(string name) where T: IMyTerminalBlock {
            var block = GridTerminalSystem.GetBlockWithName(name);
            if (block == null) {
                throw new Exception("Oh shit! I can't find block: '" + name + "'");
            }
            return (T)block;
        }

        public Program() {
            _inner_door = LoadBlock<IMyDoor>("airlock inner door");
            _outer_door = LoadBlock<IMyDoor>("airlock outer door");
            _spline_vent= LoadBlock<IMyAirVent>("airlock spline vent");
            _drain_vent = LoadBlock<IMyAirVent>("airlock drain vent");
            GridTerminalSystem.GetBlocksOfType(_airlock_tanks, tank => tank.IsSameConstructAs(Me) && tank.CustomName.Contains("airlock"));

            Pressurize();
            Enum.TryParse(Storage, out _mode);
        }

        public void Save() {
            Storage = _mode.ToString();
        }

        void Pressurize() {
            _drain_vent.Depressurize = false;
            _spline_vent.Enabled = false;
            _inner_door.CloseDoor();
            _outer_door.CloseDoor();
            _mode = Mode.Pressurizing;
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        void Depressurize() {
            _drain_vent.Depressurize = true;
            _spline_vent.Enabled = false;
            _inner_door.CloseDoor();
            _inner_door.Enabled = false;
            _outer_door.CloseDoor();
            _outer_door.Enabled = false;
            _mode = Mode.Depressurizing;
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        void Abort() {
            _inner_door.Enabled = true;
            _outer_door.Enabled = true;
            _spline_vent.Enabled = true;
            _drain_vent.Depressurize = false;
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        double? TankFullness() {
            double fullness = 0.0;
            bool tanksExist = false;
            
            foreach (IMyGasTank tank in _airlock_tanks.Where(tank => tank.IsWorking)) {
                tanksExist = true;
                fullness += tank.FilledRatio;
            }

            if (tanksExist) {
                return fullness / (double)_airlock_tanks.Count;
            } else {
                return null;
            }
        }

        public void Main(string argument, UpdateType updateSource) {
            switch (argument) {
                case "pressurize": Pressurize(); break;
                case "depressurize": Depressurize(); break;
                case "abort": Abort(); break;
            }                    

            switch (_mode) {
                case Mode.Depressurizing:
                    if (_drain_vent.GetOxygenLevel() < 0.01f || (TankFullness() ?? 1.0) >= .99) {
                        _outer_door.Enabled = true;
                        _outer_door.OpenDoor();
                        _mode = Mode.Empty;
                        Runtime.UpdateFrequency = UpdateFrequency.None;
                    }
                    break;
                case Mode.Pressurizing:
                    if (_drain_vent.GetOxygenLevel() > 0.99f || (TankFullness() ?? 0.0) <= .01) {
                        _inner_door.Enabled = true;
                        _inner_door.OpenDoor();
                        _spline_vent.Enabled = true;
                        _mode = Mode.Full;
                        Runtime.UpdateFrequency = UpdateFrequency.None;
                    }
                    break;
            }
        }
    }
}
