﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
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
    partial class Program {
        public class SitAtDockingPort : Action {
            public readonly Program _program;
            public readonly bool _atMine;
            public readonly IMyShipConnector _dockingPort;
            public readonly IMyRemoteControl _remoteControl;
            public readonly List<IMyThrust> _thrusters;
            public readonly List<IMyBatteryBlock> _batteries;
            public readonly List<IMyGyro> _gyros;
            public readonly List<IMyCargoContainer> _cargo;
            public readonly List<IMyShipDrill> _drills = new List<IMyShipDrill>();

            public SitAtDockingPort(Program program, bool atMine, IMyShipConnector dockingPort, IMyRemoteControl remoteControl, List<IMyGyro> gyros, List<IMyCargoContainer> cargo, List<IMyThrust> thrusters, List<IMyBatteryBlock> batteries) {
                _program = program;
                _atMine = atMine;
                _dockingPort = dockingPort;
                _remoteControl = remoteControl;
                _thrusters = thrusters;
                _batteries = batteries;
                _cargo = cargo;
                _gyros = gyros;
            }

            public void Begin() {
                foreach(var gyro in _gyros) {
                    gyro.GyroOverride = false;
                }

                foreach (var thruster in _thrusters) {
                    thruster.Enabled = false;
                }

                if (_atMine) {
                    _program.GridTerminalSystem.GetBlocksOfType(_drills);
                    foreach (var drill in _drills) {
                        drill.Enabled = true;
                    }
                } else {
                    foreach (IMyBatteryBlock battery in _batteries) {
                        battery.ChargeMode = ChargeMode.Recharge;
                    }
                }
            }

            public bool Step() {
                _program.ReportStatus(String.Format("Dock: {0}, {1}", _atMine, _drills.Count));
                if (_atMine) {
                    return _program.CargoFullness() < 0.9f;
                } else {
                    return _program.CargoFullness() > 0.01f && _program.PowerFullness() < 0.9f;
                }
            }

            public List<Action> End() {
                foreach(var gyro in _gyros) {
                    gyro.GyroOverride = true;
                    gyro.Pitch = 0;
                    gyro.Roll = 0;
                    gyro.Yaw = 0;
                }

                foreach (var thruster in _thrusters) {
                    thruster.Enabled = true;
                }

                foreach (var drill in _drills) {
                    drill.Enabled = false;
                }
                _drills.Clear();

                foreach (IMyBatteryBlock battery in _batteries) {
                    battery.ChargeMode = ChargeMode.Auto;
                }

                _remoteControl.HandBrake = false;
                _dockingPort.Disconnect();
                _dockingPort.Enabled = false;

                List<Action> plan = new List<Action>();
                if (_atMine) {
                    plan.Add(new FlyToWaypoint(_program, _program._mine, false, _remoteControl, _dockingPort));
                    plan.Add(new FlyToWaypoint(_program, _program._home, true, _remoteControl, _dockingPort));
                    plan.Add(new FlyToWaypoint(_program, _program._home, false, _remoteControl, _dockingPort));
                    plan.Add(new Dock(_program, false, _remoteControl, _dockingPort));
                } else {
                    plan.Add(new FlyToWaypoint(_program, _program._home, false, _remoteControl, _dockingPort));
                    plan.Add(new FlyToWaypoint(_program, _program._mine, true, _remoteControl, _dockingPort));
                    plan.Add(new FlyToWaypoint(_program, _program._mine, false, _remoteControl, _dockingPort));
                    plan.Add(new Dock(_program, true, _remoteControl, _dockingPort));
                }
                return plan;
            }
        }
    }
}
