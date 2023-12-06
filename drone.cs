
bool _broken = false;

IMyShipConnector _dockingPort;
IMyShipController _remoteControl;
IMyBlockGroup _allThrusters;

IMyWaypointInfo _mine;
IMyWaypointInfo _home;

interface Action {
    void Begin();
    bool Step();
    Action End();
}

class Fly implements Action {
    public const IMyWaypoint _target;
    public const float _desiredCloseness;
    public const bool _beFast;
    public const bool _towardMine;

    Fly(bool _towardMine, IMyWaypoint target, float desiredCloseness, bool beFast) {
        _target = target;
        _desiredCloseness = desiredCloseness;
        _beFast = beFast;
    }

    void Begin() {
        SetFastness(beFast);
        activateAI();
        Runtime.UpdateFrequency = UpdateFrequency.Update100;
    }

    bool Step() {
        return MeIsNear(Target, DesiredCloseness);
    }

    Action End() {
        clearWaypoints();
        stopAI();

        var nextTarget = _towardMine ? _mine : _home;

        if(_target == nextTarget) {
            if(_beFast) {
                return new Fly(_towardMine, nextTarget, 0.0f, false);
            } else {
                return new Dock(_towardMine);
            }
        } else {
            return new Fly(_towardMine, nextTarget, 50.0f, true);
        }
    }
}

class Dock implements Action {
    public const bool _towardMine;

    public Dock(bool towardMine) {
        _towardMine = towardMine;
    }

    void Begin() {
        SetFastness(false);
        DockingPort.turnOn();
        activateAI();
        flyDown();
        Runtime.UpdateFrequency = UpdateFrequency.Update10;
    }

    bool Step() {
        return !DockingPort.lit();
    }

    Action End() {
        dock();
        aiOff();
        return new SitAtDockingPort(_towardMine);
    }
}

class SitAtDockingPort implements Action {
    public const bool _atMine;
    public const IMyBlockGroup _drills;

    SitAtDockingPort() {
        _drills = tryGetGroup();
    }

    void Begin() {
        if(_drills != null) {
            _drills.drillsOn();
        }
        Runtime.UpdateFrequency = UpdateFrequency.Update100;
    }

    bool Step() {
        if(_drills != null) {
            return _bags < 1.0f;
        } else {
            return _bags > 0.0f && _power < 1.0f;
        }
    }

    Action End() {
        if(_drills != null) {
            drillsOff();
        }
        DockingPort.turnOff();
        if(_bags < 0.2f) {
            return new Fly(true, _home, 0.0f, false);
        }
    }
}

private IMyTerminalBlock LoadBlock(string name) {
    var block = GridTerminalSystem.GetBlockWithName(name);
    if block == null {
        throw new Exception("Oh shit! I can't find block: '" + name + "'");
    }
    return block;
}

public Program()
{
    try {
        Runtime.UpdateFrequency = UpdateFrequency.Update100;
        _remoteControl = LoadBlock("Drone remote control");
        _dockingPort = LoadBlock("Drone connector");
    } catch(Exception e) {
        Echo(e.Message);
        _broken = true;
        Runtime.UpdateFrequency = 0;
    }
}

public void Main(string argument, UpdateType updateSource)
{
    if _broken {
        return;
    }

    if(_action == null) {
        if(_dockingPort.IsConnected()) {
            _action = new SitAtDockingPort();
        } else {
            Echo("plz help me get to a docking port and then reboot me thx");
            Runtime.UpdateFrequency = 0;
            _broken = true;
        }
    }

    if(!_action.Step()) {
        _action = _action.End();
        _action.Begin();
    }
}
