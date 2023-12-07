using System;
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
        public class FlyToWaypoint : Action {
            public readonly Program _program;
            public readonly MyWaypointInfo _target;
            public readonly bool _beFast;
            public readonly IMyRemoteControl _remoteControl;
            public readonly IMyShipConnector _dockingPort;

            public FlyToWaypoint(Program program, MyWaypointInfo target, bool beFast, IMyRemoteControl remoteControl, IMyShipConnector dockingPort) {
                _program = program;
                _target = target;
                _beFast = beFast;
                _remoteControl = remoteControl;
                _dockingPort = dockingPort;
            }

            public void Begin() {
                _remoteControl.SpeedLimit = 20.0f;
                _remoteControl.AddWaypoint(_target);
                _remoteControl.SetDockingMode(!_beFast);
                _remoteControl.SetCollisionAvoidance(_beFast);
                _remoteControl.SetAutoPilotEnabled(true);
                _remoteControl.FlightMode = FlightMode.OneWay;
                _remoteControl.WaitForFreeWay = false;
            }

            public bool Step() {
                float desiredCloseness;
                if (_beFast) {
                    desiredCloseness = 50.0f;
                } else {
                    desiredCloseness = 0.0f;
                }
                _program.ReportStatus(String.Format("FlyToWaypoint: {0}, {1}", _target, _beFast));
                return Vector3.Distance(_dockingPort.Position, _target.Coords) <= desiredCloseness;
            }

            public List<Action> End() {
                _remoteControl.ClearWaypoints();
                return new List<Action>();
            }
        }

    }
}
