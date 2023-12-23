using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using IngameScript;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
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
using static VRageMath.Base6Directions;

namespace IngameScript {
    partial class Program : MyGridProgram {
        public bool _broken = false;

        public readonly IMyShipConnector _dockingPort;
        public readonly IMyRemoteControl _remoteControl;
        public readonly List<IMyThrust> _thrusters = new List<IMyThrust>();
        public readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        public readonly List<IMyGyro> _gyros = new List<IMyGyro>();
        public readonly List<IMyCargoContainer> _cargo = new List<IMyCargoContainer>();
        public readonly List<Action> _plan = new List<Action>();

        MyWaypointInfo _mine;
        MyWaypointInfo _home;

        // returns between 0.0f and 1.0f to represent the amount of cargo space that's full in our ship
        private float CargoFullness() {
            long capacityMicrolitres = 0;
            long storedCargoMicrolitres = 0;

            foreach (var cargo in _cargo) {
                var inventory = cargo.GetInventory();
                capacityMicrolitres += inventory.MaxVolume.RawValue;
                storedCargoMicrolitres += inventory.CurrentVolume.RawValue;
            }

            return ((float)storedCargoMicrolitres) / ((float)capacityMicrolitres);
        }

        private float PowerFullness() {
            float powerCapacity = 0.0f;
            float storedPower = 0.0f;

            foreach (var battery in _batteries) {
                storedPower += battery.CurrentStoredPower;
                powerCapacity += battery.MaxStoredPower;
            }

            return storedPower / powerCapacity;
        }

        public void ReportStatus(string message) {
            Echo("home: " + _home + "\n" + "mine: " + _mine + "\n" + message);
        }

        private void Breakdown(string message) {
            ReportStatus(message);
            _broken = true;
            Runtime.UpdateFrequency = 0;
        }

        private IMyTerminalBlock LoadBlock(string name) {
            var block = GridTerminalSystem.GetBlockWithName(name);
            if(block == null) {
                throw new Exception("Oh shit! I can't find block: '" + name + "'");
            }
            return block;
        }

        public Program() {
            try {
                List<MyWaypointInfo> waypoints = new List<MyWaypointInfo>();
                MyWaypointInfo.FindAll(Me.CustomData, waypoints);
                if (waypoints.Count != 2) {
                    throw new Exception("okay so you need to enter a home and a mine waypoint into custom data. Thanks!");
                }
                _home = waypoints[0];
                _mine = waypoints[1];

                Runtime.UpdateFrequency = UpdateFrequency.Update100;
                _remoteControl = (IMyRemoteControl)LoadBlock("Drone Remote Control");
                _dockingPort = (IMyShipConnector)LoadBlock("Drone Connector");
                GridTerminalSystem.GetBlocksOfType(_cargo, block => block.IsSameConstructAs(Me) && block.HasInventory);
                GridTerminalSystem.GetBlocksOfType(_thrusters, block => block.IsSameConstructAs(Me));
                GridTerminalSystem.GetBlocksOfType(_batteries, block => block.IsSameConstructAs(Me));
                GridTerminalSystem.GetBlocksOfType(_gyros, block => block.IsSameConstructAs(Me));
            } catch (Exception e) {
                Breakdown(e.Message + "\n" + e.StackTrace);
            }
        }

        public void Main(string argument, UpdateType updateSource) {
            if(_broken) {
                return;
            }

            try {
                if (_plan.Count == 0) {
                    if (_dockingPort.IsConnected) {
                        _plan.Add(new SitAtDockingPort(this, false, _dockingPort, _remoteControl, _gyros, _cargo, _thrusters, _batteries));
                        _plan[0].Begin();
                    } else {
                        Breakdown("plz help me get to a docking port and then reboot me thx");
                        return;
                    }
                } else {
                    var action = _plan[0];
                    if (!action.Step()) {
                        _plan.RemoveAt(0);
                        _plan.AddRange(action.End());
                        if (_plan.Count > 0) {
                            _plan[0].Begin();
                        }
                    }
                }
            } catch (Exception e) {
                Breakdown(e.Message + "\n" + e.StackTrace);
            }
        }
    }
}
